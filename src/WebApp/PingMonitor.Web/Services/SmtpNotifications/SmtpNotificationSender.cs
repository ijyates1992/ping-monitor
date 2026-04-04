using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Models;
using PingMonitor.Web.Data;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Diagnostics;

namespace PingMonitor.Web.Services.SmtpNotifications;

internal sealed class SmtpNotificationSender : ISmtpNotificationSender
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly INotificationSettingsService _notificationSettingsService;
    private readonly IUserNotificationSettingsService _userNotificationSettingsService;
    private readonly INotificationSuppressionService _notificationSuppressionService;
    private readonly ILogger<SmtpNotificationSender> _logger;
    private readonly IDbActivityScope _dbActivityScope;

    public SmtpNotificationSender(
        PingMonitorDbContext dbContext,
        INotificationSettingsService notificationSettingsService,
        IUserNotificationSettingsService userNotificationSettingsService,
        INotificationSuppressionService notificationSuppressionService,
        IDbActivityScope dbActivityScope,
        ILogger<SmtpNotificationSender> logger)
    {
        _dbContext = dbContext;
        _notificationSettingsService = notificationSettingsService;
        _userNotificationSettingsService = userNotificationSettingsService;
        _notificationSuppressionService = notificationSuppressionService;
        _dbActivityScope = dbActivityScope;
        _logger = logger;
    }

    public async Task<SmtpNotificationSendResult> SendTestAsync(string recipientAddress, CancellationToken cancellationToken)
    {
        return await SendEmailAsync(
            subject: "Ping Monitor SMTP Test",
            body: "SMTP notifications are working.",
            eventKey: "smtp_test",
            recipients: [recipientAddress],
            requireSmtpNotificationChannelEnabled: true,
            cancellationToken);
    }

    public async Task<SmtpNotificationSendResult> SendEmailVerificationAsync(string recipientAddress, string verificationLink, CancellationToken cancellationToken)
    {
        var body = string.Join(
            Environment.NewLine,
            [
                "Verify your Ping Monitor email address.",
                "SMTP notifications are sent only to verified email addresses.",
                string.Empty,
                $"Verification link: {verificationLink}",
                string.Empty,
                "If you did not request this, you can ignore this email."
            ]);

        return await SendEmailAsync(
            subject: "Ping Monitor: Verify your email address",
            body: body,
            eventKey: "email_verification",
            recipients: [recipientAddress],
            requireSmtpNotificationChannelEnabled: false,
            cancellationToken);
    }

    public async Task<SmtpNotificationSendResult> SendForEventAsync(EventLog eventLog, CancellationToken cancellationToken)
    {
        using var scope = _dbActivityScope.BeginScope("Notifications.Smtp");
        var mapped = MapEvent(eventLog);
        if (mapped is null)
        {
            return SmtpNotificationSendResult.Skip("Event type is not supported for SMTP notifications.");
        }

        var eligibleRecipientAddresses = await ResolveEligibleRecipientsAsync(mapped.Value.EventToggle, cancellationToken);
        if (eligibleRecipientAddresses.Count == 0)
        {
            return SmtpNotificationSendResult.Skip("No users are eligible for SMTP delivery for this event.");
        }

        var body = BuildEventBody(eventLog);
        return await SendEmailAsync(mapped.Value.Subject, body, eventLog.EventType, eligibleRecipientAddresses, requireSmtpNotificationChannelEnabled: true, cancellationToken);
    }

    private async Task<SmtpNotificationSendResult> SendEmailAsync(
        string subject,
        string body,
        string eventKey,
        IReadOnlyList<string> recipients,
        bool requireSmtpNotificationChannelEnabled,
        CancellationToken cancellationToken)
    {
        var settings = await _notificationSettingsService.GetSmtpChannelAsync(cancellationToken);
        if (requireSmtpNotificationChannelEnabled && !settings.SmtpNotificationsEnabled)
        {
            return SmtpNotificationSendResult.Skip("SMTP notifications are disabled.");
        }

        var validation = Validate(settings);
        if (!validation.Success)
        {
            return validation;
        }

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
                message.To.Add(new MailAddress(recipient));
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

        if (!string.IsNullOrWhiteSpace(settings.SmtpUsername) && string.IsNullOrWhiteSpace(settings.SmtpPassword))
        {
            return SmtpNotificationSendResult.Failed("SMTP password is required when SMTP username is provided.");
        }

        return SmtpNotificationSendResult.Sent("SMTP settings are valid.");
    }

    private async Task<IReadOnlyList<string>> ResolveEligibleRecipientsAsync(SmtpEventToggle toggle, CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users
            .Where(x => !string.IsNullOrWhiteSpace(x.Email))
            .Select(x => new { x.Id, x.Email, x.EmailConfirmed })
            .ToArrayAsync(cancellationToken);

        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
        {
            var userSettings = await _userNotificationSettingsService.GetCurrentAsync(user.Id, cancellationToken);
            if (!userSettings.SmtpNotificationsEnabled || !IsEventEnabled(toggle, userSettings))
            {
                continue;
            }

            var suppressionDecision = _notificationSuppressionService.IsSmtpNotificationSuppressed(userSettings);
            if (suppressionDecision.IsSuppressed)
            {
                continue;
            }

            if (!user.EmailConfirmed)
            {
                _logger.LogDebug("Skipping SMTP notification eligibility for user {UserId} because email is unverified.", user.Id);
                continue;
            }

            if (MailAddress.TryCreate(user.Email, out var recipient))
            {
                recipients.Add(recipient.Address);
            }
        }

        return recipients.ToArray();
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

    private static bool IsEventEnabled(SmtpEventToggle toggle, UserNotificationSettingsDto settings)
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
