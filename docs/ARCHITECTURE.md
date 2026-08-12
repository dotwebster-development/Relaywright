# Architecture

## Feature Ownership Map

Use this map as the first route from a symptom or requested change to its behavioral owner. Entry points coordinate; decision owners contain rules; persistence/I/O owners contain side effects.

| Feature | Entry point | Decision owner | Persistence/I/O owner | Principal tests | Safety invariant |
| --- | --- | --- | --- | --- | --- |
| SMTP intake | `SmtpListenerHostedService` / SMTP filters | `TrustedNetworkMailboxFilter` | `MessageSpoolService`, `MessageQueueService` | `SmtpIntakeIntegrationTests`, `MessageQueueServiceTests` | DATA is durably spooled before `250 OK`; untrusted clients are rejected. |
| Trusted networks | `ITrustedNetworkService` | `CidrRange`, `SubmissionPolicyValidator` | `TrustedNetworkService` | `CidrRangeTests`, `TrustedNetworkIntegrationTests` | CIDR parsing is centralized and overlaps are rejected. |
| Submission policy | `TrustedDevicePolicyService` | `SubmissionPolicyEvaluator` | `TrustedDevicePolicyService` | `SubmissionPolicyEvaluatorTests`, `TrustedDevicePolicyDecisionTests` | Block lists win; the stricter positive limit applies. |
| Spool persistence | `IMessageSpoolService` | Spool path rules in `MessageSpoolService` | `ISpoolFileSystem` | `MessageSpoolServiceTests` | Failed durable writes never acknowledge SMTP acceptance. |
| Queue claims | `MessageQueueService` | `QueueClaimService`, `QueueStateTransitionPolicy` | `ApplicationDbContext` through the claim service | `MessageQueueServiceTests`, `QueueStateTransitionPolicyTests` | Claims are atomic and legal state transitions are centralized. |
| Delivery transitions | `QueueDeliveryWorker` | `DeliveryFailureClassifier`, `RetryDelayCalculator`, `QueueDeliveryStateService` | `IUpstreamDeliveryService`, queue database | `QueueDeliveryWorkerTests`, `DeliveryFailureClassifierTests`, `RetryDelayCalculatorTests` | Completion is idempotent; transient and permanent failures remain distinct. |
| Queue maintenance | `QueueMaintenanceWorker` / admin queue commands | `QueueMaintenanceService`, `QueueOperatorService` | `IMessageSpoolService`, queue database | `QueueLifecycleIntegrationTests`, `MessageQueueServiceTests` | Metadata/spool cleanup order and terminal retention are preserved. |
| Relay configuration | Settings Razor Pages | `RelayConfigurationValidator` | `RelayConfigurationService` | `RelayConfigurationServiceTests`, settings page tests | Secrets stay protected; listener changes notify runtime and queue changes wake delivery. |
| Configuration history | Settings save/delete handlers | `ConfigurationSnapshotPayloadFactory`, `ConfigurationSnapshotRestorer` | `ConfigurationSnapshotService`, `ConfigurationSnapshotSerializer` | `ConfigurationSnapshotServiceTests`, `ConfigurationSnapshotRollbackTests` | Protected relay entities remain protected in history. |
| Diagnostics | Diagnostics Razor Pages | `UpstreamTestEmailSender`, `SubmissionFlowChecker` | `IUpstreamDiagnosticSmtpSession`, `DiagnosticRunRecorder` | `UpstreamTestEmailSenderTests`, `DiagnosticRunRecorderTests` | No live dependency in deterministic tests; bodies and credentials are not recorded. |
| Alerts | `AlertService` / `AlertWorker` | `AlertEvaluator`, `AlertStateCoordinator` | `AlertRuleRepository`, `IAlertNotificationSender` | `AlertEvaluatorTests`, `AlertStateCoordinatorTests` | Activation, recovery, and cooldown transitions are deterministic. |
| Backups and restore | Backup Razor Pages / `BackupWorker` | `BackupRestoreArchiveExtractor`, `RestoredBackupValidator` | `BackupService`, `BackupRestoreService`, backup filesystem/repositories | `BackupServiceTests`, `BackupRestoreArchiveExtractorTests`, `RestoredBackupValidatorTests` | Restore archives cannot escape staging; credentials and key material are not restored. |
| Admin HTTPS | Setup and web-certificate Razor Pages | `AdminHttpsCertificateService`, `AdminHttpsCertificateMaterialService` | `AdminHttpsCertificateConfigurationStore`, `AdminHttpsCertificateFileStore` | `AdminHttpsCertificateServiceTests`, `AdminHttpsCertificateMaterialServiceTests` | Private keys are required and certificate passwords are protected at rest. |
| Account setup | `Account/SetupModel` | ASP.NET Identity options, `SetupHardeningChecklist` | `UserManager<ApplicationUser>`, listener/certificate services | `AdminSecurityVisibilityTests` | Anonymous setup cannot create a second administrator. |
| Runtime status | Dashboard and system Razor Pages | `RuntimeStatusService`, `DashboardReadinessService` | runtime probes and control-state repository | `RuntimeStatusServiceTests`, `DashboardReadinessServiceTests` | Pause affects outbound delivery only; restart-required state stays visible. |
| Updates | Updates Razor Page / update service | Release comparison and validation in update services | release client and update state store | `UpdateCheckServiceTests`, `UpdateCheckWorkerTests` | Update checks do not mutate relay configuration or queue state. |

