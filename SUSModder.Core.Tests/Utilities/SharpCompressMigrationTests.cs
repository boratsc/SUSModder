using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Utilities;

public sealed class SharpCompressMigrationTests : IDisposable
{
    private const string Payload = "legacy archive payload";
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("zip")]
    [InlineData("tar")]
    [InlineData("compressed-tar-no-extension")]
    public async Task ExtractAsync_PreservesNestedFilesOverwriteAndProgress(string format)
    {
        var archivePath = CreateArchive(format);
        var destination = Path.Combine(_tempDir, "extracted");
        var nestedFile = Path.Combine(destination, "nested", "data.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedFile)!);
        File.WriteAllText(nestedFile, "old content to overwrite");
        var reports = new List<ExtractionProgress>();

        await new SharpCompressExtractor().ExtractAsync(
            archivePath, destination,
            progress: new InlineProgress(reports.Add),
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(Payload, File.ReadAllText(nestedFile));
        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(destination, "empty.txt")));
        Assert.Equal(2, Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length);
        Assert.NotEmpty(reports);
        var expectedBytes = Encoding.UTF8.GetByteCount(Payload);
        Assert.Equal(0, reports[0].BytesExtracted);
        Assert.Equal(expectedBytes, reports[^1].BytesExtracted);
        Assert.All(reports, report => Assert.Equal(expectedBytes, report.TotalBytes));
        for (var i = 1; i < reports.Count; i++)
            Assert.InRange(reports[i].BytesExtracted, reports[i - 1].BytesExtracted, expectedBytes);
        Assert.Contains(reports, report => report.CurrentFile == "nested/data.txt");
        Assert.Contains(reports, report => report.CurrentFile == "empty.txt");
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("tar")]
    [InlineData("compressed-tar-no-extension")]
    public async Task ExtractAsync_PreCancelled_DoesNotExtractFiles(string format)
    {
        var archivePath = CreateArchive(format);
        var destination = Path.Combine(_tempDir, "cancelled");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SharpCompressExtractor().ExtractAsync(archivePath, destination, ct: cancellation.Token));

        AssertNoExtractedFiles(destination);
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("tar")]
    [InlineData("compressed-tar-no-extension")]
    public async Task ExtractAsync_CancelledAfterTotals_DoesNotExtractFilesOrReportCompletion(string format)
    {
        var archivePath = CreateArchive(format);
        var destination = Path.Combine(_tempDir, "cancelled");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var reports = new List<ExtractionProgress>();
        var progress = new InlineProgress(report =>
        {
            reports.Add(report);
            cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SharpCompressExtractor().ExtractAsync(
                archivePath, destination, progress: progress, ct: cancellation.Token));

        var initial = Assert.Single(reports);
        Assert.Equal(0, initial.BytesExtracted);
        Assert.Equal(Encoding.UTF8.GetByteCount(Payload), initial.TotalBytes);
        AssertNoExtractedFiles(destination);
    }

    private string CreateArchive(string format)
    {
        var source = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllText(Path.Combine(source, "nested", "data.txt"), Payload);
        File.WriteAllText(Path.Combine(source, "empty.txt"), string.Empty);
        if (format == "zip")
        {
            var zip = Path.Combine(_tempDir, "payload.zip");
            ZipFile.CreateFromDirectory(source, zip);
            return zip;
        }

        var tar = Path.Combine(_tempDir, "payload.tar");
        TarFile.CreateFromDirectory(source, tar, includeBaseDirectory: false);
        if (format == "tar")
            return tar;

        // Deliberately omit the extension: detection must inspect the archive content.
        var compressedTar = Path.Combine(_tempDir, "download");
        using var output = File.Create(compressedTar);
        using var gzip = new GZipStream(output, CompressionLevel.Fastest);
        using var input = File.OpenRead(tar);
        input.CopyTo(gzip);
        return compressedTar;
    }

    private static void AssertNoExtractedFiles(string destination)
    {
        if (Directory.Exists(destination))
            Assert.Empty(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // Progress<T> dispatches asynchronously; capture reports synchronously for deterministic assertions.
    private sealed class InlineProgress(Action<ExtractionProgress> report) : IProgress<ExtractionProgress>
    {
        public void Report(ExtractionProgress value) => report(value);
    }
}
