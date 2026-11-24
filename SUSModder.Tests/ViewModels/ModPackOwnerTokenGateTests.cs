using System;
using System.Collections.Generic;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;
using SUSModder.Core.Utilities;
using SUSModder.Services.Localization;
using SUSModder.Tests.Support;
using SUSModder.ViewModels;
using SUSModder.ViewModels.ModPackWizard;
using Xunit;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// Kontrakt 02.8 (ownerToken): gating akcji zapisu bez żądań do serwera,
/// tri-state Available/Missing/Unreadable, neutralne komunikaty PL/EN z datą i bez,
/// precyzja 403 PACK_NOT_MANAGEABLE (ownerToken vs legacy), reguły ukrycia „Ponów”
/// oraz mapowanie OWNER_TOKEN_STORE_FAILED / PACK_NOT_MANAGEABLE na klucze i18n.
/// </summary>
public class ModPackOwnerTokenGateTests
{
    private const string PackCode = "ABCD-1234";
    private static readonly DateTimeOffset ExpiresAt = new(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);

    private static ModPackListEntry Entry(string? ownershipMode, DateTimeOffset? expiresAt = null) => new()
    {
        PackCode = PackCode,
        ModName = "Town of Us",
        OwnershipMode = ownershipMode,
        ExpiresAt = expiresAt,
        Active = true
    };

    // ── Czysta reguła gatingu (tri-state) ──

    [Theory]
    [InlineData(null, OwnerTokenState.Missing, false)]        // nieznany tryb → legacy, bez blokady
    [InlineData("legacy", OwnerTokenState.Missing, false)]
    [InlineData("legacy", OwnerTokenState.Unreadable, false)]
    [InlineData("ownerToken", OwnerTokenState.Available, false)]
    [InlineData("ownerToken", OwnerTokenState.Missing, true)]
    [InlineData("ownerToken", OwnerTokenState.Unreadable, true)]
    [InlineData("OWNERTOKEN", OwnerTokenState.Missing, true)] // case-insensitive
    public void IsWriteBlockedFollowsOwnershipModeAndState(string? mode, OwnerTokenState state, bool expected) =>
        Assert.Equal(expected, ModPackOwnerTokenGate.IsWriteBlocked(mode, state));

    [Theory]
    [InlineData(null, OwnerTokenState.Missing, null)]
    [InlineData("legacy", OwnerTokenState.Unreadable, null)]
    [InlineData("ownerToken", OwnerTokenState.Available, null)]
    [InlineData("ownerToken", OwnerTokenState.Unreadable, "ModPacks.Shared.OwnerTokenUnreadable")]
    [InlineData("ownerToken", OwnerTokenState.Missing, "ModPacks.Shared.OwnerTokenLost")]
    public void WriteBlockedMessageMapsStateToKey(string? mode, OwnerTokenState state, string? expected) =>
        Assert.Equal(expected, ModPackOwnerTokenGate.WriteBlockedMessage(mode, state, null, new FakeLocalizationService()));

    // ── Kreator mid-flow: nieodwracalna utrata (Missing) vs przejściowa (Unreadable → ponów) ──

    [Theory]
    [InlineData(null, OwnerTokenState.Missing, false)]        // legacy/null → bez nieodwracalnej utraty
    [InlineData("legacy", OwnerTokenState.Unreadable, false)]
    [InlineData("ownerToken", OwnerTokenState.Available, false)]
    [InlineData("ownerToken", OwnerTokenState.Unreadable, false)] // przejściowe → „Ponów” zostaje
    [InlineData("ownerToken", OwnerTokenState.Missing, true)]     // nieodwracalne → ukryj „Ponów”
    [InlineData("OWNERTOKEN", OwnerTokenState.Missing, true)]     // case-insensitive
    public void IsIrreversibleWriteLossDistinguishesMissingFromUnreadable(string? mode, OwnerTokenState state, bool expected) =>
        Assert.Equal(expected, ModPackOwnerTokenGate.IsIrreversibleWriteLoss(mode, state));

