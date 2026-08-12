# Development Guidelines

Use these guidelines when changing Relaywright. They are intentionally conservative because this app handles mail durability, credentials, and operational troubleshooting.

## Before Editing

- Identify the subsystem: SMTP intake, queueing, delivery, security, configuration, diagnostics, UI, or tests.
- Read the relevant service interface and implementation together.
- Check existing tests for the behavior before adding new tests.
- Ignore generated files under `bin` and `obj`.
- Treat `App_Data` as local runtime state, not source.

## Coding Conventions

- Keep nullable reference types clean; do not silence nullability warnings with broad null-forgiving operators.
- Prefer constructor injection, as used throughout the app.
- Use `sealed` for service/page classes unless there is a concrete reason not to.
- Keep async all the way down for database, file, SMTP, HTTP, and hosted-service work.
- Pass `CancellationToken` through public async methods and background loops.
- Use `DateTimeOffset.UtcNow` for persisted timestamps.
- Keep comments rare and useful; prefer clear names and small methods.
- Keep the repository on the pinned C# language version and run the CI build with warnings as errors before merging infrastructure or composition changes.

## EF Core And Data

- Use `IDbContextFactory<ApplicationDbContext>` from singleton services and background workers.
- Use `AsNoTracking()` for read-only queries.
- Include related entities deliberately; avoid accidental lazy-loading assumptions.
- Preserve existing indexes and relationship delete behavior unless changing queue semantics intentionally.
- If changing database shape, update `ApplicationDbContext`, `DatabaseSchemaInitializer`, tests, and documentation together.
- Keep EF entity metadata in `Data/Configuration/ApplicationModelConfiguration`; `ApplicationDbContext` should remain the context boundary and `DbSet` catalog.
- Be careful with `EnsureCreated`; this app does not currently use a migrations pipeline.
- Add legacy SQLite convergence work as a named, ordered `ILegacySqliteSchemaStep`; keep the version orchestrator responsible for ordering, transactions, and version recording rather than accumulating more SQL in one method.
- SQLite cannot translate all `DateTimeOffset` ordering expressions. For small admin/history result sets, materialize first and order in .NET, and add a SQLite-backed regression test.

## Queue And Spool

- Preserve the order: write spool file, save queue metadata, then acknowledge SMTP success.
- Keep spool paths relative in database records and resolve absolute paths through `AppPaths` or `IMessageSpoolService`.
- Do not expose raw spool paths in UI unless needed for diagnostics.
- Do not remove queue metadata without considering the matching spool file.
- Route every queue status change through `QueueStateTransitionPolicy`; do not add page-local or service-local transition rules.
- Preserve optimistic concurrency on `QueuedMessage.Status` and make delivery-attempt completion idempotent so retrying an ambiguous database commit is safe.
- Wake the delivery worker with `IQueueSignal` after enqueue, manual retry, or relevant configuration changes.
- Keep queue responsibilities separated: `MessageQueueService` is the public facade and enqueue boundary, `QueueClaimService` owns atomic claims and attempt creation, and `QueueDeliveryStateService` owns delivery completion.

## Delivery And Retry

- Keep upstream host validation explicit; missing upstream configuration is a configuration failure.
- Keep delivery result classification centralized in `DeliveryFailureClassifier`.
- Treat SMTP 5xx as permanent and SMTP 4xx as transient unless there is a documented reason to differ.
- Keep retry delay behavior in `RetryDelayCalculator` and cover changes with tests.
- Avoid live network dependencies in automated tests.

## Secrets And Authentication

- Persist secrets only as protected values.
- Never write secrets, OAuth tokens, or passwords to logs, operational events, status messages, test output, or docs.
- Keep first-run admin setup one-time only; once any user exists, anonymous setup must not create another account.
- Keep Microsoft token acquisition in `MicrosoftOAuthTokenProvider`.
- Keep authentication choices in `UpstreamAuthenticationService`.
- When adding a new auth mode, update the enum, edit model, snapshot, validation, settings UI, authentication service, diagnostics, and tests.

