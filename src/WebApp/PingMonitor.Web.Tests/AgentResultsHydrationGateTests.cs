using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PingMonitor.Web.Contracts.Results;
using PingMonitor.Web.Controllers;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Metrics;
using PingMonitor.Web.Support;

using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AgentResultsHydrationGateTests
{
    [Fact]
    public async Task PostAsync_DuringHydration_Returns503AndDoesNotIngest()
    {
        var hydrationState = new RollingWindowHydrationState();
        hydrationState.MarkRunning(DateTimeOffset.Parse("2026-05-29T00:00:00Z"));
        var ingestion = new FakeResultIngestionService();
        var controller = BuildController(hydrationState, ingestion);

        var result = await controller.PostAsync(BuildRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.Equal("30", controller.Response.Headers.RetryAfter.ToString());
        var body = Assert.IsType<ApiErrorResponse>(objectResult.Value);
        Assert.Equal("ingestion_temporarily_unavailable", body.Error.Code);
        Assert.Contains("hydration", body.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Retry", body.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, ingestion.CallCount);
    }

    [Fact]
    public async Task PostAsync_AfterHydrationCompletes_IngestsNormally()
    {
        var hydrationState = new RollingWindowHydrationState();
        hydrationState.MarkRunning(DateTimeOffset.Parse("2026-05-29T00:00:00Z"));
        hydrationState.MarkComplete(DateTimeOffset.Parse("2026-05-29T00:01:00Z"));
        var ingestion = new FakeResultIngestionService();
        var controller = BuildController(hydrationState, ingestion);

        var result = await controller.PostAsync(BuildRequest(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<SubmitResultsResponse>(okResult.Value);
        Assert.True(body.Accepted);
        Assert.Equal(1, body.AcceptedCount);
        Assert.Equal(1, ingestion.CallCount);
    }

    [Fact]
    public async Task PostAsync_WhenHydrationFailed_Returns503AndDoesNotIngest()
    {
        var hydrationState = new RollingWindowHydrationState();
        hydrationState.MarkRunning(DateTimeOffset.Parse("2026-05-29T00:00:00Z"));
        hydrationState.MarkFailed(DateTimeOffset.Parse("2026-05-29T00:01:00Z"), "database timeout");
        var ingestion = new FakeResultIngestionService();
        var controller = BuildController(hydrationState, ingestion);

        var result = await controller.PostAsync(BuildRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        var body = Assert.IsType<ApiErrorResponse>(objectResult.Value);
        Assert.Contains("failed", body.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, ingestion.CallCount);
    }

    private static AgentResultsController BuildController(
        IRollingWindowHydrationState hydrationState,
        FakeResultIngestionService ingestionService)
    {
        var controller = new AgentResultsController(
            new FakeAgentAuthenticationService(),
            ingestionService,
            hydrationState,
            NullLogger<AgentResultsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return controller;
    }

    private static SubmitResultsRequest BuildRequest()
    {
        return new SubmitResultsRequest(
            DateTimeOffset.Parse("2026-05-29T00:00:00Z"),
            "batch-1",
            [new CheckResultDto(
                "assignment-1",
                "endpoint-1",
                "icmp",
                DateTimeOffset.Parse("2026-05-29T00:00:00Z"),
                true,
                12,
                null,
                null)]);
    }

    private sealed class FakeAgentAuthenticationService : IAgentAuthenticationService
    {
        public Task<AgentAuthenticationResult> AuthenticateAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(AgentAuthenticationResult.Success(new Agent
            {
                AgentId = "agent-1",
                InstanceId = "instance-1",
                Enabled = true
            }));
        }
    }

    private sealed class FakeResultIngestionService : IResultIngestionService
    {
        public int CallCount { get; private set; }

        public Task<SubmitResultsResponse> IngestAsync(Agent agent, SubmitResultsRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new SubmitResultsResponse(true, request.Results.Count, false, DateTimeOffset.Parse("2026-05-29T00:00:01Z")));
        }
    }
}