Relaywright is an ASP.NET Core Razor Pages application that accepts SMTP submissions from trusted devices, stores accepted messages durably, and relays them to one configured upstream smart host.

## Runtime Startup

`Program.cs` owns startup and middleware composition. Feature registrations and hosted-worker registrations are grouped in `ServiceCollectionExtensions`:

- Windows service and systemd hosting support
- `StorageOptions`, `DatabaseOptions`, and `BootstrapAdminOptions`
- `AppPaths` for local data, spool, and Data Protection key directories
- provider-aware EF Core contexts and context factory for SQLite, SQL Server, or MySQL
- ASP.NET Core Identity
- Razor Pages authorization
- singleton domain services
- hosted services for SMTP intake, delivery, maintenance, alerts, and scheduled backups

`RelaywrightStartupOptions` binds and validates storage, database, bootstrap-admin, update-check, and queue-processing settings once before hosted services start. Invalid providers, unsafe storage leaf names, incomplete external database settings, invalid update ranges, and out-of-range stale-claim settings fail startup with an actionable options validation error.

`DataSeeder` runs at startup and delegates database creation, version-tracked SQLite schema upgrades, and SQL Server/MySQL schema verification to `DatabaseSchemaInitializer`. SQLite databases record the latest applied upgrade in `SchemaVersions`; unversioned databases are treated as the legacy baseline and converged through ordered, named `ILegacySqliteSchemaStep` implementations before the current version is recorded. After the schema is ready, `DataSeeder` ensures one relay configuration row exists, seeds localhost trusted networks, seeds runtime control and submission policy rows, seeds built-in alert rules, and creates the bootstrap admin only when an explicit bootstrap password is configured. Otherwise the first admin is created through the first-run setup page.

## Storage

Default runtime storage lives under `App_Data`:

- SQLite database: `App_Data/relay.db`
- Message spool: `App_Data/spool`
- Data Protection keys: `App_Data/keys`
- Backup bundles: `App_Data/backups`

The installers can configure SQL Server or MySQL instead of SQLite by writing `Database__Provider` and `Database__ConnectionString` into the service environment. This is intentionally not an admin UI setting. Server database backups are owned by the database platform; Relaywright's built-in database snapshot backup/restore path is SQLite-only.

These are runtime artifacts and should remain ignored by source control.

## SMTP Intake Flow

1. `SmtpRelayHostedService` reads a `RelayConfigurationSnapshot`.
2. `SmtpOptionsFactory` creates SmtpServer listener options.
3. `TrustedNetworkMailboxFilter` allows submissions only from enabled trusted IPs/CIDRs.
4. `TrustedDevicePolicyService` evaluates the matching device profile and global submission policy for sender, declared size, recipient domain, and recipient count.
5. `TrustedDeviceRateLimiter` enforces any per-device hourly message limit.
6. `RelayMessageStore.SaveAsync` receives SMTP DATA.
7. `MessageSpoolService.WriteAsync` writes the raw message to disk.
8. `MessageQueueService.EnqueueAsync` stores queue metadata and recipients in the configured database.
9. `OperationalEventService` records session and queue events.

The critical guarantee is that accepted message content is written to the spool and queue metadata is saved before the SMTP server returns success. Submission-policy rejections happen before DATA is accepted, so rejected content is not spooled.

## Submission Policy

Trusted-device policy has two layers:

- Global submission policy in `SubmissionPolicies`, edited at Settings > Submission Policy.
- Per-device profile fields on `TrustedNetworks`, edited at Settings > Trusted IPs.

Both layers support sender allow/block lists, recipient-domain allow/block lists, maximum message size, and maximum recipients. Per-device profiles also support owner, location, and hourly rate limits. Block lists take precedence over allow lists. If both the global policy and device profile define a numeric limit, the stricter value applies.

