using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.SmtpNotifications;

public interface ISmtpNotificationSender
{
    Task<SmtpNotificationSendResult> SendTestAsync(string recipientAddress, CancellationToken cancellationToken);
    Task<SmtpNotificationSendResult> SendEmailVerificationAsync(string recipientAddress, string verificationLink, CancellationToken cancellationToken);
    Task<SmtpNotificationSendResult> SendForEventAsync(EventLog eventLog, CancellationToken cancellationToken);
}
