using System.Net;
using System.Net.Mail;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services;

namespace PingMonitor.Web.Services.SmtpNotifications;

internal sealed class SmtpNotificationSender : ISmtpNotificationSender
{
    private readonly INotificationSettingsService _notificationSettingsService;
    private readonly INotificationSuppressionService _notificationSuppressionService;
    private readonly ILogger<SmtpNotificationSender> _logger;

    public SmtpNotificationSender(
        INotificationSettingsService notificationSettingsService,
        INotificationSuppressionService notificationSuppressionService,
        ILogger<SmtpNotificationSender> logger)
    {
        _notificationSettingsService = notificationSettingsService;
        _notificationSuppressionService = notificationSuppressionService;
        _logger = logger;
    }

    public async Task<SmtpNotificationSendResult> SendTestAsync(CancellationToken cancellationToken)
    {
        var suppressionDecision = await _notificationSuppressionService.IsSmtpNotificationSuppressedAsync(cancellationToken);
        if (suppressionDecision.IsSuppressed)
        {
            _logger.LogDebug("SMTP test notification suppressed by quiet hours. Reason={Reason}", suppressionDecision.Reason);
            return SmtpNotificationSendResult.Skip("SMTP notification suppressed by quiet hours.");
        }

        return await SendEmailAsync(
            subject: "Ping Monitor SMTP Test",
            body: "SMTP notifications are working.",
            eventKey: "smtp_test",
            cancellationToken);
    }

    public async Task<SmtpNotificationSendResult> SendForEventAsync(EventLog eventLog, CancellationToken cancellationToken)
    {
        var mapped = MapEvent(eventLog);
        if (mapped is null)
        {
            return SmtpNotificationSendResult.Skip("Event type is not supported for SMTP notifications.");
        }

        var settings = await _notificationSettingsService.GetSmtpChannelAsync(cancellationToken);
        if (!settings.SmtpNotificationsEnabled)
        {
            return SmtpNotificationSendResult.Skip("SMTP notifications are disabled.");
        }

        if (!IsEventEnabled(mapped.Value.EventToggle, settings))
        {
            _logger.LogDebug("SMTP notification skipped because event toggle is disabled for {EventType}.", eventLog.EventType);
            return SmtpNotificationSendResult.Skip("SMTP notification is disabled for this event type.");
        }

        var suppressionDecision = await _notificationSuppressionService.IsSmtpNotificationSuppressedAsync(cancellationToken);
        if (suppressionDecision.IsSuppressed)
        {
            _logger.LogDebug("SMTP notification suppressed by quiet hours for event {EventType}. Reason={Reason}", eventLog.EventType, suppressionDecision.Reason);
            return SmtpNotificationSendResult.Skip("SMTP notification suppressed by quiet hours.");
        }

        var body = BuildEventBody(eventLog);
        return await SendEmailAsync(mapped.Value.Subject, body, eventLog.EventType, cancellationToken);
    }

