using System.ComponentModel.DataAnnotations;
using PingMonitor.Web.Services.AiChat;

namespace PingMonitor.Web.ViewModels.AiAssistant;

public sealed class AiAssistantPageViewModel
{
    public const int MaxUserMessageLength = 4000;

    [Display(Name = "Message")]
    [StringLength(MaxUserMessageLength, ErrorMessage = "Message must be 4000 characters or fewer.")]
    public string? Message { get; set; }

    public string? HistoryJson { get; set; }
    public IList<AiChatMessageDto> Messages { get; set; } = new List<AiChatMessageDto>();
    public bool AssistantEnabled { get; set; }
    public bool WebChatEnabled { get; set; }
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public string HelpText { get; set; } = "The assistant can use a limited read-only network health summary for broad status questions. Raw CheckResults diagnostics, endpoint diagnostic packs, diagram lookup, and memory are not connected yet.";
}
