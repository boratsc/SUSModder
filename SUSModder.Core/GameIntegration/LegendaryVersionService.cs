using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.GameIntegration
{
    /// <summary>
    /// Ścieżki narzędzia legendary.exe w katalogu aplikacji.
    /// </summary>
    /// <remarks>
    /// Wspólne dla <see cref="EpicVersionManager"/> i <see cref="LegendaryVersionService"/>,
    /// żeby podmiana binarki nie rozjechała się ze ścieżką, z której uruchamia się narzędzie.
    /// </remarks>
    public static class LegendaryToolPaths
    {
        /// <summary>Katalog, w którym leży legendary.exe.</summary>
        public static string ToolDirectory => AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>Pełna ścieżka legendary.exe używanego przez aplikację.</summary>
        public static string ExecutablePath => Path.Combine(ToolDirectory, "legendary.exe");

        /// <summary>Kopia poprzedniej wersji narzędzia (przechowywana po podmianie).</summary>
        public static string PreviousExecutablePath => Path.Combine(ToolDirectory, "legendary.exe.previous");
    }

    /// <summary>
    /// Serwis zarządzający wersją narzędzia legendary.exe (tryb Epic).
    /// </summary>
    /// <remarks>
    /// Tryb domyślny (pinned) — przypięte, niezależnie utrzymywane wydanie z własnym SHA-256.
    /// Tryb ręczny — wydanie wybrane przez użytkownika; suma kontrolna pochodzi z metadanych GitHub
    /// (kontrola integralności, nie niezależny audyt wydawcy). Brak digestu w API = odmowa.
    /// </remarks>
    public sealed class LegendaryVersionService : ILegendaryVersionProvider
    {
        /// <summary>Przypięte wydanie: upstream legendary 0.21.1 (Windows x64).</summary>
        public const string PinnedRepository = "legendary-gl/legendary";

        /// <summary>Przypięty tag wydania.</summary>
        public const string PinnedTag = "0.21.1";

        /// <summary>Przypięty asset Windows x64 upstreamu (inne nazwy assetów niż w Heroic).</summary>
        public const string PinnedAssetName = "legendary_windows_x64.exe";

        /// <summary>
        /// SHA-256 przypiętego assetu — niezależnie utrzymywany, nie brany z API przy każdym uruchomieniu.
        /// </summary>
        public const string PinnedSha256 =
            "6be77857dc0a6dd33ea7442ce8b5291944ec6da53757f81ca95b57ce794baf5b";

        /// <summary>URL przypiętego assetu.</summary>
        public const string PinnedDownloadUrl =
            "https://github.com/legendary-gl/legendary/releases/download/0.21.1/legendary_windows_x64.exe";

        /// <summary>
        /// Maksymalny rozmiar pobieranego narzędzia (ochrona przed nieprawidłowym źródłem).
        /// Prawdziwe binarki mają ~17 MB — limit z dużym zapasem.
        /// </summary>
        internal const long MaxDownloadBytes = 64L * 1024 * 1024;

        /// <summary>Limit czasu jednego transferu (całego pobrania, nie tylko nawiązania).</summary>
        internal static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

        private readonly LegendaryReleaseClient _client;
        private readonly UserSettingsService _settings;
        private readonly HttpClient _httpClient;
        private readonly string _executablePath;
        private readonly string _previousPath;
        private readonly long _maxDownloadBytes;

        /// <summary>
        /// Wspólny klient HTTP — jedno połączenie dla całego procesu (bez wycieku socketów).
        /// </summary>
        private static readonly Lazy<HttpClient> SharedHttpClient = new(() =>
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SUSModder");
            return client;
        });

        public LegendaryVersionService(
            IUserSettingsRepository? settingsRepository = null,
            HttpClient? httpClient = null,
            IDiagnosticsOutput? output = null,
            string? executablePath = null,
            long maxDownloadBytes = MaxDownloadBytes)
        {
            _settings = settingsRepository != null
                ? new UserSettingsService(settingsRepository)
                : new UserSettingsService();

            if (httpClient != null)
            {
                _httpClient = httpClient;
            }
            else
            {
                _httpClient = SharedHttpClient.Value;
            }

            _client = new LegendaryReleaseClient(_httpClient, output);

            _executablePath = executablePath ?? LegendaryToolPaths.ExecutablePath;
            _previousPath = _executablePath + ".previous";
            _maxDownloadBytes = maxDownloadBytes;
        }

        /// <summary>Wydanie przypięte — to, co aplikacja używa domyślnie.</summary>
        public static LegendaryReleaseInfo PinnedRelease { get; } = new(
            LegendaryReleaseSource.Upstream,
            PinnedTag,
            PinnedAssetName,
            PinnedSha256);

        /// <inheritdoc/>
        public async Task<LegendaryVersionState> GetStateAsync(CancellationToken ct = default)
        {
            var settings = _settings.LoadUserSettings();
            LegendaryReleaseInfo? selected = ReadSelectedRelease(settings);

            // Uszkodzony lub niekompletny zapis wyboru ręcznego = brak wiarygodnego wydania.
            // Wtedy tryb przypięty jest jedyną bezpieczną opcją (fail closed).
            bool usePinned = settings.LegendaryUsePinned || selected == null;

            string? installedSha = await ComputeInstalledShaAsync(ct);

            LegendaryReleaseInfo? installed = null;
            if (installedSha != null)
            {
                if (string.Equals(installedSha, PinnedSha256, StringComparison.OrdinalIgnoreCase))
                    installed = PinnedRelease;
                else if (selected != null &&
                         string.Equals(installedSha, selected.Sha256, StringComparison.OrdinalIgnoreCase))
                    installed = selected;
            }

            return new LegendaryVersionState(usePinned, selected, PinnedRelease, installed);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<LegendaryReleaseInfo>> GetReleasesAsync(CancellationToken ct = default)
        {
            var assets = await _client.ListAsync(ct);
            return assets
                .Select(a => new LegendaryReleaseInfo(a.Source, a.Tag, a.AssetName, a.Sha256, a.PublishedAt))
                .ToList();
        }

        /// <inheritdoc/>
        public async Task<LegendaryToolInspection> InspectInstalledAsync(CancellationToken ct = default)
        {
            _settings.ClearCache();
            var settings = _settings.LoadUserSettings();
            LegendaryReleaseInfo? selected = ReadSelectedRelease(settings);

            string? expectedSha = selected?.Sha256 ?? PinnedSha256;
            bool isSelected = selected != null;

            string? installedSha = await ComputeInstalledShaAsync(ct);
            if (installedSha == null)
                return new LegendaryToolInspection(LegendaryToolTrust.Missing, null, false);

            bool matchesSelection = string.Equals(installedSha, expectedSha, StringComparison.OrdinalIgnoreCase);
            if (matchesSelection)
                return new LegendaryToolInspection(LegendaryToolTrust.Trusted, installedSha, true);

            // Binarka SUSModder, ale inna niż wybrana (np. poprzednia przypięta 0.20.41) — uruchomić można,
            // tło wymieni ją na właściwą wersję.
            if (LegendaryToolTrustPolicy.IsTrustedSha256(installedSha, selected?.Sha256))
                return new LegendaryToolInspection(LegendaryToolTrust.TrustedButNotSelected, installedSha, false);

            // Nieznana suma — plik obcy. Nie wolno go uruchamiać.
            return new LegendaryToolInspection(LegendaryToolTrust.Untrusted, installedSha, false);
        }

        /// <inheritdoc/>
        public async Task<LegendarySwitchResult> SwitchAsync(
            LegendaryReleaseInfo? release,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            return await SwitchCoreAsync(release, progress, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<LegendarySwitchResult> EnsureInstalledAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            return await EnsureInstalledCoreAsync(progress, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Upewnia się, że zainstalowany legendary.exe odpowiada wybranemu wydaniu.
        /// </summary>
        /// <remarks>
        /// Wybór jest sprawdzany ponownie pod bramką przed zmianą pliku.
        /// </remarks>
        private async Task<LegendarySwitchResult> EnsureInstalledCoreAsync(
            IProgress<string>? progress,
            CancellationToken ct)
        {
            _settings.ClearCache();
            // Wstępny wybór celu pobrania; przed zmianą pliku sprawdzamy go ponownie.
            var settings = _settings.LoadUserSettings();
            LegendaryReleaseInfo? selected = ReadSelectedRelease(settings);
            bool usePinned = settings.LegendaryUsePinned || selected == null;

            // Tryb ręczny: celem jest zawsze wybrane wydanie — nigdy przypięte.
            // Używamy sumy zapisanej przy wyborze (zweryfikowanej wtedy z API),
            // żeby przy starcie nie odpytywać GitHubu i nie zużywać limitu API.
            if (!usePinned && selected != null)
            {
                if (await IsInstalledAsync(selected.Sha256, ct))
                    return new LegendarySwitchResult(true, ErrorCode: null, Installed: selected);

                // Odtwarzanie po restarcie: plik nie odpowiada zapisanej selekcji, ale odpowiada
                // kopii zapasowej — przywracamy kopię zamiast pobierać narzędzie od nowa.
                if (IsToolBusy())
                    return new LegendarySwitchResult(false, LegendaryErrorCodes.ToolBusy);

                if (await TryRestoreSelectedBackupAsync(selected, ct))
                    return new LegendarySwitchResult(true, ErrorCode: null, Installed: selected);

                var manualAsset = new LegendaryReleaseAsset(
                    selected.Source,
                    selected.Tag,
                    selected.AssetName,
                    selected.Sha256,
                    BuildDownloadUrl(selected),
                    PublishedAt: null);

                // Mismatch w trybie ręcznym to nie jest sukces — instalacja wróci błąd,
                // a stan w ustawieniach pozostaje niezmieniony (wybór użytkownika jest zachowany).
                // Ustawień NIE zapisujemy: to operacja automatyczna, nie świadomy wybór.
                return await InstallAsync(manualAsset, usePinned: false, persistSelection: false, progress, ct);
            }

            var pinnedAsset = new LegendaryReleaseAsset(
                PinnedRelease.Source,
                PinnedRelease.Tag,
                PinnedRelease.AssetName,
                PinnedSha256,
                PinnedDownloadUrl,
                PublishedAt: null);

            // Weryfikacja SHA zainstalowanej binarki — brak zmian, gdy jest już zgodna.
            if (await IsInstalledAsync(PinnedSha256, ct))
                return new LegendarySwitchResult(true, ErrorCode: null, Installed: PinnedRelease);

            // Odtwarzanie po restarcie (np. przerwany zapis wyboru) — przywracamy kopię zapasową,
            // gdy odpowiada ona sumie przypiętego wydania.
            if (IsToolBusy())
                return new LegendarySwitchResult(false, LegendaryErrorCodes.ToolBusy);

            if (await TryRestoreSelectedBackupAsync(null, ct))
                return new LegendarySwitchResult(true, ErrorCode: null, Installed: PinnedRelease);

            // Ustawień NIE zapisujemy — to operacja automatyczna odtwarzająca przypiętą wersję.
            return await InstallAsync(pinnedAsset, usePinned: true, persistSelection: false, progress, ct);
        }

        /// <summary>
        /// Przywraca kopię zapasową narzędzia, gdy odpowiada oczekiwanej sumie.
        /// </summary>
        /// <remarks>
        /// Używane po restarcie, gdy podmiana pliku i zapis wyboru rozjechały się (np. przerwany zapis
        /// w SQLite). Zamiast pobierać narzędzie od nowa przywracamy lokalną kopię.
        /// </summary>
        private async Task<bool> TryRestoreSelectedBackupAsync(LegendaryReleaseInfo? selected, CancellationToken ct)
        {
            using var lease = await LegendaryOperationGate.AcquireAsync(ct).ConfigureAwait(false);
            if (IsToolBusy() || !SelectionMatches(selected))
                return false;
            return await TryRestoreBackupAsync(selected?.Sha256 ?? PinnedSha256, ct).ConfigureAwait(false);
        }

        private bool SelectionMatches(LegendaryReleaseInfo? expected)
        {
            _settings.ClearCache();
            return ReadSelectedRelease(_settings.LoadUserSettings()) == expected;
        }

        private async Task<bool> TryRestoreBackupAsync(string expectedSha256, CancellationToken ct)
        {
            if (!File.Exists(_previousPath))
                return false;

            string? backupSha;
            try
            {
                backupSha = await Sha256Verifier.ComputeFileHexAsync(_previousPath, ct);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            if (!string.Equals(backupSha, expectedSha256, StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                RestorePrevious(_executablePath, _previousPath);
                System.Diagnostics.Debug.WriteLine(
                    "[Legendary] Odtworzono narzędzie z kopii zapasowej (zapis wyboru nie zdążył).");
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Adres assetu wydań w dozwolonych repozytoriach — deterministyczny z repo+tag+asset.
        /// </summary>
        private static string BuildDownloadUrl(LegendaryReleaseInfo release) =>
            $"https://github.com/{release.Repository}/releases/download/{release.Tag}/{release.AssetName}";

        /// <summary>
        /// Sprawdza, czy zainstalowany plik odpowiada oczekiwanej sumie SHA-256.
        /// </summary>
        private async Task<bool> IsInstalledAsync(string expectedSha256, CancellationToken ct)
        {
            string? installedSha = await ComputeInstalledShaAsync(ct);
            return installedSha != null &&
                   string.Equals(installedSha, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Podmiana wersji narzędzia na wybrane wydanie (null = powrót do przypiętego).
        /// </summary>
        private async Task<LegendarySwitchResult> SwitchCoreAsync(
            LegendaryReleaseInfo? release,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            // Sprawdzamy zajętość narzędzia przed ruchem sieciowym — nie pobieramy
            // dużego pliku tylko po to, by się dowiedzieć, że podmiana i tak nie nastąpi.
            if (IsToolInUse(_executablePath))
                return new LegendarySwitchResult(false, LegendaryErrorCodes.ToolBusy);

            if (release == null)
            {
                var pinnedAsset = new LegendaryReleaseAsset(
                    PinnedRelease.Source,
                    PinnedRelease.Tag,
                    PinnedRelease.AssetName,
                    PinnedSha256,
                    PinnedDownloadUrl,
                    PublishedAt: null);

                // Ręczny powrót do przypiętej wersji — zapisujemy wybór.
                return await InstallAsync(pinnedAsset, usePinned: true, persistSelection: true, progress, ct);
            }

            // Ręczny wybór: autorytatywne dane (asset + digest) pobieramy ponownie z API,
            // żeby nie ufać temu, co przyszło z warstwy UI.
            var asset = await _client.GetByTagAsync(release.Source, release.Tag, ct);
            if (asset == null)
                return new LegendarySwitchResult(false, LegendaryErrorCodes.NoWindowsAsset);

            // Świadomy wybór użytkownika — zapisujemy go.
            return await InstallAsync(asset, usePinned: false, persistSelection: true, progress, ct);
        }

        /// <summary>
        /// Pobiera, weryfikuje i podmienia narzędzie. Przy błędzie stary plik zostaje na miejscu.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Podmiana pliku i zapis wyboru w SQLite muszą być spójne. Gdy zapis się nie powiedzie,
        /// przywracamy poprzednią binarkę z kopii zapasowej — inaczej plik i zapis wskazywałyby
        /// na różne wydania. Po restarcie odzyskanie następuje po sumie zapisanej selekcji:
        /// jeśli plik nie odpowiada zapisanej selekcji, ale odpowiada kopii zapasowej,
        /// przywracamy kopię zapasową zamiast pobierać narzędzie od nowa.
        /// </para>
        /// <para>
        /// <paramref name="persistSelection"/> = false dla operacji automatycznych (tło, odtwarzanie):
        /// te nie mogą zmieniać świadomego wyboru użytkownika. Ręczna zmiana w UI zapisuje wybór.
        /// </para>
        /// </remarks>
        private async Task<LegendarySwitchResult> InstallAsync(
            LegendaryReleaseAsset asset,
            bool usePinned,
            bool persistSelection,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            string tempPath = _executablePath + ".download." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                progress?.Report(LegendaryProgressPhases.Download);
                if (!await DownloadAsync(asset.DownloadUrl, tempPath, ct))
                {
                    TryDelete(tempPath);
                    return new LegendarySwitchResult(false, LegendaryErrorCodes.Offline);
                }

                progress?.Report(LegendaryProgressPhases.Verify);
                string actualSha = await Sha256Verifier.ComputeFileHexAsync(tempPath, ct);
                if (!string.Equals(actualSha, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(tempPath);
                    return new LegendarySwitchResult(false, LegendaryErrorCodes.HashMismatch);
                }

                progress?.Report(LegendaryProgressPhases.Install);
                using var lease = await LegendaryOperationGate.AcquireAsync(ct).ConfigureAwait(false);
                if (IsToolBusy())
                    return new LegendarySwitchResult(false, LegendaryErrorCodes.ToolBusy);
                if (!persistSelection && !SelectionMatches(usePinned ? null : new LegendaryReleaseInfo(
                        asset.Source, asset.Tag, asset.AssetName, asset.Sha256)))
                    return new LegendarySwitchResult(false, LegendaryErrorCodes.ToolBusy);
                _settings.ClearCache();
                bool hadPreviousExecutable = File.Exists(_executablePath);
                if (!TrySwapExecutable(tempPath, _executablePath, _previousPath))
                {
                    TryDelete(tempPath);
                    return new LegendarySwitchResult(false, LegendaryErrorCodes.ToolBusy);
                }

                // Zapis wyboru po podmianie pliku — gdy zawiedzie, przywracamy poprzednią binarkę.
                // Operacje automatyczne (persistSelection = false) w ogóle nie zapisują wyboru.
                if (persistSelection)
                {
                    try
                    {
                        SaveSelection(asset, usePinned);
                    }
                    catch (Exception ex)
                    {
                        _settings.ClearCache();
                        if (hadPreviousExecutable)
                            RestorePrevious(_executablePath, _previousPath);
                        else
                            TryDelete(_executablePath);
                        System.Diagnostics.Debug.WriteLine(
                            $"[Legendary] Zapis wyboru failed ({ex.Message}) — przywrócono poprzednią binarkę.");
                        return new LegendarySwitchResult(false, LegendaryErrorCodes.Failed);
                    }
                }

                return new LegendarySwitchResult(
                    true,
                    ErrorCode: null,
                    Installed: new LegendaryReleaseInfo(
                        asset.Source, asset.Tag, asset.AssetName, asset.Sha256, asset.PublishedAt));
            }
            catch (OperationCanceledException)
            {
                TryDelete(tempPath);
                throw;
            }
            catch (HttpRequestException)
            {
                TryDelete(tempPath);
                return new LegendarySwitchResult(false, LegendaryErrorCodes.Offline);
            }
            catch (IOException)
            {
                TryDelete(tempPath);
                return new LegendarySwitchResult(false, LegendaryErrorCodes.Failed);
            }
            catch (UnauthorizedAccessException)
            {
                TryDelete(tempPath);
                return new LegendarySwitchResult(false, LegendaryErrorCodes.Failed);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        /// <summary>
        /// Podmienia plik narzędzia, zachowując poprzednią wersję w pliku kopii zapasowej
        /// (dzięki temu ręczny powrót do poprzedniego wydania pozostaje możliwy).
        /// </summary>
        /// <returns>false gdy pliku nie da się teraz podmienić (np. jest używany).</returns>
        private static bool TrySwapExecutable(string tempPath, string targetPath, string previousPath)
        {
            try
            {
                if (File.Exists(targetPath))
                {
                    File.Copy(targetPath, previousPath, overwrite: true);
                }

                File.Move(tempPath, targetPath, overwrite: true);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Przywraca poprzednią wersję narzędzia z kopii zapasowej.
        /// </summary>
        /// <remarks>
        /// Kopia jest źródłem prawdy — nadpisujemy nią plik narzędzia niezależnie od tego,
        /// czy podmiana się udała (plik może już zawierać nową wersję).
        /// </remarks>
        private static void RestorePrevious(string targetPath, string previousPath)
        {
            string tempPath = targetPath + ".restore." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(previousPath, tempPath);
                File.Move(tempPath, targetPath, overwrite: true);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        /// <summary>
        /// Czy plik narzędzia jest w użyciu — otwarcie z wyłącznym udziałem zwraca błąd,
        /// gdy proces (np. trwające legendary) ma go otwartego.
        /// </summary>
        private static bool IsToolInUse(string targetPath)
        {
            if (!File.Exists(targetPath))
                return false;

            try
            {
                using var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.None);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        /// <summary>
        /// Czy narzędzie jest w użyciu — trwająca operacja legendary albo otwarty plik.
        /// </summary>
        /// <remarks>
        /// Procesu nie ubijamy: podmiana jest odraczana (ToolBusy), a użytkownik może spróbować ponownie.
        /// </remarks>
        private bool IsToolBusy() =>
            LegendaryOperationGate.ActiveOperations > 0 || IsToolInUse(_executablePath);

        private async Task<bool> DownloadAsync(string url, string destinationPath, CancellationToken ct)
        {
            // Limit czasu całego transferu — nie tylko nawiązania, ale całego pobierania.
            using var timeoutCts = new CancellationTokenSource(DownloadTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                using var response = await _httpClient.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token);

                if (!response.IsSuccessStatusCode)
                    return false;

                // Limit całego transferu — odrzucamy za duże pliki zanim zmarnujemy dysk.
                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > _maxDownloadBytes)
                    return false;

                await using var source = await response.Content.ReadAsStreamAsync(linkedCts.Token);
                await using var destination = new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true);

                byte[] buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), linkedCts.Token)) > 0)
                {
                    total += read;
                    if (total > _maxDownloadBytes)
                        return false;

                    await destination.WriteAsync(buffer.AsMemory(0, read), linkedCts.Token);
                }

                return total > 0;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // Przekroczony limit czasu transferu — traktujemy jak nieudane pobranie.
                return false;
            }
        }

        /// <summary>
        /// Odczytuje z ustawień ręcznie wybrane wydanie (null = brak wyboru).
        /// </summary>
        private LegendaryReleaseInfo? ReadSelectedRelease(UserSettings settings)
        {
            if (settings.LegendaryUsePinned)
                return null;

            if (!Enum.TryParse(settings.LegendaryReleaseSource, ignoreCase: true, out LegendaryReleaseSource source))
                return null;

            if (string.IsNullOrWhiteSpace(settings.LegendaryReleaseTag))
                return null;

            if (!Sha256Verifier.IsWellFormedHash(settings.LegendaryReleaseSha256))
                return null;

            string assetName = LegendaryReleaseCatalog.AssetFor(source);
            if (!string.IsNullOrWhiteSpace(settings.LegendaryReleaseAsset) &&
                !string.Equals(settings.LegendaryReleaseAsset, assetName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new LegendaryReleaseInfo(
                source,
                settings.LegendaryReleaseTag,
                assetName,
                settings.LegendaryReleaseSha256.ToLowerInvariant());
        }

        /// <summary>Zapisuje aktywny wybór (przypięty lub ręczny) w ustawieniach.</summary>
        private void SaveSelection(LegendaryReleaseAsset asset, bool usePinned)
        {
            _settings.UpdateUserSetting(s =>
            {
                s.LegendaryUsePinned = usePinned;
                s.LegendaryReleaseSource = usePinned ? string.Empty : asset.Source.ToString();
                s.LegendaryReleaseTag = usePinned ? string.Empty : asset.Tag;
                s.LegendaryReleaseAsset = usePinned ? string.Empty : asset.AssetName;
                s.LegendaryReleaseSha256 = usePinned ? string.Empty : asset.Sha256;
            });
        }

        /// <summary>SHA-256 zainstalowanego narzędzia (null = brak pliku / błąd odczytu).</summary>
        private async Task<string?> ComputeInstalledShaAsync(CancellationToken ct)
        {
            if (!File.Exists(_executablePath))
                return null;

            try
            {
                return await Sha256Verifier.ComputeFileHexAsync(_executablePath, ct);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                // best effort
            }
            catch (UnauthorizedAccessException)
            {
                // best effort
            }
        }
    }
}
