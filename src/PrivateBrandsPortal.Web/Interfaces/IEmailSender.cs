namespace PrivateBrandsPortal.Web.Interfaces;
public sealed record EmailMessage(string IdempotencyKey, string To, string Subject, string TextBody);
public enum EmailSendResult { Delivered, Simulated }
public interface IEmailSender
{
    // Implementations must honour cancellation and deduplicate by IdempotencyKey across retries.
    // A transport without durable provider-side idempotency can offer only at-least-once delivery.
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
