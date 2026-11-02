# Microsoft Graph email provider

## Scope

GraphEmailSender implements the existing IEmailSender. Approval/Implementation services,
recipient routing, permissions and database schema are unchanged. No migration is required.
Azure.Identity **1.21.0** is the new direct NuGet dependency (Azure.Core/MSAL are transitive).
Graph sendMail uses HttpClient, not the Graph SDK, so there is no additional HTTP retry handler.

Authentication is app-only ClientCertificateCredential with scope
`https://graph.microsoft.com/.default`. Tenant/client IDs and thumbprint come from configuration.
The dedicated application certificate is read by thumbprint from **LocalMachine/My**, never
CurrentUser, a file, a remote store or the portal HTTPS binding. No private-key export occurs.
The source checks private-key presence, validity and RSA signing access. The process caches the
credential/token and retains the certificate handle; restart after certificate rotation.

## Configuration examples (documentation only)

DEV defaults remain safe; no Graph settings or certificate are needed:

```json
{
  "Notifications": {
    "WorkerEnabled": false,
    "EmailProvider": "Simulated",
    "FromAddress": "",
    "BaseUrl": "https://portalmp.forte.local/"
  },
  "MicrosoftGraph": {
    "TenantId": "",
    "ClientId": "",
    "CertificateThumbprint": ""
  }
}
```

Future PROD configuration, to be applied separately by an authorized administrator:

```json
{
  "Notifications": {
    "WorkerEnabled": false,
    "EmailProvider": "Graph",
    "FromAddress": "portalmp@forte-sweden.com",
    "BaseUrl": "https://portalmp.forte.local/"
  },
  "MicrosoftGraph": {
    "TenantId": "<TENANT-GUID>",
    "ClientId": "<APPLICATION-CLIENT-GUID>",
    "CertificateThumbprint": "<40-HEX-CHARACTERS-OF-DEDICATED-MAILER-CERTIFICATE>"
  }
}
```

Keep WorkerEnabled=false until the explicitly approved live test. Existing PollSeconds,
BatchSize, RetrySeconds and MaxAttempts still apply. Environment variable equivalents:
Notifications__EmailProvider, Notifications__FromAddress, Notifications__WorkerEnabled,
MicrosoftGraph__TenantId, MicrosoftGraph__ClientId, MicrosoftGraph__CertificateThumbprint.
These identifiers are not private keys; nevertheless, actual environment configuration belongs
outside the repository. No client secret, username/password or SMTP configuration is supported.

Startup validates provider and required Graph settings even if its worker is disabled. Simulated
requires none of the Graph values. Certificate lookup is lazy at the first send attempt; merely
selecting Graph does not load a certificate or contact Entra. Missing/expired/unusable certificates
produce safe, explicit diagnostics in Outbox LastError and follow its existing retry limits.

## HTTP, retries and diagnostics

The endpoint is `POST https://graph.microsoft.com/v1.0/users/{escaped FromAddress}/sendMail`.
The message has one recipient, the existing subject/plain-text body/production link, and
saveToSentItems=true. `/me/sendMail` is never used. Automatic redirects are disabled.
The operation honours cancellation and has a 30-second deadline covering token acquisition
and HTTP. Azure credential transport retry is disabled (MaxRetries=0); no sendMail retry loop.

Only HTTP **202 Accepted** is considered success. The existing Outbox then records Sent and
SentAtUtc, meaning **accepted by Graph**, not confirmed inbox delivery. All other statuses,
including 429, 5xx, 401/403, return a safe EmailDeliveryException to existing retry handling.
Timeout/network errors may have an unknown acceptance outcome. Retry-After seconds and HTTP-date
are supported; the dispatcher waits at least the greater of normal backoff and provider hint.
Its exception contract is provider-neutral; the dispatcher does not reference Graph classes.

Graph sendMail does not expose a durable idempotency-key contract. Existing event uniqueness
and SQL locks prevent enqueue/concurrent-worker duplicates, but an ambiguous timeout or crash
after Graph acceptance can still result in another message on retry. The stable event key is for
correlation, not a promise of Graph deduplication. Do not automatically replay Sent rows.

Logs contain Outbox Id, event type, recipient, attempt/result, notification key and Graph request-id
when provided as a UUID. The latter is also included in safe HTTP failure diagnostics. Graph
response bodies, authorization headers, tokens, private keys and mail bodies are not logged.
HttpClient default logging and Azure credential diagnostics are disabled for this provider.
Recipient addresses in operational logs require appropriate access and retention controls.

