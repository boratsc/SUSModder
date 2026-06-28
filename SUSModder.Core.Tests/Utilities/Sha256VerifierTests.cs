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
}
