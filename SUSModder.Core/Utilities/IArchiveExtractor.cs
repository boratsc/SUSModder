using System.Threading;

namespace SUSModder.Core.Utilities;

/// <summary>
/// Postęp operacji ekstrakcji archiwum.
/// </summary>
/// <param name="BytesExtracted">Liczba bajtów już rozpakowanych.</param>
/// <param name="TotalBytes">Całkowity rozmiar (0 jeśli nieznany, np. dla 7z solid archive).</param>
/// <param name="CurrentFile">Nazwa aktualnie rozpakowywanego pliku (opcjonalne).</param>
/// <param name="PercentComplete">Procent ukończenia (0-100) od SharpCompress, jeśli dostępny.</param>
public record ExtractionProgress(
    long BytesExtracted,
    long TotalBytes,
    string? CurrentFile = null,
    double? PercentComplete = null);

/// <summary>
/// Abstrakcja dla ekstrakcji archiwów (7z, zip, rar, itp.) z opcjonalnym hasłem i progresem.
/// </summary>
public interface IArchiveExtractor
{
    /// <summary>
    /// Ekstrakcja archiwum do wskazanego katalogu.
    /// </summary>
    /// <param name="archivePath">Ścieżka do archiwum.</param>
    /// <param name="extractPath">Katalog docelowy.</param>
    /// <param name="password">Opcjonalne hasło (dla 7z).</param>
    /// <param name="progress">Raportowanie postępu.</param>
    /// <param name="ct">Token anulowania.</param>
    Task ExtractAsync(
        string archivePath,
        string extractPath,
        string? password = null,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken ct = default);
}