## Before the first real test — administrator work still required

No server, IIS, Entra, Exchange, production certificate or production database was accessed by
this implementation. Reported infrastructure readiness has not been independently verified.

1. On **192.168.1.170**, confirm the dedicated `CN=PrivateBrandsPortal-Mailer` certificate and
   friendly name `PrivateBrandsPortal Graph Authentication` exist in LocalMachine/My, have a
   valid RSA private key and match the public certificate registered in the correct Entra app.
   Record its thumbprint; do not use the HTTPS certificate or copy/export its private key to DEV.
2. Identify the actual IIS application-pool/service identity. Grant that identity **Read** on this
   certificate's private key via Manage Private Keys (not broad access to all keys). Verify signing
   access under that identity; an administrator's successful access is not sufficient.
3. Confirm TenantId and application ClientId refer to PrivateBrandsPortal-Mailer; verify the
   uploaded certificate's validity/expiry and the effective Mail.Send application authorization.
4. Confirm `portalmp@forte-sweden.com` is the correct Exchange Online mailbox/UPN accepted by
   `/users/{FromAddress}`. If it is only an alias, configure the actual mailbox UPN instead.
   Verify the application's effective mailbox scope permits this mailbox. Restrict other mailbox
   access using the organization's approved Exchange application access configuration. RBAC
   grants are additive with unscoped Entra grants: simply adding a narrow RBAC scope does not
   cancel an existing tenant-wide Entra Mail.Send grant. Any permission changes require separate
   administration approval; none were performed here.
5. Verify DNS/outbound HTTPS from the server/process identity to graph.microsoft.com and the
   Microsoft identity endpoints (including login.microsoftonline.com), proxy/firewall rules,
   TLS trust and system time. No inbound Internet exposure is required for app-only mail.
6. Resolve existing NuGet security advisories for Negotiate 10.0.12 and transitive Pkcs 9.0.11
   through a separately tested dependency update before production rollout. This task does not
   modify existing authentication or SQL Client packages to suppress those warnings.
7. After separate deployment/configuration authorization, supply the real identifiers on the
   server with EmailProvider=Graph and **WorkerEnabled=false**. Verify startup/configuration;
   remember certificate/token access has not yet been exercised by a disabled worker.
8. Confirm the production Outbox schema is present via the separately authorized deployment
   procedure. Review pending/retry/blocked/abandoned rows and recipient mappings. Enabling the
   worker processes all due records; it is not a single-message test switch. Do not blindly enable
   it against a backlog. Choose one approved test recipient/event in an isolated or explicitly
   controlled queue. Do not reset Sent/Simulated records indiscriminately.
9. Obtain the user's **separate permission to send a real email**. Only then enable the worker
   for the controlled test. Check 202/request-id, Outbox state, Sent Items and recipient delivery
   (Exchange message trace if needed), then return to the agreed worker state.
10. For ongoing processing, configure a reliable application lifetime under IIS/service hosting
    (idle shutdown/recycling can delay queued messages), expiry monitoring/rotation of the
    certificate, and monitoring for Retry/Failed/Blocked rows. These IIS changes are not included.

## Tests and changes

Tests use fake tokens and HttpMessageHandler, fake empty/denied certificate stores and DEV SQL
fixtures. They make **no Entra or Graph calls**, access no real certificate store, and send no mail.
Run `./dotnet.ps1 build` and `./dotnet.ps1 test --no-build` with the existing DEV-only SQL secrets.
Targeted tests: `./dotnet.ps1 test --filter 'FullyQualifiedName~GraphEmailSenderTests|FullyQualifiedName~Graph_transport_result'`.

Changed files are listed in [graph-email-changes.md](graph-email-changes.md).

## Microsoft reference documentation

- [Graph sendMail: app permissions, endpoint, 202 semantics](https://learn.microsoft.com/en-us/graph/api/user-sendmail?view=graph-rest-1.0)
- [Graph throttling and Retry-After](https://learn.microsoft.com/en-us/graph/throttling)
- [ClientCertificateCredential](https://learn.microsoft.com/en-us/dotnet/api/azure.identity.clientcertificatecredential)
- [Exchange RBAC for applications and independent Entra grants](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac)
