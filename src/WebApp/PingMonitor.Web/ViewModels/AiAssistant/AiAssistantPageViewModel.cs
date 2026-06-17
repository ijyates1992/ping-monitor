using System.ComponentModel.DataAnnotations;
using PingMonitor.Web.Services.AiChat;

namespace PingMonitor.Web.ViewModels.AiAssistant;

public sealed class AiAssistantPageViewModel
{
    public const int MaxUserMessageLength = 4000;

    [Display(Name = "Message")]
    [StringLength(MaxUserMessageLength, ErrorMessage = "Message must be 4000 characters or fewer.")]
    public string? Message { get; set; }

    public IList<AiChatMessageDto> Messages { get; set; } = new List<AiChatMessageDto>();
    public bool AssistantEnabled { get; set; }
    public bool WebChatEnabled { get; set; }
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public string HelpText { get; set; } = "Ask about your monitored network. This web conversation is kept only for the current browser session and is not saved to the database.";
}
