using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Tests;

public sealed class GraphEmailSenderTests
{
    internal static NotificationOptions Notifications() => new() { EmailProvider="Graph", FromAddress="portalmp@forte-sweden.com" };
    internal static MicrosoftGraphOptions Graph() => new() { TenantId="11111111-1111-1111-1111-111111111111", ClientId="22222222-2222-2222-2222-222222222222", CertificateThumbprint=new string('A',40) };
    internal sealed class Tokens : IGraphTokenProvider {
        public int Calls;
        public ValueTask<string> GetTokenAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested();Calls++;return ValueTask.FromResult("fake-token-for-unit-test"); }
    }
    internal sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct) { Calls++;return send(r,ct); }
    }
    internal static GraphEmailSender Sender(HttpClient http, IGraphTokenProvider? tokens=null) => new(http,tokens??new Tokens(),Options.Create(Notifications()),Options.Create(Graph()),TimeProvider.System,NullLogger<GraphEmailSender>.Instance);
    private static EmailMessage Message() => new("stable-event-key","recipient@example.invalid","PB-2026-001 — Approved","Customer: ALDI\nOpen: https://portalmp.forte.local/Projects/Details/1");
    private static ServiceProvider Provider(string provider,bool configured) {
        var values=new Dictionary<string,string?> { ["Notifications:EmailProvider"]=provider };
        if(configured) { values["Notifications:FromAddress"]=Notifications().FromAddress;values["MicrosoftGraph:TenantId"]=Graph().TenantId;values["MicrosoftGraph:ClientId"]=Graph().ClientId;values["MicrosoftGraph:CertificateThumbprint"]=Graph().CertificateThumbprint; }
        var config=new ConfigurationBuilder().AddInMemoryCollection(values).Build();var services=new ServiceCollection();
        services.AddLogging();services.AddSingleton(TimeProvider.System);services.Configure<NotificationOptions>(config.GetSection("Notifications"));services.AddPortalEmail(config);
        return services.BuildServiceProvider();
    }
    [Fact] public void Simulated_does_not_require_graph_configuration_or_certificate() {
        using var sp=Provider("Simulated",false);Assert.IsType<LoggingEmailSender>(sp.GetRequiredService<IEmailSender>());
    }
    [Fact] public void Graph_provider_is_selected_without_loading_certificate_until_send() {
        using var sp=Provider("Graph",true);Assert.IsType<GraphEmailSender>(sp.GetRequiredService<IEmailSender>());
    }
    [Fact] public void Graph_missing_configuration_has_explicit_keys() {
        using var sp=Provider("Graph",false);var error=Assert.Throws<OptionsValidationException>(()=>sp.GetRequiredService<IEmailSender>());
        foreach(var key in new[]{"TenantId","ClientId","CertificateThumbprint","FromAddress"})Assert.Contains(key,error.Message);
    }
    [Fact] public void Unknown_provider_is_not_silently_simulated() {
        using var sp=Provider("Other",false);Assert.Throws<OptionsValidationException>(()=>sp.GetRequiredService<IEmailSender>());
    }
    [Theory][InlineData("TenantId")][InlineData("ClientId")][InlineData("CertificateThumbprint")][InlineData("FromAddress")]
    public void Each_required_graph_setting_is_validated(string key) {
        var n=Notifications();var g=Graph();if(key=="FromAddress")n.FromAddress="";else typeof(MicrosoftGraphOptions).GetProperty(key)!.SetValue(g,"");
        Assert.Contains(EmailProviderConfiguration.Errors(n,g),x=>x.Contains(key));
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task Correct_request_uses_app_mailbox_recipient_subject_body_and_acceptance(bool html) {
        var message=Message() with { HtmlBody = html ? "<p>Produkt został zatwierdzony</p>" : null };var tokens=new Tokens();
        var handler=new Handler(async(r,ct)=>{
            Assert.Equal(HttpMethod.Post,r.Method);Assert.Equal("https://graph.microsoft.com/v1.0/users/portalmp%40forte-sweden.com/sendMail",r.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer",r.Headers.Authorization!.Scheme);Assert.Equal("fake-token-for-unit-test",r.Headers.Authorization.Parameter);
            using var body=JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));var m=body.RootElement.GetProperty("message");
            Assert.Equal(message.To,m.GetProperty("toRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString());
            Assert.Equal(message.Subject,m.GetProperty("subject").GetString());Assert.Equal(message.HtmlBody ?? message.TextBody,m.GetProperty("body").GetProperty("content").GetString());
            Assert.Equal(html ? "HTML" : "Text",m.GetProperty("body").GetProperty("contentType").GetString());Assert.True(body.RootElement.GetProperty("saveToSentItems").GetBoolean());
            return new(HttpStatusCode.Accepted);
        });using var http=new HttpClient(handler);
        Assert.Equal(EmailSendResult.Delivered,await Sender(http,tokens).SendAsync(message,default));Assert.Equal(1,handler.Calls);Assert.Equal(1,tokens.Calls);
    }
    [Theory][InlineData(429)][InlineData(500)][InlineData(503)][InlineData(401)][InlineData(403)][InlineData(400)][InlineData(302)]
    public async Task Http_errors_are_sanitized_without_internal_retry(int code) {
        var requestId=Guid.NewGuid();var handler=new Handler((r,ct)=>{var response=new HttpResponseMessage((HttpStatusCode)code){Content=new StringContent("SECRET RAW RESPONSE")};response.Headers.Add("request-id",requestId.ToString());response.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromMinutes(4));return Task.FromResult(response);});
        using var http=new HttpClient(handler);var error=await Assert.ThrowsAsync<EmailDeliveryException>(()=>Sender(http).SendAsync(Message(),default));
        Assert.Contains(code.ToString(),error.Message);Assert.Contains(requestId.ToString(),error.Message);Assert.DoesNotContain("SECRET",error.Message);Assert.Equal(TimeSpan.FromMinutes(4),error.RetryAfter);Assert.Equal(1,handler.Calls);
    }
    [Fact] public async Task Retry_after_http_date_is_supported() {
        var handler=new Handler((r,ct)=>{var response=new HttpResponseMessage(HttpStatusCode.TooManyRequests);response.Headers.RetryAfter=new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(10));return Task.FromResult(response);});
        using var http=new HttpClient(handler);var e=await Assert.ThrowsAsync<EmailDeliveryException>(()=>Sender(http).SendAsync(Message(),default));Assert.InRange(e.RetryAfter!.Value.TotalSeconds,590,601);
    }
    [Fact] public async Task Timeout_is_safe_and_is_not_retried_by_sender() {
        var handler=new Handler((r,ct)=>throw new TaskCanceledException("secret timeout text"));using var http=new HttpClient(handler);
        var e=await Assert.ThrowsAsync<EmailDeliveryException>(()=>Sender(http).SendAsync(Message(),default));Assert.Contains("timed out",e.Message);Assert.DoesNotContain("secret",e.Message);Assert.Equal(1,handler.Calls);
    }
    [Fact] public async Task Cancellation_propagates_without_attempting_network() {
        using var cts=new CancellationTokenSource();cts.Cancel();var handler=new Handler((r,ct)=>throw new Exception("Network must not run"));using var http=new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Sender(http).SendAsync(Message(),cts.Token));Assert.Equal(0,handler.Calls);
    }
    private sealed class EmptyStore : IGraphCertificateStore { public X509Certificate2Collection Read()=>new(); }
    private sealed class DeniedStore : IGraphCertificateStore { public X509Certificate2Collection Read()=>throw new UnauthorizedAccessException("private details"); }
    [Fact] public void Missing_certificate_has_clear_diagnostic_without_reading_real_store() {
        var error=Assert.Throws<EmailDeliveryException>(()=>new WindowsGraphCertificateSource(new EmptyStore()).Load(Graph().CertificateThumbprint));
        Assert.Contains("not found",error.Message);Assert.Contains("LocalMachine/My",error.Message);
    }
    [Fact] public void Certificate_access_failure_is_safe_and_actionable() {
        var error=Assert.Throws<EmailDeliveryException>(()=>new WindowsGraphCertificateSource(new DeniedStore()).Load(Graph().CertificateThumbprint));
        Assert.Contains("private-key Read permission",error.Message);Assert.DoesNotContain("private details",error.Message);
    }
    [Fact] public async Task Host_tests_force_simulation_even_when_graph_is_configured() {
        await using var baseline=new AuthenticatedFactory();
        await using var host=baseline.WithWebHostBuilder(builder=>builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?> {
            ["Notifications:EmailProvider"]="Graph",["Notifications:WorkerEnabled"]="true"
        })));
        using var client=host.CreateClient();
        var options=host.Services.GetRequiredService<IOptions<NotificationOptions>>().Value;
        Assert.False(options.WorkerEnabled);Assert.Equal("Simulated",options.EmailProvider);
        Assert.IsType<LoggingEmailSender>(host.Services.GetRequiredService<IEmailSender>());
    }
}
