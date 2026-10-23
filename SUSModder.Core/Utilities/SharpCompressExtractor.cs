using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace SUSModder.Core.Utilities;

/// <summary>
/// Implementacja <see cref="IArchiveExtractor"/> oparta na SharpCompress.
/// Obsługuje 7z (z hasłem), ZIP, RAR i inne formaty.
/// </summary>
/// <remarks>
/// <para>
/// Ekstrakcja jest w pełni synchroniczna (CPU-bound LZMA), uruchamiana na ThreadPool
/// przez <c>Task.Run</c>. Wybór ścieżki zależy od <c>archive.IsSolid</c>:
/// </para>
/// <list type="bullet">
///   <item><description><b>Solid (7z)</b> — <c>ExtractAllEntries()</c> zwraca <c>IReader</c>,
///   który utrzymuje jeden kontekst dekompresji (O(n)).</description></item>
///   <item><description><b>Non-solid (ZIP)</b> — iteracja <c>archive.Entries</c> z per-entry
///   <c>WriteToDirectory</c>, każdy wpis niezależny (O(n)).</description></item>
///   <item><description><b>Skompresowany TAR</b> — <c>ReaderFactory.OpenReader</c>,
///   osobny przebieg dla sum postępu i osobny dla ekstrakcji (O(n)).</description></item>
///   <item><description>W obu ścieżkach progres raportowany per-plik z bajtami
///   i licznikiem plików.</description></item>
/// </list>
/// <para>
/// Unikamy per-entry <c>archive.Entries[i].WriteToDirectory</c> dla solid 7z, bo to
/// O(n²) — każdy wpis dekompresuje od początku bloku.
/// </para>
/// </remarks>
public class SharpCompressExtractor : IArchiveExtractor
{
    /// <inheritdoc />
    public async Task ExtractAsync(
        string archivePath,
        string extractPath,
        string? password = null,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractPath);

        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archiwum nie istnieje.", archivePath);

        Directory.CreateDirectory(extractPath);

        var options = new ExtractionOptions
        {
            ExtractFullPath = true,
            Overwrite = true,
            PreserveFileTime = true
        };

        var readerOptions = new ReaderOptions
        {
            Password = password,
            LeaveStreamOpen = false,
            LookForHeader = true
        };

        // CPU-bound LZMA → ThreadPool (Task.Run zamiast async API)
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            // SharpCompress 0.50 no longer exposes compressed TARs through IArchive.
            // Keep content-based detection (downloads need not retain their extension).
            if (ArchiveFactory.GetArchiveInformation(archivePath, readerOptions) is null)
            {
                ExtractStreamingTar(archivePath, extractPath, readerOptions, options, progress, ct);
                return;
            }

            using var archive = ArchiveFactory.OpenArchive(archivePath, readerOptions);

            // Pre-compute całkowity rozmiar i liczbę plików
            long totalBytes = archive.TotalUncompressedSize;
            long totalFiles = archive.Entries.Count(e => !e.IsDirectory);

            ct.ThrowIfCancellationRequested();
            progress?.Report(new ExtractionProgress(0, totalBytes, "Rozpakowywanie..."));

            long extractedBytes = 0;
            int fileIndex = 0;

            if (archive.IsSolid)
            {
                // Solid archive (7z) — ExtractAllEntries zwraca IReader, który utrzymuje
                // jeden kontekst dekompresji (O(n)). MoveToNextEntry + WriteEntryToDirectory
                // raportuje progres per-plik.
                using var reader = archive.ExtractAllEntries();
                ExtractReader(reader, extractPath, options, totalBytes, totalFiles, progress, ct);
            }
            else
            {
                // Non-solid (ZIP) — każdy entry jest niezależny, WriteToDirectory na każdym = O(n)
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!entry.IsDirectory)
                    {
                        fileIndex++;
                        progress?.Report(new ExtractionProgress(
                            extractedBytes, totalBytes, entry.Key));

                        InstallPathWriteHelper.RunWithWriteRetry(
                            () => entry.WriteToDirectory(extractPath, options),
                            extractPath,
                            entry.Key);

                        extractedBytes += entry.Size;
                        progress?.Report(new ExtractionProgress(
                            extractedBytes, totalBytes,
                            $"({fileIndex}/{totalFiles}) {entry.Key}"));
                    }
                }
            }

            progress?.Report(new ExtractionProgress(
                totalBytes, totalBytes,
                $"Rozpakowano {totalFiles} plików"));

        }, ct).ConfigureAwait(false);
    }

    private static void ExtractStreamingTar(
        string archivePath,
        string extractPath,
        ReaderOptions readerOptions,
        ExtractionOptions options,
        IProgress<ExtractionProgress>? progress,
        CancellationToken ct)
    {
        long totalBytes = 0;
        long totalFiles = 0;
        // As with archive.Entries, enumerate once for accurate progress totals.
        using (var counter = ReaderFactory.OpenReader(archivePath, readerOptions))
        {
            if (counter.Type != ArchiveType.Tar)
                throw new InvalidFormatException("Unsupported archive format.");

            while (counter.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                if (!counter.Entry.IsDirectory)
                {
                    totalBytes += counter.Entry.Size;
                    totalFiles++;
                }
            }
        }

        ct.ThrowIfCancellationRequested();
        progress?.Report(new ExtractionProgress(0, totalBytes, "Rozpakowywanie..."));
        using var reader = ReaderFactory.OpenReader(archivePath, readerOptions);
        ExtractReader(reader, extractPath, options, totalBytes, totalFiles, progress, ct);
        progress?.Report(new ExtractionProgress(
            totalBytes, totalBytes, $"Rozpakowano {totalFiles} plików"));
    }

    private static void ExtractReader(
        IReader reader,
        string extractPath,
        ExtractionOptions options,
        long totalBytes,
        long totalFiles,
        IProgress<ExtractionProgress>? progress,
        CancellationToken ct)
    {
        long extractedBytes = 0;
        int fileIndex = 0;
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.Entry.IsDirectory)
                continue;

            fileIndex++;
            progress?.Report(new ExtractionProgress(extractedBytes, totalBytes, reader.Entry.Key));
            InstallPathWriteHelper.RunWithWriteRetry(
                () => reader.WriteEntryToDirectory(extractPath, options),
                extractPath,
                reader.Entry.Key);
            extractedBytes += reader.Entry.Size;
            progress?.Report(new ExtractionProgress(
                extractedBytes, totalBytes, $"({fileIndex}/{totalFiles}) {reader.Entry.Key}"));
        }
    }
}
