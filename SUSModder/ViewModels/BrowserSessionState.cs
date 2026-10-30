using System;
using System.Collections.Generic;

namespace SUSModder.ViewModels;

/// <summary>Process-only navigation state. No installation or bulk-operation state belongs here.</summary>
public sealed class BrowserTabState
{
    public string Query { get; set; } = string.Empty;
    public bool FavoritesOnly { get; set; }
    public int? ModId { get; set; }
    public string? InstanceId { get; set; }
    private double _scrollOffset;
    public double ScrollOffset
    {
        get => _scrollOffset;
        set => _scrollOffset = double.IsFinite(value) ? Math.Max(0, value) : 0;
    }
}

public sealed class BrowserSessionState
{
    private readonly Dictionary<MainWindowViewModel.ModBrowserTab, BrowserTabState> _tabs = new();

    public BrowserTabState For(MainWindowViewModel.ModBrowserTab tab)
    {
        if (!_tabs.TryGetValue(tab, out var state))
            _tabs[tab] = state = new BrowserTabState();
        return state;
    }
}
