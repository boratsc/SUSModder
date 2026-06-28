using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Microsoft.Win32;

namespace SUSModder.Services;

/// <summary>
/// Wykrywa warunki wymuszające nieprzezroczysty fallback motywu Szklany.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GlassThemeFallbackService
{
    private const uint SpiGetHighContrast = 0x0042;
    private const int HcfHighContrastOn = 0x00000001;

    public enum FallbackReason
    {
        None = 0,
        UserPreference,
        HighContrast,
        SystemTransparencyDisabled,
        BackdropUnsupported
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct HighContrastInfo
    {
        public int CbSize;
        public int DwFlags;
        public IntPtr LpszDefaultScheme;
    }

    public FallbackReason GetFallbackReason(bool userReduceTransparency, WindowTransparencyLevel? actualLevel)
    {
        if (userReduceTransparency)
            return FallbackReason.UserPreference;

        if (IsHighContrastEnabled())
            return FallbackReason.HighContrast;

        if (IsWindowsTransparencyDisabled())
            return FallbackReason.SystemTransparencyDisabled;

        if (actualLevel == WindowTransparencyLevel.None)
            return FallbackReason.BackdropUnsupported;

        return FallbackReason.None;
    }

    public bool ShouldUseOpaqueFallback(bool userReduceTransparency, WindowTransparencyLevel? actualLevel) =>
        GetFallbackReason(userReduceTransparency, actualLevel) != FallbackReason.None;

    public static bool IsHighContrastEnabled()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return false;

            var info = new HighContrastInfo
            {
                CbSize = Marshal.SizeOf<HighContrastInfo>(),
                LpszDefaultScheme = IntPtr.Zero
            };
            if (SystemParametersInfo(SpiGetHighContrast, (uint)info.CbSize, ref info, 0))
                return (info.DwFlags & HcfHighContrastOn) != 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlassTheme] High contrast detection failed: {ex.Message}");
        }

        return false;
    }

    public static bool IsWindowsTransparencyDisabled()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return true;

            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("EnableTransparency") is int enabled)
                return enabled == 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlassTheme] Transparency registry check failed: {ex.Message}");
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        ref HighContrastInfo pvParam,
        uint fWinIni);
}
