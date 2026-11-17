using System;
using System.Collections.Generic;
using System.Linq;
using SUSModder.Core.Data;
using SUSModder.Core.Models;
using SUSModder.Services.Localization;
using SUSModder.Tests.Support;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// F3b: paczki zestawów na liście polecanych Discordów — mapowanie pól z backendu,
/// widoczność akcji (4 stany wiersza), wykrywanie instancji po SourcePackCode
/// oraz bezterminowość paczek (ExpiresAt = null). Czyste funkcje bez Avalonii/DI.
/// </summary>
public class DiscordServerPackTests
{
    private static DiscordServer Server(string? packCode = null, bool installable = false) => new()
    {
        Id = 1,
        Name = "Test Server",
        InviteLink = "https://discord.gg/test",
        Description = "Opis serwera",
        PackCode = packCode,
        PackModName = packCode != null ? "Town of Us" : null,
        FullModVersion = packCode != null ? "5.0.0" : null,
        Installable = installable,
        VtStatus = packCode != null ? "clean" : null
    };

    private static ModInstance Instance(string id, string? sourcePackCode, string origin = "shared_pack") => new()
    {
        InstanceId = id,
        DisplayName = "Zestaw testowy",
        SourcePackCode = sourcePackCode,
        Origin = origin
    };

    // ── Mapowanie pól paczki ──

    [Fact]
    public void MapsPackFieldsFromModel()
    {
        var vm = new DiscordServerViewModel(Server("ABCD-1234", installable: true));
        Assert.Equal("ABCD-1234", vm.PackCode);
        Assert.Equal("Town of Us", vm.PackModName);
        Assert.Equal("5.0.0", vm.FullModVersion);
        Assert.True(vm.Installable);
        Assert.Equal("clean", vm.VtStatus);
        Assert.True(vm.HasPack);
    }

    [Fact]
    public void OldBackendWithoutPackFieldsHasNoPackUi()
    {
        var vm = new DiscordServerViewModel(Server());
        Assert.False(vm.HasPack);
        Assert.Null(vm.PackCode);
        Assert.False(vm.Installable); // fail-closed
        Assert.Equal(string.Empty, vm.PackSummary);
    }

    // ── Widoczność akcji: 4 stany wiersza ──

    [Fact]
    public void NoPackCodeShowsJoinOnly()
    {
        var vm = new DiscordServerViewModel(Server());
        Assert.False(vm.ShowInstallAction);
        Assert.False(vm.ShowUpdateAction);
        Assert.False(vm.ShowPackUnavailable);
    }

    [Fact]
    public void InstallablePackWithoutInstanceShowsInstall()
    {
        var vm = new DiscordServerViewModel(Server("ABCD-1234", installable: true));
        Assert.True(vm.ShowInstallAction);
        Assert.False(vm.ShowUpdateAction);
        Assert.False(vm.ShowPackUnavailable);
    }

    [Fact]
    public void InstallablePackWithInstanceShowsUpdate()
    {
        var vm = new DiscordServerViewModel(Server("ABCD-1234", installable: true))
        {
            HasPackInstance = true
        };
        Assert.False(vm.ShowInstallAction);
        Assert.True(vm.ShowUpdateAction);
        Assert.False(vm.ShowPackUnavailable);
    }

    [Fact]
    public void NonInstallablePackShowsNeutralUnavailableWithoutInstallAction()
    {
        DiscordServerViewModel.InitializeLocalization(new FakeLocalizationService());
        var vm = new DiscordServerViewModel(Server("ABCD-1234", installable: false));
        Assert.False(vm.ShowInstallAction);
        Assert.False(vm.ShowUpdateAction);
        Assert.True(vm.ShowPackUnavailable);
        Assert.True(vm.HasDetails); // detale techniczne pod „Szczegóły"
        Assert.Equal("ModPacks.Shared.VtClean", vm.VtStatusDisplay);
    }

    [Fact]
    public void BusyServerHidesPackActions()
    {
        var vm = new DiscordServerViewModel(Server("ABCD-1234", installable: true))
        {
            IsPackBusy = true
        };
        Assert.False(vm.ShowInstallAction);
        Assert.False(vm.ShowUpdateAction);
    }