`TrustedDevicePolicyService` is the application facade for global policy persistence and operational events. `SubmissionPolicyValidator` owns list/limit validation and normalization for global and per-device policy values. `SubmissionPolicyEvaluator` owns pure sender, recipient, and effective-limit decisions and has no EF Core dependency.

SMTP enforcement remains in `TrustedNetworkMailboxFilter`, which resolves the trusted-device profile and global policy before delegating decisions. Policy enforcement must not move into pages or queue services because the relay must reject disallowed submissions before SMTP DATA is accepted.

## Configuration Flow

`RelayConfigurationService` owns relay configuration:

- loads immutable snapshots for runtime services
- loads edit models for Razor Pages
- delegates entity-to-snapshot and entity-to-edit-model conversion to `RelayConfigurationMapper`
- delegates save-policy validation to `RelayConfigurationValidator`
- protects and unprotects persisted secrets through `ISecretProtector`
- notifies SMTP listener restarts through `IRuntimeConfigurationNotifier`
- wakes delivery through `IQueueSignal`
- records configuration events

Listener changes are applied by restarting the SMTP listener in `SmtpRelayHostedService`.

Configuration history has separate ownership boundaries:

- `ConfigurationSnapshotService` persists history, creates automatic safety snapshots, records rollback audit entries, and coordinates rollback.
- `ConfigurationSnapshotPayloadFactory` captures the six supported areas using their stored representation. Relay settings remain protected persisted values rather than plaintext edit models.
- `ConfigurationSnapshotSerializer` owns snapshot JSON conversion.
- `ConfigurationSnapshotRestorer` applies category-specific persisted state and owns required SMTP notifications, queue wake-up signals, admin-listener file handling, and application restart requests.
- `ConfigurationSnapshotAreas` is the supported-area catalog used by capture and restore routing.

## Delivery Flow

`QueueDeliveryWorker` loops while the app is running:

1. Reads the latest relay configuration snapshot.
2. Claims eligible messages through `QueueClaimService`. The stale in-progress claim threshold comes from `QueueProcessing:StaleClaimMinutes` and defaults to 15 minutes.
3. Processes up to `DeliveryConcurrency` work items.
4. Sends mail through `UpstreamDeliveryService`.
5. Authenticates through `UpstreamAuthenticationService`.
6. Persists success or failure through `QueueDeliveryStateService`.

Failures are classified by `DeliveryFailureClassifier`. Transient failures are retried with exponential backoff from `RetryDelayCalculator`; permanent/configuration failures become terminal unless retry limits or expiration decide first.

Outbound delivery can be paused from Operations > Status. The pause only stops claiming new outbound work; SMTP intake continues to accept and durably spool messages from trusted devices. Resuming delivery pulses `IQueueSignal` so eligible work wakes promptly.

## Maintenance Flow

`MaintenanceWorker` runs periodic cleanup through `QueueMaintenanceService`, separate from live queue commands:

- removes delivered messages after delivered retention
- removes failed or expired messages after failed retention
- expires active messages past `ExpiresUtc`
- removes old operational events
- deletes spool files for removed messages

Cleanup and purge paths coordinate with backups through `IBackupCoordinator` so spool files are not removed while a backup bundle is collecting files referenced by its database snapshot.

## Operations

Runtime status, alerts, backups, diagnostics, and queue actions are intentionally operational rather than decorative:

