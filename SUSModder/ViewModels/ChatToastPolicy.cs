using System.Collections.Generic;
using System.Linq;

namespace SUSModder.ViewModels;

/// <summary>
/// E5: polityka toastów czatu (czysta funkcja, testowalna unit).
/// 1 nowa wiadomość = pojedynczy podgląd; ≥2 = licznik grupowy + najwyżej 2 najnowsze podglądy.
/// Brak toastów, gdy czat otwarty I okno ma focus. Własne wiadomości nigdy nie toastują.
/// Teksty i emisję do ToastService buduje MainWindowViewModel (warstwa lokalizacji).
/// </summary>
public static class ChatToastPolicy
{
    public const int MaxPreviews = 2;

    public static ChatToastPlan? Plan(
        IReadOnlyList<CommunityChatMessageViewModel> newMessages,
        bool chatOpen,
        bool windowFocused)
    {
        var fresh = newMessages.Where(m => !m.IsOwn).ToList();
        if (fresh.Count == 0 || (chatOpen && windowFocused))
            return null;

        if (fresh.Count == 1)
            return new ChatToastPlan(null, fresh);

        var previews = fresh.Skip(fresh.Count - MaxPreviews).ToList();
        return new ChatToastPlan(fresh.Count, previews);
    }
}

/// <summary>Plan emisji: licznik grupowy (null = pojedynczy toast) + podglądy do pokazania.</summary>
public sealed record ChatToastPlan(int? GroupCount, IReadOnlyList<CommunityChatMessageViewModel> Previews);
