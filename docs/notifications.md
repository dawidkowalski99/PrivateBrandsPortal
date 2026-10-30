# Workflow notification outbox — DEV

## Workflow confirmed in code

Roles: ProjectManager, Manager, SuperAdmin. There is no Director role, department-based
review assignment or operation returning an entire project to Draft. Implementation rejection
keeps the SKU in Implementation; the owner can correct documents and resubmit.

| Action | Outbox event | Recipient / link |
|---|---|---|
| Save/edit Draft | None | No decision required |
| Submit for review | ProjectSubmitted | Assigned approver; Approvals/Review/{projectId} |
| Submit with existing manager bypass | ProjectApprovalBypassed | PM; project details |
| Review SKU | ProductApproved / ProductRejected / ProductEditedAndApproved | PM; project details |
| Enter Implementation | ImplementationRequested | Assigned approver; Implementation/Review/{requestId} |
| Resubmit Implementation | ImplementationResubmitted | Assigned approver; implementation review |
| Implementation decision | ImplementationApproved / ImplementationRejected | PM; project details |
| Other commercial change, including delivery | CommercialStatus:{status} | PM; project details |
| Customer rejects SKU | ProductRejectedByCustomer | PM; project details |
| Transfer project | ProjectManagerChanged | New PM; project details |

Commercial notifications are informational, including when the PM performed the action.
The explicit approver is shared between initial review and Implementation. No broadcast to all
Managers, no guessing by department. A Director must currently have a Manager/SuperAdmin account.
Future distinct stage approvers or multiple recipients should use an assignment table.

## Data and authorization

AppUsers.NotificationApproverId is nullable and self-references AppUsers with NO ACTION deletion.
Administration → Users → Create/Edit exposes Email and Notification approver. Existing ManageUsers
permission governs editing. Candidates must be active Managers/SuperAdmins. This assignment only
routes notifications; it neither grants access nor makes that person the sole authorized reviewer.
Manager self-approval of Implementation remains disallowed; SuperAdmin self-approval is allowed.
No role or permission was assigned to any real user by this implementation.

NotificationOutbox contains event/recipient snapshots, project/product identifiers, subject/body,
relative link, status, attempt count, next attempt time, sent/processed time and safe last error.
Payload contains project number, customer, SKU, event, actor, reason/comment and UTC occurrence time.
Snapshot IDs intentionally have no business foreign keys, so business-row deletion cannot erase
notification history. Protect message content like AuditLogs. Logs contain IDs and error types,
not addresses, message bodies, credentials or arbitrary sender exception messages.

Enqueue requires the existing business transaction and uses its DbContext before CommitAsync.
Outbox persistence failure rolls back the business transaction; external sender failure happens
later and cannot affect a committed business action. Invalid/stale requests, no-op status updates
and rollback create no committed notifications. Old AuditLogs are not replayed/backfilled.

## Delivery and reliability

A unique SHA-256 EventKey uses event type, project/product, action UTC version and recipient purpose.
The same key is passed to IEmailSender on all attempts. SQL Server session application locks
serialize processing of each row across workers, without holding a transaction during delivery.
Processing/attempt count is saved before IO. Sender timeout is 30 seconds; implementations must
honor cancellation. Processing rows abandoned by a crashed worker become due after two minutes;
the SQL lock prevents takeover while the original worker still owns its session.

Statuses:
- Pending: ready.
- Blocked: missing/inactive/invalid recipient or missing email; checked again after five minutes
  without consuming attempts. Fix Users configuration; do not repeat the business action.
- Processing: started; abandoned due rows recover automatically.
- Retry: sender failure, exponential backoff from RetrySeconds, capped at one hour.
- Failed: MaxAttempts reached; operator investigation required.
- Sent: actual sender confirms delivery/acceptance; SentAtUtc populated.
- Simulated: logging sender only; no real email and SentAtUtc remains null.

Recipient routing/address can be resolved again until the first send attempt. After that the
recipient identity/address are frozen for retries; current active state/role eligibility is checked.
Do not automatically replay Failed, Sent or Simulated rows. Investigate Failed rows before any
controlled retry, preserve EventKey and check whether the provider already accepted the message.

This provides durable at-least-once delivery, application deduplication and serialized attempts.
A generic SMTP Outbox cannot promise exactly-once delivery if a process dies after provider
acceptance but before persisting Sent. Future real senders should support durable provider-side
idempotency using the provided key. A Message-ID header alone is not an exactly-once guarantee.
No SMTP/Graph integration or credentials are configured in this change.

## DEV manual verification

Only appsettings.Development.json was extended; no production settings were modified.
Defaults: WorkerEnabled=false, BaseUrl=https://portalmp.forte.local/, PollSeconds=15,
BatchSize=20, MaxAttempts=5, RetrySeconds=30. Links use this configured origin, never the HTTP Host.

1. Confirm User Secrets ConnectionStrings:DefaultConnection targets PrivateBrandsPortal_DEV.
   Apply migration `20261030144145_WorkflowNotificationOutbox`:
   `./dotnet.ps1 ef database update --project src/PrivateBrandsPortal.Web`.
   Never run this command against PrivateBrandsPortal production for this task.
2. In Users, configure a DEV PM Email and explicit Notification approver; configure that active
   Manager/SuperAdmin's Email. example.invalid addresses suffice for the logging-only sender.
   Do not assign an unrelated real reviewer simply to unblock the queue.
3. Save Draft (no event), Submit (ProjectSubmitted), then review one SKU (decision targets PM).
   Follow existing document requirements for Implementation. Reject/resubmit or approve/deliver.
4. Inspect DEV only:

   ```sql
   USE PrivateBrandsPortal_DEV;
   SELECT Id, EventType, ProjectId, ProductId, Audience, RecipientUserId, Status, Attempts,
          NextAttemptAtUtc, SentAtUtc, ProcessedAtUtc, LastError, RelativePath
   FROM dbo.NotificationOutbox ORDER BY Id DESC;
   ```

   Subject/Body can be inspected locally; they contain customer/user information.
5. Stop any existing localhost process before running a DEV worker:

   ```powershell
   $env:ASPNETCORE_ENVIRONMENT='Development'
   $env:Notifications__WorkerEnabled='true'
   ./dotnet.ps1 run --project src/PrivateBrandsPortal.Web --no-launch-profile --urls http://localhost:5080
   ```

   It processes all due DEV records and logs simulation keys. Expected status: Simulated, not Sent.
   No email is sent. Stop that process afterward and clear the flag:
   `Remove-Item Env:Notifications__WorkerEnabled`.
6. Clear a test recipient's Email and perform another valid transition. Workflow succeeds and the
   notification is Blocked. Restore Email; a later poll processes it without repeating the action.
7. Automated fake-sender tests cover failure → Retry → Sent, max attempts, crash recovery, two
   workers, unique keys, transaction rollback and actual workflow recipients:
   `./dotnet.ps1 test --filter FullyQualifiedName~NotificationTests`.

Future production work must separately address a real transport, reliable process lifetime
(IIS idle/recycling delays processing until restart), monitoring old/Blocked/Failed rows and a
retention policy. No production deployment, production database access or remote push is included.
