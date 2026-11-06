using System.Globalization;
using SUSModder.Core.Storage;
using Xunit;

namespace SUSModder.Core.Tests.Storage;

public class StorageSizeFormatterTests
{
    [Theory]
    [InlineData(0L, StorageSizeUnit.Bytes, 0d)]
    [InlineData(512L, StorageSizeUnit.Bytes, 512d)]
    [InlineData(1024L, StorageSizeUnit.KibiBytes, 1d)]
    [InlineData(1536L, StorageSizeUnit.KibiBytes, 1.5d)]
    [InlineData(1024L * 1024L, StorageSizeUnit.MebiBytes, 1d)]
    [InlineData(1024L * 1024L * 1024L, StorageSizeUnit.GibiBytes, 1d)]
    public void Split_UsesBinaryUnits(long bytes, StorageSizeUnit expectedUnit, double expectedValue)
    {
        var display = StorageSizeFormatter.Split(bytes);

        Assert.Equal(expectedUnit, display.Unit);
        Assert.Equal(expectedValue, display.Value, 3);
    }

    [Fact]
    public void Format_UsesBinarySuffixes_NotDecimalGigabytes()
    {
        var text = StorageSizeFormatter.Format(1024L * 1024L * 1024L, CultureInfo.InvariantCulture);

        Assert.Equal("1 GiB", text);
        Assert.DoesNotContain("GB", text);
    }

    [Fact]
    public void Format_KeepsFractionalValues()
    {
        var text = StorageSizeFormatter.Format(1536L, CultureInfo.InvariantCulture);

        Assert.Equal("1.5 KiB", text);
    }

    [Fact]
    public void Format_UsesCultureForDecimalSeparator()
    {
        var text = StorageSizeFormatter.Format(1536L, new CultureInfo("pl-PL"));

        Assert.Equal("1,5 KiB", text);
    }

    [Fact]
    public void Format_BytesAreWholeNumbers()
    {
        Assert.Equal("0 B", StorageSizeFormatter.Format(0L, CultureInfo.InvariantCulture));
        Assert.Equal("512 B", StorageSizeFormatter.Format(512L, CultureInfo.InvariantCulture));
    }
}
