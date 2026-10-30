using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Views
{
    /// <summary>
    /// Dialog potwierdzenia aktualizacji moda.
    ///
    /// 3.1-06: dialog rozszerzono o podsumowanie aktualizacji (preview) —
    /// prognozę losu dodatków DLL i ocenę zachowania konfiguracji.
    /// Podsumowanie jest wyłącznie odczytem i nie obiecuje wyniku:
    /// brak danych oznacza niepewność, nigdy potwierdzoną zgodność.
    /// </summary>
    public partial class UpdateModConfirmDialog : Window
    {
        /// <summary>
        /// Komplet kluczy lokalizacyjnych potrzebnych do renderowania podsumowania.
        /// Dopóki wszystkie nie istnieją, sekcja podsumowania pozostaje ukryta —
        /// w interfejsie nie pojawia się tekst nieprzetłumaczony.
        /// </summary>
        private static readonly string[] RequiredPreviewKeys =
        {
            "Dialogs.UpdateMod.Preview.Header",
            "Dialogs.UpdateMod.Preview.Loading",
            "Dialogs.UpdateMod.Preview.Failed",
            "Dialogs.UpdateMod.Preview.AddonsHeader",
            "Dialogs.UpdateMod.Preview.AddonsNone",
            "Dialogs.UpdateMod.Preview.Addon.Retained",
            "Dialogs.UpdateMod.Preview.Addon.RetainedUnknown",
            "Dialogs.UpdateMod.Preview.Addon.Incompatible",
            "Dialogs.UpdateMod.Preview.Addon.MissingCatalog",
            "Dialogs.UpdateMod.Preview.Addon.CustomFile",
            "Dialogs.UpdateMod.Preview.Addon.Unknown",
            "Dialogs.UpdateMod.Preview.ConfigHeader",
            "Dialogs.UpdateMod.Preview.ConfigNotGuaranteed",
            "Dialogs.UpdateMod.Preview.ConfigUnknown"
        };

        public bool Result { get; private set; }

        /// <summary>
        /// Podsumowanie aktualizacji zbudowane dla tego dialogu (null, gdy nie udało się go przygotować).
        /// </summary>
        public UpdatePreview? Preview { get; private set; }

        /// <summary>
        /// Wersja docelowa zatwierdzona przez użytkownika (ta sama, którą pokazuje okno „Nowa wersja moda”).
        /// Wywołujący musi zainstalować dokładnie tę wersję — po zmianie katalogu
        /// wymagane jest ponowne potwierdzenie (patrz UpdateTargetRevalidator).
        /// </summary>
        public string? ApprovedTargetVersion => _updateInfo?.NewVersion ?? Preview?.TargetVersion;

        /// <summary>Wersja Among Us pokazana w oknie (wersja gry dla wersji docelowej).</summary>
        public string? ApprovedTargetAmongVersion => _updateInfo?.RemoteMod?.AmongVersion ?? Preview?.TargetAmongVersion;

        /// <summary>Stabilny klucz zatwierdzonego celu (wersja + platforma + wersja gry).</summary>
        public string? ApprovedTargetKey => Preview?.TargetKey;

        private readonly ILocalizationService _localizationService;
        private readonly ModUpdateInfo? _updateInfo;
        private CancellationTokenSource? _previewCancellation;

        public UpdateModConfirmDialog()
        {
            InitializeComponent();
            _localizationService = App.GetService<ILocalizationService>();
        }

        public UpdateModConfirmDialog(ModUpdateInfo updateInfo)
            : this(updateInfo, null)
        {
        }

        /// <summary>
        /// Dialog potwierdzenia z opcjonalnym, gotowym podsumowaniem.
        /// Gdy podsumowanie nie zostanie przekazane, dialog buduje je sam (odczyt).
        /// </summary>
        public UpdateModConfirmDialog(ModUpdateInfo updateInfo, UpdatePreview? preview)
        {
            InitializeComponent();
            _localizationService = App.GetService<ILocalizationService>();
            _updateInfo = updateInfo;

            Title = _localizationService.Get("Dialogs.UpdateMod.Title");
            TitleText.Text = _localizationService.GetFormatted("Dialogs.UpdateMod.TitleWithName", updateInfo.ModName);
            MessageText.Text = _localizationService.Get("Dialogs.UpdateMod.Message");

            // Ustaw informacje o wersjach
            var unknownVersion = _localizationService.Get("Dialogs.UpdateMod.UnknownVersion");
            CurrentModVersionText.Text = updateInfo.CurrentVersion ?? unknownVersion;
            NewModVersionText.Text = updateInfo.NewVersion ?? unknownVersion;
            AmongVersionText.Text = updateInfo.RemoteMod?.AmongVersion ?? unknownVersion;

            // Asynchronicznie sprawdź rozmiar pliku do pobrania
            var downloadLink = updateInfo.RemoteMod?.GitHubRepoOrLink;
            if (!string.IsNullOrEmpty(downloadLink))
            {
                _ = LoadDownloadSizeAsync(downloadLink);
            }

            if (preview != null)
            {
                RenderPreview(preview);
            }
            else
            {
                _ = LoadPreviewAsync();
            }
        }

        private async Task LoadDownloadSizeAsync(string downloadUrl)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                // Wykonaj HEAD request aby uzyskać rozmiar bez pobierania
                var request = new HttpRequestMessage(HttpMethod.Head, downloadUrl);
                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength.HasValue)
                {
                    var sizeBytes = response.Content.Headers.ContentLength.Value;
                    var sizeText = FormatBytes(sizeBytes);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DownloadSizeText.Text = _localizationService.GetFormatted("Dialogs.UpdateMod.DownloadSize", sizeText);
                        DownloadSizeInfoBorder.IsVisible = true;
                    });
                }
            }
            catch
            {
                // Jeśli nie można uzyskać rozmiaru, po prostu nie pokazuj info
            }
        }

        /// <summary>
        /// Buduje podsumowanie aktualizacji (odczyt) i renderuje je w dialogu.
        /// </summary>
        private async Task LoadPreviewAsync()
        {
            if (_updateInfo == null)
                return;

            if (!HasPreviewLocalization())
            {
                // Brak kompletu kluczy — sekcja pozostaje ukryta zamiast pokazywać surowe klucze.
                return;
            }

            try
            {
                PreviewBorder.IsVisible = true;
                PreviewLoadingText.Text = _localizationService.Get("Dialogs.UpdateMod.Preview.Loading");
                PreviewLoadingText.IsVisible = true;

                _previewCancellation?.Cancel();
                _previewCancellation?.Dispose();
                _previewCancellation = new CancellationTokenSource();

                var service = UpdatePreviewService.CreateDefault();
                var preview = await service.BuildPreviewAsync(
                    _updateInfo,
                    ResolvePlatform(),
                    _previewCancellation.Token);

                await Dispatcher.UIThread.InvokeAsync(() => RenderPreview(preview));
            }
            catch (OperationCanceledException)
            {
                // Okno zamknięte w trakcie budowania podsumowania — nic do pokazania.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdatePreview] Nie udało się przygotować podsumowania: {ex.Message}");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    PreviewLoadingText.IsVisible = false;
                    PreviewErrorText.Text = _localizationService.Get("Dialogs.UpdateMod.Preview.Failed");
                    PreviewErrorText.IsVisible = true;
                });
            }
        }

        /// <summary>
        /// Renderuje podsumowanie: listę dodatków DLL i ocenę konfiguracji.
        /// </summary>
        private void RenderPreview(UpdatePreview preview)
        {
            Preview = preview;

            PreviewLoadingText.IsVisible = false;
            PreviewErrorText.IsVisible = false;

            PreviewHeaderText.Text = _localizationService.Get("Dialogs.UpdateMod.Preview.Header");

            PreviewAddonsHeaderText.Text = _localizationService.Get("Dialogs.UpdateMod.Preview.AddonsHeader");
            PreviewAddonsHeaderText.IsVisible = true;

            PreviewAddonList.Children.Clear();

            if (preview.Addons.Count == 0)
            {
                PreviewAddonList.Children.Add(CreatePreviewText(
                    _localizationService.Get("Dialogs.UpdateMod.Preview.AddonsNone"),
                    isAttention: false));
            }
            else
            {
                foreach (var addon in preview.Addons)
                {
                    PreviewAddonList.Children.Add(CreatePreviewText(
                        FormatAddonStatus(addon),
                        addon.RequiresAttention));
                }
            }

            PreviewConfigHeaderText.Text = _localizationService.Get("Dialogs.UpdateMod.Preview.ConfigHeader");
            PreviewConfigHeaderText.IsVisible = true;

            PreviewConfigText.Text = preview.ConfigStatus switch
            {
                UpdateConfigStatus.NotGuaranteed =>
                    _localizationService.Get("Dialogs.UpdateMod.Preview.ConfigNotGuaranteed"),
                _ => _localizationService.Get("Dialogs.UpdateMod.Preview.ConfigUnknown")
            };
            PreviewConfigText.IsVisible = true;

            PreviewBorder.IsVisible = true;
        }

        private string FormatAddonStatus(UpdateAddonPreview addon)
        {
            string key = addon.Status switch
            {
                UpdateAddonStatus.Retained => "Dialogs.UpdateMod.Preview.Addon.Retained",
                UpdateAddonStatus.RetainedCompatibilityUnknown => "Dialogs.UpdateMod.Preview.Addon.RetainedUnknown",
                UpdateAddonStatus.RetainedIncompatible => "Dialogs.UpdateMod.Preview.Addon.Incompatible",
                UpdateAddonStatus.NotRestoredMissingCatalog => "Dialogs.UpdateMod.Preview.Addon.MissingCatalog",
                UpdateAddonStatus.NotRestoredCustomFile => "Dialogs.UpdateMod.Preview.Addon.CustomFile",
                _ => "Dialogs.UpdateMod.Preview.Addon.Unknown"
            };

            return _localizationService.GetFormatted(key, ViewModels.Helpers.UpdateAddonPresentation.NameWithVersions(addon));
        }

        private static TextBlock CreatePreviewText(string text, bool isAttention)
        {
            var block = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap
            };
            block.Classes.Add("preview-addon");
            if (isAttention)
                block.Classes.Add("attention");

            return block;
        }

        private bool HasPreviewLocalization()
        {
            foreach (var key in RequiredPreviewKeys)
            {
                if (!TryGetLocalized(key, out _))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Zwraca tekst tylko, gdy klucz istnieje w aktualnym języku (lub w fallbackzie PL).
        /// Brak kluczu oznacza "[klucz]" — takiego tekstu nie pokazujemy użytkownikowi.
        /// </summary>
        private bool TryGetLocalized(string key, out string value)
        {
            value = _localizationService.Get(key);
            return !string.IsNullOrWhiteSpace(value) && !string.Equals(value, $"[{key}]", StringComparison.Ordinal);
        }

        private static string ResolvePlatform()
        {
            try
            {
                var settings = new UserSettingsService().LoadUserSettings();
                return string.IsNullOrWhiteSpace(settings.Mode) ? "steam" : settings.Mode;
            }
            catch
            {
                return "steam";
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _previewCancellation?.Cancel();
            _previewCancellation?.Dispose();
            _previewCancellation = null;
            base.OnClosed(e);
        }
    }
}
