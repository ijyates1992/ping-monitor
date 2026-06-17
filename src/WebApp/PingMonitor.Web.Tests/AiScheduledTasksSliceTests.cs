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
    public void AiChatSource_HasScheduledTaskSource()
    {
        Assert.True(Enum.IsDefined(typeof(AiChatSource), AiChatSource.ScheduledTask));
    }

    [Fact]
    public void NoSchedulingAiToolIsRegisteredInProgram()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", "Program.cs"));
        Assert.DoesNotContain("ScheduleAiTool", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AiScheduledTaskTool", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartupGateDeclaresAiScheduledTasksSchema()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", "Services", "StartupGate", "StartupSchemaService.cs"));
        Assert.Contains("AiScheduledTasks", source);
        Assert.Contains("RequiredAiScheduledTaskColumns", source);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `AiScheduledTasks`", source);
    }

    [Fact]
    public void ScheduleCalculation_DailyWeeklyMonthlyStayAtLeastDaily()
    {
        var service = new AiScheduledTaskService(null!);
        var now = new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);
        Assert.True(service.CalculateNextRunUtc(AiScheduledTaskScheduleKind.Daily, null, new TimeOnly(23, 59), null, null, "UTC", now) - now >= TimeSpan.FromHours(1));
        Assert.Equal(new DateTimeOffset(2026, 6, 22, 8, 0, 0, TimeSpan.Zero), service.CalculateNextRunUtc(AiScheduledTaskScheduleKind.Weekly, null, new TimeOnly(8, 0), DayOfWeek.Monday, null, "UTC", now));
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), service.CalculateNextRunUtc(AiScheduledTaskScheduleKind.Monthly, null, new TimeOnly(8, 0), null, 1, "UTC", now));
    }
}
