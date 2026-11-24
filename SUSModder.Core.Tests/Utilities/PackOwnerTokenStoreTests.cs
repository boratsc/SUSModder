using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Utilities;

public class PackOwnerTokenStoreTests
{
    private const string PackCode = "ABCD-EFGH-JKLM";
    private static readonly string Token = new('d', 64);

    [Fact]
    public void SaveAndTryGet_Roundtrip_ThroughDpapi()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var path = TestPath();
        try
        {
            PackOwnerTokenStore.Save(path, PackCode, Token);

            Assert.True(PackOwnerTokenStore.TryGet(path, PackCode, out var read));
            Assert.Equal(Token, read);
            // Plik nie zawiera jawnego tokenu — tylko blob DPAPI.
            Assert.DoesNotContain(Token, File.ReadAllText(path));
        }
        finally { DeleteTestPath(path); }
    }

    [Fact]
    public void Save_KeepsOtherPacks_AndOverwritesSameCode()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var path = TestPath();
        var otherCode = "WXYZ-ABCD-2345";
        var otherToken = new string('e', 64);
        var newToken = new string('f', 64);
        try
        {
            PackOwnerTokenStore.Save(path, PackCode, Token);
            PackOwnerTokenStore.Save(path, otherCode, otherToken);
            PackOwnerTokenStore.Save(path, PackCode, newToken);

            Assert.True(PackOwnerTokenStore.TryGet(path, PackCode, out var read));
            Assert.Equal(newToken, read);
            Assert.True(PackOwnerTokenStore.TryGet(path, otherCode, out var other));
            Assert.Equal(otherToken, other);
        }
        finally { DeleteTestPath(path); }
    }

    [Fact]
    public void Save_NormalizesPackCode()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var path = TestPath();
        try
        {
            PackOwnerTokenStore.Save(path, "abcd-efgh-jklm", Token);
            Assert.True(PackOwnerTokenStore.TryGet(path, PackCode, out var read));
            Assert.Equal(Token, read);
        }
        finally { DeleteTestPath(path); }
    }

    [Fact]
    public void Save_LeavesNoTemporaryFiles()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var path = TestPath();
        try
        {
            PackOwnerTokenStore.Save(path, PackCode, Token);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }
        finally { DeleteTestPath(path); }
    }

    [Fact]
    public void TryGet_MissingFileOrEntry_ReturnsFalse()
    {
        var path = TestPath();
        Assert.False(PackOwnerTokenStore.TryGet(path, PackCode, out _));
        Assert.False(PackOwnerTokenStore.TryGet(null, PackCode, out _));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"ABCD-EFGH-JKLM\":\"!!!not-base64!!!\"}")]
    [InlineData("{\"ABCD-EFGH-JKLM\":\"AQIDBA==\"}")] // poprawny base64, ale nie blob DPAPI
    public void TryGet_CorruptedEntry_ReturnsFalse(string fileContent)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var path = TestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, fileContent);
        try
        {
            Assert.False(PackOwnerTokenStore.TryGet(path, PackCode, out _));
        }
        finally { DeleteTestPath(path); }
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void TryGet_DecryptedValueNot64Hex_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var path = TestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var blob = Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes("za-krotki"), null, DataProtectionScope.CurrentUser));
        File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, string> { [PackCode] = blob }));
        try
        {
            Assert.False(PackOwnerTokenStore.TryGet(path, PackCode, out _));
        }
        finally { DeleteTestPath(path); }
    }

    [Fact]
    public void Save_CorruptedStoreFile_ThrowsInsteadOfOverwritingOtherTokens()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var path = TestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not-json");
        try
        {
            Assert.ThrowsAny<Exception>(() => PackOwnerTokenStore.Save(path, PackCode, Token));
            Assert.Equal("not-json", File.ReadAllText(path));
        }
        finally { DeleteTestPath(path); }
    }

    [Fact]
    public void Save_IoFailure_Propagates()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        // Ścieżka magazynu jest istniejącym katalogiem — zapis atomowy musi rzucić, nie połknąć.
        var parent = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(parent, "pack-owner-tokens");
        Directory.CreateDirectory(dir);
        try
        {
            var ex = Record.Exception(() => PackOwnerTokenStore.Save(dir, PackCode, Token));
            Assert.NotNull(ex);
            Assert.True(ex is IOException or UnauthorizedAccessException, $"Unexpected: {ex.GetType().Name}");
            // Sprzątanie .tmp w finally — po nieudanym Move nie zostaje śmieć.
            Assert.Empty(Directory.GetFiles(parent, "*.tmp"));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public void SaveAndTryGet_UppercaseToken_RoundtripVerbatim()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var path = TestPath();
        var uppercase = new string('D', 64);
        try
        {
            PackOwnerTokenStore.Save(path, PackCode, uppercase);
            Assert.True(PackOwnerTokenStore.TryGet(path, PackCode, out var read));
            Assert.Equal(uppercase, read); // bez normalizacji case
        }
        finally { DeleteTestPath(path); }
    }

    [Fact]
    public void GetState_MissingFileOrEntry_ReturnsMissing()
    {
        var path = TestPath();
        Assert.Equal(OwnerTokenState.Missing, PackOwnerTokenStore.GetState(path, PackCode, out _));
        Assert.Equal(OwnerTokenState.Missing, PackOwnerTokenStore.GetState(null, PackCode, out _));

        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        try
        {
            PackOwnerTokenStore.Save(path, "WXYZ-ABCD-2345", Token);
            Assert.Equal(OwnerTokenState.Missing, PackOwnerTokenStore.GetState(path, PackCode, out _));
            Assert.Equal(OwnerTokenState.Available, PackOwnerTokenStore.GetState(path, "WXYZ-ABCD-2345", out var read));
            Assert.Equal(Token, read);
        }
        finally { DeleteTestPath(path); }
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"ABCD-EFGH-JKLM\":\"!!!not-base64!!!\"}")]
    [InlineData("{\"ABCD-EFGH-JKLM\":\"AQIDBA==\"}")] // poprawny base64, ale nie blob DPAPI
    public void GetState_CorruptedFileOrEntry_ReturnsUnreadable(string fileContent)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var path = TestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, fileContent);
        try
        {
            Assert.Equal(OwnerTokenState.Unreadable, PackOwnerTokenStore.GetState(path, PackCode, out _));
            Assert.False(PackOwnerTokenStore.TryGet(path, PackCode, out _));
        }
        finally { DeleteTestPath(path); }
    }

    [Fact]
    public void Save_SingleCorruptedEntry_DoesNotBlockOtherCodes()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var path = TestPath();
        var otherCode = "WXYZ-ABCD-2345";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Uszkodzony wpis (zły base64), ale poprawny JSON całej mapy.
        File.WriteAllText(path, "{\"ABCD-EFGH-JKLM\":\"!!!not-base64!!!\"}");
        try
        {
            PackOwnerTokenStore.Save(path, otherCode, Token);

            Assert.True(PackOwnerTokenStore.TryGet(path, otherCode, out var read));
            Assert.Equal(Token, read);
            Assert.False(PackOwnerTokenStore.TryGet(path, PackCode, out _));
            // Uszkodzony wpis zostaje w pliku (nie kasujemy cudzych danych).
            Assert.Contains("!!!not-base64!!!", File.ReadAllText(path));
        }
        finally { DeleteTestPath(path); }
    }

    [Fact]
    public void Save_NonStringEntry_CorruptsWholeFile_BlocksFailClosed()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var path = TestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Wartość niebędąca stringiem uszkadza parsowanie CAŁEJ mapy — fail-closed blokuje zapis.
        File.WriteAllText(path, "{\"ABCD-EFGH-JKLM\":123}");
        try
        {
            Assert.ThrowsAny<Exception>(() => PackOwnerTokenStore.Save(path, "WXYZ-ABCD-2345", Token));
            Assert.Equal("{\"ABCD-EFGH-JKLM\":123}", File.ReadAllText(path));
        }
        finally { DeleteTestPath(path); }
    }

    [Fact]
    public void GetStates_MixedEntries_SingleFileRead_CorrectStates()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var path = TestPath();
        var unreadableCode = "MNPR-STUV-WXYZ";
        try
        {
            PackOwnerTokenStore.Save(path, PackCode, Token);
            // Dopisz uszkodzony wpis obok poprawnego.
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
            map[unreadableCode] = "!!!not-base64!!!";
            File.WriteAllText(path, JsonSerializer.Serialize(map));

            var states = PackOwnerTokenStore.GetStates(path, new[] { PackCode, unreadableCode, "2345-6789-ABCD", "nie-kod" });

            Assert.Equal(OwnerTokenState.Available, states[PackCode]);
            Assert.Equal(OwnerTokenState.Unreadable, states[unreadableCode]);
            Assert.Equal(OwnerTokenState.Missing, states["2345-6789-ABCD"]);
            Assert.Equal(OwnerTokenState.Missing, states["nie-kod"]);
        }
        finally { DeleteTestPath(path); }
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("")]
    public void Save_InvalidToken_Throws(string token)
    {
        Assert.Throws<ArgumentException>(() => PackOwnerTokenStore.Save(TestPath(), PackCode, token));
    }

    [Fact]
    public void Save_InvalidPackCode_Throws()
    {
        Assert.Throws<ArgumentException>(() => PackOwnerTokenStore.Save(TestPath(), "not-a-code", Token));
    }

    private static string TestPath() =>
        Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"), "pack-owner-tokens");

    private static void DeleteTestPath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}