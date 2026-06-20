# Project Improvement Review And GUI Redesign Plan

Review date: 2026-06-20

Scope: source, config, docs, and tests. Generated `bin`/`obj` output and runtime `App_Data` state were excluded from the review.

Implementation note: most items from this review were implemented on 2026-06-20. See `docs/IMPLEMENTATION_SUMMARY.md` for the completed work and remaining caveats.

## Top Improvement Backlog

### P0 - Reliability, Security, And Data Safety

1. Fix spool durability.
   `MessageSpoolService.WriteAsync` currently calls `FlushAsync`, but the project promise is durable spool-before-accept. Use an atomic temp-file write plus `Flush(true)` or `FileOptions.WriteThrough`, then move into place.

2. Protect spool paths.
   `AppPaths.GetSpoolAbsolutePath` combines paths without checking that the resolved path stays under `SpoolRootDirectory`. Validate normalized paths before open/read/delete.

3. Make queue claims atomic.
   `MessageQueueService.TryClaimNextAsync` loads candidate rows into memory and then updates the chosen message. Use a transaction or conditional update so multiple workers/processes cannot claim the same item.

4. Stop deleting spool files before DB cleanup succeeds.
   `CleanupAsync` deletes spool files before `SaveChangesAsync`. If saving fails, queue rows can remain without files. Delete after a successful metadata transition, or use a cleanup marker.

5. Restrict destructive queue actions.
   `RetryNowAsync` and `PurgeAsync` allow any message status. Prevent retrying delivered/in-progress items and prevent purging active deliveries unless an explicit force path exists.

6. Fix runtime configuration notification races.
   `RuntimeConfigurationNotifier.WaitForSmtpSettingsChangeAsync` can miss a notification between reading the version and returning the task. Capture the current source and version consistently.

7. Avoid `async void` SMTP event handlers.
   `SmtpRelayHostedService` uses `async void` handlers. Wrap event writes in safe fire-and-forget tasks or use a helper that catches/logs event write failures.

8. Make operational event writes safer.
   `OperationalEventService.WriteAsync` can throw inside primary flows, including SMTP accept/error paths. Consider best-effort event logging or explicit failure handling so logging failures do not break mail handling.

9. Remove production-use default admin password.
   `appsettings.json` and `BootstrapAdminOptions` contain `ChangeMe!12345`. Move dev defaults to development-only configuration and require production override.