- `RuntimeStatusService` tracks hosted-service state, delivery pause state, active deliveries, and cleanup heartbeat data.
- `ApplicationRestartService` persists restart-required state and requests a graceful process stop only when hosted by a restart-capable service manager.
- `DashboardService` assembles the dashboard application snapshot; `DashboardMetricsService` calculates message flow, storage usage, outgoing upstream route-local IP, and backup readiness.
- `DashboardReadinessService` derives the production checklist from HTTPS posture, upstream configuration, non-loopback trusted devices, an explicitly saved submission policy, successful diagnostics recorded after the current relay configuration, backup readiness, and optional alert-email routing. It does not maintain a parallel setup state.
- `AlertService` evaluates built-in operational risk rules and sends direct upstream email notifications when configured.
- `BackupService` coordinates backup runs, schedules, retention, and operator-visible results. `BackupFileStore` owns safe backup paths, working directories, deletion, and storage accounting. `BackupArchiveService` owns snapshot creation, credential sanitization, archive construction, encryption, and archive inspection. `BackupRestoreArchiveExtractor` owns restore archive trust checks and extraction, `RestoredBackupValidator` owns required SQLite structure and trusted-network validation, and `BackupRestoreService` keeps upload, decryption, sanitization, validation, pending-stage movement, and cleanup orchestration visible. `BackupRestoreFileSystem` owns the startup filesystem swap. Data Protection keys and admin HTTPS certificate password configuration are not included. Encrypted manual backups use a one-time password; scheduled backups remain unencrypted because the password is not stored.
- `BackupRunRepository` owns backup-run creation, updates, and recent-run ordering, including SQLite-safe `DateTimeOffset` ordering.
- `DiagnosticRunRecorder` persists staged connectivity, submission flow, and test-email diagnostics without SMTP transcripts, secrets, or message bodies.
- `AlertService` coordinates each evaluation pass. `AlertRuleRepository` owns rule administration and recent results, `AlertEvaluator` owns the supported observations, and `AlertStateCoordinator` owns activation/recovery, cooldown, notification, operational events, and result persistence.
- `ConfigurationSnapshotService` stores settings snapshots before operator changes and can roll back supported settings areas without touching queue, spool, keys, or certificate files.
- `QueueStateTransitionPolicy` is the single definition of allowed queue lifecycle transitions. Queue status is an EF Core concurrency token so competing delivery, maintenance, and operator mutations fail instead of silently overwriting one another.
- `MessageQueueService` is the `IMessageQueueService` facade: enqueue stays at the durability boundary, `QueueClaimService` owns provider-aware atomic claims and attempt creation, and `QueueDeliveryStateService` owns idempotent delivered/failed completion. `QueueOperatorService` owns admin retry and purge commands through `IQueueOperatorService`.
- `QueueQueryService` and `OperationalLogQueryService` own filtering, paging, provider-specific ordering, and read models for their Razor pages.
- `DetailedHealthService` owns authenticated database, spool, and configuration health probes. Dedicated `SecurityHeadersMiddleware` and `RequestLoggingMiddleware` own HTTP hardening and structured request logging; `ApplicationBuilderExtensions` remains a small pipeline-composition boundary.
- `FirstRunSetupService` owns first-admin concurrency, account creation, setup audit events, certificate operations, and hardening-state assembly; the anonymous setup page owns only HTTP and model-state flow.

## Security Boundaries

- SMTP clients are trusted by IP/CIDR, not by SMTP AUTH.
- Trusted IP/CIDR checks are only the first gate; submission policies can further restrict sender addresses, recipient domains, message size, recipient count, and per-device send rate.
- Admin UI requires ASP.NET Core Identity authentication except the login page and `/health`.
- Stored secrets are protected with ASP.NET Core Data Protection.
- Microsoft 365 OAuth uses client credentials and caches access tokens in memory only.
- Operational events must never include raw secrets or access tokens.
- Diagnostics and alert details must not include message bodies, passwords, certificate passwords, OAuth tokens, client secrets, or protected secret blobs.

## UI Sections

Navigation is centralized in `UI/AppNavigation.cs`.

- Overview: dashboard, runtime status, and outbound delivery pause/resume
- Settings: relay settings, submission policy, and trusted IP profiles
- Operations: queue and logs
- System: alerts, backups, change history, admin web listener settings, admin web certificate settings, and password changes
- Diagnostics: upstream connectivity checks, submission flow checks, and test email

The UI is server-rendered Razor Pages with reusable tag helpers for status
messages, table scroll regions, pagination, and destructive confirmations.
Shared deferred modules under `wwwroot/js` own browser behavior; inline scripts
and event handlers are prohibited so the strict self-only Content Security
Policy remains enforceable.

CSS is loaded as ordered, versioned static files:

- `foundation.css` for tokens and element defaults
- `layout.css` for the application shell and navigation
- `pages/setup.css` for first-run and certificate workflows
- `components/application.css` for operational components
- `responsive.css` for breakpoint behavior
- `components/shared-ui.css` for reusable tag-helper and dialog primitives

SQLite initialization is an ordered `ISqliteSchemaUpgrade` pipeline. The legacy
baseline is itself split into ordered, named `ILegacySqliteSchemaStep` units.
Each contiguous version is applied transactionally and recorded only after success.
Backup persistence is isolated in `BackupRunRepository` and
`BackupScheduleRepository`; `BackupService` coordinates the workflow without
issuing EF queries.

Rendered interaction coverage lives in the separate
`tests/Relaywright.Web.BrowserTests` Playwright lane. It launches Relaywright
against an isolated temporary data directory and verifies CSP/client errors,
keyboard settings search, shared confirmation cancellation, and mobile drawer
focus behavior.
