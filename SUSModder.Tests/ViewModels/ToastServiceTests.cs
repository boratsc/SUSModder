using System.Linq;
using SUSModder.ViewModels;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// E11: DismissChatToasts usuwa aktywne i oczekujące toasty czatu, zostawiając inne kategorie.
/// AutoCloseMs = 0 — bez DispatcherTimer w kontekście unit (rendering hosta nietestowany tutaj).
/// </summary>
public sealed class ToastServiceTests
{
    private static ToastNotification Chat(string title)
    {
        var toast = ToastNotification.CreateChat(title, "autor", "podglad");
        toast.AutoCloseMs = 0;
        return toast;
    }

    [Fact]
    public void DismissChatToasts_RemovesActiveAndPendingChat_KeepsOtherTypes()
    {
        var service = new ToastService();
        var info = ToastNotification.Create(ToastType.Info, "info", autoCloseMs: 0);
        service.Enqueue(info);
        service.Enqueue(Chat("c1"));
        service.Enqueue(Chat("c2")); // 3 aktywne (limit widocznych hosta)
        service.Enqueue(Chat("c3")); // oczekujący w kolejce

        service.DismissChatToasts();

        Assert.Same(info, Assert.Single(service.ActiveToasts));
        // Kolejka czatu wyczyszczona przed usuwaniem aktywnych — zwolnione miejsce nie promuje czatu.
        service.Enqueue(ToastNotification.Create(ToastType.Success, "ok", autoCloseMs: 0));
        Assert.Equal(2, service.ActiveToasts.Count);
        Assert.DoesNotContain(service.ActiveToasts, t => t.IsChat);
    }

    [Fact]
    public void DismissChatToasts_NoChatToasts_LeavesActiveUntouched()
    {
        var service = new ToastService();
        var info = ToastNotification.Create(ToastType.Info, "info", autoCloseMs: 0);
        service.Enqueue(info);

        service.DismissChatToasts();

        Assert.Same(info, Assert.Single(service.ActiveToasts));
    }
}
