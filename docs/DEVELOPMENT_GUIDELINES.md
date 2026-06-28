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

## EF Core And Data

- Use `IDbContextFactory<ApplicationDbContext>` from singleton services and background workers.
- Use `AsNoTracking()` for read-only queries.
- Include related entities deliberately; avoid accidental lazy-loading assumptions.
- Preserve existing indexes and relationship delete behavior unless changing queue semantics intentionally.
- If changing database shape, update `ApplicationDbContext`, `DataSeeder.UpgradeSchemaAsync`, and documentation together.
- Be careful with `EnsureCreated`; this app does not currently use a migrations pipeline.

## Queue And Spool

- Preserve the order: write spool file, save queue metadata, then acknowledge SMTP success.
- Keep spool paths relative in database records and resolve absolute paths through `AppPaths` or `IMessageSpoolService`.
- Do not expose raw spool paths in UI unless needed for diagnostics.
- Do not remove queue metadata without considering the matching spool file.
- Wake the delivery worker with `IQueueSignal` after enqueue, manual retry, or relevant configuration changes.

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

## Razor Pages UI

- Prefer existing classes: `page`, `page-header`, `settings-block`, `compact-grid`, `table`, `tabs`, `status`, and `actions`.
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
- delivery failure classification
- queue claiming and state transitions
- schema upgrade behavior
- secret protection boundaries
- new authentication modes

Do not add automated tests that require real printers, live SMTP relays, Microsoft Entra tenants, or machine-specific local IP addresses.

## Operational Checks

For manual local checks:

```powershell
dotnet run --project src/Relaywright.Web/Relaywright.Web.csproj --urls http://127.0.0.1:5010
```

Then create or sign in with the initial admin account, configure a trusted network, and verify the relevant page or flow.
