using SUSModder.Core.Configuration;

namespace SUSModder.Core.Utilities;

/// <summary>
/// Ikony wbudowane w aplikację (avares://SUSModder/Assets/...), nie z CDN/API.
/// </summary>
public static class BundledModIconHelper
{
    public const string VanillaIconFileName = "Vanilla.png";

    public static bool IsVanillaMod(ModConfiguration mod)
    {
        if (mod is null)
            return false;

        return mod.Id == 0 ||
               mod.ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ||
               mod.ModName.Equals("AmongUs", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsBundledAssetFileName(string? fileName) =>
        string.Equals(fileName?.Trim(), VanillaIconFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Przywraca lokalną nazwę pliku, gdy wcześniejszy sync zamienił ją na URL CDN.
    /// </summary>
    public static string NormalizeVanillaIconReference(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return VanillaIconFileName;

        if (IsBundledAssetFileName(fileName))
            return VanillaIconFileName;

        var trimmed = fileName.Trim();
        if (trimmed.Contains("Vanilla.png", StringComparison.OrdinalIgnoreCase))
            return VanillaIconFileName;

        return VanillaIconFileName;
    }
}
