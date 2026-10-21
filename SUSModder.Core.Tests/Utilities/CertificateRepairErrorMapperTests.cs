using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Utilities;

public class CertificateRepairErrorMapperTests
{
    [Theory]
    [InlineData(-2147012894, CertificateRepairErrorMapper.KindTimeout, "0x80072EE2")]
    [InlineData(unchecked((int)0x80072EE7), CertificateRepairErrorMapper.KindNameNotResolved, "0x80072EE7")]
    [InlineData(unchecked((int)0x80072EFD), CertificateRepairErrorMapper.KindCannotConnect, "0x80072EFD")]
    [InlineData(unchecked((int)0x80070005), CertificateRepairErrorMapper.KindAccessDenied, "0x80070005")]
    [InlineData(1, CertificateRepairErrorMapper.KindUnknown, "0x00000001")]
    public void Classify_MapsKnownHresults(int exitCode, string expectedKind, string expectedHex)
    {
        var result = CertificateRepairErrorMapper.Classify(exitCode);

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedHex, result.HexCode);
        Assert.Equal(exitCode, result.ExitCode);
    }
}