## Logging

- Prefer structured logging with named properties instead of interpolated strings.
- Include correlation values when available: message ID, session ID, remote IP, queue status, SMTP response code, and elapsed milliseconds.
- Log user/admin actions at `Information`, rejected or suspicious actions at `Warning`, and failed operations with the exception at `Error`.
- Keep high-frequency detail such as request starts, queue polling, spool probes, and page reads at `Debug`.
- Do not log message bodies, passwords, OAuth tokens, client secrets, certificate passwords, protected secret blobs, or full raw MIME content.
- When logging configured secrets, log only whether a value is present or updated.
- Write user-visible operational history through `IOperationalEventService` when the event belongs in the admin Logs page; use `ILogger<T>` for service diagnostics and troubleshooting context.

## Trusted Networks

- Accept IP addresses or CIDR ranges through `CidrRange`.
- Do not duplicate IP/CIDR parsing elsewhere.
- Keep localhost seed networks unless the product decision changes.
- Security denials should create warning operational events without revealing sensitive message content.
- Treat trusted networks as device profiles, not only CIDR rows. Owner, location, sender lists, recipient-domain lists, size limits, recipient limits, and hourly rate limits belong on `TrustedNetwork`.
- Keep global submission policy persistence and events behind `ITrustedDevicePolicyService`.
- Keep sender/recipient decisions in `SubmissionPolicyEvaluator`; it must remain deterministic and independent of EF Core, logging, networking, and operational events.
- Keep shared global/per-device policy validation and normalization in `SubmissionPolicyValidator` instead of duplicating policy-list rules in persistence services.
- Enforce submission policy in `TrustedNetworkMailboxFilter` before SMTP DATA is accepted.
- Block lists must take precedence over allow lists.
- When both global and per-device numeric limits exist, the stricter limit should apply.
- Do not log message bodies or raw SMTP DATA when recording policy denials.

## Runtime Operations

- Delivery pause/resume must affect outbound delivery only. SMTP intake must continue to accept and spool mail from trusted devices.
- Pulse `IQueueSignal` when resuming delivery or after queue changes that should wake the delivery worker.
- Admin web listener and web certificate changes require a full application restart; use the restart service so unsupported hosting leaves a visible restart-required state instead of stopping the process.
- Keep alert administration in `AlertRuleRepository`, observations in `AlertEvaluator`, and activation/recovery/cooldown/notification transitions in `AlertStateCoordinator`; `AlertService` should remain orchestration.
- Keep alert evaluation deterministic and avoid live network dependencies in tests. Add direct evaluator and state-transition coverage when adding an alert type.
- Use the registered `TimeProvider` for operational timestamps and recurring-worker delays so time-based behavior stays deterministic in tests.
- Alert notifications must send directly through the configured upstream relay path rather than through the queued message pipeline.
- Backup creation must hold the backup coordinator lock while collecting spool files referenced by the database snapshot.
- Route spool and backup file operations through their filesystem boundaries. Cleanup failures must not hide the original write or archive failure.
- Backup validation should prove restore readiness without performing destructive restore work from the admin UI. Restore staging belongs behind authenticated admin pages and must not restore admin passwords, protected relay secrets, Data Protection keys, or admin HTTPS certificate passwords.
- Keep restore archive trust and extraction rules in `BackupRestoreArchiveExtractor`, restored database/CIDR validation in `RestoredBackupValidator`, and the ordered staging workflow in `BackupRestoreService`.
- Diagnostics should store staged results and sanitized details, not full SMTP transcripts.
- Keep diagnostic test-email sequencing in `UpstreamTestEmailSender` and real SMTP operations behind `IUpstreamDiagnosticSmtpSession`. Tests should use a fake session rather than a live relay.
- Submission flow diagnostics must reuse trusted-network and submission-policy services, and must not consume device rate-limit quota.
- Settings pages that support rollback should capture a configuration snapshot before saving or deleting operator-managed settings.
- Keep snapshot history/audit orchestration in `ConfigurationSnapshotService`, payload capture in `ConfigurationSnapshotPayloadFactory`, JSON conversion in `ConfigurationSnapshotSerializer`, and restoration side effects in `ConfigurationSnapshotRestorer`.
- Add a new snapshot area to `ConfigurationSnapshotAreas`, capture routing, restore routing, the feature ownership map, and direct capture/rollback tests together.
- Snapshot protected relay entities in their persisted form; do not serialize plaintext secret-bearing edit models into configuration history.
- Keep admin HTTPS validation and protected-password orchestration in `AdminHttpsCertificateService`, cryptographic material work in `AdminHttpsCertificateMaterialService`, JSON persistence in `AdminHttpsCertificateConfigurationStore`, and file mutation in `AdminHttpsCertificateFileStore`.
- Add legacy SQLite convergence work as a filename-aligned `ILegacySqliteSchemaStep`; register it and cover the resulting schema through `DatabaseSchemaInitializerTests`.

