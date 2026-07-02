# Architecture

Relaywright is an ASP.NET Core Razor Pages application that accepts SMTP submissions from trusted devices, stores accepted messages durably, and relays them to one configured upstream smart host.

## Runtime Startup

`Program.cs` wires the application:

- Windows service and systemd hosting support
- `StorageOptions`, `DatabaseOptions`, and `BootstrapAdminOptions`
- `AppPaths` for local data, spool, and Data Protection key directories
- provider-aware EF Core contexts and context factory for SQLite, SQL Server, or MySQL
- ASP.NET Core Identity
- Razor Pages authorization
- singleton domain services
- hosted services for SMTP intake, delivery, maintenance, alerts, and scheduled backups

`DataSeeder` runs at startup. For SQLite it creates the local database if needed and performs the current manual schema upgrades. For SQL Server/MySQL it initializes an empty configured database or verifies that the current Relaywright schema already exists. It then ensures one relay configuration row exists, seeds localhost trusted networks, seeds runtime control and submission policy rows, seeds built-in alert rules, and creates the bootstrap admin only when an explicit bootstrap password is configured. Otherwise the first admin is created through the first-run setup page.

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

Policy enforcement belongs in `TrustedNetworkMailboxFilter`, not in pages or queue services, because the relay must reject disallowed submissions before SMTP DATA is accepted.

## Configuration Flow

`RelayConfigurationService` owns relay configuration:

- loads immutable snapshots for runtime services
- loads edit models for Razor Pages
- validates settings before saving
- protects and unprotects persisted secrets through `ISecretProtector`
- notifies SMTP listener restarts through `IRuntimeConfigurationNotifier`
- wakes delivery through `IQueueSignal`
- records configuration events

Listener changes are applied by restarting the SMTP listener in `SmtpRelayHostedService`.

## Delivery Flow

`QueueDeliveryWorker` loops while the app is running:

1. Reads the latest relay configuration snapshot.
2. Claims eligible messages with `MessageQueueService.TryClaimNextAsync`.
3. Processes up to `DeliveryConcurrency` work items.
4. Sends mail through `UpstreamDeliveryService`.
5. Authenticates through `UpstreamAuthenticationService`.
6. Marks success or failure through `MessageQueueService`.

Failures are classified by `DeliveryFailureClassifier`. Transient failures are retried with exponential backoff from `RetryDelayCalculator`; permanent/configuration failures become terminal unless retry limits or expiration decide first.

Outbound delivery can be paused from Operations > Status. The pause only stops claiming new outbound work; SMTP intake continues to accept and durably spool messages from trusted devices. Resuming delivery pulses `IQueueSignal` so eligible work wakes promptly.

## Maintenance Flow

`MaintenanceWorker` runs periodic cleanup through `MessageQueueService.CleanupAsync`:

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
- `DashboardMetricsService` summarizes recent message flow, storage usage, outgoing upstream route-local IP, and backup readiness.
- `AlertService` evaluates built-in operational risk rules and sends direct upstream email notifications when configured.
- `BackupService` creates, validates, downloads, deletes, and prunes backup bundles. Backup database snapshots strip admin users, protected relay secrets, configuration snapshot history, and persisted diagnostic/operational failure text before archiving; Data Protection keys and admin HTTPS certificate password configuration are not included. Encrypted manual backups use a one-time password; scheduled backups remain unencrypted because the password is not stored. Successful backups are validated immediately so the dashboard can report restore readiness.
- `DiagnosticRunRecorder` persists staged connectivity, submission flow, and test-email diagnostics without SMTP transcripts, secrets, or message bodies.
- `ConfigurationSnapshotService` stores settings snapshots before operator changes and can roll back supported settings areas without touching queue, spool, keys, or certificate files.
- `MessageQueueService` supports single and bulk retry/purge operations while preserving queue-state guards and spool deletion ordering.

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

The UI is server-rendered Razor Pages with small page-local scripts for form behavior.
