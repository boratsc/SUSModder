using System.Collections.Generic;
using SUSModder.Core.Models;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// F3g: delta powiadomień o aktualizacjach paczek — toast tylko przy NOWEJ aktualizacji,
/// cisza przy kolejnych checkach bez zmian, powrót monitu po aktualizacji i kolejnej zmianie admina.
/// Czysta funkcja bez Avalonii/DI.
/// </summary>
public class PackUpdateDeltaTests
{
    private static ModPackUpdateInfo Result(string id, bool hasUpdate, bool checkSucceeded = true) => new()
    {
        InstanceId = id,
        PackCode = "PACK-" + id,
        HasUpdate = hasUpdate,
        CheckSucceeded = checkSucceeded
    };

    [Fact]
    public void FirstCheckCountsAllUpdatesAsNew()
    {
        var results = new[] { Result("i1", true), Result("i2", true), Result("i3", false) };

        var (toastCount, next) = MainWindowViewModel.ComputePackUpdateDelta(
            results, new HashSet<string>(), onlyNew: true);

        Assert.Equal(2, toastCount);
        Assert.Equal(new[] { "i1", "i2" }, next.Order());
    }

    [Fact]
    public void SecondCheckWithoutChangesIsSilent()
    {
        var results = new[] { Result("i1", true), Result("i2", true) };
        var (_, first) = MainWindowViewModel.ComputePackUpdateDelta(results, new HashSet<string>(), onlyNew: true);

        var (toastCount, next) = MainWindowViewModel.ComputePackUpdateDelta(results, first, onlyNew: true);

        Assert.Equal(0, toastCount);
        Assert.Equal(first.Order(), next.Order());
    }

    [Fact]
    public void NoUpdateClearsNotifiedId()
    {
        var notified = new HashSet<string> { "i1" };

        var (toastCount, next) = MainWindowViewModel.ComputePackUpdateDelta(
            new[] { Result("i1", false) }, notified, onlyNew: true);

        Assert.Equal(0, toastCount);
        Assert.Empty(next);
    }

    [Fact]
    public void UpdateAfterClearIsNewAgain()
    {
        // Scenariusz: admin zmienia → toast; użytkownik aktualizuje (HasUpdate=false czyści id);
        // admin zmienia PONOWNIE → monit wraca.
        var results = new[] { Result("i1", true) };
        var (_, first) = MainWindowViewModel.ComputePackUpdateDelta(results, new HashSet<string>(), onlyNew: true);
        var (_, afterUpdate) = MainWindowViewModel.ComputePackUpdateDelta(new[] { Result("i1", false) }, first, onlyNew: true);

        var (toastCount, next) = MainWindowViewModel.ComputePackUpdateDelta(results, afterUpdate, onlyNew: true);

        Assert.Equal(1, toastCount);
        Assert.Contains("i1", next);
    }

    [Fact]
    public void OnlyNewFalseCountsAllCurrentUpdatesLikeExistingCallers()
    {
        var notified = new HashSet<string> { "i1" };

        var (toastCount, next) = MainWindowViewModel.ComputePackUpdateDelta(
            new[] { Result("i1", true), Result("i2", true) }, notified, onlyNew: false);

        Assert.Equal(2, toastCount);
        Assert.Equal(new[] { "i1", "i2" }, next.Order());
    }

    [Fact]
    public void FailedCheckKeepsNotifiedStateSoToastDoesNotRepeatAfterNetworkError()
    {
        var notified = new HashSet<string> { "i1" };

        // Nieudany check (HasUpdate=false, CheckSucceeded=false) nie czyści id...
        var (toastCount, afterFailure) = MainWindowViewModel.ComputePackUpdateDelta(
            new[] { Result("i1", false, checkSucceeded: false) }, notified, onlyNew: true);
        Assert.Equal(0, toastCount);
        Assert.Contains("i1", afterFailure);

        // ...więc kolejny udany check z tą samą aktualizacją milczy.
        var (retryToast, _) = MainWindowViewModel.ComputePackUpdateDelta(
            new[] { Result("i1", true) }, afterFailure, onlyNew: true);
        Assert.Equal(0, retryToast);
    }

    [Fact]
    public void NewInstanceAlongsideNotifiedOneToastsOnlyForTheNewOne()
    {
        var notified = new HashSet<string> { "i1" };

        var (toastCount, next) = MainWindowViewModel.ComputePackUpdateDelta(
            new[] { Result("i1", true), Result("i2", true) }, notified, onlyNew: true);

        Assert.Equal(1, toastCount);
        Assert.Equal(new[] { "i1", "i2" }, next.Order());
    }
}