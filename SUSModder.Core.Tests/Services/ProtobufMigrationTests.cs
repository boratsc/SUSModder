using ProtoBuf;
using SUSModder.Core.GameIntegration.Steam;

namespace SUSModder.Core.Tests.Services;

public sealed class ProtobufMigrationTests
{
    // Fixed legacy protobuf map fields: 2 = penalties, 4 = tokens, 5 = guard data.
    // These bytes are independent of the current serializer and contain only synthetic values.
    private static readonly byte[] LegacyWireBytes =
    [
        0x12, 0x05, 0x0A, 0x01, 0x73, 0x10, 0x07,
        0x22, 0x06, 0x0A, 0x01, 0x75, 0x12, 0x01, 0x74,
        0x2A, 0x06, 0x0A, 0x01, 0x75, 0x12, 0x01, 0x67
    ];

    [Fact]
    public void Deserialize_PreservesLegacyAccountSettingsWireContract()
    {
        using var input = new MemoryStream(LegacyWireBytes, writable: false);

        var store = Serializer.Deserialize<DdAccountSettingsStore>(input);

        Assert.Equal(new KeyValuePair<string, int>("s", 7), Assert.Single(store.ContentServerPenalty));
        Assert.Equal(new KeyValuePair<string, string>("u", "t"), Assert.Single(store.LoginTokens));
        Assert.Equal(new KeyValuePair<string, string>("u", "g"), Assert.Single(store.GuardData));
    }

    [Fact]
    public void Serialize_ProducesLegacyAccountSettingsWireBytes()
    {
        var store = new DdAccountSettingsStore
        {
            ContentServerPenalty = { ["s"] = 7 },
            LoginTokens = { ["u"] = "t" },
            GuardData = { ["u"] = "g" }
        };
        using var output = new MemoryStream();

        Serializer.Serialize(output, store);

        Assert.Equal(LegacyWireBytes, output.ToArray());
    }
}
