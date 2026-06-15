using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class DegradedEndpointQueryTranslationTests
{
    [Fact]
    public void StateEvaluationService_DoesNotQueryCheckResultsForDegradedRuntimeEvaluation()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PingMonitor.Web",
            "Services",
            "StateEvaluationService.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("_dbContext.CheckResults", source);
        Assert.DoesNotContain("CheckResults.AsNoTracking", source);
    }

    [Theory]
    [InlineData("Services", "Status", "EndpointStatusQueryService.cs")]
    [InlineData("Services", "Endpoints", "EndpointManagementQueryService.cs")]
    public void SummaryPages_DoNotRefreshRollingMetricsDuringPageQueries(params string[] relativePath)
    {
        var pathParts = new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web" }
            .Concat(relativePath)
            .ToArray();
        var sourcePath = Path.GetFullPath(Path.Combine(pathParts));
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("RefreshAssignmentsAsync", source);
    }

    [Fact]
    public void AiMonitoringContextService_GuardsEmptyIdCollectionsBeforeMySqlContainsQueries()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PingMonitor.Web",
            "Services",
            "AiChat",
            "AiMonitoringContextService.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("if (visibleEndpointIds is { Length: 0 })", source);
        Assert.Contains("if (agentIds.Length > 0)", source);
        Assert.Contains("if (visibleIdsFromRows.Length > 0)", source);
        Assert.Contains("WhereStringEqualsAny", source);
        Assert.DoesNotContain("visibleEndpointIds.Contains", source);
        Assert.DoesNotContain("agentIds.Contains", source);
        Assert.DoesNotContain("visibleIdsFromRows.Contains", source);
    }
}