    [Fact]
    public void InstanceDetectionChangeNotifiesActionVisibility()
    {
        var vm = new DiscordServerViewModel(Server("ABCD-1234", installable: true));
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.HasPackInstance = true;

        Assert.Contains(nameof(DiscordServerViewModel.ShowInstallAction), raised);
        Assert.Contains(nameof(DiscordServerViewModel.ShowUpdateAction), raised);
        Assert.True(vm.ShowUpdateAction);
    }

    // ── Wykrywanie instancji po SourcePackCode ──

    [Fact]
    public void FindPackInstanceMatchesSourcePackCodeCaseInsensitively()
    {
        var instances = new[] { Instance("i1", "abcd-1234"), Instance("i2", "ZZZZ-9999") };
        var match = DiscordServerViewModel.FindPackInstance(instances, "ABCD-1234");
        Assert.NotNull(match);
        Assert.Equal("i1", match!.InstanceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindPackInstanceWithoutPackCodeReturnsNull(string? packCode)
    {
        var instances = new[] { Instance("i1", "ABCD-1234") };
        Assert.Null(DiscordServerViewModel.FindPackInstance(instances, packCode));
    }

    [Fact]
    public void FindPackInstanceWithoutMatchReturnsNull()
    {
        var instances = new[] { Instance("i1", "ZZZZ-9999"), Instance("i2", null) };
        Assert.Null(DiscordServerViewModel.FindPackInstance(instances, "ABCD-1234"));
    }

    // ── W4: preferencja shared_pack (klon kopiuje SourcePackCode, checker flaguje tylko shared_pack) ──

    [Fact]
    public void FindPackInstancePrefersSharedPackOverCloneListedFirst()
    {
        var instances = new[]
        {
            Instance("clone-1", "ABCD-1234", "clone"),
            Instance("shared-1", "ABCD-1234")
        };
        var match = DiscordServerViewModel.FindPackInstance(instances, "ABCD-1234");
        Assert.Equal("shared-1", match!.InstanceId);
    }

    [Fact]
    public void FindPackInstancePrefersSharedPackOverMoreRecentlyLaunchedClone()
    {
        // Kolejność z repozytorium (UpdatedAt malejąco) nie może wygrywać z origin.
        var instances = new[]
        {
            Instance("shared-1", "ABCD-1234"),
            Instance("clone-1", "ABCD-1234", "clone"),
            Instance("clone-2", "abcd-1234", "clone")
        };
        var match = DiscordServerViewModel.FindPackInstance(instances, "ABCD-1234");
        Assert.Equal("shared-1", match!.InstanceId);
    }

    [Fact]
    public void FindPackInstanceFallsBackToCloneWhenNoSharedPack()
    {
        var instances = new[] { Instance("clone-1", "ABCD-1234", "clone") };
        var match = DiscordServerViewModel.FindPackInstance(instances, "ABCD-1234");
        Assert.Equal("clone-1", match!.InstanceId);
    }

    [Fact]
    public void ApplyPackInstancesUsesRepositoryData()
    {
        var repo = new FakeInstanceRepository(new List<ModInstance>
        {
            Instance("i1", "abcd-1234"),
            Instance("i2", "ZZZZ-9999")
        });
        var withInstance = new DiscordServerViewModel(Server("ABCD-1234", installable: true));
        var withoutInstance = new DiscordServerViewModel(Server("YYYY-5678", installable: true));
        var withoutPack = new DiscordServerViewModel(Server());

        DiscordServerViewModel.ApplyPackInstances(
            new[] { withInstance, withoutInstance, withoutPack }, repo.GetPackInstances());

        Assert.True(withInstance.HasPackInstance);
        Assert.True(withInstance.ShowUpdateAction);
        Assert.False(withoutInstance.HasPackInstance);
        Assert.True(withoutInstance.ShowInstallAction);
        Assert.False(withoutPack.HasPackInstance);
    }

    // ── F3g: badge „Dostępna aktualizacja" + lista zmian z ModInstanceItem ──

    [Fact]
    public void ApplyPackInstancesMapsUpdateBadgeAndChangesFromPackItems()
    {
        var instance = Instance("i1", "ABCD-1234");
        var item = new ModInstanceItem(instance, null, 2, false)
        {
            HasUpdateAvailable = true,
            PackUpdateInfo = new ModPackUpdateInfo { InstanceId = "i1", HasUpdate = true },
            PackUpdateChangesText = "• Mod główny: 5.0.0 → 5.1.0"
        };
        var server = new DiscordServerViewModel(Server("ABCD-1234", installable: true));

        DiscordServerViewModel.ApplyPackInstances(new[] { server }, new[] { instance }, new[] { item });

        Assert.True(server.HasPackInstance);
        Assert.True(server.HasPackUpdate);
        Assert.True(server.ShowPackUpdateBadge);
        Assert.True(server.HasPackUpdateChanges);
        Assert.Equal("• Mod główny: 5.0.0 → 5.1.0", server.PackUpdateChangesText);
    }

    [Fact]
    public void ApplyPackInstancesWithoutUpdateStateClearsBadgeAndChanges()
    {
        var instance = Instance("i1", "ABCD-1234");
        var server = new DiscordServerViewModel(Server("ABCD-1234", installable: true))
        {
            HasPackUpdate = true,
            PackUpdateChangesText = "• stara zmiana"
        };

        DiscordServerViewModel.ApplyPackInstances(new[] { server }, new[] { instance });

        Assert.True(server.HasPackInstance);
        Assert.False(server.HasPackUpdate);
        Assert.False(server.ShowPackUpdateBadge);
        Assert.False(server.HasPackUpdateChanges);
        Assert.Equal(string.Empty, server.PackUpdateChangesText);
    }

    [Fact]
    public void ApplyPackInstancesFollowsSharedPackPreferenceNotClone()
    {
        var shared = Instance("shared-1", "ABCD-1234");
        var clone = Instance("clone-1", "ABCD-1234", "clone");
        var sharedItem = new ModInstanceItem(shared, null, 0, false); // aktualny
        var cloneItem = new ModInstanceItem(clone, null, 0, false)
        {
            HasUpdateAvailable = true,
            PackUpdateInfo = new ModPackUpdateInfo { InstanceId = "clone-1", HasUpdate = true }
        };
        var server = new DiscordServerViewModel(Server("ABCD-1234", installable: true));

        DiscordServerViewModel.ApplyPackInstances(
            new[] { server }, new[] { clone, shared }, new[] { cloneItem, sharedItem });

        Assert.False(server.HasPackUpdate); // checker flaguje tylko shared_pack (W4)
    }

    [Fact]
    public void UpdateBadgeHiddenWhenPackUnavailableOrBusy()
    {
        var unavailable = new DiscordServerViewModel(Server("ABCD-1234", installable: false))
        {
            HasPackUpdate = true
        };
        Assert.False(unavailable.ShowPackUpdateBadge);

        var busy = new DiscordServerViewModel(Server("ABCD-1234", installable: true))
        {
            HasPackUpdate = true,
            IsPackBusy = true
        };
        Assert.False(busy.ShowPackUpdateBadge);
        busy.IsPackBusy = false;
        Assert.True(busy.ShowPackUpdateBadge);
    }

    [Fact]
    public void HasPackUpdateChangeNotifiesBadgeVisibility()
    {
        var vm = new DiscordServerViewModel(Server("ABCD-1234", installable: true));
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.HasPackUpdate = true;

        Assert.Contains(nameof(DiscordServerViewModel.ShowPackUpdateBadge), raised);
    }

    // ── W2: badge/gate z manifestu paczki (PackUpdateInfo), nie ze sticky flagi katalogowej ──

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void HasManifestPackUpdateFollowsPackUpdateInfoNotStickyFlag(bool hasUpdate, bool expected)
    {
        var item = new ModInstanceItem(Instance("i1", "ABCD-1234"), null, 0, false)
        {
            HasUpdateAvailable = true, // sticky katalogowy sam w sobie nie wystarcza
            PackUpdateInfo = new ModPackUpdateInfo { InstanceId = "i1", HasUpdate = hasUpdate }
        };
        Assert.Equal(expected, item.HasManifestPackUpdate);
    }

    [Fact]
    public void HasManifestPackUpdateWithoutCheckResultIsFalse()
    {
        // Aktualizacja-widmo: katalog wyprzedza pin instancji (normalny stan), brak wyniku checku paczki.
        var item = new ModInstanceItem(Instance("i1", "ABCD-1234"), null, 0, false) { HasUpdateAvailable = true };
        Assert.Null(item.PackUpdateInfo);
        Assert.False(item.HasManifestPackUpdate);
    }

    [Fact]
    public void ApplyPackInstancesBadgeIgnoresStickyCatalogFlagWithoutManifest()
    {
        var instance = Instance("i1", "ABCD-1234");
        var item = new ModInstanceItem(instance, null, 0, false) { HasUpdateAvailable = true }; // bez PackUpdateInfo
        var server = new DiscordServerViewModel(Server("ABCD-1234", installable: true));

        DiscordServerViewModel.ApplyPackInstances(new[] { server }, new[] { instance }, new[] { item });

        Assert.True(server.HasPackInstance);
        Assert.False(server.HasPackUpdate);
        Assert.False(server.ShowPackUpdateBadge);
    }

    [Fact]
    public void ApplyPackInstancesBadgeShownWhenManifestHasUpdateEvenWithoutStickyFlag()
    {
        var instance = Instance("i1", "ABCD-1234");
        var item = new ModInstanceItem(instance, null, 0, false)
        {
            HasUpdateAvailable = false,
            PackUpdateInfo = new ModPackUpdateInfo { InstanceId = "i1", HasUpdate = true }
        };
        var server = new DiscordServerViewModel(Server("ABCD-1234", installable: true));

        DiscordServerViewModel.ApplyPackInstances(new[] { server }, new[] { instance }, new[] { item });

        Assert.True(server.HasPackUpdate);
        Assert.True(server.ShowPackUpdateBadge);
    }

    // ── Podsumowanie paczki (nazwa + wersja) ──

    [Fact]
    public void PackSummaryFormatsNameAndVersion()
    {
        var vm = new DiscordServerViewModel(Server("ABCD-1234", installable: true));
        Assert.Equal("Town of Us · v5.0.0", vm.PackSummary);
    }

    [Fact]
    public void PackSummaryWithoutVersionShowsNameOnly()
    {
        var model = Server("ABCD-1234", installable: true);
        model.FullModVersion = null;
        var vm = new DiscordServerViewModel(model);
        Assert.Equal("Town of Us", vm.PackSummary);
    }

    [Fact]
    public void PackSummaryWithoutNameFallsBackToPackCode()
    {
        var model = Server("ABCD-1234", installable: true);
        model.PackModName = null;
        var vm = new DiscordServerViewModel(model);
        Assert.Equal("ABCD-1234 · v5.0.0", vm.PackSummary);
    }

    // ── Bezterminowość (ExpiresAt = null) ──

    [Fact]
    public void SharedPackItemWithoutExpiryUsesNeverExpiresKey()
    {
        var entry = new ModPackListEntry { PackCode = "ABCD-1234", ExpiresAt = null };
        var item = new SharedModPackItem(entry, new FakeLocalizationService());
        Assert.Equal("ModPacks.NeverExpires", item.ExpiresAtText);
    }

    [Fact]
    public void SharedPackItemWithExpiryShowsDate()
    {
        var entry = new ModPackListEntry
        {
            PackCode = "ABCD-1234",
            ExpiresAt = new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero)
        };
        var item = new SharedModPackItem(entry, new FakeLocalizationService());
        Assert.NotEqual("ModPacks.NeverExpires", item.ExpiresAtText);
        Assert.Contains("2026-12-01", item.ExpiresAtText);
    }

    [Fact]
    public void PreviewExpiryLineWithoutExpiryUsesNeverExpiresKey()
    {
        var line = ModPackPreviewViewModel.FormatExpiryLine(null, new FakeLocalizationService());
        Assert.Equal("ModPacks.ExpiresAt: ModPacks.NeverExpires", line);
    }

    [Fact]
    public void PreviewExpiryLineWithExpiryContainsDate()
    {
        var expiresAt = new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);
        var line = ModPackPreviewViewModel.FormatExpiryLine(expiresAt, new FakeLocalizationService());
        Assert.StartsWith("ModPacks.ExpiresAt: ", line);
        Assert.DoesNotContain("ModPacks.NeverExpires", line);
        Assert.Contains("2026", line);
    }

