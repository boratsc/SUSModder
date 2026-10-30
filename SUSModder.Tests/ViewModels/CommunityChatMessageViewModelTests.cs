using SUSModder.Core.Models;
using SUSModder.Tests.Support;
using SUSModder.ViewModels;

namespace SUSModder.Tests.ViewModels;

public class CommunityChatMessageViewModelTests
{
    [Fact]
    public void AuthorDisplaysServerNicknameAndRefreshesAfterLanguageChange()
    {
        var message = new CommunityChatMessage
        {
            Id = "m1", Content = "hello", Author = new("server-id", new("Nazwa z API", "API name"))
        };
        var loc = new FakeLocalizationService();
        var vm = new CommunityChatMessageViewModel(message, loc);
        Assert.Equal("Nazwa z API", vm.AuthorDisplay);
        loc.ChangeCulture("en");
        Assert.Equal("API name", vm.AuthorDisplay);
        var notified = false;
        vm.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(vm.AuthorDisplay);
        vm.RefreshLocalizedDisplay();
        Assert.True(notified);
        Assert.False(vm.CanDelete);
    }

    [Fact]
    public void MissingAuthorDoesNotInventADisplayName()
    {
        var vm = new CommunityChatMessageViewModel(new CommunityChatMessage
            { Id = "m1", Content = "hello" }, new FakeLocalizationService());
        Assert.Equal(string.Empty, vm.AuthorDisplay);
    }

    [Fact]
    public void UnknownLocaleFallsBackToPolishWithoutGeneratingNickname()
    {
        var loc = new FakeLocalizationService();
        loc.ChangeCulture("unknown");
        var vm = new CommunityChatMessageViewModel(new CommunityChatMessage
            { Id = "m1", Content = "hello", Author = new("server-id", new("Nazwa z API", "API name")) }, loc);
        Assert.Equal("Nazwa z API", vm.AuthorDisplay);
    }
}
