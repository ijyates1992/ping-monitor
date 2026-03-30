namespace PingMonitor.Web.Services;

internal sealed class NotificationSuppressionService : INotificationSuppressionService
{
    public NotificationSuppressionDecision IsBrowserNotificationSuppressed(UserNotificationSettingsDto settings)
    {
        if (!settings.QuietHoursSuppressBrowserNotifications)
        {
            return new NotificationSuppressionDecision
            {
                IsSuppressed = false,
                Reason = "Quiet hours browser suppression is disabled."
            };
        }

        var evaluation = EvaluateQuietHours(settings);
        return new NotificationSuppressionDecision
        {
            IsSuppressed = evaluation.QuietHoursActiveNow,
            Reason = evaluation.Reason
        };
    }

    public NotificationSuppressionDecision IsSmtpNotificationSuppressed(UserNotificationSettingsDto settings)
    {
        if (!settings.QuietHoursSuppressSmtpNotifications)
        {
            return new NotificationSuppressionDecision
            {
                IsSuppressed = false,
                Reason = "Quiet hours SMTP suppression is disabled."
            };
        }

        var evaluation = EvaluateQuietHours(settings);
        return new NotificationSuppressionDecision
        {
            IsSuppressed = evaluation.QuietHoursActiveNow,
            Reason = evaluation.Reason
        };
    }

    public NotificationSuppressionStatus GetCurrentStatus(UserNotificationSettingsDto settings)
    {
        return EvaluateQuietHours(settings);
    }

    private static NotificationSuppressionStatus EvaluateQuietHours(UserNotificationSettingsDto settings)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var quietHoursStart = ParseLocalTime(settings.QuietHoursStartLocalTime, "22:00");
        var quietHoursEnd = ParseLocalTime(settings.QuietHoursEndLocalTime, "07:00");
        var resolvedTimeZone = ResolveTimeZone(settings.QuietHoursTimeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, resolvedTimeZone);

        if (!settings.QuietHoursEnabled)
        {
            return BuildStatus(
                settings,
                nowUtc,
                resolvedTimeZone.Id,
                quietHoursStart,
                quietHoursEnd,
                isActive: false,
                reason: "Quiet hours are disabled.");
        }

        var currentLocalTime = nowLocal.TimeOfDay;
        var start = quietHoursStart;
        var end = quietHoursEnd;

        bool isActive;
        if (start == end)
        {
            isActive = true;
        }
        else if (start < end)
        {
            isActive = currentLocalTime >= start && currentLocalTime < end;
        }
        else
        {
            isActive = currentLocalTime >= start || currentLocalTime < end;
        }

        var reason = isActive
            ? $"Inside quiet hours window ({quietHoursStart:HH\\:mm}-{quietHoursEnd:HH\\:mm}) using timezone {resolvedTimeZone.Id}."
            : $"Outside quiet hours window ({quietHoursStart:HH\\:mm}-{quietHoursEnd:HH\\:mm}) using timezone {resolvedTimeZone.Id}.";

        return BuildStatus(settings, nowUtc, resolvedTimeZone.Id, quietHoursStart, quietHoursEnd, isActive, reason);
    }

    private static NotificationSuppressionStatus BuildStatus(
        UserNotificationSettingsDto settings,
        DateTimeOffset evaluatedAtUtc,
        string effectiveTimeZoneId,
        TimeSpan quietHoursStart,
        TimeSpan quietHoursEnd,
        bool isActive,
        string reason)
    {
        return new NotificationSuppressionStatus
        {
            QuietHoursEnabled = settings.QuietHoursEnabled,
            QuietHoursActiveNow = settings.QuietHoursEnabled && isActive,
            QuietHoursStartLocalTime = $"{quietHoursStart:hh\\:mm}",
            QuietHoursEndLocalTime = $"{quietHoursEnd:hh\\:mm}",
            ConfiguredTimeZoneId = string.IsNullOrWhiteSpace(settings.QuietHoursTimeZoneId) ? "UTC" : settings.QuietHoursTimeZoneId.Trim(),
            EffectiveTimeZoneId = effectiveTimeZoneId,
            EvaluatedAtUtc = evaluatedAtUtc,
            Reason = reason
        };
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    private static TimeSpan ParseLocalTime(string? value, string fallback)
    {
        if (TimeOnly.TryParseExact(value?.Trim(), "HH:mm", out var parsed))
        {
            return parsed.ToTimeSpan();
        }

        return TimeOnly.ParseExact(fallback, "HH:mm").ToTimeSpan();
    }
}