    [Fact]
    public void NeverExpiresIsLocalizedInPlAndEn()
    {
        var loc = new LocalizationService();
        Assert.Equal("bezterminowa", loc.Get("ModPacks.NeverExpires"));
        loc.ChangeCulture("en");
        Assert.Equal("never expires", loc.Get("ModPacks.NeverExpires"));
    }

    [Fact]
    public void UpdateConfirmMessageStatesTouOverwriteAndAddonRetentionInBothLocales()
    {
        var loc = new LocalizationService();
        var pl = loc.Get("RecommendedDiscords.UpdateConfirmMessage");
        Assert.Contains("ToU", pl);
        Assert.Contains("zastąpią Twoje lokalne ustawienia", pl);
        Assert.Contains("zachowane", pl);
        loc.ChangeCulture("en");
        var en = loc.Get("RecommendedDiscords.UpdateConfirmMessage");
        Assert.Contains("ToU", en);
        Assert.Contains("replace your local settings", en);
        Assert.Contains("kept", en);
    }

    // ── I2: potwierdzenie aktualizacji z nazwą zestawu i serwera (placeholders PL/EN zgodne) ──

    [Fact]
    public void UpdateConfirmMessageFormatsPackAndServerNamesInBothLocales()
    {
        var loc = new LocalizationService();
        var pl = loc.GetFormatted("RecommendedDiscords.UpdateConfirmMessage", "Town of Us", "Among Us Polska");
        Assert.Contains("Town of Us", pl);
        Assert.Contains("Among Us Polska", pl);
        Assert.DoesNotContain("{0}", pl);
        Assert.DoesNotContain("{1}", pl);
        loc.ChangeCulture("en");
        var en = loc.GetFormatted("RecommendedDiscords.UpdateConfirmMessage", "Town of Us", "Among Us Polska");
        Assert.Contains("Town of Us", en);
        Assert.Contains("Among Us Polska", en);
        Assert.DoesNotContain("{0}", en);
        Assert.DoesNotContain("{1}", en);
    }

