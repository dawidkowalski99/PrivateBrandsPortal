namespace PrivateBrandsPortal.Web.Interfaces;
// Provider-neutral safe diagnostic and retry hint. Never include raw HTTP/token responses.
public sealed class EmailDeliveryException(string diagnostic, TimeSpan? retryAfter = null) : Exception(diagnostic)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