    [Fact]
    public void WizardUnreadableMidFlowKeepsRetryAndNeutralMessage()
    {
        // Przejściowy błąd odczytu magazynu w trakcie kreatora: NIE trwała utrata, „Ponów” widoczny.
        const string mode = "ownerToken";
        const OwnerTokenState state = OwnerTokenState.Unreadable;
        var loss = ModPackOwnerTokenGate.IsIrreversibleWriteLoss(mode, state);
        Assert.False(loss);
        Assert.Equal("ModPacks.Shared.OwnerTokenUnreadable",
            ModPackOwnerTokenGate.WriteBlockedMessage(mode, state, ExpiresAt, new FakeLocalizationService()));
        Assert.True(ModPackOwnerTokenGate.ShouldShowRetry(settled: true, allDone: false, stageCount: 1, irreversibleWriteLoss: loss));
    }

    [Fact]
    public void WizardMissingMidFlowHidesRetryWithContractMessage()
    {
        const string mode = "ownerToken";
        const OwnerTokenState state = OwnerTokenState.Missing;
        var loss = ModPackOwnerTokenGate.IsIrreversibleWriteLoss(mode, state);
        Assert.True(loss);
        Assert.StartsWith("ModPacks.Shared.OwnerTokenLostWithDate|",
            ModPackOwnerTokenGate.WriteBlockedMessage(mode, state, ExpiresAt, new FakeLocalizationService()));
        Assert.False(ModPackOwnerTokenGate.ShouldShowRetry(settled: true, allDone: false, stageCount: 1, irreversibleWriteLoss: loss));
    }

    [Fact]
    public void WizardAvailableMidFlowIsNotBlocked()
    {
        Assert.False(ModPackOwnerTokenGate.IsWriteBlocked("ownerToken", OwnerTokenState.Available));
        Assert.False(ModPackOwnerTokenGate.IsIrreversibleWriteLoss("ownerToken", OwnerTokenState.Available));
        Assert.Null(ModPackOwnerTokenGate.WriteBlockedMessage("ownerToken", OwnerTokenState.Available, ExpiresAt, new FakeLocalizationService()));
    }

    // ── SharedModPackItem: widoczność akcji zapisu ──

    [Fact]
    public void LegacyPackCanManage()
    {
        var item = new SharedModPackItem(Entry("legacy"), new FakeLocalizationService(), _ => OwnerTokenState.Missing);
        Assert.True(item.CanManage);
        Assert.False(item.IsWriteBlocked);
        Assert.Null(item.WriteBlockedMessage);
    }

    [Fact]
    public void UnknownOwnershipModeTreatedAsLegacy()
    {
        var item = new SharedModPackItem(Entry(null), new FakeLocalizationService(), _ => OwnerTokenState.Missing);
        Assert.True(item.CanManage);
    }

    [Fact]
    public void OwnerTokenWithTokenCanManage()
    {
        var item = new SharedModPackItem(Entry("ownerToken", ExpiresAt), new FakeLocalizationService(), _ => OwnerTokenState.Available);
        Assert.True(item.CanManage);
        Assert.Null(item.WriteBlockedMessage);
    }

