using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Interfaces;
namespace PrivateBrandsPortal.Web.Services;

public sealed class GraphEmailSender(HttpClient http, IGraphTokenProvider tokens,
    IOptions<NotificationOptions> notifications, IOptions<MicrosoftGraphOptions> graph,
    TimeProvider clock, ILogger<GraphEmailSender> logger) : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var errors = EmailProviderConfiguration.Errors(notifications.Value, graph.Value);
        if (errors.Count > 0) throw new EmailDeliveryException(string.Join(" ", errors));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try {
            var token = await tokens.GetTokenAsync(timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://graph.microsoft.com/v1.0/users/" + Uri.EscapeDataString(notifications.Value.FromAddress) + "/sendMail");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("client-request-id", Guid.NewGuid().ToString());
            request.Content = JsonContent.Create(new {
                message = new {
                    subject = message.Subject, body = new { contentType = "Text", content = message.TextBody },
                    toRecipients = new[] { new { emailAddress = new { address = message.To } } }
                }, saveToSentItems = true
            });
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var requestId = response.Headers.TryGetValues("request-id", out var values) && Guid.TryParse(values.FirstOrDefault(), out var parsed) ? parsed.ToString() : "unavailable";
            if (response.StatusCode == HttpStatusCode.Accepted) {
                logger.LogInformation("Graph accepted notification {NotificationKey} for {Recipient}; request {RequestId}.", message.IdempotencyKey, message.To, requestId);
                // 202 means accepted by Graph, not proof of delivery to the recipient inbox.
                return EmailSendResult.Delivered;
            }
            var delay = response.Headers.RetryAfter?.Delta;
            if (delay is null && response.Headers.RetryAfter?.Date is { } date) delay = date - clock.GetUtcNow();
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            logger.LogWarning("Graph notification {NotificationKey} for {Recipient} failed with HTTP {Status}; request {RequestId}.", message.IdempotencyKey, message.To, (int)response.StatusCode, requestId);
            throw new EmailDeliveryException($"Graph sendMail HTTP {(int)response.StatusCode}; request-id {requestId}.", delay);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw new EmailDeliveryException("Graph email operation timed out; acceptance may be unknown.");
        }
        catch (HttpRequestException) { throw new EmailDeliveryException("Graph email transport failed; acceptance may be unknown."); }
    }
}
