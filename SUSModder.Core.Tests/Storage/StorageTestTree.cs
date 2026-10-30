namespace SUSModder.Core.Tests.Storage;

/// <summary>
/// Sztuczne drzewo plików w katalogu tymczasowym — bezpieczne, izolowane, sprzątane po teście.
/// </summary>
internal sealed class StorageTestTree : IDisposable
{
    public string Root { get; }

    public StorageTestTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "susmodder-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string CreateDirectory(string relativePath)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(full);
        return full;
    }

    public string CreateFile(string relativePath, int sizeInBytes = 0)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var payload = new byte[sizeInBytes];
        for (var i = 0; i < sizeInBytes; i++)
            payload[i] = (byte)(i % 251);

        File.WriteAllBytes(full, payload);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Sprzątanie katalogu testowego nie może wywrócić testu.
        }
    }
}