10. Address the vulnerable SQLite native package.
    `dotnet list package --vulnerable --include-transitive` reports `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 with high severity advisory `GHSA-2m69-gcr7-jv3q`.

### P1 - Operational Quality

1. Move queue, log, and dashboard ordering/filtering into SQL.
   Several pages call `ToListAsync` before `OrderBy`/`Take`. This will get slow as logs and queues grow.

2. Add pagination and search to queue/log pages.
   Hard `Take(250)` views are simple but not enough for production troubleshooting.

3. Expand test coverage around queue state transitions.
   Current tests cover retry delay, CIDR, and basic failure classification only.

4. Add validation for all relay numeric settings.
   Save currently validates only some ranges. Validate retry counts, delays, retention hours, max message size, and upstream host intent consistently.

5. Improve schema evolution.
   The app uses `EnsureCreated` and manual `ALTER TABLE` upgrades. Either formalize that approach with tests or move to EF migrations.

6. Add CI.
   `.github` exists but has no workflow files. Add restore/build/test and package vulnerability checks.

7. Add service health checks.
   `/health` only returns `ok`; it does not verify DB access, spool directory access, configuration validity, or worker/listener state.

### P2 - Maintainability And UX

1. Split the large relay settings form into clearer sections or tabs.
2. Add status badges for queue status, event severity, auth mode, and listener state.
3. Add confirmation UI for delete/purge operations.
4. Reduce repeated configuration model properties or generate mapping carefully.
5. Add optional telemetry-style counters for accepted, delivered, failed, retrying, and denied submissions.
6. Update documentation with deployment, Windows service setup, backup/restore, and recovery guidance.

## File-By-File Improvement Notes

### Repository And Documentation

- `.gitignore`
  Add `.tmp-run.*`, local logs, optional `.env`, and explicit runtime DB patterns as belt-and-suspenders protection. `App_Data/` is ignored, but runtime files are currently present in the working folder.

- `AGENTS.md`
  Good guardrail. Keep it updated when queue semantics, migrations, or the UI redesign direction changes.

- `Directory.Build.props`
  Consider enabling warnings as errors in CI, adding .NET analyzers, and avoiding `LangVersion=latest` if reproducibility matters more than language drift.

- `README.md`
  Add deployment steps, Windows service installation, backup/restore, production secret setup, health checks, troubleshooting, and known dependency advisory notes.

- `Relaywright.sln`
  No immediate issue. Consider solution folders only if the project grows.

- `docs/ARCHITECTURE.md`
  Good baseline. Add diagrams or sequence flows after queue-claim and spool-durability changes.

- `docs/DEVELOPMENT_GUIDELINES.md`
  Add a release checklist once CI, dependency scanning, and deployment guidance exist.

- `global.json`
  Align the stated verified SDK with README. It requests `10.0.100` with feature roll-forward, while the current machine and README use `10.0.300`.

### Project, Startup, And Options

- `src/Relaywright.Web/Relaywright.Web.csproj`
  Update Microsoft packages from `10.0.0` to current patch versions, investigate whether that resolves the SQLite native advisory, and evaluate `SmtpServer` 11.1.0 separately because it is a major update.

- `src/Relaywright.Web/appsettings.json`
  Do not ship a real default bootstrap password in the main config. Move sample credentials to docs or development config and require production override.

- `src/Relaywright.Web/appsettings.Development.json`
  If development credentials are needed, put local-only bootstrap values here or use user secrets.

- `src/Relaywright.Web/Program.cs`
  Add options validation, cookie/lockout hardening, richer health checks, startup error handling for hosted services, and possibly extension methods to group service registration.

- `src/Relaywright.Web/Options/BootstrapAdminOptions.cs`
  Add validation and remove the production-looking default password.

- `src/Relaywright.Web/Options/StorageOptions.cs`
  Validate non-empty directory and file names. Consider rejecting unsafe file names for the database/spool/key directories.

- `src/Relaywright.Web/Infrastructure/AppPaths.cs`
  Guard against path traversal for spool paths. Consider centralizing safe path resolution and adding tests.

### Configuration And Data Model

- `src/Relaywright.Web/Configuration/RelayConfigurationEditModel.cs`
  Add data annotations or a dedicated validator. Consider separate secret replacement fields so GET does not need to carry unprotected secrets.

- `src/Relaywright.Web/Configuration/RelayConfigurationSnapshot.cs`
  Keep as runtime-only. If secrets remain here, ensure snapshots never leak to UI or event details.

- `src/Relaywright.Web/Data/ApplicationDbContext.cs`
  Add indexes for cleanup and dashboard queries, especially status plus delivered/expired timestamps and operational event filters. Consider concurrency tokens for queue claiming.

- `src/Relaywright.Web/Data/Entities/DeliveryAttempt.cs`
  Good shape. Add indexes if attempt history grows or if querying by completion/failure becomes common.

- `src/Relaywright.Web/Data/Entities/DeliveryFailureCategory.cs`
  Good. Consider adding `MessageFormat` if invalid MIME/address data should be separated from upstream failures.

- `src/Relaywright.Web/Data/Entities/EventSeverity.cs`
  Good. Consider a success/notice style only if the UI needs finer display states.

- `src/Relaywright.Web/Data/Entities/OperationalEvent.cs`
  Add length trimming before persistence and consider a correlation/request id for admin actions.

- `src/Relaywright.Web/Data/Entities/OperationalEventCategory.cs`
  Good. Consider separate `Authentication` or `Admin` categories if account activity becomes auditable.

- `src/Relaywright.Web/Data/Entities/QueuedMessage.cs`
  Add concurrency control and indexes. Consider storing normalized sender/recipient columns for searching.

- `src/Relaywright.Web/Data/Entities/QueuedMessageRecipient.cs`
  Add a unique index on message plus recipient if duplicates should be impossible at the database level.

- `src/Relaywright.Web/Data/Entities/QueuedMessageStatus.cs`
  Good. Document allowed transitions in tests.

- `src/Relaywright.Web/Data/Entities/RelayConfiguration.cs`
  Validate persisted ranges more strictly. Decide whether disabling auth should clear stored auth secrets.

- `src/Relaywright.Web/Data/Entities/TrustedNetwork.cs`
  Normalize CIDR and description on create/update. Consider storing parsed network family/prefix for faster lookup if needed.

- `src/Relaywright.Web/Data/Entities/UpstreamAuthenticationMode.cs`
  Good. When adding modes, update settings UI, validation, diagnostics, and tests together.

- `src/Relaywright.Web/Identity/ApplicationUser.cs`
  Add max length configuration for `DisplayName` and consider audit fields for account operations.

### Relay, Security, And Events Services

- `src/Relaywright.Web/Services/Relay/DataSeeder.cs`
  Replace ad hoc schema upgrades with tested migrations or a formal schema upgrader. Fail production startup if bootstrap admin password is still default. Fix/cover upgrade idempotency.

- `src/Relaywright.Web/Services/Relay/IRelayConfigurationService.cs`
  Good. If configuration grows, split read/write interfaces or add a versioned snapshot.

- `src/Relaywright.Web/Services/Relay/IRuntimeConfigurationNotifier.cs`
  Good contract. Add tests for missed notifications and multiple waiters.

- `src/Relaywright.Web/Services/Relay/RelayConfigurationService.cs`
  Add complete validation for retry/retention settings, preserve secrets only when fields are intentionally unchanged, and avoid returning clear secrets to edit forms.

- `src/Relaywright.Web/Services/Relay/RuntimeConfigurationNotifier.cs`
  Fix the wait race by capturing source/version atomically or using a channel.

- `src/Relaywright.Web/Services/Security/CidrRange.cs`
  Good and covered by tests. Add edge-case tests for `/0`, `/32`, `/128`, malformed prefixes, IPv4/IPv6 mismatch, and whitespace.

- `src/Relaywright.Web/Services/Security/DataProtectionSecretProtector.cs`
  The fallback that returns protected text after any unprotect failure is risky. Limit fallback to known legacy plain-text values, log a warning without secrets, or force re-entry.

- `src/Relaywright.Web/Services/Security/ISecretProtector.cs`
  Good. Consider a result type if callers need to know whether unprotect failed.

- `src/Relaywright.Web/Services/Security/ITrustedNetworkService.cs`
  Good. Add a method or notification hook if trusted networks become cached.

- `src/Relaywright.Web/Services/Security/TrustedNetworkService.cs`
  Trim/normalize new records, fix the create/update event message after EF assigns the new id, and consider caching enabled networks with invalidation on save/delete.

- `src/Relaywright.Web/Services/Events/IOperationalEventService.cs`
  Good. Consider exposing a best-effort method for event paths where failures must not affect SMTP behavior.

- `src/Relaywright.Web/Services/Events/OperationalEventRequest.cs`
  Add validation or a factory for required message/category fields.

- `src/Relaywright.Web/Services/Events/OperationalEventService.cs`
  Make event persistence resilient, trim long message/detail values, and avoid allowing logging failures to break primary flows.

### SMTP, Queueing, And Delivery

- `src/Relaywright.Web/Services/Smtp/RelayMessageStore.cs`
  Clean up orphan spool files if queue metadata save fails. Make event-write failures non-fatal in the catch path.

- `src/Relaywright.Web/Services/Smtp/SmtpOptionsFactory.cs`
  Catch certificate loading errors at listener startup and surface a clear operational event. Consider validating certificate path/password during settings save or diagnostics.

- `src/Relaywright.Web/Services/Smtp/SmtpRelayHostedService.cs`
  Remove `async void` handlers, catch `CreateServer` failures, and expose listener status for health/dashboard UI.

- `src/Relaywright.Web/Services/Smtp/SmtpSessionContextExtensions.cs`
  Good. Add tests if SmtpServer session context can be constructed/mocked easily.

- `src/Relaywright.Web/Services/Smtp/TrustedNetworkMailboxFilter.cs`
  Good boundary. Consider logging accepted submissions at a lower-volume point to avoid event noise on busy relays.

- `src/Relaywright.Web/Services/Queueing/DeliveryResult.cs`
  Good. Consider distinguishing upstream response from local processing failure more explicitly.

- `src/Relaywright.Web/Services/Queueing/DeliveryWorkItem.cs`
  Good. Include accepted/created timestamps if worker metrics need them.

- `src/Relaywright.Web/Services/Queueing/IMessageMetadataService.cs`
  Good.

- `src/Relaywright.Web/Services/Queueing/IMessageQueueService.cs`
  Add state-aware operations or return results so UI can show "not allowed" instead of throwing or blindly mutating state.

- `src/Relaywright.Web/Services/Queueing/IMessageSpoolService.cs`
  Add safe-path semantics and possibly a temp-write method to support atomic durability.

- `src/Relaywright.Web/Services/Queueing/IQueueSignal.cs`
  Good.

- `src/Relaywright.Web/Services/Queueing/MessageMetadataService.cs`
  Handle malformed spool files gracefully and consider parsing headers only instead of loading full MIME bodies for details pages.

- `src/Relaywright.Web/Services/Queueing/MessageMetadataSummary.cs`
  Good. Add size or attachment summary if operators need it.

- `src/Relaywright.Web/Services/Queueing/MessageQueueService.cs`
  Highest-change service: atomic claims, SQL-side selection, state guards, safer cleanup ordering, pagination-ready queries, and more tests.

- `src/Relaywright.Web/Services/Queueing/MessageSpoolService.cs`
  Add atomic temp-file writes, stronger disk flush, safe path resolution, and tests for delete/open behavior.

- `src/Relaywright.Web/Services/Queueing/NewQueuedMessageRequest.cs`
  Good. Validate recipient count and non-empty spool path before enqueue.

- `src/Relaywright.Web/Services/Queueing/QueueSignal.cs`
  Good simple signal. Add tests if queue worker behavior gets more complex.

- `src/Relaywright.Web/Services/Queueing/RetryDelayCalculator.cs`
  Good and tested. Consider optional jitter only if upstream retry storms become a real issue.

- `src/Relaywright.Web/Services/Delivery/DeliveryFailureClassifier.cs`
  Add tests for SMTP 4xx/5xx. Treat malformed MIME/address parse failures as permanent or message-format failures rather than generic transient failures.

- `src/Relaywright.Web/Services/Delivery/IUpstreamAuthenticationService.cs`
  Good.

- `src/Relaywright.Web/Services/Delivery/IUpstreamDeliveryService.cs`
  Good.

- `src/Relaywright.Web/Services/Delivery/MaintenanceWorker.cs`
  Good simple worker. Add jitter or schedule configuration if many instances ever run.

- `src/Relaywright.Web/Services/Delivery/MicrosoftOAuthTokenProvider.cs`
  Include secret/config version in the token cache key, add focused tests around transient vs configuration errors, and avoid storing failed response payloads in overly visible places.

- `src/Relaywright.Web/Services/Delivery/QueueDeliveryWorker.cs`
  If `ProcessWorkItemAsync` fails outside `DeliverAsync`, mark the message for retry/failure or explicitly document the stale in-progress recovery path.

- `src/Relaywright.Web/Services/Delivery/UpstreamAuthenticationService.cs`
  Add tests for disabled auth, basic auth validation, OAuth auth flow boundaries, and unsupported modes.

- `src/Relaywright.Web/Services/Delivery/UpstreamDeliveryService.cs`
  Validate/parse envelope and recipients before connecting where possible, classify local parse failures correctly, and disconnect in a `finally` when connected.

### Diagnostics Services

- `src/Relaywright.Web/Services/Diagnostics/ConnectivityTestResult.cs`
  Add elapsed time, endpoint, TLS/auth stage, or failure category for richer UI diagnostics.

- `src/Relaywright.Web/Services/Diagnostics/IUpstreamConnectivityTester.cs`
  Good.

- `src/Relaywright.Web/Services/Diagnostics/IUpstreamTestEmailSender.cs`
  Good.

- `src/Relaywright.Web/Services/Diagnostics/TestEmailRequest.cs`
  Good DTO. Keep validation in page model or add a shared validator if reused.

- `src/Relaywright.Web/Services/Diagnostics/TestEmailResult.cs`
  Add elapsed time or upstream response if useful.

- `src/Relaywright.Web/Services/Diagnostics/UpstreamConnectivityTester.cs`
  Return staged diagnostics instead of a single message, and use failure classification so UI can distinguish configuration from transient network errors.

- `src/Relaywright.Web/Services/Diagnostics/UpstreamTestEmailSender.cs`
  Good event trail. Redact or limit exception detail if upstream responses can contain sensitive tenant/account info.

### Razor Pages And UI

- `src/Relaywright.Web/Pages/_ViewImports.cshtml`
  Good.

- `src/Relaywright.Web/Pages/_ViewStart.cshtml`
  Good.

- `src/Relaywright.Web/Pages/Account/ChangePassword.cshtml`
  Add validation summary and autocomplete attributes.

- `src/Relaywright.Web/Pages/Account/ChangePassword.cshtml.cs`
  Add input validation attributes and consider logging/admin event for password changes.

- `src/Relaywright.Web/Pages/Account/Login.cshtml`
  Add autocomplete attributes and a more static login surface in the redesign.

- `src/Relaywright.Web/Pages/Account/Login.cshtml.cs`
  Enable lockout or throttling. Keep return URL handling local-only as it is now.

- `src/Relaywright.Web/Pages/Account/Logout.cshtml`
  Fine as POST-only endpoint.

- `src/Relaywright.Web/Pages/Account/Logout.cshtml.cs`
  Good.

- `src/Relaywright.Web/Pages/Diagnostics/Index.cshtml`
  Add current endpoint summary and staged result display. Keep the action visually quiet.

- `src/Relaywright.Web/Pages/Diagnostics/Index.cshtml.cs`
  Good. Consider a result object that includes stage and elapsed time.

- `src/Relaywright.Web/Pages/Diagnostics/TestEmail.cshtml`
  Good feature. In redesign, use a two-column layout with compose on the left and run log on the right.

- `src/Relaywright.Web/Pages/Diagnostics/TestEmail.cshtml.cs`
  Initialize defaults after invalid POST too if the form needs to stay complete. Consider preserving latest run id across refresh if useful.

- `src/Relaywright.Web/Pages/Error.cshtml`
  Add request id and a less generic admin-facing message.

- `src/Relaywright.Web/Pages/Error.cshtml.cs`
  Capture request id if exposed in the page.

- `src/Relaywright.Web/Pages/Index.cshtml`
  Reduce marketing-style explanatory copy and make it a compact operations overview.

- `src/Relaywright.Web/Pages/Index.cshtml.cs`
  Move delivered-today and recent-event ordering/filtering into SQL. Add listener/upstream health state if available.

- `src/Relaywright.Web/Pages/Logs/Index.cshtml`
  Replace `title` detail with an accessible detail row/page. Add severity badges, search, and pagination.

- `src/Relaywright.Web/Pages/Logs/Index.cshtml.cs`
  Order and limit in SQL. Add date range, text search, and pagination.

- `src/Relaywright.Web/Pages/Messages/Details.cshtml`
  Add destructive-action confirmation, status badges, and a delivery-attempt timeline. Hide or de-emphasize raw spool paths.

- `src/Relaywright.Web/Pages/Messages/Details.cshtml.cs`
  Return 404 for missing messages. Guard retry/purge by status.

- `src/Relaywright.Web/Pages/Queue/Index.cshtml`
  Add status badges, queue search, pagination, and row actions appropriate to status.

- `src/Relaywright.Web/Pages/Queue/Index.cshtml.cs`
  Order/take in SQL and expose paging/filter metadata.

- `src/Relaywright.Web/Pages/Settings/Relay.cshtml`
  Split into flatter sections or tabs: Listener, Upstream, Authentication, Retry/Retention. Avoid carrying clear secret values to the browser.

- `src/Relaywright.Web/Pages/Settings/Relay.cshtml.cs`
  Add validation attributes or service-side result model. Preserve existing secrets when password fields are blank.

- `src/Relaywright.Web/Pages/Settings/TrustedNetworks.cshtml`
  Add validation summary, delete confirmation, and normalized CIDR display.

- `src/Relaywright.Web/Pages/Settings/TrustedNetworks.cshtml.cs`
  Good page model, but service errors are currently hard to see because the page lacks a validation summary.

- `src/Relaywright.Web/Pages/Shared/_Layout.cshtml`
  Current layout is visually heavy for an admin tool. Redesign around a flatter static header/sidebar and reduce inline SVG/icon complexity.

- `src/Relaywright.Web/UI/AppNavigation.cs`
  Good central navigation. Consider adding a compact page title/section descriptor model for the redesigned header.

- `src/Relaywright.Web/wwwroot/css/site.css`
  Main redesign target. Remove gradients, backdrop blur, hover transforms, heavy shadows, pill-heavy controls, and large rounded panels. Replace with a flat token system.

### Tests

- `tests/Relaywright.Web.Tests/Relaywright.Web.Tests.csproj`
  Update test SDK and runner packages. Consider adding FluentAssertions or a small test fixture library only if it improves clarity.

- `tests/Relaywright.Web.Tests/CidrRangeTests.cs`
  Add malformed input, `/0`, host-route, whitespace, and family mismatch cases.

- `tests/Relaywright.Web.Tests/DeliveryFailureClassifierTests.cs`
  Add SMTP 4xx/5xx cases and local parse/error classification cases.

- `tests/Relaywright.Web.Tests/RetryDelayCalculatorTests.cs`
  Add sanitization edge cases for zero/negative attempts and max delay below initial delay.

## GUI Redesign Plan

### Design Direction

Aim for a flat operations console, not a product landing page. The interface should feel like a reliable control panel for a local service:

- static, quiet, and predictable
- white/gray surfaces with crisp borders
- restrained blue accent for primary actions and active navigation
- status colors only for state: green, amber, red, gray
- compact typography and smaller page headers
- no gradients, blur, decorative shadows, hover lifts, or animated nav
- 4px to 8px radii instead of pill-heavy controls
- tables and forms optimized for scanning

### Proposed Layout

Use a fixed shell:

1. Top bar
   Product name on the left, service health badges in the middle or right, user/logout actions on the right.

2. Left navigation
   Simple text+small-icon list: Overview, Queue, Logs, Settings, Diagnostics. Flat active state with a left border or muted background.

3. Content header
   Small title, optional one-line context, and page actions aligned right. Remove long explanatory paragraphs from most pages.

4. Work area
   Use flat bordered sections, compact tables, form fieldsets, and status badges.

### Screen Concepts

- Dashboard
  A compact service strip at top: SMTP listener, upstream relay, queue worker, database/spool. Below that, four small counters and two tables: "Needs attention" and "Recent events".

- Queue
  Primary operations screen. Filter toolbar with status, text search, date range, and refresh. Table rows use status badges and concise columns. Row details link opens the message page.

- Message details
  Summary panel on top, parsed headers below, delivery attempt timeline/table on the side or below. Retry/purge actions are status-aware and confirm destructive actions.

- Logs
  Event explorer with category tabs, severity filter, search, date range, and an event detail page or expandable row. Avoid using tooltip-only details.

- Relay settings
  A settings workbench with static sections or top tabs:
  Listener, Upstream, Authentication, Delivery, Retention. Save button at the bottom of each section. Secret fields use "leave blank to keep current" behavior.

- Trusted IPs
  Table-first layout with an add/edit form in a narrow side panel or top fieldset. Normalize CIDR display and show enabled state as a badge.

- Diagnostics
  Two tools: connection test and test email. Results show stages: resolve/connect/TLS/auth/send, elapsed time, and final outcome.

- Login/password pages
  Minimal centered panel, no gradients, no product marketing copy.

### CSS Implementation Plan

1. Replace current tokens.
   Define neutral colors, spacing, border radius, and status colors.

2. Remove dynamic styling.
   Delete gradients, `backdrop-filter`, large shadows, hover `transform`, and most transitions.

3. Rebuild layout classes.
   New shell classes: `app-frame`, `topbar`, `sidebar`, `main`, `page-titlebar`, `toolbar`.

4. Rebuild component classes.
   `button`, `button-secondary`, `badge`, `status-badge`, `data-table`, `field-grid`, `section`, `empty-state`, `danger-zone`.

5. Migrate pages incrementally.
   Start with layout and dashboard, then queue/logs, then settings, then diagnostics/account.

### Suggested Redesign Phases

1. Phase 1 - Static shell and tokens
   Update `_Layout.cshtml`, `AppNavigation.cs`, and `site.css` without changing page behavior.

2. Phase 2 - Tables and badges
   Update queue, logs, dashboard, and message details with status/severity badges and denser tables.

3. Phase 3 - Settings cleanup
   Split relay settings visually, add validation summaries, and change secret fields to blank-means-unchanged.

4. Phase 4 - Diagnostics and account polish
   Add staged diagnostic results and simplify login/password pages.

5. Phase 5 - Verification
   Build, run tests, manually check desktop and mobile widths, and verify no text overlap in dense tables/forms.

## Suggested Implementation Order

1. Fix dependency advisory and package patch updates.
2. Fix spool safety/durability and queue cleanup ordering.
3. Add queue state guards for retry/purge.
4. Add SQL-side ordering/pagination for queue/log/dashboard.
5. Add tests for the above.
6. Redesign shell/CSS.
7. Redesign operational pages.
8. Redesign settings/diagnostics pages.
9. Add CI and deployment docs.
