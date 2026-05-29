using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class DegradedEndpointQueryTranslationTests
{
    [Fact]
    public void DegradedEvaluationRuntimeQuery_TranslatesForMySqlProvider()
    {
        var options = new DbContextOptionsBuilder<PingMonitorDbContext>()
            .UseMySQL("server=localhost;database=ping_monitor_translation;user=test;password=test")
            .Options;
        using var dbContext = new PingMonitorDbContext(options);
        var assignmentId = "assignment-1";
        var nowUtc = new DateTimeOffset(2026, 05, 28, 12, 00, 00, TimeSpan.Zero);
        var windowStartUtc = nowUtc.AddHours(-24);

        var sql = dbContext.CheckResults.AsNoTracking()
            .Where(x => x.AssignmentId == assignmentId
                && x.CheckedAtUtc >= windowStartUtc
                && x.CheckedAtUtc <= nowUtc)
            .OrderBy(x => x.CheckedAtUtc)
            .ThenBy(x => x.ReceivedAtUtc)
            .ThenBy(x => x.CheckResultId)
            .Select(x => new
            {
                x.AssignmentId,
                x.CheckedAtUtc,
                x.Success,
                x.RoundTripMs
            })
            .ToQueryString();

        Assert.Contains("CheckResults", sql);
        Assert.Contains("AssignmentId", sql);
        Assert.Contains("CheckedAtUtc", sql);
        Assert.Contains("ReceivedAtUtc", sql);
        Assert.Contains("CheckResultId", sql);
    }
}