    private async Task<SmtpNotificationSendResult> SendEmailAsync(
        string subject,
        string body,
        string eventKey,
        CancellationToken cancellationToken)
    {
        var settings = await _notificationSettingsService.GetSmtpChannelAsync(cancellationToken);
        if (!settings.SmtpNotificationsEnabled)
        {
            return SmtpNotificationSendResult.Skip("SMTP notifications are disabled.");
        }

        var validation = Validate(settings);
        if (!validation.Success)
        {
            return validation;
        }

        var recipients = ParseRecipients(settings.SmtpRecipientAddresses!);

        try
        {
            using var message = new MailMessage
            {
                Subject = subject,
                Body = body,
                IsBodyHtml = false,
                From = new MailAddress(settings.SmtpFromAddress!, settings.SmtpFromDisplayName)
            };

            foreach (var recipient in recipients)
            {
                message.To.Add(recipient);
            }

            using var client = new SmtpClient(settings.SmtpHost!, settings.SmtpPort)
            {
                EnableSsl = settings.SmtpUseTls,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = string.IsNullOrWhiteSpace(settings.SmtpUsername)
                    ? CredentialCache.DefaultNetworkCredentials
                    : new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword)
            };

            await client.SendMailAsync(message, cancellationToken);

            _logger.LogInformation(
                "SMTP notification sent for event {EventKey}. RecipientCount={RecipientCount}",
                eventKey,
                recipients.Count);

            return SmtpNotificationSendResult.Sent("SMTP email sent successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP notification failed for event {EventKey}.", eventKey);
            return SmtpNotificationSendResult.Failed($"SMTP email failed: {ex.Message}");
        }
    }

    private static SmtpNotificationSendResult Validate(SmtpChannelSettingsDto settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            return SmtpNotificationSendResult.Failed("SMTP host is required.");
        }

        if (settings.SmtpPort <= 0 || settings.SmtpPort > 65535)
        {
            return SmtpNotificationSendResult.Failed("SMTP port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(settings.SmtpFromAddress))
        {
            return SmtpNotificationSendResult.Failed("SMTP from address is required.");
        }

        if (!MailAddress.TryCreate(settings.SmtpFromAddress, out _))
        {
            return SmtpNotificationSendResult.Failed("SMTP from address is not valid.");
        }

        if (string.IsNullOrWhiteSpace(settings.SmtpRecipientAddresses))
        {
            return SmtpNotificationSendResult.Failed("At least one SMTP recipient address is required.");
        }

        if (!string.IsNullOrWhiteSpace(settings.SmtpUsername) && string.IsNullOrWhiteSpace(settings.SmtpPassword))
        {
            return SmtpNotificationSendResult.Failed("SMTP password is required when SMTP username is provided.");
        }

        var recipients = ParseRecipients(settings.SmtpRecipientAddresses);
        if (recipients.Count == 0)
        {
            return SmtpNotificationSendResult.Failed("At least one valid SMTP recipient address is required.");
        }

        return SmtpNotificationSendResult.Sent("SMTP settings are valid.");
    }

    private static List<MailAddress> ParseRecipients(string recipientAddresses)
    {
        var recipients = new List<MailAddress>();
        var pieces = recipientAddresses.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var piece in pieces)
        {
            if (MailAddress.TryCreate(piece, out var recipient))
            {
                recipients.Add(recipient);
            }
        }

        return recipients;
    }

    private static MappedEvent? MapEvent(EventLog eventLog)
    {
        if (eventLog.EventType == EventType.EndpointStateChanged)
        {
            if (eventLog.Message.Contains("went down.", StringComparison.Ordinal))
            {
                return new MappedEvent(SmtpEventToggle.EndpointDown, "Ping Monitor: Endpoint Down");
            }

            if (eventLog.Message.Contains("recovered", StringComparison.Ordinal))
            {
                return new MappedEvent(SmtpEventToggle.EndpointRecovered, "Ping Monitor: Endpoint Recovered");
            }

            return null;
        }

        if (eventLog.EventType == EventType.AgentBecameOffline)
        {
            return new MappedEvent(SmtpEventToggle.AgentOffline, "Ping Monitor: Agent Offline");
        }

        if (eventLog.EventType == EventType.AgentBecameOnline)
        {
            return new MappedEvent(SmtpEventToggle.AgentOnline, "Ping Monitor: Agent Online");
        }

        return null;
    }

    private static bool IsEventEnabled(SmtpEventToggle toggle, SmtpChannelSettingsDto settings)
    {
        return toggle switch
        {
            SmtpEventToggle.EndpointDown => settings.SmtpNotifyEndpointDown,
            SmtpEventToggle.EndpointRecovered => settings.SmtpNotifyEndpointRecovered,
            SmtpEventToggle.AgentOffline => settings.SmtpNotifyAgentOffline,
            SmtpEventToggle.AgentOnline => settings.SmtpNotifyAgentOnline,
            _ => false
        };
    }

    private static string BuildEventBody(EventLog eventLog)
    {
        return string.Join(
            Environment.NewLine,
            [
                eventLog.Message,
                string.Empty,
                $"Event time (UTC): {eventLog.OccurredAtUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss}",
                $"Event type: {eventLog.EventType}",
                $"Endpoint ID: {eventLog.EndpointId ?? "(none)"}",
                $"Agent ID: {eventLog.AgentId ?? "(none)"}"
            ]);
    }

    private enum SmtpEventToggle
    {
        EndpointDown,
        EndpointRecovered,
        AgentOffline,
        AgentOnline
    }

    private readonly record struct MappedEvent(SmtpEventToggle EventToggle, string Subject);
}