    // ── F3f: tooltip akcji zestawu (nazwa serwera, PLACEHOLDER {0} zgodny PL/EN) ──

    [Fact]
    public void PackActionTooltipFormatsServerNameThroughLocalizationKey()
    {
        DiscordServerViewModel.InitializeLocalization(new FakeLocalizationService());
        var vm = new DiscordServerViewModel(Server("ABCD-1234", installable: true));
        Assert.Equal("RecommendedDiscords.PackActionTooltip|Test Server", vm.PackActionTooltip);
    }

    [Fact]
    public void PackActionTooltipIsLocalizedInPlAndEn()
    {
        var loc = new LocalizationService();
        DiscordServerViewModel.InitializeLocalization(loc);
        var vm = new DiscordServerViewModel(Server("ABCD-1234", installable: true));
        Assert.Equal("Zainstalowany zostanie zestaw modów używany przez serwer Test Server.", vm.PackActionTooltip);
        loc.ChangeCulture("en");
        vm.RefreshLocalization();
        Assert.Equal("The mod set used by server Test Server will be installed.", vm.PackActionTooltip);
    }

    [Fact]
    public void RefreshLocalizationNotifiesPackActionTooltip()
    {
        DiscordServerViewModel.InitializeLocalization(new FakeLocalizationService());
        var vm = new DiscordServerViewModel(Server("ABCD-1234", installable: true));
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.RefreshLocalization();

        Assert.Contains(nameof(DiscordServerViewModel.PackActionTooltip), raised);
    }

