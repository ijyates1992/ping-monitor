using System.ComponentModel.DataAnnotations;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.AiScheduledTasks;
using PingMonitor.Web.Services.Time;

namespace PingMonitor.Web.ViewModels.AiScheduledTasks;

public sealed class AiScheduledTasksPageViewModel
{
    public IReadOnlyList<AiScheduledTaskDto> Tasks { get; set; } = [];
    public bool HasLinkedTelegramAccount { get; set; }
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<DisplayTimeZoneOption> TimeZoneOptions { get; set; } = [];
    public AiScheduledTaskFormViewModel Form { get; set; } = new();
}

public sealed class AiScheduledTaskFormViewModel
{
    public string? AiScheduledTaskId { get; set; }
    [Required, StringLength(128)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(4000)] public string Prompt { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    [Required(ErrorMessage = "First run date is required.")] public DateOnly? FirstRunDate { get; set; }
    [Required(ErrorMessage = "First run time is required.")] public TimeOnly? FirstRunTime { get; set; }
    public bool RepeatEnabled { get; set; }
    [Range(1, int.MaxValue)] public int? RepeatEvery { get; set; } = 1;
    public AiScheduledTaskRepeatUnit? RepeatUnit { get; set; } = AiScheduledTaskRepeatUnit.Days;
    public AiScheduledTaskMissedRunPolicy MissedRunPolicy { get; set; } = AiScheduledTaskMissedRunPolicy.Skip;
    [Required, StringLength(128)] public string TimeZoneId { get; set; } = "UTC";
    public AiScheduledTaskDeliveryTarget DeliveryTarget { get; set; } = AiScheduledTaskDeliveryTarget.TelegramOwner;
}
