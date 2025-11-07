using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Logging;
using Velopack;
using Velopack.Sources;
using Velopack.Util;

namespace SUSModder.Core.Services
{
    internal sealed class VelopackApiSource : IUpdateSource, IDisposable
    {
        private static readonly MediaTypeWithQualityHeaderValue JsonMediaType = new("application/json");
        private static readonly JsonDocumentOptions JsonOptions = new()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _timeout;
    private Uri? _downloadBaseUri;

        public VelopackApiSource(Uri endpoint, HttpClient? httpClient = null, TimeSpan? timeout = null)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _timeout = timeout ?? TimeSpan.FromSeconds(30);

            if (httpClient == null)
            {
                _httpClient = new HttpClient
                {
                    Timeout = _timeout
                };
                _ownsHttpClient = true;
            }
            else
            {
                _httpClient = httpClient;
                if (_httpClient.Timeout == System.Threading.Timeout.InfiniteTimeSpan)
                {
                    _httpClient.Timeout = _timeout;
                }
            }
        }

        public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(channel)) channel = "win";

            var requestUri = BuildRequestUri(channel, stagingId, latestLocalRelease);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            if (!request.Headers.Accept.Contains(JsonMediaType))
            {
                request.Headers.Accept.Add(JsonMediaType);
            }

            logger.Info($"[VelopackApiSource] Fetching manifest from '{requestUri}'.");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            return await HandleResponseAsync(response, logger).ConfigureAwait(false);
        }

        public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (releaseEntry == null) throw new ArgumentNullException(nameof(releaseEntry));
            if (string.IsNullOrWhiteSpace(localFile)) throw new ArgumentNullException(nameof(localFile));

            var downloadUri = ResolveDownloadUri(releaseEntry.FileName);

            logger.Info($"[VelopackApiSource] Downloading '{releaseEntry.FileName}' from '{downloadUri}'.");

            var directory = Path.GetDirectoryName(localFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var response = await _httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancelToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;

            await using var sourceStream = await response.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
            await using var destinationStream = new FileStream(localFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long totalRead = 0;

            progress?.Invoke(0);

            while (true)
            {
                var bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancelToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    break;

                await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancelToken).ConfigureAwait(false);
                totalRead += bytesRead;

                if (contentLength.HasValue && contentLength.Value > 0)
                {
                    var percent = (int)Math.Clamp((totalRead * 100d) / contentLength.Value, 0d, 100d);
                    progress?.Invoke(percent);
                }
            }

            progress?.Invoke(100);
        }

        private async Task<VelopackAssetFeed> HandleResponseAsync(HttpResponseMessage response, IVelopackLogger logger)
        {
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(payload))
                throw new InvalidOperationException("Velopack API returned an empty manifest payload.");

            using var document = JsonDocument.Parse(payload, JsonOptions);
            var root = document.RootElement;

            if (root.TryGetProperty("success", out var successElement) && successElement.ValueKind == JsonValueKind.False)
            {
                var error = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
                    ? errorElement.GetString()
                    : "unknown_error";

                var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                    ? messageElement.GetString()
                    : "Velopack API response indicated failure.";

                throw new InvalidOperationException($"Velopack API error ({error}): {message}");
            }

            // Get channel from response
            var channel = root.TryGetProperty("channel", out var channelElement) && channelElement.ValueKind == JsonValueKind.String
                ? channelElement.GetString() ?? "release"
                : "release";

            UpdateDownloadBaseUri(root, logger, channel);

            if (root.TryGetProperty("manifest", out var manifestElement) && manifestElement.ValueKind == JsonValueKind.Object)
            {
                // Convert custom "Releases" format to Velopack "Assets" format
                var convertedManifest = ConvertCustomManifestToVelopackFormat(manifestElement, channel);
                return VelopackAssetFeed.FromJson(convertedManifest);
            }

            return VelopackAssetFeed.FromJson(payload);
        }

        private string ConvertCustomManifestToVelopackFormat(JsonElement manifestElement, string channel = "release")
        {
            var assets = new List<Dictionary<string, object>>();

            if (manifestElement.TryGetProperty("Releases", out var releasesElement) && releasesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var release in releasesElement.EnumerateArray())
                {
                    if (!release.TryGetProperty("File", out var fileElement) || fileElement.ValueKind != JsonValueKind.String)
                        continue;

                    var fileName = fileElement.GetString();
                    if (string.IsNullOrWhiteSpace(fileName))
                        continue;

                    // FIX: Backend nie używa podkatalogów dla kanałów - nie dodawaj prefiksu
                    // Jeśli backend zwróci absolute URL, ResolveDownloadUri użyje go bezpośrednio (linia 215)
                    var fileNameWithPath = fileName;

                    var asset = new Dictionary<string, object>
                    {
                        ["PackageId"] = "SUSModder",
                        ["FileName"] = fileNameWithPath
                    };

                    if (release.TryGetProperty("Version", out var versionElement) && versionElement.ValueKind == JsonValueKind.String)
                        asset["Version"] = versionElement.GetString()!;

                    if (release.TryGetProperty("SHA256", out var sha256Element) && sha256Element.ValueKind == JsonValueKind.String)
                        asset["SHA256"] = sha256Element.GetString()!;

                    if (release.TryGetProperty("Size", out var sizeElement) && (sizeElement.ValueKind == JsonValueKind.Number || sizeElement.ValueKind == JsonValueKind.String))
                        asset["Size"] = sizeElement.ValueKind == JsonValueKind.Number ? sizeElement.GetInt64() : long.Parse(sizeElement.GetString()!);

                    // Determine Type: Full or Delta
                    asset["Type"] = fileName.Contains("-delta.nupkg", StringComparison.OrdinalIgnoreCase) ? "Delta" : "Full";

                    assets.Add(asset);
                }
            }

            var velopackManifest = new Dictionary<string, object>
            {
                ["Assets"] = assets
            };

            return JsonSerializer.Serialize(velopackManifest);
        }

        private Uri ResolveDownloadUri(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Invalid release file name.", nameof(fileName));

            if (Uri.TryCreate(fileName, UriKind.Absolute, out var absoluteUri))
                return absoluteUri;

            var baseUri = _downloadBaseUri ?? GetFallbackBaseUri();
            var baseString = baseUri.AbsoluteUri.TrimEnd('/') + "/";
            return new Uri(baseString + fileName);
        }

        private Uri BuildRequestUri(string channel, Guid? stagingId, VelopackAsset? latestLocalRelease)
        {
            var queryParameters = new List<string>
            {
                $"channel={Uri.EscapeDataString(channel)}"
            };

            if (VelopackRuntimeInfo.SystemArch != RuntimeCpu.Unknown)
            {
                queryParameters.Add($"arch={Uri.EscapeDataString(VelopackRuntimeInfo.SystemArch.ToString())}");
            }

            if (VelopackRuntimeInfo.SystemOs != RuntimeOs.Unknown)
            {
                queryParameters.Add($"os={Uri.EscapeDataString(VelopackRuntimeInfo.SystemOs.GetOsShortName())}");
                queryParameters.Add($"rid={Uri.EscapeDataString(VelopackRuntimeInfo.SystemRid)}");
            }

            if (stagingId.HasValue)
            {
                queryParameters.Add($"stagingId={Uri.EscapeDataString(stagingId.Value.ToString())}");
            }

            if (latestLocalRelease != null)
            {
                queryParameters.Add($"id={Uri.EscapeDataString(latestLocalRelease.PackageId)}");
                queryParameters.Add($"localVersion={Uri.EscapeDataString(latestLocalRelease.Version.ToString())}");
            }

            var builder = new UriBuilder(_endpoint)
            {
                Query = string.Join("&", queryParameters)
            };

            return builder.Uri;
        }

        private void UpdateDownloadBaseUri(JsonElement root, IVelopackLogger logger, string channel = "release")
        {
            if (TryReadUri(root, "downloadBaseUrl", out var baseUri) || TryReadUri(root, "downloadBaseURL", out baseUri))
            {
                SetDownloadBaseUri(baseUri, logger);
                return;
            }

            if (root.TryGetProperty("manifest", out var manifestElement) && manifestElement.ValueKind == JsonValueKind.Object)
            {
                if (TryReadUri(manifestElement, "downloadBaseUrl", out baseUri) || TryReadUri(manifestElement, "downloadBaseURL", out baseUri))
                {
                    SetDownloadBaseUri(baseUri, logger);
                }
            }
        }

        private void SetDownloadBaseUri(Uri? baseUri, IVelopackLogger logger)
        {
            if (baseUri == null)
            {
                logger.Info("[VelopackApiSource] downloadBaseUrl missing, falling back to API host.");
                _downloadBaseUri = GetFallbackBaseUri();
                return;
            }

            _downloadBaseUri = baseUri;
        }

        private Uri GetFallbackBaseUri()
        {
            if (_endpoint.IsAbsoluteUri)
            {
                return new Uri(_endpoint.GetLeftPart(UriPartial.Authority));
            }

            return _endpoint;
        }

        private bool TryReadUri(JsonElement element, string propertyName, out Uri? uri)
        {
            uri = null;
            if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
                return false;

            var value = property.GetString();
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
            {
                uri = absolute;
                return true;
            }

            if (_endpoint.IsAbsoluteUri && Uri.TryCreate(new Uri(_endpoint.GetLeftPart(UriPartial.Authority)), value, out var relative))
            {
                uri = relative;
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }
        
        /// <summary>
        /// Publiczny wrapper dla GetReleaseFeed - używany przy cross-channel switches
        /// </summary>
        public async Task<IEnumerable<VelopackAsset>> GetReleaseFeedAsync(string channel, CancellationToken cancellationToken = default)
        {
            var logger = new VelopackApiSourceLogger();
            var feed = await GetReleaseFeed(logger, null, channel, null, null).ConfigureAwait(false);
            return feed.Assets ?? Enumerable.Empty<VelopackAsset>();
        }
        
        /// <summary>
        /// Prosty logger dla wewnętrznego użycia
        /// </summary>
        private class VelopackApiSourceLogger : IVelopackLogger
        {
            public void Log(VelopackLogLevel level, string? message, Exception? exception = null) { }
            public void Error(string message) { }
            public void Warn(string message) { }
            public void Info(string message) { }
            public void Debug(string message) { }
        }
    }
}
