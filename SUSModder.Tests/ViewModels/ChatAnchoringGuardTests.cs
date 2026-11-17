using System.Xml.Linq;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// Zakotwiczenie czatu: reguły, których nie da się sprawdzić bez renderu (MainWindowViewModel
/// nie jest konstruowalny w testach — ciężkie DI/Avalonia; brak Avalonia.Headless).
/// Pilnujemy ich na źródle, wzorzec: SettingsPanelLayoutGuardTests.
/// </summary>
public sealed class ChatAnchoringGuardTests
{
    [Fact]
    public void TopBarToggle_ScrollsWhenDocked_TogglesOverlayOtherwise()
    {
        var source = ReadViewModel("MainWindowViewModel.FrontendLayout.cs");
        var init = source[(source.IndexOf("private void InitializeFrontendLayout()", StringComparison.Ordinal))..];
        init = init[..init.IndexOf("\n        }", StringComparison.Ordinal)];

        Assert.Contains("if (IsChatDocked)", init);
        Assert.Contains("CommunityChat.OnNewMessagesButton();", init);
        Assert.Contains("IsChatOpen = !IsChatOpen;", init);
    }

    [Fact]
    public void ToastClick_ScrollsWhenDocked_OpensOverlayOtherwise()
    {
        var source = ReadViewModel("MainWindowViewModel.FrontendLayout.cs");

        // Klik toastu nie otwiera drugiego czatu przy dokowaniu — jedna ścieżka z rozgałęzieniem.
        Assert.DoesNotContain("() => IsChatOpen = true", source);
        var method = source[(source.IndexOf("private void OpenChatFromToast()", StringComparison.Ordinal))..];
        method = method[..method.IndexOf("\n        }", StringComparison.Ordinal)];
        Assert.Contains("if (IsChatDocked)", method);
        Assert.Contains("CommunityChat.OnNewMessagesButton();", method);
        Assert.Contains("IsChatOpen = true;", method);
    }

    [Fact]
    public void Undocking_KeepsConversationAsOverlay_WithoutExplicitOpenSideEffects()
    {
        var source = ReadViewModel("MainWindowViewModel.FrontendLayout.cs");

        // Efektywny tryb trafia do VM czatu; przejście automatyczne (auto: true) nie robi mark-read/fokusu.
        Assert.Contains("CommunityChat.IsDockedVisible = value;", source);
        Assert.Contains("CommunityChat.SetOverlayOpen(true, auto: true);", source);
    }

    [Fact]
    public void Escape_ClosesOnlyOverlay_NeverDockedColumn()
    {
        var source = ReadView("MainWindow.axaml.cs");
        Assert.Contains("if (vm.IsChatOpen && !vm.IsChatDocked)", source);
    }

    [Fact]
    public void WindowWidening_HappensOnlyOnPreferenceChangeAndStartup_InNormalState()
    {
        var source = ReadView("MainWindow.axaml.cs");

        // Poszerzenie/cofnięcie tylko z ApplyChatAnchoredPreference (po zapisie) i jednorazowego
        // hooka startowego (OnOpened) — nie w każdym SizeChanged.
        var occurrences = CountOccurrences(source, "AdjustChatAnchorWidth(");
        Assert.Equal(3, occurrences); // definicja + zapis ustawień + hook startowy

        var method = source[(source.IndexOf("private void AdjustChatAnchorWidth(bool anchored)", StringComparison.Ordinal))..];
        method = method[..method.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("if (WindowState != WindowState.Normal)", method);
        Assert.Contains("ChatDockingLayout.ShouldRevertAutoWidth(Width, _chatAutoGrantedWidth)", method);
        // DIP-aware: px fizyczne obszaru roboczego przeliczane przez RenderScaling.
        Assert.Contains("screen.WorkingArea.Width / RenderScaling", method);
    }

    [Fact]
    public void StartupHook_RestoresDockingOnce_NotOnEverySizeChanged()
    {
        var source = ReadView("MainWindow.axaml.cs");

        // Hook startowy w Opened (uchwyt okna → Screens działa); flaga jednorazowości
        // ustawiana przed resize, bo AdjustChatAnchorWidth(true) dodaje 520 bezwarunkowo.
        var hook = source[(source.IndexOf("protected override void OnOpened(EventArgs e)", StringComparison.Ordinal))..];
        hook = hook[..hook.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("base.OnOpened(e);", hook);
        Assert.Contains("if (Design.IsDesignMode || _chatAnchorStartupApplied)", hook);
        Assert.Contains("vm.ChatAnchored", hook);
        Assert.Contains("AdjustChatAnchorWidth(true);", hook);
        Assert.Contains("ApplyRightColumnLayout();", hook);
        Assert.True(hook.IndexOf("_chatAnchorStartupApplied = true;", StringComparison.Ordinal)
            < hook.IndexOf("AdjustChatAnchorWidth(true);", StringComparison.Ordinal));

        // SizeChanged tylko przelicza układ — bez poszerzania okna.
        Assert.Contains("SizeChanged += (_, _) => ApplyRightColumnLayout();", source);
    }

    [Fact]
    public void Layout_PushesEffectiveDocking_AndManagesFourthColumn()
    {
        var source = ReadView("MainWindow.axaml.cs");
        Assert.Contains("ChatDockingLayout.IsDocked(vm.ChatAnchored, Bounds.Width)", source);
        Assert.Contains("vm.IsChatDocked = docked;", source);
        Assert.Contains("content.ColumnDefinitions[3].Width", source);
        Assert.Contains("ChatDockingLayout.IsDetailsNarrow(docked, Bounds.Width)", source);
    }

    [Fact]
    public void PanelCloseButton_IsHiddenWhenDocked()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "SUSModder", "Views", "CommunityChatPanel.axaml"));
        XNamespace ns = "https://github.com/avaloniaui";

        var close = doc.Descendants(ns + "Button")
            .Single(e => (string?)e.Attribute("Command") == "{Binding CloseChatCommand}");
        Assert.Equal("{Binding IsDockedVisible, Converter={x:Static BoolConverters.Not}}",
            (string?)close.Attribute("IsVisible"));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string ReadViewModel(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "SUSModder", "ViewModels", fileName));

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "SUSModder", "Views", fileName));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SUSModder", "Views", "MainWindow.axaml"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found");
    }
}
