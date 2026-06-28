using System;
using System.Diagnostics;

namespace SUSModder.Services;

public static class OfficialDiscordHub
{
    public const string Url = "https://discord.gg/YRcbKPj6VS";

    public static void Open()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OfficialDiscordHub] Failed to open link: {ex.Message}");
        }
    }
}
