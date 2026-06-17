using System.ComponentModel.DataAnnotations;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.AiScheduledTasks;

namespace PingMonitor.Web.ViewModels.AiScheduledTasks;

public sealed class AiScheduledTasksPageViewModel
{
    public IReadOnlyList<AiScheduledTaskDto> Tasks { get; set; } = [];
    public bool HasLinkedTelegramAccount { get; set; }
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public AiScheduledTaskFormViewModel Form { get; set; } = new();
}

public sealed class AiScheduledTaskFormViewModel
{
    public string? AiScheduledTaskId { get; set; }
    [Required, StringLength(128)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(4000)] public string Prompt { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public AiScheduledTaskScheduleKind ScheduleKind { get; set; } = AiScheduledTaskScheduleKind.Once;
    public DateTimeOffset? RunOnceAtUtc { get; set; }
    public TimeOnly? TimeOfDayLocal { get; set; }
    public DayOfWeek? DayOfWeek { get; set; }
    [Range(1,28)] public int? DayOfMonth { get; set; }
    [Required, StringLength(128)] public string TimeZoneId { get; set; } = "UTC";
    public AiScheduledTaskDeliveryTarget DeliveryTarget { get; set; } = AiScheduledTaskDeliveryTarget.TelegramOwner;
}
