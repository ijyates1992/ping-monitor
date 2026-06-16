using PingMonitor.Web.Services.AiMemory;

namespace PingMonitor.Web.ViewModels.AiAssistant;

public sealed class AiMemoriesPageViewModel
{
    public bool AssistantEnabled { get; set; }
    public bool MemoryEnabled { get; set; }
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<AiUserMemoryDto> Memories { get; set; } = [];
}
