using System.Text.Json;
using PingMonitor.Web.Contracts.Results;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class CheckResultDtoJsonTests
{
    [Fact]
    public void Deserialize_OldIntegerRoundTripMsPayload_StillReadsSuccessfully()
    {
        const string json = """
            {
              "assignmentId": "assignment-1",
              "endpointId": "endpoint-1",
              "checkType": "icmp",
              "checkedAtUtc": "2026-05-28T00:00:00Z",
              "success": true,
              "roundTripMs": 2,
              "errorCode": null,
              "errorMessage": null
            }
            """;

        var result = JsonSerializer.Deserialize<CheckResultDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(result);
        Assert.Equal(2m, result.RoundTripMs);
    }

    [Fact]
    public void Deserialize_DecimalRoundTripMsPayload_PreservesPrecision()
    {
        const string json = """
            {
              "assignmentId": "assignment-1",
              "endpointId": "endpoint-1",
              "checkType": "icmp",
              "checkedAtUtc": "2026-05-28T00:00:00Z",
              "success": true,
              "roundTripMs": 2.345,
              "errorCode": null,
              "errorMessage": null
            }
            """;

        var result = JsonSerializer.Deserialize<CheckResultDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(result);
        Assert.Equal(2.345m, result.RoundTripMs);
    }
}