    [Fact]
    public void OwnerTokenWithoutTokenIsWriteBlockedWithDateMessage()
    {
        var item = new SharedModPackItem(Entry("ownerToken", ExpiresAt), new FakeLocalizationService(), _ => OwnerTokenState.Missing);
        Assert.False(item.CanManage);
        Assert.True(item.IsWriteBlocked);
        Assert.StartsWith("ModPacks.Shared.OwnerTokenLostWithDate|", item.WriteBlockedMessage);
        // Format daty spójny z ExpiresAtText (yyyy-MM-dd HH:mm, czas lokalny).
        Assert.Contains(ExpiresAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"), item.WriteBlockedMessage);
    }

    [Fact]
    public void OwnerTokenWithoutTokenAndWithoutExpiryUsesBaseContractMessage()
    {
        // Paczka osobista ma zawsze TTL (7/30/90 dni) — ExpiresAt null = data nieznana, NIE bezterminowa.
        // Dlatego bazowy tekst kontraktu (ze zdaniem o wygaśnięciu), bez wariantu NoExpiry.
        var item = new SharedModPackItem(Entry("ownerToken", null), new FakeLocalizationService(), _ => OwnerTokenState.Missing);
        Assert.False(item.CanManage);
        Assert.Equal("ModPacks.Shared.OwnerTokenLost", item.WriteBlockedMessage);
    }

    [Fact]
    public void OwnerTokenUnreadableShowsNeutralRetryMessage()
    {
        // Błąd przejściowy odczytu magazynu: neutralny komunikat, akcje do ponowienia (nie trwała utrata).
        var item = new SharedModPackItem(Entry("ownerToken", ExpiresAt), new FakeLocalizationService(), _ => OwnerTokenState.Unreadable);
        Assert.False(item.CanManage);
        Assert.True(item.IsWriteBlocked);
        Assert.Equal("ModPacks.Shared.OwnerTokenUnreadable", item.WriteBlockedMessage);
    }

    [Fact]
    public void OwnerTokenWithoutTokenProbeFailsClosed()
    {
        var item = new SharedModPackItem(Entry("ownerToken", ExpiresAt), new FakeLocalizationService());
        Assert.False(item.CanManage);
    }

    // ── SharedModPacksViewModel: refresh + delete bez żądania ──

    [Fact]
    public async Task RefreshGatesEachPackByLocalToken()
    {
        var service = new FakeModPackService
        {
            ListResult = new ModPackListResult
            {
                Success = true,
                Packs = new[]
                {
                    Entry("legacy"),
                    new ModPackListEntry { PackCode = "ZZZZ-9999", OwnershipMode = "ownerToken", Active = true },
                    new ModPackListEntry { PackCode = "YYYY-5678", OwnershipMode = "ownerToken", Active = true }
                },
                ActiveCount = 3
            }
        };
        service.Tokens.Add(PackCode); // legacy nie potrzebuje tokenu; YYYY-5678 ma token
        service.Tokens.Add("YYYY-5678");
        var vm = CreateVm(service, out _);

        await vm.RefreshAsync();

        Assert.Equal(3, vm.Packs.Count);
        Assert.True(vm.Packs[0].CanManage);  // legacy
        Assert.False(vm.Packs[1].CanManage); // ownerToken bez tokenu
        Assert.True(vm.Packs[2].CanManage);  // ownerToken z tokenem
    }

    [Fact]
    public async Task RefreshReadsTokenStatesOnceNotPerRow()
    {
        // Wydajność: migawka stanów czytana RAZ przy refresh (poza wątkiem UI), nie per wiersz.
        var service = new FakeModPackService
        {
            ListResult = new ModPackListResult
            {
                Success = true,
                Packs = new[]
                {
                    new ModPackListEntry { PackCode = "AAAA-1111", OwnershipMode = "ownerToken", Active = true },
                    new ModPackListEntry { PackCode = "BBBB-2222", OwnershipMode = "ownerToken", Active = true },
                    new ModPackListEntry { PackCode = "CCCC-3333", OwnershipMode = "ownerToken", Active = true }
                },
                ActiveCount = 3
            }
        };
        var vm = CreateVm(service, out _);

        await vm.RefreshAsync();

        Assert.Equal(1, service.GetOwnerTokenStatesCalls);
        Assert.Equal(3, vm.Packs.Count);
    }

    [Fact]
    public async Task RefreshUnreadablePackShowsNeutralMessage()
    {
        var service = new FakeModPackService
        {
            ListResult = new ModPackListResult
            {
                Success = true,
                Packs = new[] { Entry("ownerToken", ExpiresAt) },
                ActiveCount = 1
            }
        };
        service.States[PackCode] = OwnerTokenState.Unreadable;
        var vm = CreateVm(service, out _);

        await vm.RefreshAsync();

        var item = Assert.Single(vm.Packs);
        Assert.False(item.CanManage);
        Assert.Equal("ModPacks.Shared.OwnerTokenUnreadable", item.WriteBlockedMessage);
    }

    [Fact]
    public async Task DeleteBlockedPackSendsNoRequestAndNoConfirmation()
    {
        var service = new FakeModPackService
        {
            ListResult = new ModPackListResult
            {
                Success = true,
                Packs = new[] { Entry("ownerToken", ExpiresAt) },
                ActiveCount = 1
            }
        };
        var vm = CreateVm(service, out var dialogs);
        await vm.RefreshAsync();

        await vm.DeleteCommand.Execute(vm.Packs[0]).ToTask();

        Assert.Empty(service.DeleteCalls);
        Assert.Empty(dialogs.Confirmations);
        Assert.Empty(dialogs.Errors);
        Assert.Single(vm.Packs); // nic nie usunięto
    }

    [Fact]
    public async Task DeleteUnreadablePackSendsNoRequest()
    {
        var service = new FakeModPackService
        {
            ListResult = new ModPackListResult
            {
                Success = true,
                Packs = new[] { Entry("ownerToken", ExpiresAt) },
                ActiveCount = 1
            }
        };
        service.States[PackCode] = OwnerTokenState.Unreadable;
        var vm = CreateVm(service, out var dialogs);
        await vm.RefreshAsync();

        await vm.DeleteCommand.Execute(vm.Packs[0]).ToTask();

        Assert.Empty(service.DeleteCalls);
        Assert.Empty(dialogs.Confirmations);
    }

    [Fact]
    public async Task DeleteLegacyPackStillWorks()
    {
        var service = new FakeModPackService
        {
            ListResult = new ModPackListResult
            {
                Success = true,
                Packs = new[] { Entry("legacy") },
                ActiveCount = 1
            },
            DeleteResult = new ModPackDeleteResult { Success = true }
        };
        var vm = CreateVm(service, out var dialogs);
        await vm.RefreshAsync();

        await vm.DeleteCommand.Execute(vm.Packs[0]).ToTask();

        Assert.Equal(new[] { PackCode }, service.DeleteCalls);
        Assert.Empty(vm.Packs);
        Assert.Single(dialogs.Messages);
    }

    [Fact]
    public async Task DeletePackNotManageableOwnerTokenMapsToTokenLostMessage()
    {
        // ownerToken z tokenem (command dozwolony), ale serwer zwraca 403 → komunikat utraty tokenu, nie surowy błąd.
        var service = new FakeModPackService
        {
            ListResult = new ModPackListResult
            {
                Success = true,
                Packs = new[] { Entry("ownerToken", ExpiresAt) },
                ActiveCount = 1
            },
            DeleteResult = new ModPackDeleteResult { Success = false, ErrorCode = "PACK_NOT_MANAGEABLE", ErrorMessage = "surowy komunikat serwera" }
        };
        service.Tokens.Add(PackCode);
        var vm = CreateVm(service, out var dialogs);
        await vm.RefreshAsync();

        await vm.DeleteCommand.Execute(vm.Packs[0]).ToTask();

        var error = Assert.Single(dialogs.Errors);
        Assert.StartsWith("ModPacks.Shared.OwnerTokenLostWithDate|", error);
        Assert.DoesNotContain("surowy", error);
    }

    [Fact]
    public async Task DeletePackNotManageableLegacyMapsToNeutralMessage()
    {
        // legacy/null → neutralny brak uprawnień, bez straszenia utratą tokenu.
        var service = new FakeModPackService
        {
            ListResult = new ModPackListResult
            {
                Success = true,
                Packs = new[] { Entry("legacy", ExpiresAt) },
                ActiveCount = 1
            },
            DeleteResult = new ModPackDeleteResult { Success = false, ErrorCode = "PACK_NOT_MANAGEABLE", ErrorMessage = "surowy komunikat serwera" }
        };
        var vm = CreateVm(service, out var dialogs);
        await vm.RefreshAsync();

        await vm.DeleteCommand.Execute(vm.Packs[0]).ToTask();

        var error = Assert.Single(dialogs.Errors);
        Assert.Equal("ModPacks.NotManageable", error);
        Assert.DoesNotContain("surowy", error);
    }

    // ── 403 PACK_NOT_MANAGEABLE: precyzja ownerToken vs legacy ──

    [Theory]
    [InlineData("ownerToken", "ModPacks.Shared.OwnerTokenLost")]
    [InlineData("OWNERTOKEN", "ModPacks.Shared.OwnerTokenLost")]
    [InlineData("legacy", "ModPacks.NotManageable")]
    [InlineData(null, "ModPacks.NotManageable")]
    public void NotManageableMessageDistinguishesOwnershipMode(string? mode, string expected) =>
        Assert.Equal(expected, ModPackOwnerTokenGate.NotManageableMessage(mode, null, new FakeLocalizationService()));

    [Fact]
    public void NotManageableMessageOwnerTokenWithDateUsesTokenLost() =>
        Assert.StartsWith("ModPacks.Shared.OwnerTokenLostWithDate|",
            ModPackOwnerTokenGate.NotManageableMessage("ownerToken", ExpiresAt, new FakeLocalizationService()));

    // ── Reguły ukrycia „Ponów” / wskazówki finalizacji (jeden przycisk na problem) ──

    [Theory]
    [InlineData(true, false, 1, false, true)]   // settled, nie allDone, są etapy, brak utraty → ponów
    [InlineData(true, false, 1, true, false)]   // nieodwracalna utrata tokenu → ukryj ponów
    [InlineData(true, true, 1, false, false)]   // wszystko gotowe → nie ma czego ponawiać
    [InlineData(false, false, 1, false, false)] // trwa wykonanie → nie pokazuj ponów
    [InlineData(true, false, 0, false, false)]  // brak etapów → nie pokazuj ponów
    public void ShouldShowRetryRule(bool settled, bool allDone, int stages, bool loss, bool expected) =>
        Assert.Equal(expected, ModPackOwnerTokenGate.ShouldShowRetry(settled, allDone, stages, loss));

    [Theory]
    [InlineData(true, false, true)]   // paczka na serwerze, brak utraty → wskazówka ponowienia
    [InlineData(true, true, false)]   // utrata tokenu → bez wskazówki (nie ma czego ponawiać)
    [InlineData(false, false, false)] // paczka nie powstała → bez wskazówki
    public void ShouldShowFinalizeRetryHintRule(bool packCreated, bool loss, bool expected) =>
        Assert.Equal(expected, ModPackOwnerTokenGate.ShouldShowFinalizeRetryHint(packCreated, loss));

    // ── Komunikaty: klucze, placeholdery, dokładna treść PL, brak żargonu ──

    [Fact]
    public void TokenLostBaseMessageHasExactContractPolishText()
    {
        var loc = new LocalizationService();
        const string contract = "Nie można już zmienić tej paczki na tym urządzeniu. Paczka wygaśnie zgodnie z ustawionym terminem. Nie musisz nic robić.";
        Assert.Equal(contract, loc.Get("ModPacks.Shared.OwnerTokenLost"));
        // Tekst kontraktu jest osiągalny w runtime: ExpiresAt null (data nieznana) → bazowy klucz.
        Assert.Equal(contract, ModPackOwnerTokenGate.TokenLostMessage(null, loc));
    }

    [Theory]
    [InlineData("ModPacks.Shared.OwnerTokenLost")]
    [InlineData("ModPacks.Shared.OwnerTokenLostWithDate")]
    [InlineData("ModPacks.Shared.OwnerTokenUnreadable")]
    [InlineData("ModPacks.OwnerTokenStoreFailed")]
    [InlineData("ModPacks.OwnerTokenStoreFailedWithDate")]
    [InlineData("ModPacks.OwnerTokenStoreFailedDetails")]
    [InlineData("ModPacks.NotManageable")]
    public void MessagesExistInPlAndEnWithoutJargon(string key)
    {
        var loc = new LocalizationService();
        var pl = loc.Get(key);
        loc.ChangeCulture("en");
        var en = loc.Get(key);

        Assert.NotEqual(key, pl);
        Assert.NotEqual(key, en);
        Assert.NotEqual(pl, en);
        foreach (var text in new[] { pl, en })
        {
            Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DPAPI", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("manifest", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("ModPacks.Shared.OwnerTokenLostWithDate")]
    [InlineData("ModPacks.OwnerTokenStoreFailedWithDate")]
    public void DateVariantsHaveMatchingPlaceholdersInPlAndEn(string key)
    {
        var loc = new LocalizationService();
        var pl = loc.Get(key);
        loc.ChangeCulture("en");
        var en = loc.Get(key);
        Assert.Contains("{0}", pl);
        Assert.Contains("{0}", en);
        Assert.DoesNotContain("{1}", pl);
        Assert.DoesNotContain("{1}", en);
    }

    [Fact]
    public void TokenLostMessageWithDateFormatsThroughLocalization()
    {
        var msg = ModPackOwnerTokenGate.TokenLostMessage(ExpiresAt, new FakeLocalizationService());
        Assert.Equal($"ModPacks.Shared.OwnerTokenLostWithDate|{ExpiresAt.LocalDateTime:yyyy-MM-dd HH:mm}", msg);
    }

    [Fact]
    public void StoreFailedMessageUsesNeutralKeys()
    {
        var fake = new FakeLocalizationService();
        Assert.StartsWith("ModPacks.OwnerTokenStoreFailedWithDate|", ModPackOwnerTokenGate.StoreFailedMessage(ExpiresAt, fake));
        // ExpiresAt null → bazowy klucz (data nieznana, TTL zawsze istnieje), bez wariantu NoExpiry.
        Assert.Equal("ModPacks.OwnerTokenStoreFailed", ModPackOwnerTokenGate.StoreFailedMessage(null, fake));
    }

    [Fact]
    public void StoreFailedMessageWithDateIsLocalizedInPlAndEn()
    {
        var loc = new LocalizationService();
        var pl = ModPackOwnerTokenGate.StoreFailedMessage(ExpiresAt, loc);
        Assert.Contains("Paczka powstała", pl);
        Assert.Contains(ExpiresAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"), pl);
        Assert.DoesNotContain("{0}", pl);
        loc.ChangeCulture("en");
        var en = ModPackOwnerTokenGate.StoreFailedMessage(ExpiresAt, loc);
        Assert.Contains("The pack was created", en);
        Assert.DoesNotContain("{0}", en);
    }

    // ── Helpers ──

    private sealed class DialogLog
    {
        public List<string> Confirmations { get; } = new();
        public List<string> Messages { get; } = new();
        public List<string> Errors { get; } = new();
    }

    private static SharedModPacksViewModel CreateVm(FakeModPackService service, out DialogLog dialogs)
    {
        var log = new DialogLog();
        dialogs = log;
        return new SharedModPacksViewModel(
            service,
            new FakeLocalizationService(),
            confirmAsync: (title, msg, yes, no, _) => { log.Confirmations.Add(msg); return Task.FromResult(true); },
            showMessageAsync: (title, msg) => { log.Messages.Add(msg); return Task.CompletedTask; },
            showErrorAsync: (msg, title) => { log.Errors.Add(msg); return Task.CompletedTask; },
            runOnUi: action => action());
    }

    /// <summary>Fake IModPackService — tylko powierzchnie potrzebne liście „Udostępnione”.</summary>
    private sealed class FakeModPackService : IModPackService
    {
        public ModPackListResult ListResult { get; set; } = new() { Success = true };
        public ModPackDeleteResult DeleteResult { get; set; } = new() { Success = true };
        public HashSet<string> Tokens { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, OwnerTokenState> States { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int GetOwnerTokenStatesCalls { get; private set; }
        public List<string> DeleteCalls { get; } = new();

        public bool HasOwnerToken(string packCode) => GetOwnerTokenState(packCode) == OwnerTokenState.Available;

        public OwnerTokenState GetOwnerTokenState(string packCode) =>
            States.TryGetValue(packCode, out var state)
                ? state
                : Tokens.Contains(packCode) ? OwnerTokenState.Available : OwnerTokenState.Missing;

        public IReadOnlyDictionary<string, OwnerTokenState> GetOwnerTokenStates(IEnumerable<string> packCodes)
        {
            GetOwnerTokenStatesCalls++;
            var states = new Dictionary<string, OwnerTokenState>(StringComparer.Ordinal);
            foreach (var code in packCodes)
                states[ModPackCodeValidator.Normalize(code)] = GetOwnerTokenState(code);
            return states;
        }

        public Task<ModPackListResult> ListOwnPacksDetailedAsync(CancellationToken ct = default) =>
            Task.FromResult(ListResult);

        public Task<ModPackDeleteResult> DeletePackDetailedAsync(string packCode, CancellationToken ct = default)
        {
            DeleteCalls.Add(packCode);
            return Task.FromResult(DeleteResult);
        }

        public string CreatorHash => new('a', 64);
        public Task<ModPackCreateResult> CreatePackAsync(ModPackCreateRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ModPack?> GetPackAsync(string packCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModPackListEntry>> ListOwnPacksAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeletePackAsync(string packCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ModPackExternalDll?> UploadExternalDllAsync(string packCode, string filePath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ModPackCustomArtifact?> UploadCustomDllAsync(string packCode, string filePath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ModPackArtifactStatusResult> GetExternalDllStatusAsync(string packCode, string sha256, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ModPackCustomArtifact?> DeclareGitHubCustomModAsync(string packCode, ModPackCustomGithubModRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ModPackArtifactStatusResult> GetCustomArtifactStatusAsync(string packCode, string artifactId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ModPackFinalizeResult> FinalizePackAsync(string packCode, CancellationToken ct = default) => throw new NotSupportedException();
        public ModPackValidationResult ValidatePack(ModPack pack, bool externalDllConsentGiven) => throw new NotSupportedException();
    }
}