## Razor Pages UI

- Prefer existing classes: `page`, `page-header`, `settings-block`, `compact-grid`, `table`, `tabs`, `status`, and `actions`.
- Put shared styling in the responsibility asset that owns it: surfaces, forms, data, controls, or status. Preserve the load order in `_Layout.cshtml` and `site.css`.
- Keep forms server-rendered and POST-based.
- Use redirects after successful POSTs to avoid duplicate submissions.
- Put small page-specific scripts in the page's `Scripts` section.
- Keep visual changes consistent with the current admin-console style.

## Testing Expectations

Run the focused test project for most changes:

```powershell
dotnet test tests/Relaywright.Web.Tests/Relaywright.Web.Tests.csproj
```

Use `dotnet build Relaywright.sln` when touching Razor Pages, project files, dependency wiring, or shared models.

Add tests for:

- retry/backoff math
- CIDR parsing and matching
- trusted-device submission policy and SMTP rejection behavior
- delivery failure classification
- queue claiming and state transitions
- bulk queue retry/purge guards and summary counts
- schema upgrade behavior
- secret protection boundaries
- new authentication modes
- backup bundle creation/validation and retention pruning
- alert thresholds, cooldowns, notification recording, and SQLite-backed history ordering
- diagnostic run/stage persistence without secret or message-body leakage
- application restart-required state and unsupported-host fallback behavior
- submission flow checks without mutating rate-limit counters
- configuration snapshot creation and rollback behavior

Do not add automated tests that require real printers, live SMTP relays, Microsoft Entra tenants, or machine-specific local IP addresses.

## Operational Checks

For manual local checks:

```powershell
dotnet run --project src/Relaywright.Web/Relaywright.Web.csproj --urls http://127.0.0.1:5010
```

Then create or sign in with the initial admin account, configure a trusted network, and verify the relevant page or flow.

For frontend changes, reuse `rw-status-message`, `rw-table-shell`,
`rw-pagination`, and `rw-confirm`. Put reusable behavior in deferred modules
under `wwwroot/js`; inline scripts and inline event handlers fail architecture
tests and violate the application CSP. Preserve focus and combobox/listbox
semantics when changing navigation, search, or dialogs.

Run rendered browser and accessibility checks separately on Windows with the
installed Microsoft Edge channel:

```powershell
.\scripts\Test-Browser.ps1
```

For CI or a machine without Edge, install Playwright's pinned Chromium build and
use it explicitly:

```powershell
.\scripts\Test-Browser.ps1 -BrowserChannel chromium -InstallChromium
```

The browser project remains outside `Relaywright.sln` deliberately so the
normal deterministic unit-test lane does not require a browser installation.
The Windows and Linux test-deployment workflows also run this suite against the
newly deployed HTTPS service, using each environment's existing health URL and
bootstrap credentials.
