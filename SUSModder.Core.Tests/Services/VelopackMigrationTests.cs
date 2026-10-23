using System.Net;
using System.Text;
using SUSModder.Core.Services;
using Velopack;

namespace SUSModder.Core.Tests.Services;

public sealed class VelopackMigrationTests
{
    [Fact]
    public async Task LegacyFeed_PreservesVersionChecksumsSizeAndDownloadBase()
    {
        const string payload = """
            {
              "success": true,
              "channel": "beta",
              "downloadBaseUrl": "https://downloads.example.invalid/beta/",
              "manifest": {
                "Releases": [{
                  "File": "SUSModder-3.1.0-beta-full.nupkg",
                  "Version": "3.1.0-beta",
                  "SHA256": "abc123",
                  "SHA1": "def456",
                  "Size": "7"
                }]
              }
            }
            """;
        Uri? downloadUri = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/releases")
            {
                Assert.Contains("channel=beta", request.RequestUri.Query);
                return Json(payload);
            }

            downloadUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("package") };
        }));
        using var source = new VelopackApiSource(new Uri("https://api.example.invalid/api/v2/releases"), client);
        var release = Assert.Single(await source.GetReleaseFeedAsync("beta", TestContext.Current.CancellationToken));

        Assert.Equal("3.1.0-beta", release.Version.ToString());
        Assert.Equal("abc123", release.SHA256);
        Assert.Equal("def456", release.SHA1);
        Assert.Equal(7, release.Size);
        Assert.Equal(VelopackAssetType.Full, release.Type);

        var directory = Path.Combine(Path.GetTempPath(), "SUSModder.VelopackTests", Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(directory, "package.nupkg");
        try
        {
            await source.DownloadReleaseEntry(new SilentLogger(), release, destination, _ => { }, TestContext.Current.CancellationToken);
            Assert.Equal("https://downloads.example.invalid/beta/SUSModder-3.1.0-beta-full.nupkg", downloadUri!.AbsoluteUri);
            Assert.Equal("package", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task NativeFeed_ParsesVelopackSemanticVersionsAndAssetTypes()
    {
        const string payload = """
            { "Assets": [
              { "PackageId": "SUSModder", "Version": "3.1.0-beta.2", "Type": "Full", "FileName": "full.nupkg", "SHA256": "abc", "Size": 7 },
              { "PackageId": "SUSModder", "Version": "3.1.0-beta.2", "Type": "Delta", "FileName": "delta.nupkg", "SHA256": "def", "Size": 3 }
            ] }
            """;
        using var client = new HttpClient(new StubHandler(_ => Json(payload)));
        using var source = new VelopackApiSource(new Uri("https://api.example.invalid/releases"), client);

        var assets = (await source.GetReleaseFeedAsync("beta", TestContext.Current.CancellationToken)).ToArray();
        Assert.Equal(2, assets.Length);
        Assert.Equal(VelopackAssetType.Full, assets[0].Type);
        Assert.Equal(VelopackAssetType.Delta, assets[1].Type);
        Assert.Equal("3.1.0-beta.2", assets[0].Version.ToString());
        Assert.True(assets[0].Version < SemanticVersion.Parse("3.1.0"));
        Assert.True(assets[0].Version > SemanticVersion.Parse("3.1.0-beta.1"));
    }

    [Fact]
    public async Task LegacyFeed_PreservesSha1OnlyReleases()
    {
        const string payload = """
            { "manifest": { "Releases": [
              { "File": "SUSModder-3.0.0-full.nupkg", "Version": "3.0.0", "SHA1": "legacy-sha1", "Size": 42 }
            ] } }
            """;
        using var client = new HttpClient(new StubHandler(_ => Json(payload)));
        using var source = new VelopackApiSource(new Uri("https://api.example.invalid/releases"), client);

        var release = Assert.Single(await source.GetReleaseFeedAsync("release", TestContext.Current.CancellationToken));
        Assert.Equal("legacy-sha1", release.SHA1);
        Assert.True(string.IsNullOrEmpty(release.SHA256));
    }

    [Fact]
    public async Task FeedErrors_AreNotTreatedAsEmptySuccessfulFeeds()
    {
        using var client = new HttpClient(new StubHandler(_ => Json("""
            { "success": false, "error": "not_available", "message": "Test error" }
            """)));
        using var source = new VelopackApiSource(new Uri("https://api.example.invalid/releases"), client);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetReleaseFeedAsync("beta", TestContext.Current.CancellationToken));
        Assert.Contains("not_available", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StalledManifestBody_ObservesTimeoutOrCallerCancellation(bool cancelFromCaller)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StalledStream())
        })) { Timeout = Timeout.InfiniteTimeSpan };
        using var source = new VelopackApiSource(new Uri("https://api.example.invalid/releases"), client,
            timeout: cancelFromCaller ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(100));
        if (cancelFromCaller) cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.GetReleaseFeedAsync("beta", cancellation.Token)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StalledPackageBody_ObservesIdleTimeoutOrCallerCancellation(bool cancelFromCaller)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StalledStream())
        })) { Timeout = Timeout.InfiniteTimeSpan };
        using var source = new VelopackApiSource(new Uri("https://api.example.invalid/releases"), client,
            timeout: cancelFromCaller ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(100));
        var directory = Path.Combine(Path.GetTempPath(), "SUSModder.VelopackTests", Guid.NewGuid().ToString("N"));
        if (cancelFromCaller) cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                source.DownloadReleaseEntry(new SilentLogger(), new VelopackAsset { FileName = "package.nupkg" },
                    Path.Combine(directory, "package.nupkg"), _ => { }, cancellation.Token)
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    private sealed class SilentLogger : Velopack.Logging.IVelopackLogger
    {
        public void Log(Velopack.Logging.VelopackLogLevel level, string? message, Exception? exception = null) { }
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
