using PrivateBrandsPortal.Web.Interfaces;
namespace PrivateBrandsPortal.Web.Services;
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Do not log recipient addresses, customer information or message bodies.
        logger.LogInformation("Simulated notification {NotificationKey}; no email was sent.", message.IdempotencyKey);
        return Task.FromResult(EmailSendResult.Simulated);
    }
}
