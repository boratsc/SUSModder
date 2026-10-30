using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Services;

namespace SUSModder.ViewModels;

public partial class MainWindowViewModel
{
    private bool _iconAssetsInitialized;
    private CatalogSyncService? _catalogIconSync;
    private readonly CancellationTokenSource _iconAssetsCts = new();
    private Dictionary<string, IconAssignment> _versionedIconAssignments = new(StringComparer.Ordinal);
    private bool _versionedIconSchemaActive;
    private int _iconAssignmentRefreshVersion;

    public void InitializeIconAssets()
    {
        if (_iconAssetsInitialized) return;
        _iconAssetsInitialized = true;
        ModIconPreloader.IconAvailable += OnCatalogIconAvailable;
        AvailableFullMods.CollectionChanged += OnIconModelsAdded;
        ModsWithDllInstalled.CollectionChanged += OnIconModelsAdded;
        ModsWithoutDllInstalled.CollectionChanged += OnIconModelsAdded;
        try
        {
            _catalogIconSync = App.GetService<CatalogSyncService>();
            _catalogIconSync.IconAssignmentsChanged += OnIconAssignmentsChanged;
            _ = RefreshVersionedIconAssetsAsync();
        }
        catch (InvalidOperationException) { } // Design-time services need not exist.
    }

    public void DisposeIconAssets()
    {
        if (!_iconAssetsInitialized) return;
        _iconAssetsInitialized = false;
        ModIconPreloader.IconAvailable -= OnCatalogIconAvailable;
        AvailableFullMods.CollectionChanged -= OnIconModelsAdded;
        ModsWithDllInstalled.CollectionChanged -= OnIconModelsAdded;
        ModsWithoutDllInstalled.CollectionChanged -= OnIconModelsAdded;
        if (_catalogIconSync != null)
            _catalogIconSync.IconAssignmentsChanged -= OnIconAssignmentsChanged;
        _iconAssetsCts.Cancel();
        _iconAssetsCts.Dispose();
        ModIconPreloader.Shutdown();
    }

    private void OnIconAssignmentsChanged(object? sender, IconAssignmentsChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => { if (!_disposed) _ = RefreshVersionedIconAssetsAsync(); });

    private async Task RefreshVersionedIconAssetsAsync()
    {
        if (_disposed || _catalogIconSync == null) return;
        var version = Interlocked.Increment(ref _iconAssignmentRefreshVersion);
        var token = _iconAssetsCts.Token;
        try
        {
            var active = _catalogIconSync.IsVersionedIconSchemaActive;
            var assignments = active ? _catalogIconSync.GetIconAssignments()
                .Where(a => a.SchemaVersion == CatalogSyncService.IconSchemaVersion).ToArray() : Array.Empty<IconAssignment>();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || version != _iconAssignmentRefreshVersion) return;
                _versionedIconSchemaActive = active;
                _versionedIconAssignments = assignments.ToDictionary(a => a.AssignmentKey, StringComparer.Ordinal);
                ApplyCurrentIconKeys();
            });
            if (_disposed || version != _iconAssignmentRefreshVersion || !active) return;
            await Task.WhenAll(assignments.Select(assignment => ModIconPreloader.PreloadVersionedAsync(
                assignment.AssignmentKey, assignment.IsRemoved ? null : new CatalogIconDto
                {
                    AssetId = assignment.AssetId, Url = assignment.Url, Sha256 = assignment.Sha256,
                    ContentType = assignment.ContentType, SizeBytes = assignment.SizeBytes
                }, token))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { System.Diagnostics.Debug.WriteLine("ICON_ASSIGNMENT_REFRESH_FAILED"); }
    }

    private void OnIconModelsAdded(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems == null) return;
        foreach (var item in e.NewItems.OfType<ModItem>())
            item.VersionedIconKey = FindVersionedIconKey(item);
    }

    private string? FindVersionedIconKey(ModItem item)
    {
        var key = item.IsVanilla ? IconAssignmentKeys.GlobalVanilla
            : item.IsDllMod ? IconAssignmentKeys.GlobalDll : IconAssignmentKeys.ForMod(item.Id);
        return _versionedIconSchemaActive && _versionedIconAssignments.ContainsKey(key) ? key : null;
    }

    private void ApplyCurrentIconKeys()
    {
        var mods = _catalogModsSnapshot.Concat(_dllModsSnapshot).Concat(Mods).Concat(DllMods)
            .Concat(AvailableFullMods).Concat(ModsWithDllInstalled).Concat(ModsWithoutDllInstalled).Distinct().ToArray();
        foreach (var item in mods)
            item.VersionedIconKey = FindVersionedIconKey(item);
        foreach (var pack in _packInstancesSnapshot.Concat(PackInstances).Distinct())
        {
            var baseMod = mods.FirstOrDefault(m => m.Id == pack.BaseModId);
            var key = baseMod?.IsVanilla == true ? IconAssignmentKeys.GlobalVanilla : IconAssignmentKeys.ForMod(pack.BaseModId);
            pack.VersionedIconKey = _versionedIconSchemaActive && _versionedIconAssignments.ContainsKey(key) ? key : null;
        }
        this.RaisePropertyChanged(nameof(SelectedDllModIconSourceKey));
    }

    private async Task ReconcileLegacyIconReferencesAsync()
    {
        if (_disposed) return;
        var references = _catalogModsSnapshot.Concat(_dllModsSnapshot).Concat(Mods).Concat(DllMods)
            .Select(m => m.PngFileName).Concat(_packInstancesSnapshot.Concat(PackInstances).Select(p => p.PngFileName))
            .Distinct().ToArray();
        try { await ModIconPreloader.ReconcileLegacyAssignmentsAsync(references, _iconAssetsCts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception) { System.Diagnostics.Debug.WriteLine("ICON_CACHE_RECONCILE_FAILED"); }
    }

    private void OnCatalogIconAvailable(string key)
    {
        foreach (var item in _catalogModsSnapshot.Concat(_dllModsSnapshot).Concat(Mods).Concat(DllMods)
                     .Concat(AvailableFullMods).Concat(ModsWithDllInstalled).Concat(ModsWithoutDllInstalled)
                     .Distinct())
        {
            if (item.PngFileName != key && item.VersionedIconKey != key) continue;
            item.RaisePropertyChanged(nameof(ModItem.IconSourceKey));
            item.RaisePropertyChanged(nameof(ModItem.PngFileName));
            item.RaisePropertyChanged(nameof(ModItem.IconPath));
        }
        foreach (var pack in _packInstancesSnapshot.Concat(PackInstances).Distinct())
        {
            if (pack.PngFileName != key && pack.VersionedIconKey != key) continue;
            pack.RaisePropertyChanged(nameof(ModInstanceItem.IconSourceKey));
            pack.RaisePropertyChanged(nameof(ModInstanceItem.PngFileName));
        }
        if (SelectedMod is { } selected && (selected.PngFileName == key || selected.VersionedIconKey == key))
            selected.RaisePropertyChanged(nameof(ModItem.IconSourceKey));
        if (SelectedDllMod?.PngFileName == key || SelectedDllMod?.VersionedIconKey == key)
        {
            this.RaisePropertyChanged(nameof(SelectedDllModPngFileName));
            this.RaisePropertyChanged(nameof(SelectedDllModIconSourceKey));
        }
    }

    public string SelectedDllModIconSourceKey => SelectedDllMod?.IconSourceKey ?? string.Empty;
}
