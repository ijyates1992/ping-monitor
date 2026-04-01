using PingMonitor.Web.Models;

namespace PingMonitor.Web.Support;

public static class LogSeverityPresentation
{
    public static LogVisualSeverity FromEventSeverity(EventSeverity severity)
    {
        return severity switch
        {
            EventSeverity.Info => LogVisualSeverity.Info,
            EventSeverity.Warning => LogVisualSeverity.Warning,
            EventSeverity.Error => LogVisualSeverity.Error,
            _ => LogVisualSeverity.Info
        };
    }

    public static LogVisualSeverity FromSecurityAuthAttempt(bool success, string? failureReason)
    {
        if (success)
        {
            return LogVisualSeverity.Info;
        }

        if (string.IsNullOrWhiteSpace(failureReason))
        {
            return LogVisualSeverity.Warning;
        }

        return failureReason.Trim().ToLowerInvariant() switch
        {
            "ip_temporarily_blocked" => LogVisualSeverity.Error,
            "ip_permanently_blocked" => LogVisualSeverity.Error,
            "account_locked" => LogVisualSeverity.Error,
            "account_temporarily_locked" => LogVisualSeverity.Error,
            "login_not_allowed" => LogVisualSeverity.Error,
            "disabled_agent" => LogVisualSeverity.Error,
            "revoked_key" => LogVisualSeverity.Error,
            _ => LogVisualSeverity.Warning
        };
    }

    public static string ToCssClass(LogVisualSeverity severity)
    {
        return severity switch
        {
            LogVisualSeverity.Info => "log-info",
            LogVisualSeverity.Warning => "log-warning",
            LogVisualSeverity.Error => "log-error",
            _ => "log-info"
        };
    }
}
