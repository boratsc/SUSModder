using System;
using System.Collections.Generic;
using SUSModder.Core.Models;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// 3.1-04: wybór klucza ikony wersjonowanej. Tombstone moda oznacza celowy
/// placeholder, ale zdjęcie globalnego nadpisania wraca do ikony bundlowanej
/// (Vanilla) lub ikon per-mod (DLL).
/// </summary>
public class IconAssignmentProjectionTests
{
    private static readonly Dictionary<string, IconAssignment> Assignments = new(StringComparer.Ordinal)
    {
        ["global:vanilla"] = new() { AssignmentKey = "global:vanilla", IsRemoved = true },
        ["global:dll"] = new() { AssignmentKey = "global:dll", AssetId = "icon_dll" },
        ["mod:5"] = new() { AssignmentKey = "mod:5", IsRemoved = true },
        ["mod:7"] = new() { AssignmentKey = "mod:7", AssetId = "icon_7" }
    };

    [Fact]
    public void RemovedGlobalVanilla_FallsBackToBundledIcon()
    {
        Assert.Null(MainWindowViewModel.ResolveIconKey(true, Assignments, IconAssignmentKeys.GlobalVanilla));
    }

    [Fact]
    public void RemovedGlobalDll_FallsBackToPerModIcons()
    {
        var assignments = new Dictionary<string, IconAssignment>(StringComparer.Ordinal)
        {
            ["global:dll"] = new() { AssignmentKey = "global:dll", IsRemoved = true }
        };

        Assert.Null(MainWindowViewModel.ResolveIconKey(true, assignments, IconAssignmentKeys.GlobalDll));
    }

    [Fact]
    public void AssignedGlobalDll_UsesVersionedKey()
    {
        Assert.Equal("global:dll", MainWindowViewModel.ResolveIconKey(true, Assignments, IconAssignmentKeys.GlobalDll));
    }

    [Fact]
    public void RemovedModAssignment_KeepsPlaceholderSemantics()
    {
        Assert.Equal("mod:5", MainWindowViewModel.ResolveIconKey(true, Assignments, IconAssignmentKeys.ForMod(5)));
    }

    [Fact]
    public void ActiveModAssignment_UsesVersionedKey()
    {
        Assert.Equal("mod:7", MainWindowViewModel.ResolveIconKey(true, Assignments, IconAssignmentKeys.ForMod(7)));
    }

    [Fact]
    public void LegacySchemaOrMissingAssignment_UsesLegacyIcon()
    {
        Assert.Null(MainWindowViewModel.ResolveIconKey(false, Assignments, IconAssignmentKeys.GlobalDll));
        Assert.Null(MainWindowViewModel.ResolveIconKey(true, Assignments, IconAssignmentKeys.ForMod(99)));
    }
}
