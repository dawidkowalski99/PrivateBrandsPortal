using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Interfaces;
namespace PrivateBrandsPortal.Web.Services;

public interface IGraphCertificateSource { X509Certificate2 Load(string thumbprint); }
public interface IGraphTokenProvider { ValueTask<string> GetTokenAsync(CancellationToken ct); }
public interface IGraphCertificateStore { X509Certificate2Collection Read(); }
public sealed class LocalMachineGraphCertificateStore : IGraphCertificateStore
{
    public X509Certificate2Collection Read()
    {
        if (!OperatingSystem.IsWindows()) throw new EmailDeliveryException("Graph certificate requires Windows LocalMachine/My certificate store.");
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        return store.Certificates;
    }
}
public sealed class WindowsGraphCertificateSource(IGraphCertificateStore store) : IGraphCertificateSource
{
    public X509Certificate2 Load(string thumbprint)
    {
        try {
            var certificates = store.Read();
            try {
                var match = certificates.Cast<X509Certificate2>().SingleOrDefault(c =>
                    string.Equals(c.Thumbprint, EmailProviderConfiguration.NormalizeThumbprint(thumbprint), StringComparison.OrdinalIgnoreCase));
                if (match is null) throw new EmailDeliveryException("Graph certificate was not found in LocalMachine/My. Check MicrosoftGraph:CertificateThumbprint.");
                if (!match.HasPrivateKey) throw new EmailDeliveryException("Graph certificate in LocalMachine/My has no private key.");
                if (DateTime.UtcNow < match.NotBefore.ToUniversalTime() || DateTime.UtcNow >= match.NotAfter.ToUniversalTime())
                    throw new EmailDeliveryException("Graph certificate is expired or not yet valid.");
                using var rsa = match.GetRSAPrivateKey();
                if (rsa is null) throw new EmailDeliveryException("Graph certificate requires an accessible RSA private key.");
                // Verify the process identity can use the non-exported private key before token acquisition.
                rsa.SignData(new byte[] { 1 }, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return new X509Certificate2(match);
            } finally { foreach (var certificate in certificates) certificate.Dispose(); }
        }
        catch (EmailDeliveryException) { throw; }
        catch (Exception ex) when (ex is CryptographicException or UnauthorizedAccessException) {
            throw new EmailDeliveryException("Cannot access Graph certificate/private key in LocalMachine/My. Check application-pool identity and private-key Read permission.");
        }
    }
}
public sealed class CertificateGraphTokenProvider(IGraphCertificateSource certificates,
    IOptions<MicrosoftGraphOptions> graph, IOptions<NotificationOptions> notifications) : IGraphTokenProvider, IDisposable
{
    private readonly object gate = new();
    private X509Certificate2? certificate;
    private ClientCertificateCredential? credential;
    public async ValueTask<string> GetTokenAsync(CancellationToken ct)
    {
        var errors = EmailProviderConfiguration.Errors(notifications.Value, graph.Value);
        if (errors.Count > 0) throw new EmailDeliveryException(string.Join(" ", errors));
        ClientCertificateCredential current;
        lock (gate) {
            if (credential is null) {
                certificate = certificates.Load(graph.Value.CertificateThumbprint);
                var options = new ClientCertificateCredentialOptions();
                options.Retry.MaxRetries = 0;
                options.Diagnostics.IsLoggingEnabled = false;
                credential = new ClientCertificateCredential(graph.Value.TenantId, graph.Value.ClientId, certificate, options);
            }
            current = credential;
        }
        try {
            var token = await current.GetTokenAsync(new TokenRequestContext(["https://graph.microsoft.com/.default"]), ct);
            return token.Token;
        }
        catch (OperationCanceledException) { throw; }
        catch (AuthenticationFailedException) { throw new EmailDeliveryException("Graph app-only authentication failed. Check tenant/client IDs, registered certificate, key access and certificate validity."); }
    }
    public void Dispose() { lock (gate) { certificate?.Dispose(); } }
}
