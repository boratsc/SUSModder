using SUSModder.Core.Storage;
using Xunit;

namespace SUSModder.Core.Tests.Storage;

/// <summary>
/// Deterministyczny probe systemu plików — pozwala symulować błędy dostępu
/// i opóźnienia bez ingerencji w uprawnienia systemowe.
/// Wyliczenie jest strumieniowe (jak w produkcji), więc błędy są zgłaszane przy pobraniu wpisu.
/// </summary>
internal sealed class FakeStorageFileSystemProbe : IStorageFileSystemProbe
{
    private readonly IStorageFileSystemProbe _inner = DefaultStorageFileSystemProbe.Instance;

    /// <summary>Katalogi, których odczyt kończy się błędem dostępu.</summary>
    public HashSet<string> AccessDeniedDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Katalogi, których odczyt kończy się błędem I/O.</summary>
    public HashSet<string> FailingDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pliki, które zniknęły w trakcie skanu.</summary>
    public HashSet<string> MissingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Wpisy, których atrybutów nie da się odczytać.</summary>
    public HashSet<string> FailingAttributePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pliki, których rozmiaru nie da się odczytać.</summary>
    public HashSet<string> FailingLengthPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opóźnienie symulowane przed każdym odczytem katalogu.</summary>
    public TimeSpan DelayPerDirectory { get; set; } = TimeSpan.Zero;

    /// <summary>Liczba odczytów katalogów.</summary>
    public int EnumerateCallCount { get; private set; }

    public IEnumerable<FileSystemInfo> EnumerateEntries(string directoryPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EnumerateCallCount++;

        if (DelayPerDirectory > TimeSpan.Zero)
            Thread.Sleep(DelayPerDirectory);

        if (AccessDeniedDirectories.Contains(directoryPath))
            throw new UnauthorizedAccessException($"Brak dostępu (test): {directoryPath}");

        if (FailingDirectories.Contains(directoryPath))
            throw new IOException($"Błąd I/O (test): {directoryPath}");

        foreach (var entry in _inner.EnumerateEntries(directoryPath, cancellationToken))
            yield return entry;
    }

    public FileAttributes GetAttributes(FileSystemInfo entry)
    {
        if (FailingAttributePaths.Contains(entry.FullName))
            throw new IOException($"Atrybuty nieczytelne (test): {entry.FullName}");

        return _inner.GetAttributes(entry);
    }

    public long GetFileLength(FileInfo file)
    {
        if (FailingLengthPaths.Contains(file.FullName))
            throw new IOException($"Rozmiar nieczytelny (test): {file.FullName}");

        if (MissingFiles.Contains(file.FullName))
            throw new FileNotFoundException("Plik zniknął (test).", file.FullName);

        if (!file.Exists)
            throw new FileNotFoundException("Plik zniknął (test).", file.FullName);

        return _inner.GetFileLength(file);
    }
}
