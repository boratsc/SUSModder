using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Utilities;

public class AnonymousUserHashTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcd")]
    [InlineData("2a4bb6c85cd24aa280fd33dabe6cf6e8")] // GUID "N" = 32 hex — bug historyczny
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // uppercase
    [InlineData("g111111111111111111111111111111111111111111111111111111111111111")] // non-hex
    public void IsValid_RejectsInvalidFormats(string? value)
    {
        Assert.False(AnonymousUserHash.IsValid(value));
    }

    [Fact]
    public void IsValid_AcceptsLowercase64Hex()
    {
        var hash = new string('a', 64);
        Assert.True(AnonymousUserHash.IsValid(hash));
    }

    [Fact]
    public void CreateFallback_ReturnsValid64Hex_NotRawGuid()
    {
        var hash = AnonymousUserHashStore.GetOrCreate(TestPath(), () => new string('a', 64));

        Assert.True(AnonymousUserHash.IsValid(hash));
        Assert.Equal(64, hash.Length);
        Assert.NotEqual(32, hash.Length);
    }

    [Fact]
    public void EnsureValid_RehashesInvalidGuidFallback()
    {
        var rawGuid = Guid.NewGuid().ToString("N");
        Assert.Equal(32, rawGuid.Length);

        var fixedHash = AnonymousUserHash.EnsureValid(rawGuid);

        Assert.True(AnonymousUserHash.IsValid(fixedHash));
        Assert.NotEqual(rawGuid, fixedHash);
    }

    [Fact]
    public void EnsureValid_KeepsAlreadyValidHash()
    {
        var valid = new string('b', 64);
        Assert.Equal(valid, AnonymousUserHash.EnsureValid(valid));
    }

    [Fact]
    public void CreateFallback_IsStableAcrossFreshStoreCalls()
    {
        var path = TestPath();
        try
        {
            var first = AnonymousUserHashStore.GetOrCreate(path, () => new string('a', 64));
            var second = AnonymousUserHashStore.GetOrCreate(path, () => new string('b', 64));
            Assert.Equal(first, second);
        }
        finally { DeleteTestPath(path); }
    }

    [Fact]
    public void FallbackPersistsAcrossStoreInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "hash");
        try
        {
            var first = AnonymousUserHashStore.GetOrCreate(path, () => new string('c', 64));
            var restarted = AnonymousUserHashStore.GetOrCreate(path, () => new string('d', 64));

            Assert.Equal(new string('c', 64), first);
            Assert.Equal(first, restarted);
            Assert.Equal(first, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)!))
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void CorruptStoreIsReplacedOnceAndThenStable()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "hash");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "invalid");
        var generations = 0;
        try
        {
            var first = AnonymousUserHashStore.GetOrCreate(path, () => { generations++; return new string('e', 64); });
            var next = AnonymousUserHashStore.GetOrCreate(path, () => { generations++; return new string('f', 64); });

            Assert.Equal(new string('e', 64), first);
            Assert.Equal(first, next);
            Assert.Equal(1, generations);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EnsureValidEmptyUsesStableFallback(string? value)
    {
        var first = AnonymousUserHash.EnsureValid(value);
        Assert.Equal(first, AnonymousUserHash.EnsureValid(value));
        Assert.True(AnonymousUserHash.IsValid(first));
    }

    [Fact]
    public void FailedStoreAccessKeepsMemoizedValueAndLeavesNoTemporaryFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"), "hash");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var generations = 0;
        var blocker = Path.GetDirectoryName(path)!;
        try
        {
            var first = AnonymousUserHashStore.GetOrCreate(path, () => { generations++; return new string('a', 64); });
            Assert.Empty(Directory.GetFiles(blocker, "*.tmp"));
            File.Delete(path);
            Directory.Delete(blocker);
            File.WriteAllText(blocker, "file blocks directory");
            var second = AnonymousUserHashStore.GetOrCreate(path, () => { generations++; return new string('b', 64); });
            var third = AnonymousUserHashStore.GetOrCreate(path, () => { generations++; return new string('c', 64); });
            Assert.Equal(first, second);
            Assert.Equal(second, third);
            Assert.Equal(1, generations);
        }
        finally { DeleteTestPath(path); }
    }

    [Fact]
    public void InvalidPathKeepsMemoizedValueAcrossCalls()
    {
        const string path = "invalid\0hash";
        var generations = 0;
        var first = AnonymousUserHashStore.GetOrCreate(path, () => { generations++; return new string('d', 64); });
        var second = AnonymousUserHashStore.GetOrCreate(path, () => { generations++; return new string('e', 64); });
        Assert.Equal(first, second);
        Assert.Equal(1, generations);
    }

    [Fact]
    public void EnsureValidEmptyUsesPersistentFallback()
    {
        var path = TestPath();
        var first = AnonymousUserHashStore.GetOrCreate(path, () => new string('a', 64));
        var second = AnonymousUserHashStore.GetOrCreate(path, () => new string('b', 64));

        Assert.Equal(first, second);
        Assert.True(AnonymousUserHash.IsValid(first));
    }

    private static string TestPath() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "hash");

    private static void DeleteTestPath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (File.Exists(directory)) File.Delete(directory);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