    // ── I1: status VT przez wspólne klucze (bez surowych „suspicious"/„pending" w PL UI) ──

    [Theory]
    [InlineData("clean", "ModPacks.Shared.VtClean")]
    [InlineData("pending", "ModPacks.Shared.VtPending")]
    [InlineData("suspicious", "ModPacks.Shared.VtSuspicious")]
    [InlineData("SOMETHING_NEW", "ModPacks.Shared.VtUnknown")]
    public void VtStatusDisplayUsesSharedPackLocalizationKeys(string status, string expectedKey)
    {
        DiscordServerViewModel.InitializeLocalization(new FakeLocalizationService());
        var model = Server("ABCD-1234");
        model.VtStatus = status;
        var vm = new DiscordServerViewModel(model);
        Assert.Equal(expectedKey, vm.VtStatusDisplay);
    }

    [Fact]
    public void VtStatusDisplayWithoutStatusShowsDash()
    {
        DiscordServerViewModel.InitializeLocalization(new FakeLocalizationService());
        var model = Server("ABCD-1234");
        model.VtStatus = null;
        var vm = new DiscordServerViewModel(model);
        Assert.Equal("—", vm.VtStatusDisplay);
    }

    [Fact]
    public void VtStatusDisplayIsLocalizedInPlAndEn()
    {
        var loc = new LocalizationService();
        DiscordServerViewModel.InitializeLocalization(loc);
        var model = Server("ABCD-1234");
        model.VtStatus = "suspicious";
        var vm = new DiscordServerViewModel(model);
        Assert.Equal("VT: podejrzana", vm.VtStatusDisplay);
        loc.ChangeCulture("en");
        vm.RefreshLocalization();
        Assert.Equal("VT: suspicious", vm.VtStatusDisplay);
    }

