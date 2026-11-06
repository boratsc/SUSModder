using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Storage;
using SUSModder.Tests.Support;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// Testy VM podsumowania miejsca (3.1-08) i etykiety rozmiaru kafelka (3.1-09).
/// Headless: bez Avalonia Dispatcher, bez prawdziwego systemu plików, bez kasowania.
/// </summary>
public class StorageOverviewViewModelTests
{
    // ── Fake'e ──

    private sealed class FakeBackend : IStorageOverviewBackend
    {
        public Func<StorageCleanupPlanRequest, CancellationToken, Task<StorageCleanupPlan>>? PlanAsyncImpl { get; init; }
        public int PlanCalls;

        public Task<StorageCleanupPlan> PlanAsync(StorageCleanupPlanRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref PlanCalls);
            if (PlanAsyncImpl is not null)
                return PlanAsyncImpl(request, cancellationToken);

            return Task.FromResult(EmptyPlan(request.LibraryRoot));
        }
    }

    private static StorageCleanupPlan EmptyPlan(string root) => new()
    {
        LibraryRoot = root,
        GeneratedAtUtc = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero),
        Entries = Array.Empty<StorageCleanupEntry>(),
        Categories = Array.Empty<StorageCleanupCategorySummary>()
    };

    private static StorageOverviewScope Scope(bool exists = true) => new(
        @"R:\Fake\Mods",
        new[] { @"R:\Fake\Mods\TownOfUs" },
        exists);

    private static StorageOverviewViewModel CreateVm(
        IStorageOverviewBackend backend,
        Func<StorageOverviewScope>? scopeProvider = null,
        FakeLocalizationService? loc = null)
        => new(loc ?? new FakeLocalizationService(), backend, scopeProvider ?? (() => Scope()));

    // ── Klasyfikacja = tylko pomiar ──

    [Fact]
    public async Task MeasureAsync_BuildsLocalizedCategoryRows()
    {
        var plan = new StorageCleanupPlan
        {
            LibraryRoot = @"R:\Fake\Mods",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            MeasuredBytes = 3000,
            Entries = new[]
            {
                new StorageCleanupEntry
                {
                    Id = "installation:x",
                    Category = StorageCleanupCategory.ModInstallations,
                    Path = @"R:\Fake\Mods\TownOfUs",
                    Eligibility = StorageCleanupEligibility.Protected,
                    ReasonCode = "mod_installation",
                    MeasuredBytes = 2000,
                    SizeState = StorageSizeState.Complete
                },
                new StorageCleanupEntry
                {
                    Id = "vanilla_archive:x",
                    Category = StorageCleanupCategory.VanillaArchive,
                    Path = @"R:\Fake\Mods\Among Us - Vanilla\1.0.0.7z",
                    Eligibility = StorageCleanupEligibility.Allowlisted,
                    ReasonCode = "vanilla_archive_redundant",
                    MeasuredBytes = 1000,
                    SizeState = StorageSizeState.Complete
                }
            },
            Categories = new[]
            {
                new StorageCleanupCategorySummary { Category = StorageCleanupCategory.ModInstallations, EntryCount = 1, MeasuredBytes = 2000 },
                new StorageCleanupCategorySummary { Category = StorageCleanupCategory.VanillaArchive, EntryCount = 1, MeasuredBytes = 1000 }
            }
        };

        var backend = new FakeBackend
        {
            PlanAsyncImpl = (_, _) => Task.FromResult(plan)
        };

        var vm = CreateVm(backend);
        await vm.MeasureAsync();

        Assert.Equal(2, vm.Categories.Count);

        var install = vm.Categories.Single(c => c.Category == StorageCleanupCategory.ModInstallations);
        Assert.Equal("UI.Storage.Category.ModInstallations", install.Name);
        Assert.Equal("UI.Storage.Overview.EntryCount|1", install.CountText);

        // Decyzja bezpieczeństwa 3.1: wpisy Allowlisted NIE dostają osobnej etykiety
        // „do usunięcia” — kategorie są prezentowane wyłącznie jako pomiar.
        var archive = vm.Categories.Single(c => c.Category == StorageCleanupCategory.VanillaArchive);
        Assert.Equal("UI.Storage.Eligibility.MeasurementOnly", archive.ClassificationText);
        Assert.Equal("UI.Storage.Eligibility.MeasurementOnly", install.ClassificationText);

        Assert.Contains("UI.Storage.Reason.vanilla_archive_redundant", archive.Explanation);
        Assert.Contains("UI.Storage.Reason.mod_installation", install.Explanation);

        Assert.False(vm.IsMeasuring);
        Assert.True(vm.HasFreshMeasurement);
        Assert.Equal("UI.Storage.Overview.Ready", vm.StatusText);
    }

    [Fact]
    public void ViewModel_ExposesNoRemovalOrCleanupOperations()
    {
        // Bramka bezpieczeństwa UI: widok 3.1 nie może wystawiać żadnej akcji kasującej,
        // nawet gdy bieżący snapshot Core nazwie wpisy „allowlisted”.
        var forbidden = new[] { "delete", "cleanup", "remove", "execute", "purge", "wipe", "uninstall", "reclaim", "empty" };

        var members = typeof(StorageOverviewViewModel)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            // add_/remove_ to wygenerowane akcesory zdarzeń (CloseRequested), nie API kasowania.
            .Where(n => !(n.StartsWith("add_", StringComparison.Ordinal) || n.StartsWith("remove_", StringComparison.Ordinal)))
            .ToList();

        Assert.DoesNotContain(members, name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));

        // ...i żaden typ widoku nie może wywodzić się od / zawierać wykonawcy sprzątania.
        Assert.DoesNotContain(typeof(StorageOverviewViewModel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
            f => f.FieldType.Name.Contains("Executor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Category_WithAllUnmeasuredEntries_DoesNotShowFalseZero()
    {
        var plan = new StorageCleanupPlan
        {
            LibraryRoot = @"R:\Fake\Mods",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Entries = new[]
            {
                new StorageCleanupEntry
                {
                    Id = "install_workspace:x",
                    Category = StorageCleanupCategory.InstallWorkspace,
                    Path = @"R:\Fake\Mods\temp\abc",
                    ReasonCode = "install_workspace_active_use_unprovable",
                    SizeState = StorageSizeState.Unavailable,
                    MeasuredBytes = 0
                }
            },
            Categories = new[]
            {
                new StorageCleanupCategorySummary { Category = StorageCleanupCategory.InstallWorkspace, EntryCount = 1, MeasuredBytes = 0 }
            }
        };

        var vm = CreateVm(new FakeBackend { PlanAsyncImpl = (_, _) => Task.FromResult(plan) });
        await vm.MeasureAsync();

        var row = Assert.Single(vm.Categories);
        // Niedostępność ≠ 0 B.
        Assert.Equal("UI.Storage.Overview.RowUnmeasured", row.SizeText);
        Assert.True(row.IsSizeUnknown);
        Assert.Contains("UI.Storage.Overview.RowUnmeasuredCount|1", row.Explanation);
    }

    [Fact]
    public async Task MissingLibrary_ShowExplicitState_NotZeroBytes()
    {
        var backend = new FakeBackend();
        var vm = CreateVm(backend, () => Scope(exists: false));

        await vm.MeasureAsync();

        Assert.True(vm.IsLibraryMissing);
        Assert.Empty(vm.TotalMeasuredText); // nie udajemy „0 B”
        Assert.Equal("UI.Storage.Overview.LibraryMissing", vm.StatusText);
        Assert.Equal(0, backend.PlanCalls);
        Assert.Empty(vm.Categories);
    }

    [Fact]
    public async Task PartialPlanState_ShowExplicitPartialNote()
    {
        // Core P2: plan niesie jawny stan sumy — UI pokazuje zaniżoną wartość z adnotacją.
        var plan = EmptyPlan(@"R:\Fake\Mods") with
        {
            State = StorageSizeState.Partial,
            MeasuredBytes = 10
        };

        var vm = CreateVm(new FakeBackend { PlanAsyncImpl = (_, _) => Task.FromResult(plan) });
        await vm.MeasureAsync();

        Assert.Equal("10 B", vm.TotalMeasuredText);
        Assert.Equal("UI.Storage.Overview.TotalPartial", vm.TotalNoteText);
    }

    [Fact]
    public async Task UnavailablePlanState_ShowExplanation_NotZeroBytes()
    {
        // Niedostępność to NIE „0 B” — suma zostaje pusta, wyjaśnienie idzie po kodzie błędu.
        var plan = EmptyPlan(@"R:\Fake\Mods") with
        {
            State = StorageSizeState.Unavailable,
            ErrorCode = StorageSizeErrorCode.AccessDenied,
            MeasuredBytes = 0
        };

        var vm = CreateVm(new FakeBackend { PlanAsyncImpl = (_, _) => Task.FromResult(plan) });
        await vm.MeasureAsync();

        Assert.Empty(vm.TotalMeasuredText);
        Assert.Equal("UI.Storage.Overview.TotalNotAccessible", vm.TotalNoteText);
    }

    // ── Wyścigi: stary przebieg nie nadpisuje nowszego ──

    [Fact]
    public async Task StaleRunResult_IsIgnored_NewerRunWins()
    {
        // Deterministyczny scenariusz wyścigu: przebieg #1 zostaje wygaszony przez #2
        // (anulowanie jego tokena) — jego wynik nigdy nie może nadpisać świeżego.
        var gate2 = new TaskCompletionSource<StorageCleanupPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var freshPlan = EmptyPlan(@"R:\Fake\Mods") with
        {
            Entries = new[]
            {
                new StorageCleanupEntry
                {
                    Id = "installation:new",
                    Category = StorageCleanupCategory.ModInstallations,
                    Path = @"R:\Fake\Mods\New",
                    MeasuredBytes = 42,
                    SizeState = StorageSizeState.Complete
                }
            },
            Categories = new[]
            {
                new StorageCleanupCategorySummary { Category = StorageCleanupCategory.ModInstallations, EntryCount = 1, MeasuredBytes = 42 }
            }
        };

        var backend = new FakeBackend
        {
            PlanAsyncImpl = (req, ct) =>
            {
                var n = Interlocked.Increment(ref calls);
                return n == 1
                    ? Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => EmptyPlan(req.LibraryRoot), ct)
                    : gate2.Task.WaitAsync(ct);
            }
        };

        var vm = CreateVm(backend);

        var first = vm.MeasureAsync();
        var second = vm.MeasureAsync(); // wygasza pierwszy przebieg

        gate2.TrySetResult(freshPlan);
        await Task.WhenAll(first, second);

        Assert.Single(vm.Categories); // tylko świeży przebieg opublikował wiersze
        var row = vm.Categories[0];
        Assert.Equal(StorageCleanupCategory.ModInstallations, row.Category);
        Assert.Equal("42 B", row.SizeText); // wartości ze świeżego przebiegu
        Assert.False(vm.IsMeasuring);
        Assert.True(vm.HasFreshMeasurement);
        Assert.Equal("UI.Storage.Overview.Ready", vm.StatusText);
    }

    [Fact]
    public async Task CancelDuringMeasure_SurfacesCancelledState_AndCompletes()
    {
        var never = new TaskCompletionSource<StorageCleanupPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeBackend { PlanAsyncImpl = (_, ct) => never.Task.WaitAsync(ct) };

        var vm = CreateVm(backend);
        var measure = vm.MeasureAsync();

        await Task.Yield();
        vm.CancelMeasurement();

        await measure; // nie może rzucić na zewnątrz

        Assert.False(vm.IsMeasuring);
        Assert.Equal("UI.Storage.Overview.Cancelled", vm.StatusText);
        Assert.False(vm.HasFreshMeasurement);
    }

    [Fact]
    public async Task BackendFailure_ShowStableErrorState()
    {
        var backend = new FakeBackend
        {
            PlanAsyncImpl = (_, _) => Task.FromException<StorageCleanupPlan>(new IOException("disk"))
        };

        var vm = CreateVm(backend);
        await vm.MeasureAsync();

        Assert.False(vm.IsMeasuring);
        Assert.Equal("UI.Storage.Overview.Error", vm.StatusText);
        Assert.False(vm.HasFreshMeasurement);
    }

    [Fact]
    public void RequestClose_RaisesCloseRequested()
    {
        var vm = CreateVm(new FakeBackend());
        var raised = false;
        vm.CloseRequested += (_, _) => raised = true;

        vm.RequestClose();

        Assert.True(raised);
    }

    [Fact]
    public async Task MeasureAsync_PassesScopeToBackend()
    {
        StorageCleanupPlanRequest? captured = null;
        var backend = new FakeBackend
        {
            PlanAsyncImpl = (req, ct) =>
            {
                captured = req;
                return Task.FromResult(EmptyPlan(req.LibraryRoot));
            }
        };

        var vm = CreateVm(backend);
        await vm.MeasureAsync();

        Assert.NotNull(captured);
        Assert.Equal(@"R:\Fake\Mods", captured!.LibraryRoot);
        Assert.True(captured.MeasureLibraryTotal);
        Assert.Contains(@"R:\Fake\Mods\TownOfUs", captured.KnownInstallPaths);
    }

    // ── 3.1-09: prezentacja etykiety kafelka ──

    [Fact]
    public void TilePresenter_Loading_ShowCalculatingKey()
    {
        var loc = new FakeLocalizationService();
        var result = StorageSizeResult.Loading(@"R:\Fake\Mods\TownOfUs");

        Assert.Equal("UI.Storage.Tile.Calculating", StorageTileSizePresenter.BuildText(loc, result));
    }

    [Fact]
    public void TilePresenter_EmptyDirectory_ShowsRealZeroBytes()
    {
        var loc = new FakeLocalizationService();
        var result = new StorageSizeResult
        {
            RootPath = @"R:\Fake\Mods\Empty",
            LogicalBytes = 0,
            State = StorageSizeState.Complete,
            HasMeasuredValue = true
        };

        // Pusty katalog to prawdziwe „0 B” — nie stan „niedostępny”.
        Assert.Equal("0 B", StorageTileSizePresenter.BuildText(loc, result));
    }

    [Fact]
    public void TilePresenter_PartialAndStale_ShowMarkedValues()
    {
        var loc = new FakeLocalizationService();
        loc.ChangeCulture("en");

        var partial = new StorageSizeResult
        {
            RootPath = @"R:\Fake\Mods\X",
            LogicalBytes = 1536,
            State = StorageSizeState.Partial,
            SkippedEntryCount = 2,
            HasMeasuredValue = true
        };
        var stale = new StorageSizeResult
        {
            RootPath = @"R:\Fake\Mods\X",
            LogicalBytes = 1536,
            State = StorageSizeState.Stale,
            HasMeasuredValue = true
        };

        // Wartość pozostaje widoczna, ale z jawnym oznaczeniem niepełności/nieaktualności.
        Assert.Equal("UI.Storage.Tile.Partial|1.5 KiB", StorageTileSizePresenter.BuildText(loc, partial));
        Assert.Equal("UI.Storage.Tile.Stale|1.5 KiB", StorageTileSizePresenter.BuildText(loc, stale));
    }

    [Fact]
    public void TilePresenter_UnavailableIsNeverZero()
    {
        var loc = new FakeLocalizationService();

        var missing = StorageSizeResult.Unavailable(@"R:\Fake\Mods\Gone", StorageSizeErrorCode.PathNotFound);
        var denied = StorageSizeResult.Unavailable(@"R:\Fake\Mods\Locked", StorageSizeErrorCode.AccessDenied);
        var io = StorageSizeResult.Unavailable(@"R:\Fake\Mods\X", StorageSizeErrorCode.IoError);

        Assert.Equal("UI.Storage.Tile.FolderMissing", StorageTileSizePresenter.BuildText(loc, missing));
        Assert.Equal("UI.Storage.Tile.NotAccessible", StorageTileSizePresenter.BuildText(loc, denied));
        Assert.Equal("UI.Storage.Tile.Unavailable", StorageTileSizePresenter.BuildText(loc, io));
    }

    [Fact]
    public void TilePresenter_CompleteValue_FormatsWithAppCulture()
    {
        var pl = new FakeLocalizationService(); // domyślnie "pl"
        var en = new FakeLocalizationService();
        en.ChangeCulture("en");

        var result = new StorageSizeResult
        {
            RootPath = @"R:\Fake\Mods\TownOfUs",
            LogicalBytes = 1_536,
            State = StorageSizeState.Complete,
            HasMeasuredValue = true
        };

        // PL: przecinek dziesiętny, EN: kropka; jednostki IEC (GiB, nie dziesiętne GB).
        Assert.Equal("1,5 KiB", StorageTileSizePresenter.BuildText(pl, result));
        Assert.Equal("1.5 KiB", StorageTileSizePresenter.BuildText(en, result));
    }

    [Fact]
    public void TilePresenter_UnknownCulture_FallsBackToInvariant()
    {
        var loc = new FakeLocalizationService();
        loc.ChangeCulture("!!not-a-culture!!");

        var result = new StorageSizeResult
        {
            RootPath = @"R:\Fake\Mods\TownOfUs",
            LogicalBytes = 1_536,
            State = StorageSizeState.Complete,
            HasMeasuredValue = true
        };

        Assert.Equal("1.5 KiB", StorageTileSizePresenter.BuildText(loc, result));
    }

    [Fact]
    public void TilePresenter_PathIsLink_ShowsLinkState()
    {
        // Core P2: korzeń bedący junction/symlink jest odrzucany — UI mówi wprost, nie „0 B”.
        var loc = new FakeLocalizationService();
        var link = StorageSizeResult.Unavailable(@"R:\Fake\Mods\Link", StorageSizeErrorCode.PathIsLink);

        Assert.Equal("UI.Storage.Tile.IsLink", StorageTileSizePresenter.BuildText(loc, link));
    }

    [Fact]
    public void TilePresenter_StateWithoutMeasuredValue_ShowNeutralState()
    {
        // Stan „z wartością” bez faktycznie wyznaczonej wartości (Core: HasMeasuredValue=false)
        // nie może udawać pomiaru — nawet Complete bez wartości pozostaje neutralne.
        var loc = new FakeLocalizationService();

        var fakeComplete = new StorageSizeResult
        {
            RootPath = @"R:\Fake\Mods\X",
            LogicalBytes = 999,
            State = StorageSizeState.Complete,
            HasMeasuredValue = false
        };
        var fakeStale = new StorageSizeResult
        {
            RootPath = @"R:\Fake\Mods\X",
            LogicalBytes = 999,
            State = StorageSizeState.Stale,
            HasMeasuredValue = false
        };

        Assert.Equal("UI.Storage.Tile.Unavailable", StorageTileSizePresenter.BuildText(loc, fakeComplete));
        Assert.Equal("UI.Storage.Tile.Unavailable", StorageTileSizePresenter.BuildText(loc, fakeStale));
    }
}
