# Testing

Relaywright tests are meant to protect the relay's safety guarantees while staying fast enough to run before every meaningful change.

The core rule: any behavior change needs a regression test that would fail if that behavior broke.

## Commands

Run the focused test project for service, queue, delivery, configuration, security, and data changes:

```powershell
dotnet test tests/Relaywright.Web.Tests/Relaywright.Web.Tests.csproj
```

Build the whole solution when touching Razor Pages, dependency wiring, project files, shared models, or startup behavior:

```powershell
dotnet build Relaywright.sln
```

## Test Layers

Use `Category=Unit` for focused service behavior that does not need the full local runtime shape. These tests should be small, direct, and deterministic.

Good unit-test targets:

- validation rules in `RelayConfigurationService`
- retry delay math
- delivery failure classification
- CIDR parsing and trusted-network matching boundaries
- spool path safety
- bootstrap-admin guard rules

Use `Category=Integration` for local multi-service tests that use real SQLite, real temporary app data, and real Relaywright services. These tests still must not use live SMTP relays, Microsoft endpoints, public network calls, or machine-specific network interfaces.

Good service-integration targets:

- SMTP DATA accepted -> spool write -> queue metadata saved -> SMTP OK
- spool write succeeds but queue metadata fails -> orphan spool deleted -> SMTP transaction failure
- queue enqueue -> claim -> mark delivered or failed
- trusted network mailbox filtering through the real trusted network service
- relay configuration save with real Data Protection secrets
- data seeding and schema upgrade idempotency

## What To Add

When changing SMTP intake, add or update tests that prove accepted mail is durably spooled and queued before success is returned. Also cover failure cleanup when queue persistence fails after a spool write.

When changing queue state, add tests around each affected transition: `Pending`, `InProgress`, `RetryScheduled`, `Delivered`, `Failed`, and `Expired`. Include delivery-attempt rows and `IQueueSignal` pulses where relevant.

When changing delivery or authentication, test failure classification and retry decisions with fakes. Do not use a live upstream SMTP server or Microsoft OAuth endpoint in automated tests.

When changing configuration or secrets, test validation, blank-secret preservation, runtime notifications, queue signaling, and Data Protection round trips. Never assert or log real secret values outside controlled test literals.

When changing trusted networks, test `CidrRange` directly and at least one service-level path through `TrustedNetworkService` or `TrustedNetworkMailboxFilter`.

When changing cleanup or retention, test eligibility and ordering with more than one record. Confirm spool files are deleted only after matching metadata is removed or terminal retention cleanup is complete.

When changing Razor Pages only, run the solution build and manually sanity-check the affected page. Browser-host tests can be added later as a separate layer.

## Test Support

Reusable support lives under `tests/Relaywright.Web.Tests/Support`:

- `SqliteTestStore` for in-memory SQLite plus `EnsureCreated`
- `TempAppData` for isolated spool and Data Protection key directories
- recording fakes for operational events, queue signals, relay configuration snapshots, and upstream delivery
- SMTP session, transaction, and mailbox fakes for intake/filter tests
- `TestData` builders for relay configuration, queue records, and MIME bytes

Prefer these helpers over ad hoc setup when adding tests. Keep fakes handwritten and minimal; the project intentionally avoids a mocking library for this layer.

## Test Hygiene

Keep tests deterministic:

- use temporary directories and in-memory SQLite
- avoid sleeps except where a worker retry delay is the behavior under test
- avoid external network dependencies
- use `DateTimeOffset.UtcNow` only with broad assertions, or seed explicit timestamps when ordering matters
- assert statuses, timestamps, events, delivery attempts, and file existence where they define the behavior

Test names should describe the behavior in plain language. A future maintainer should understand the protected guarantee from the name before reading the body.