    // ── I4: neutralny toast błędu akcji zestawu (klucz istnieje w PL i EN) ──

    [Fact]
    public void PackActionFailedIsLocalizedInPlAndEn()
    {
        var loc = new LocalizationService();
        var pl = loc.Get("RecommendedDiscords.PackActionFailed");
        Assert.False(string.IsNullOrWhiteSpace(pl));
        Assert.NotEqual("RecommendedDiscords.PackActionFailed", pl);
        Assert.DoesNotContain("[RecommendedDiscords", pl);
        loc.ChangeCulture("en");
        var en = loc.Get("RecommendedDiscords.PackActionFailed");
        Assert.False(string.IsNullOrWhiteSpace(en));
        Assert.NotEqual(pl, en);
        Assert.DoesNotContain("[RecommendedDiscords", en);
    }

    // ── W2: linia TTL ukryta dla paczek bezterminowych (ExpiresAt = null) ──

    [Fact]
    public void PreviewTtlLineHiddenWhenPackNeverExpires()
    {
        // Parser fallbackuje TtlDays=30 przy braku daty — linia TTL nie może przeczy „bezterminowa".
        Assert.Null(ModPackPreviewViewModel.FormatTtlLine(null, 30, new FakeLocalizationService()));
    }

    [Fact]
    public void PreviewTtlLineShownWhenPackHasExpiry()
    {
        var expiresAt = new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);
        var line = ModPackPreviewViewModel.FormatTtlLine(expiresAt, 30, new FakeLocalizationService());
        Assert.Equal("ModPacks.TtlLabel: 30 ModPacks.TtlDaysSuffix", line);
    }

    [Fact]
    public void PreviewTtlLineHiddenWhenTtlNotPositive()
    {
        var expiresAt = new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Null(ModPackPreviewViewModel.FormatTtlLine(expiresAt, 0, new FakeLocalizationService()));
    }

    /// <summary>Minimalny fake repozytorium instancji — tylko odczyt na potrzeby testów projekcji.</summary>
    private sealed class FakeInstanceRepository : IModInstanceRepository
    {
        private readonly List<ModInstance> _instances;
        public FakeInstanceRepository(List<ModInstance> instances) => _instances = instances;

        public List<ModInstance> GetAllInstances() => _instances;
        public List<ModInstance> GetPackInstances() => _instances;
        public ModInstance? GetInstance(string instanceId) => _instances.FirstOrDefault(i => i.InstanceId == instanceId);

        public List<ModInstance> GetInstancesForBaseMod(int baseModId) => throw new NotSupportedException();
        public void AddInstance(ModInstance instance) => throw new NotSupportedException();
        public void UpdateInstance(ModInstance instance) => throw new NotSupportedException();
        public void DeleteInstance(string instanceId) => throw new NotSupportedException();
        public void RenameInstance(string instanceId, string displayName) => throw new NotSupportedException();
        public void UpdateLastLaunched(string instanceId) => throw new NotSupportedException();
        public List<ModInstanceDll> GetDlls(string instanceId) => throw new NotSupportedException();
        public long AddDll(ModInstanceDll dll) => throw new NotSupportedException();
        public void RemoveDll(long id) => throw new NotSupportedException();
        public List<ModInstanceConfig> GetConfigs(string instanceId) => throw new NotSupportedException();
        public long AddConfig(ModInstanceConfig config) => throw new NotSupportedException();
        public void UpdateConfig(ModInstanceConfig config) => throw new NotSupportedException();
        public void DeleteConfig(long id) => throw new NotSupportedException();
    }
}
