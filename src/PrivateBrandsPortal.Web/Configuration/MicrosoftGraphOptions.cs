using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Web.Configuration;

public sealed class MicrosoftGraphOptions
{
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string CertificateThumbprint { get; set; } = "";
}
public static class EmailProviderConfiguration
{
    public static bool IsGraph(NotificationOptions o) => string.Equals(o.EmailProvider, "Graph", StringComparison.OrdinalIgnoreCase);
    public static string NormalizeThumbprint(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    public static IReadOnlyList<string> Errors(NotificationOptions notifications, MicrosoftGraphOptions graph)
    {
        var errors = new List<string>();
        if (!IsGraph(notifications)) {
            if (!string.Equals(notifications.EmailProvider, "Simulated", StringComparison.OrdinalIgnoreCase)) errors.Add("Notifications:EmailProvider must be Simulated or Graph.");
            return errors;
        }
        if (!Guid.TryParse(graph.TenantId, out var tenant) || tenant == Guid.Empty) errors.Add("MicrosoftGraph:TenantId must be a tenant GUID.");
        if (!Guid.TryParse(graph.ClientId, out var client) || client == Guid.Empty) errors.Add("MicrosoftGraph:ClientId must be an application GUID.");
        if (!Regex.IsMatch(NormalizeThumbprint(graph.CertificateThumbprint), "\\A[0-9A-F]{40}\\z")) errors.Add("MicrosoftGraph:CertificateThumbprint must identify a certificate in LocalMachine/My (40 hexadecimal characters).");
        if (!MailAddress.TryCreate(notifications.FromAddress, out var from) || from.Address != notifications.FromAddress || notifications.FromAddress.Any(char.IsWhiteSpace)) errors.Add("Notifications:FromAddress must be a mailbox email address.");
        return errors;
    }
    public static IServiceCollection AddPortalEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MicrosoftGraphOptions>(configuration.GetSection("MicrosoftGraph"));
        services.AddSingleton<IValidateOptions<NotificationOptions>, EmailSettingsValidator>();
        services.AddOptions<NotificationOptions>().ValidateOnStart();
        services.AddSingleton<IGraphCertificateStore, LocalMachineGraphCertificateStore>();
        services.AddSingleton<IGraphCertificateSource, WindowsGraphCertificateSource>();
        services.AddSingleton<IGraphTokenProvider, CertificateGraphTokenProvider>();
        services.AddHttpClient<GraphEmailSender>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        services.AddSingleton<LoggingEmailSender>();
        services.AddTransient<IEmailSender>(sp => IsGraph(sp.GetRequiredService<IOptions<NotificationOptions>>().Value)
            ? sp.GetRequiredService<GraphEmailSender>() : sp.GetRequiredService<LoggingEmailSender>());
        return services;
    }
}

public sealed class EmailSettingsValidator(IOptions<MicrosoftGraphOptions> graph) : IValidateOptions<NotificationOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationOptions options)
    {
        var errors = EmailProviderConfiguration.Errors(options, graph.Value);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}