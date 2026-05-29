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
}
