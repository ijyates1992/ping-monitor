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
        Assert.Contains("Scheduled and event-triggered AI tasks can only be created, edited, enabled, disabled, or deleted from the web UI", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("they must use the Scheduled AI tasks page", AiChatService.BuiltInSystemPrompt);
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
        Assert.Contains("asp-for=\"Form.FirstRunDate\" type=\"date\"", source);
        Assert.Contains("asp-for=\"Form.FirstRunTime\" type=\"time\" step=\"60\"", source);
        Assert.Contains("The first run date and time are interpreted in the selected time zone", source);
        Assert.DoesNotContain("asp-for=\"Form.FirstRunAtUtc\"", source);
        Assert.DoesNotContain("placeholder=\"UTC\"", source);
        Assert.Contains("Minutes, seconds, cron expressions, free-text parsing, and AI-generated schedules are not supported", source);
    }

    [Fact]
    public void ScheduledTaskOverview_UsesDisplayTimeFormatterForRunTimestamps()
    {
        var source = ReadWebFile("Views", "AiScheduledTasks", "Index.cshtml");

        Assert.Contains("DisplayTimeFormatter.FormatForCurrentUserAsync(t.NextRunAtUtc, \"none\")", source);
        Assert.Contains("DisplayTimeFormatter.FormatForCurrentUserAsync(t.LastRunAtUtc, \"never\")", source);
        Assert.DoesNotContain("NextRunAtUtc?.ToString(\"u\")", source);
        Assert.DoesNotContain("LastRunAtUtc?.ToString(\"u\")", source);
    }

    [Fact]
    public void LocalFirstRunConversion_UsesEuropeLondonSummerOffset()
    {
        var result = AiScheduledTaskService.ConvertLocalFirstRunToUtc(new DateOnly(2026, 6, 17), new TimeOnly(13, 30), "Europe/London");

        Assert.True(result.Succeeded);
        Assert.Equal(new DateTimeOffset(2026, 6, 17, 12, 30, 0, TimeSpan.Zero), result.UtcValue);
    }

    [Fact]
    public void LocalFirstRunConversion_UsesUtcDirectly()
    {
        var result = AiScheduledTaskService.ConvertLocalFirstRunToUtc(new DateOnly(2026, 6, 17), new TimeOnly(13, 30), "UTC");

        Assert.True(result.Succeeded);
        Assert.Equal(new DateTimeOffset(2026, 6, 17, 13, 30, 0, TimeSpan.Zero), result.UtcValue);
    }

    [Fact]
    public void LocalFirstRunConversion_RejectsInvalidTimeZoneAndDstGap()
    {
        var invalidZone = AiScheduledTaskService.ConvertLocalFirstRunToUtc(new DateOnly(2026, 6, 17), new TimeOnly(13, 30), "Not/AZone");
        var dstGap = AiScheduledTaskService.ConvertLocalFirstRunToUtc(new DateOnly(2026, 3, 29), new TimeOnly(1, 30), "Europe/London");

        Assert.False(invalidZone.Succeeded);
        Assert.False(dstGap.Succeeded);
    }

    [Fact]
    public void LocalFirstRunConversion_AmbiguousLondonTimeChoosesEarlierOccurrence()
    {
        var result = AiScheduledTaskService.ConvertLocalFirstRunToUtc(new DateOnly(2026, 10, 25), new TimeOnly(1, 30), "Europe/London");

        Assert.True(result.Succeeded);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), result.UtcValue);
    }

    [Fact]
    public void FormModel_UsesExplicitDateAndTimeFields()
    {
        var source = ReadWebFile("ViewModels", "AiScheduledTasks", "AiScheduledTasksPageViewModel.cs");

        Assert.Contains("FirstRunDate", source);
        Assert.Contains("FirstRunTime", source);
        Assert.DoesNotContain("FirstRunAtUtc { get; set; }", source);
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
