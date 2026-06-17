using Xunit;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.AiChat;
using PingMonitor.Web.Services.AiScheduledTasks;

namespace PingMonitor.Web.Tests;

public sealed class AiScheduledTasksSliceTests
{
    [Fact]
    public void BuiltInPrompt_SaysScheduledTasksAreGuiOnly()
    {
        Assert.Contains("Scheduled AI tasks can only be created, edited, enabled, disabled, or deleted from the web UI", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("You do not have tools to schedule tasks", AiChatService.BuiltInSystemPrompt);
    }

    [Fact]
    public void NoSchedulingAiToolIsRegisteredInProgram()
    {
        var source = ReadWebFile("Program.cs");
        Assert.DoesNotContain("ScheduleAiTool", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AiScheduledTaskTool", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScheduledTaskPage_UsesTimeZoneDropdownAndNoFreeTextSchedule()
    {
        var source = ReadWebFile("Views", "AiScheduledTasks", "Index.cshtml");
        Assert.Contains("<select asp-for=\"Form.TimeZoneId\"", source);
        Assert.DoesNotContain("placeholder=\"UTC\"", source);
        Assert.Contains("Minutes, seconds, cron expressions, free-text parsing, and AI-generated schedules are not supported", source);
    }

    [Fact]
    public void StartupGateDeclaresRepeatSchemaColumns()
    {
        var source = ReadWebFile("Services", "StartupGate", "StartupSchemaService.cs");
        Assert.Contains("FirstRunAtUtc", source);
        Assert.Contains("RepeatEnabled", source);
        Assert.Contains("RepeatEvery", source);
        Assert.Contains("RepeatUnit", source);
        Assert.Contains("MissedRunPolicy", source);
    }

    [Fact]
    public void RepeatModel_CalculatesHourlyDailyWeeklyAndMonthlyRuns()
    {
        var service = new AiScheduledTaskService(null!);
        var first = new DateTimeOffset(2026, 1, 31, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 1, 31, 14, 0, 0, TimeSpan.Zero), service.CalculateNextRunUtc(first, true, 6, AiScheduledTaskRepeatUnit.Hours, "UTC", new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero), service.CalculateNextRunUtc(first, true, 1, AiScheduledTaskRepeatUnit.Days, "UTC", new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateTimeOffset(2026, 2, 14, 8, 0, 0, TimeSpan.Zero), service.CalculateNextRunUtc(first, true, 2, AiScheduledTaskRepeatUnit.Weeks, "UTC", new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 8, 0, 0, TimeSpan.Zero), service.CalculateNextRunUtc(first, true, 1, AiScheduledTaskRepeatUnit.Months, "UTC", new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void MissedRunPolicies_NeverBacklogMultipleExecutions()
    {
        var service = new AiScheduledTaskService(null!);
        var first = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 6, 17, 8, 0, 0, TimeSpan.Zero);
        var skip = service.EvaluateDue(first, true, 1, AiScheduledTaskRepeatUnit.Days, AiScheduledTaskMissedRunPolicy.Skip, "UTC", now);
        var retry = service.EvaluateDue(first, true, 1, AiScheduledTaskRepeatUnit.Days, AiScheduledTaskMissedRunPolicy.RetryOnce, "UTC", now);
        Assert.False(skip.ShouldRunNow);
        Assert.True(skip.NextRunAtUtc > now);
        Assert.True(retry.ShouldRunNow);
        Assert.True(retry.NextRunAtUtc > now);
    }

    private static string ReadWebFile(params string[] parts) => File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", .. parts]));
}
