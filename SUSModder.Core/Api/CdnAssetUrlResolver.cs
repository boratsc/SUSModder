using System.IO;

namespace SUSModder.Core.Api;

public static class CdnAssetUrlResolver
{
  private const string DefaultStaticAssetsBaseUrl = "https://susmodder.app";

  public static string Resolve(string? assetPath, string apiV2BaseUrl, string? staticAssetsBaseUrl = null)
  {
    if (string.IsNullOrWhiteSpace(assetPath))
      return string.Empty;

    var path = assetPath.Trim();
    if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
      return RewriteMisroutedIconUrl(path, staticAssetsBaseUrl);
    }

    if (path.StartsWith("/api/v2/", StringComparison.OrdinalIgnoreCase))
    {
      var apiBase = NormalizeBaseUrl(apiV2BaseUrl) ?? "https://api.susmodder-cdn.ovh/v2";
      var suffix = path["/api/v2".Length..];
      return $"{apiBase}{suffix}";
    }

    var assetsBase = NormalizeBaseUrl(staticAssetsBaseUrl) ?? DefaultStaticAssetsBaseUrl;

    if (path.StartsWith("/icons/", StringComparison.OrdinalIgnoreCase))
      return $"{assetsBase}{path}";
    return path.StartsWith('/')
      ? $"{assetsBase}{path}"
      : $"{assetsBase}/{path}";
  }

  public static string DeriveCdnBaseUrl(string apiV2BaseUrl)
  {
    var baseUrl = apiV2BaseUrl.TrimEnd('/');
    if (baseUrl.EndsWith("/v2", StringComparison.OrdinalIgnoreCase))
      baseUrl = baseUrl[..^3];

    const string apiPrefix = "://api.";
    var schemeIndex = baseUrl.IndexOf(apiPrefix, StringComparison.OrdinalIgnoreCase);
    if (schemeIndex >= 0)
      baseUrl = string.Concat(baseUrl.AsSpan(0, schemeIndex + 3), baseUrl.AsSpan(schemeIndex + apiPrefix.Length));

    return baseUrl.TrimEnd('/');
  }

  private static string RewriteMisroutedIconUrl(string url, string? staticAssetsBaseUrl)
  {
    if (!url.Contains("/icons/", StringComparison.OrdinalIgnoreCase))
      return url;

    var assetsBase = NormalizeBaseUrl(staticAssetsBaseUrl) ?? DefaultStaticAssetsBaseUrl;
    if (url.StartsWith(assetsBase, StringComparison.OrdinalIgnoreCase))
      return url;

    try
    {
      var fileName = Path.GetFileName(new Uri(url).LocalPath);
      if (string.IsNullOrWhiteSpace(fileName))
        return url;

      return $"{assetsBase}/icons/{fileName}";
    }
    catch
    {
      return url;
    }
  }

  private static string? NormalizeBaseUrl(string? baseUrl)
  {
    if (string.IsNullOrWhiteSpace(baseUrl))
      return null;

    return baseUrl.TrimEnd('/');
  }
}
