using System.Text;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Utilities;

public class Sha256VerifierTests
{
    [Fact]
    public void VerifyBytes_ReturnsTrue_ForMatchingSha256()
    {
        var bytes = Encoding.UTF8.GetBytes("susmodder");
        var hash = Sha256Verifier.ComputeHex(bytes);

        Assert.True(Sha256Verifier.VerifyBytes(bytes, hash));
    }

    [Fact]
    public void VerifyBytes_ReturnsFalse_ForDifferentSha256()
    {
        var bytes = Encoding.UTF8.GetBytes("susmodder");
        var otherHash = Sha256Verifier.ComputeHex(Encoding.UTF8.GetBytes("different"));

        Assert.False(Sha256Verifier.VerifyBytes(bytes, otherHash));
    }

    [Fact]
    public void VerifyBytes_ReturnsFalse_ForInvalidHash()
    {
        var bytes = Encoding.UTF8.GetBytes("susmodder");

        Assert.False(Sha256Verifier.VerifyBytes(bytes, "not-a-sha"));
        Assert.False(Sha256Verifier.VerifyBytes(bytes, new string('z', 64)));
    }

    [Fact]
    public void IsWellFormedHash_AcceptsValidHex64()
    {
        var hash = Sha256Verifier.ComputeHex(Encoding.UTF8.GetBytes("ok"));
        Assert.True(Sha256Verifier.IsWellFormedHash(hash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void IsWellFormedHash_RejectsInvalid(string? value)
    {
        Assert.False(Sha256Verifier.IsWellFormedHash(value));
    }

    [Fact]
    public async Task VerifyFileAsync_Match_ReturnsTrue()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sha256-ok-{Guid.NewGuid():N}.bin");
        try
        {
            var bytes = Encoding.UTF8.GetBytes("file-ok");
            await File.WriteAllBytesAsync(path, bytes);
            var hash = Sha256Verifier.ComputeHex(bytes);

            Assert.True(await Sha256Verifier.VerifyFileAsync(path, hash));
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task VerifyFileAsync_Mismatch_DeletesAndReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sha256-bad-{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("file-bad"));
            var otherHash = Sha256Verifier.ComputeHex(Encoding.UTF8.GetBytes("other"));

            Assert.False(await Sha256Verifier.VerifyFileAsync(path, otherHash, deleteOnMismatch: true));
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task VerifyFileAsync_MissingHash_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sha256-missing-{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("x"));
            Assert.False(await Sha256Verifier.VerifyFileAsync(path, null));
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
