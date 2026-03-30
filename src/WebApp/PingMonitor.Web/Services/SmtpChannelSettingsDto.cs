namespace PingMonitor.Web.Services;

public sealed class SmtpChannelSettingsDto
{
    public bool SmtpNotificationsEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; }
    public bool SmtpUseTls { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromDisplayName { get; set; }
}
