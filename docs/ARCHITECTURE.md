# Architecture

Relaywright is an ASP.NET Core Razor Pages application that accepts SMTP submissions from trusted devices, stores accepted messages durably, and relays them to one configured upstream smart host.

## Runtime Startup

`Program.cs` wires the application:

- Windows service and systemd hosting support
- `StorageOptions` and `BootstrapAdminOptions`
- `AppPaths` for database, spool, and Data Protection key directories
- SQLite EF Core contexts and context factory
- ASP.NET Core Identity
- Razor Pages authorization
- singleton domain services
- hosted services for SMTP intake, delivery, and maintenance

`DataSeeder` runs at startup. It creates the SQLite database if needed, performs the current manual schema upgrades, ensures one relay configuration row exists, seeds localhost trusted networks, and creates the bootstrap admin only when an explicit bootstrap password is configured. Otherwise the first admin is created through the first-run setup page.

## Storage

Default runtime storage lives under `App_Data`:

- SQLite database: `App_Data/relay.db`
- Message spool: `App_Data/spool`
- Data Protection keys: `App_Data/keys`

These are runtime artifacts and should remain ignored by source control.

## SMTP Intake Flow

1. `SmtpRelayHostedService` reads a `RelayConfigurationSnapshot`.
2. `SmtpOptionsFactory` creates SmtpServer listener options.
3. `TrustedNetworkMailboxFilter` allows submissions only from enabled trusted IPs/CIDRs.
4. `RelayMessageStore.SaveAsync` receives SMTP DATA.
5. `MessageSpoolService.WriteAsync` writes the raw message to disk.
6. `MessageQueueService.EnqueueAsync` stores queue metadata and recipients in SQLite.
7. `OperationalEventService` records session and queue events.

The critical guarantee is that accepted message content is written to the spool and queue metadata is saved before the SMTP server returns success.

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

## Maintenance Flow

`MaintenanceWorker` runs periodic cleanup through `MessageQueueService.CleanupAsync`:

- removes delivered messages after delivered retention
- removes failed or expired messages after failed retention
- expires active messages past `ExpiresUtc`
- removes old operational events
- deletes spool files for removed messages

## Security Boundaries

- SMTP clients are trusted by IP/CIDR, not by SMTP AUTH.
- Admin UI requires ASP.NET Core Identity authentication except the login page and `/health`.
- Stored secrets are protected with ASP.NET Core Data Protection.
- Microsoft 365 OAuth uses client credentials and caches access tokens in memory only.
- Operational events must never include raw secrets or access tokens.

## UI Sections

Navigation is centralized in `UI/AppNavigation.cs`.

- Overview: dashboard
- Settings: relay settings and trusted IPs
- Operations: queue and logs
- Diagnostics: upstream checks and test email

The UI is server-rendered Razor Pages with small page-local scripts for form behavior.
