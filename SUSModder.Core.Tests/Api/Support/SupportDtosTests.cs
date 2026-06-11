using System.Text.Json;
using SUSModder.Core.Api.Support;

namespace SUSModder.Core.Tests.Api.Support;

public sealed class SupportDtosTests
{
    [Fact]
    public void SupportQueryRequest_Serializes_Correctly()
    {
        var request = new SupportQueryRequest
        {
            Language = "pl",
            Problem = "Test problem",
            App = new SupportAppInfo { Version = "3.0.0", PlatformMode = "steam", UpdateChannel = "release" },
            Diagnostics = new SupportDiagnosticsInfo
            {
                DiagnosisCodes = ["launch.bepinex.log_missing"],
                WasRunAsAdmin = false
            }
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        Assert.Contains("\"language\":\"pl\"", json);
        Assert.Contains("\"problem\":\"Test problem\"", json);
        Assert.Contains("\"version\":\"3.0.0\"", json);
        Assert.Contains("\"platformMode\":\"steam\"", json);
        Assert.Contains("launch.bepinex.log_missing", json);
    }

    [Fact]
    public void SupportQueryRequest_NullApp_SerializesWithoutApp()
    {
        var request = new SupportQueryRequest
        {
            Language = "en",
            Problem = "Test",
            App = null,
            Diagnostics = null
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        Assert.DoesNotContain("\"app\"", json);
        Assert.DoesNotContain("\"diagnostics\"", json);
    }

    [Fact]
    public void SupportFeedbackRequest_Serializes_Correctly()
    {
        var request = new SupportFeedbackRequest
        {
            SupportSessionId = "SUP-2026-000123",
            Result = "helped",
            ArticleIds = ["firewall_001"],
            DiagnosisCodes = ["launch.firewall.rule_missing_or_blocked"],
            Language = "pl"
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        Assert.Contains("\"result\":\"helped\"", json);
        Assert.Contains("SUP-2026-000123", json);
    }

    [Fact]
    public void SupportActionCode_All_ContainsExpected()
    {
        Assert.Contains(SupportActionCode.OpenLogs, SupportActionCode.All);
        Assert.Contains(SupportActionCode.GenerateReport, SupportActionCode.All);
        Assert.Contains(SupportActionCode.OpenDiscord, SupportActionCode.All);
        Assert.Contains(SupportActionCode.None, SupportActionCode.All);
    }

    [Fact]
    public void SupportActionCode_IsValid_TrueForKnown()
    {
        Assert.True(SupportActionCode.IsValid(SupportActionCode.OpenLogs));
        Assert.True(SupportActionCode.IsValid(SupportActionCode.None));
    }

    [Fact]
    public void SupportActionCode_IsValid_FalseForUnknown()
    {
        Assert.False(SupportActionCode.IsValid("run_powershell"));
        Assert.False(SupportActionCode.IsValid(""));
        Assert.False(SupportActionCode.IsValid(null!));
    }
}
