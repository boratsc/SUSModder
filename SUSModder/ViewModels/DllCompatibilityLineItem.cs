using SUSModder.Core.Models;

namespace SUSModder.ViewModels;

public class DllCompatibilityLineItem
{
    public DllCompatibilityLineItem(
        string targetName,
        string statusEmoji,
        string statusLabel,
        CompatibilityStatus status,
        int? dllModId = null)
    {
        TargetName = targetName;
        StatusEmoji = statusEmoji;
        StatusLabel = statusLabel;
        Status = status;
        DllModId = dllModId;
        LineText = $"{statusEmoji} {targetName} — {statusLabel}";
    }

    public string TargetName { get; }
    public string StatusEmoji { get; }
    public string StatusLabel { get; }
    public CompatibilityStatus Status { get; }
    public string LineText { get; }
    public int? DllModId { get; }
}
