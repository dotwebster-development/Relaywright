# Responsibility-Driven Refactoring Plan

Status: active implementation plan
Started: August 1, 2026
Scope: `src/Relaywright.Web`, its focused tests, browser tests, and developer architecture documentation

## Objective

Improve Relaywright's feature discoverability, diagnosis, and change safety by giving each important behavior one clear owner. The target is cohesive responsibilities and explicit boundaries, not uniformly tiny files.

This plan is behavior-preserving unless a separately documented defect is found. Refactoring work must not weaken SMTP acceptance durability, trusted-network enforcement, secret protection, queue state transitions, spool cleanup ordering, runtime configuration notifications, queue wake-up signals, or operational audit events.

## Progress

| Phase | Status | Evidence |
| --- | --- | --- |
| Plan | Complete | Detailed scope, ownership targets, tests, and acceptance gates recorded in this document. |
| Phase 0 | Complete | 258 main tests and 4 Edge browser tests passed; solution build completed with 0 warnings and 0 errors; live dashboard and submission-policy pages rendered without browser console errors. |
| Phase 1 | Complete | Submission policy persistence, pure evaluation, and shared validation have distinct owners; 41 focused tests and 279 full-suite tests passed. |
| Phase 2A | Complete | Snapshot history orchestration, serialization, payload capture, and restoration have distinct owners; all six areas have rollback coverage; 287 full-suite tests passed. |
| Phase 2B | Complete | Alert administration, observation evaluation, and state/notification transitions have distinct owners; all evaluator types and lifecycle transitions have direct tests; 301 full-suite tests passed. |
| Phase 2C | Complete | Archive trust/extraction and restored SQLite/CIDR validation have distinct owners with direct safety tests; 308 full-suite tests passed. |
| Phase 2D | Complete | Certificate material, configuration JSON, and filesystem operations have distinct owners; direct material and facade tests pass. |
| Phase 2E | Complete | Diagnostic SMTP operations are behind a fakeable session boundary; ordered success, failure, authentication, cancellation, and configuration tests pass without a live relay. |
| Phase 3 | Complete | Security summaries and legacy schema steps now have filename-aligned owners; application CSS is split into five responsibility assets. |
| Phase 4 | Complete | Feature ownership routing and dependency-direction architecture guards are documented and tested. |
| Final validation | Complete locally | 319 main tests and 4 Edge browser tests passed; solution build completed with 0 warnings and 0 errors; live CSS asset inspection found no console errors. Remote Windows/Linux deployment lanes remain pending until the tranche is committed and pushed. |

## Working Rules

- Keep one primary concept per file and keep filenames aligned with the primary type.
- Separate pure decisions from persistence, network, filesystem, logging, and event side effects.
- Keep orchestration readable from one place; do not split a workflow into one-method fragments.
- Add characterization tests before extracting behavior that is not already directly covered.
- Do not introduce an interface solely to reduce file length. Add an abstraction when it creates a useful test or ownership boundary.
- Do not enforce a maximum line count. Treat large files, many constructor dependencies, unrelated reasons to change, and mixed I/O and decision logic as review signals.
- Complete and verify one subsystem before beginning the next.
- Keep refactoring changes separate from behavior changes. If a defect is found, record it and fix it in a separately reviewable change.
- Update `docs/ARCHITECTURE.md`, `docs/DEVELOPMENT_GUIDELINES.md`, and the feature ownership map when ownership moves.

## Verification Gates

Every implementation phase must meet the following gates before the next phase starts:

1. Focused tests for the changed subsystem pass.
2. `dotnet test tests/Relaywright.Web.Tests/Relaywright.Web.Tests.csproj` passes without skipped tests.
3. `dotnet build Relaywright.sln` completes without new warnings or errors.
4. Browser tests run when Razor, CSS, JavaScript, middleware-visible behavior, or user workflows change.
5. No secret, token, password, protected blob, SMTP message body, or raw credential is added to logs, events, tests, or documentation.
6. Relevant architecture and development documentation agrees with the implementation.
7. The working-tree diff is reviewed for accidental generated files and unrelated edits.

The Windows and Linux deployment lanes remain release/deployment evidence. They are not replaced by local unit or browser tests.

## Phase 0 - Stabilize The Existing Architecture Baseline

### Purpose

Preserve and validate the architecture-hardening work already present in the working tree before layering another refactoring tranche on top of it.

### Existing scope to preserve

- Database creation and schema-upgrade extraction from `DataSeeder`.
- Queue claim, completion, maintenance, operator, and query boundaries.
- Backup archive, file-store, run-repository, and schedule-repository boundaries.
- Runtime dashboard and detailed health services.
- Startup, middleware, service-registration, and option parsing boundaries.
- Shared UI tag helpers, deferred JavaScript modules, and split CSS assets.
- Browser tests and Windows/Linux deployment-workflow integration.

### Work

1. Review modified and untracked files and confirm they belong to the current architecture tranche.
2. Run the main test suite and record the count.
3. Build the solution and verify warnings and errors.
4. Run the local browser suite against a locally started Relaywright instance.
5. Review architecture and testing documentation against the implemented boundaries.
6. Record any local-only or remote-deployment validation that remains pending.
7. Leave the tranche ready for a separate version-control checkpoint; do not combine it with Phase 1 changes unless explicitly requested.

### Acceptance criteria

- Main test suite passes. Baseline at plan creation: 258 passed, 0 failed, 0 skipped.
- Local browser suite passes, or the exact blocker is recorded.
- Documentation describes the current service boundaries accurately.
- No generated `bin`, `obj`, runtime `App_Data`, or `.tmp-run.*` content is included as source work.

## Phase 1 - Submission Policy Pilot

### Purpose

Prove the responsibility-driven approach in a security-sensitive but well-bounded feature before applying it across the application.

### Current pressure point

`TrustedDevicePolicyService` combines:

- EF Core policy loading and persistence;
- normalization and validation;
- sender address and recipient domain matching;
- message-size and recipient-count limit decisions;
- logging and operational-event side effects.

### Target ownership

#### `TrustedDevicePolicyService`

Remain the existing public application facade. Own policy loading/saving, persistence mapping, structured logging, and the configuration operational event. Delegate pure policy work.

#### `SubmissionPolicyEvaluator`

Own pure acceptance and recipient decisions:

- trusted-network and global-policy precedence;
- blocked-list and allowed-list precedence;
- sender address and recipient domain matching;
- effective message-size and recipient-count limits;
- deterministic denial messages.

It must not reference EF Core, logging, filesystem, networking, or operational events.

#### `SubmissionPolicyValidator`

Own validation and normalization of persisted policy values:

- sender address patterns;
- recipient domain patterns;
- optional positive size and recipient limits;
- whitespace normalization and canonical list storage.

### Characterization tests

- Profile block list denies before profile/global allow lists.
- Global block list applies only when global policy is enabled.
- Empty allow lists do not deny.
- Non-empty allow lists require a match.
- Exact mailbox, domain-wide mailbox, exact domain, and subdomain patterns retain current semantics.
- The smaller positive profile/global limit wins.
- Disabled global policy does not contribute limits or lists.
- A recipient without a domain is denied.
- Invalid patterns and non-positive configured limits are rejected.
- Saving retains its operational event and does not log policy-list contents.

### Acceptance criteria

- Existing `ITrustedDevicePolicyService` consumers require no behavior changes.
- Pure evaluator and validator tests cover the decision matrix directly.
- Existing trusted-network and SMTP submission integration tests still pass.
- An architecture guard verifies the evaluator has no EF Core dependency.

Completed August 1, 2026. `TrustedDevicePolicyService` remains the public facade, `SubmissionPolicyEvaluator` owns pure sender/recipient decisions, and `SubmissionPolicyValidator` owns validation and normalization shared by global and per-device policy persistence.

## Phase 2A - Configuration Snapshot Capture And Restore

### Purpose

Make configuration-history failures traceable to capture, payload mapping, or category-specific restoration instead of one multi-purpose service.

### Target ownership

- Keep `ConfigurationSnapshotService` as history orchestration and persistence.
- Extract payload creation/serialization from restore execution.
- Introduce category-specific restoration handlers only where they own a complete configuration boundary; do not create one-method handlers merely to remove a switch.
- Preserve relay settings as their protected persisted representation; do not serialize an edit model containing unprotected secrets. Keep SMTP restart notification and queue wake-up behavior in the restoration boundary.
- Use established feature owners where their contracts preserve snapshot semantics. Do not add per-row operational events or validation side effects to a behavior-preserving rollback.

### Tests

- Capture and rollback for every supported snapshot category.
- Invalid and unsupported payload rejection.
- Runtime restart notification behavior for listener-affecting restoration.
- Operational history records the rollback without exposing protected values.
- SQLite-backed ordering remains correct for `DateTimeOffset` values.

### Acceptance criteria

- Snapshot persistence, payload mapping, and restoration have distinct owners.
- Adding a supported snapshot category has one documented extension path.
- Every category has direct rollback coverage.

Completed August 1, 2026. `ConfigurationSnapshotService` owns history orchestration and audit records, `ConfigurationSnapshotPayloadFactory` owns category capture, `ConfigurationSnapshotSerializer` owns JSON conversion, and `ConfigurationSnapshotRestorer` owns category restoration side effects. The stored relay entity remains the snapshot format so protected secret values are never converted into a plaintext edit model.

## Phase 2B - Alert Evaluation And State Transitions

### Purpose

Separate alert rule administration, observations, and notification state transitions so an alert problem can be diagnosed without reading one service containing every concern.

### Target ownership

- Rule query/save owner for persisted alert configuration.
- Pure or narrowly I/O-bound evaluator owner for rule observations.
- State-transition coordinator for activation, cooldown, recovery, result persistence, events, and email notification.
- Keep `AlertService` as the application facade if that preserves the existing interface cleanly.

### Tests

- Direct evaluation coverage for queue depth, oldest active message, failed/expired count, listener state, disk free space, certificate expiry, and recent upstream failures.
- Unknown rule behavior.
- Inactive-to-active, active-to-active, active-to-recovered, and cooldown behavior.
- Notification success and failure result persistence.
- Disabled rules do not evaluate or notify.
- Time-dependent behavior uses `TimeProvider`.

### Acceptance criteria

- Adding an alert evaluator does not require editing persistence and notification logic.
- State transitions are deterministic and directly tested.
- No live network dependency is added to tests.

Completed August 1, 2026. `AlertRuleRepository` owns rule queries, result queries, validation, and saves; `AlertEvaluator` owns alert observations; `AlertStateCoordinator` owns activation, recovery, cooldown, notification, event, and result transitions; `AlertService` remains the evaluation facade.

## Phase 2C - Backup Restore Validation And Orchestration

### Purpose

Make restore staging, archive safety, and restored-data validation independently understandable while preserving conservative recovery behavior.

### Target ownership

- `BackupRestoreService` remains the restore-stage orchestrator.
- Archive shape, allowed-entry, traversal, and extraction validation move to an archive component.
- Restored database and trusted-network validation move to a restored-data validator.
- Filesystem mutation stays behind the existing backup restore filesystem boundary.
- Credential sanitization continues through its dedicated owner.

### Tests

- Unsupported and duplicate archive entries.
- Absolute paths, parent traversal, alternate separators, and root escape attempts.
- Required archive content.
- Encrypted and unencrypted restore staging.
- Invalid database and missing table handling.
- Overlapping and invalid trusted-network CIDRs.
- Cleanup behavior after partial failures.
- Legacy backups continue to have credentials stripped.

### Acceptance criteria

- Path safety can be tested without applying a restore.
- Database validation can be tested without archive extraction.
- Restore staging remains one readable workflow with explicit cleanup paths.

Completed August 1, 2026. `BackupRestoreArchiveExtractor` owns manifest, archive-shape, allow-list, traversal, expansion-limit, and extraction checks; `RestoredBackupValidator` owns required-database and trusted-network validation; `BackupRestoreService` retains upload, decryption, sanitization, staging, and cleanup orchestration.

## Phase 2D - Admin HTTPS Certificate Handling

### Purpose

Separate certificate material operations from persisted listener configuration and protected-password handling.

### Target ownership

- Certificate loader/parser for PFX and PEM material.
- Self-signed certificate generator and DNS-name validation.
- Material file store for atomic copy/move/delete behavior.
- Configuration service for persisted paths, protected password, metadata, events, and runtime notification.
- Keep secret protection through Data Protection and avoid exposing passwords through returned summaries.

### Tests

- PFX and PEM loading with and without passwords.
- Unsupported extensions and missing private keys.
- DNS/SAN parsing and invalid self-signed names.
- Protected password persistence.
- Failed upload/generation cleanup.
- Existing configuration remains usable when replacement fails.
- Listener restart notification occurs only after successful configuration change.

### Acceptance criteria

- Certificate parsing/generation is testable without EF Core.
- Configuration persistence does not implement cryptographic parsing details.
- Certificate passwords remain protected at rest and absent from logs/events.

Completed August 1, 2026. `AdminHttpsCertificateService` remains the validation and protected-password workflow facade; `AdminHttpsCertificateMaterialService` owns PFX/PEM parsing and self-signed generation; `AdminHttpsCertificateConfigurationStore` owns JSON persistence; and `AdminHttpsCertificateFileStore` owns certificate file mutation.

## Phase 2E - Diagnostic Test Email Workflow

### Purpose

Make diagnostic-stage failures directly testable without requiring a live SMTP relay.

### Current pressure point

`UpstreamTestEmailSender.SendAsync` contains configuration checks, SMTP connection/authentication, message composition, diagnostic stage persistence, operational events, logging, success handling, and failure classification in one large method. It currently lacks a direct test class.

### Target ownership

- Introduce a narrow SMTP diagnostic-session boundary covering connect, authenticate, send, and disconnect operations.
- Keep the high-level stage sequence visible in `UpstreamTestEmailSender`.
- Extract repeated diagnostic stage completion/failure recording only if the result remains easier to read.
- Reuse existing authentication behavior through `IUpstreamAuthenticationService`.
- Keep exception classification aligned with production upstream delivery diagnostics.

### Tests

- Missing upstream host.
- Connect/TLS failure.
- Authentication success, skip, and failure.
- Message composition failure for invalid addresses.
- Submission success and failure.
- Cancellation behavior.
- Diagnostic stages finish in order with correct terminal state.
- Operational events and structured logs exclude the body and credentials.

### Acceptance criteria

- The full workflow is testable with a fake SMTP diagnostic session.
- The orchestration still reads as an ordered diagnostic sequence.
- Tests require no live SMTP relay or OAuth endpoint.

Completed August 1, 2026. `UpstreamTestEmailSender` retains the ordered diagnostic stage workflow and now uses `IUpstreamDiagnosticSmtpSessionFactory`; the production session reuses `IUpstreamAuthenticationService`, while direct tests provide deterministic connect, authentication, send, disconnect, failure, and cancellation behavior.

## Phase 3 - Discoverability And File Organization

### Purpose

Improve search precision after behavioral boundaries are established.

### Backend organization

- Split `AdminSecuritySummaries.cs` so each independent summary or checklist concept has a matching filename.
- Keep related small value types together when separating them would obscure their relationship.
- Review large test files and split them by production behavior, not test count or line count.
- Keep primary production and test type names aligned for searchability.

### Frontend organization

- Split `wwwroot/css/components/application.css` into named component assets such as forms, tables, statuses, toolbars/tabs, and readiness UI.
- Keep `site.css` as the import manifest and preserve import order.
- Extend frontend architecture tests to compare or enforce the intended asset organization without imposing arbitrary file-size limits.
- Extract Razor partials only for reusable or independently understandable UI; do not fragment a cohesive page solely because its markup is long.

### Acceptance criteria

- Searching a domain term leads to one obvious production owner and focused tests.
- CSS ownership can be inferred from filenames.
- Equivalent CSS behavior is verified after splitting and browser tests pass.

Completed August 1, 2026. Independent admin-security summaries and legacy SQLite schema steps now have matching filenames. The former application stylesheet is split, in unchanged rule order, into `surfaces.css`, `forms.css`, `data.css`, `controls.css`, and `status.css`; the layout loads every asset with versioning.

## Phase 4 - Feature Ownership Map And Architecture Guardrails

### Purpose

Give developers and AI agents a durable route from a feature or symptom to its owners, tests, and invariants.

### Feature ownership map

Add a table to the architecture documentation with these columns:

| Feature | Entry point | Decision owner | Persistence/I/O owner | Principal tests | Safety invariant |
| --- | --- | --- | --- | --- | --- |

Cover at least SMTP intake, trusted networks, submission policy, spool persistence, queue claims, delivery transitions, maintenance, relay configuration, configuration history, diagnostics, alerts, backups/restore, admin HTTPS, account setup, runtime status, and updates.

### Guardrails

- SMTP services do not construct trusted-network or CIDR implementations.
- Razor pages do not mutate queue sets directly.
- Queue maintenance and operator commands do not return to the live enqueue/delivery contract.
- Pure policy evaluation has no EF Core dependency.
- Certificate material logic has no database dependency.
- Snapshot restoration uses established configuration owners.
- Application startup remains composition rather than feature implementation.

### Acceptance criteria

- `docs/ARCHITECTURE.md` provides a usable feature-to-code route.
- `docs/DEVELOPMENT_GUIDELINES.md` describes the new extension points.
- Architecture tests protect important dependency directions without testing incidental filenames or line counts.

Completed August 1, 2026. The architecture feature ownership map routes symptoms to entry points, decision owners, I/O owners, tests, and invariants. Architecture tests protect the certificate, diagnostic SMTP, policy, snapshot, alert, restore, SMTP trust, queue/page, and startup composition directions.

## Final Validation

After all phases:

1. Run the complete deterministic test suite.
2. Build the solution in the repository's supported configuration.
3. Start Relaywright locally and run the browser suite.
4. Review the final diff for unrelated or generated content.
5. Verify the documentation map against actual registrations and call sites.
6. Record Windows and Linux deployment-workflow execution as pending unless those remote lanes are actually run.
7. Summarize completed phases, deviations, test counts, and remaining external validation explicitly.

Completed locally August 1, 2026. The deterministic suite passed 319 tests with no failures or skips, the solution build passed with no warnings or errors, and the rendered Edge suite passed all 4 tests. A separate live browser inspection confirmed that the five responsibility CSS assets loaded with versioned URLs and parsed rules, with no console warnings or errors. The Windows and Linux deployment workflows were not run because this working-tree tranche has not been committed or pushed.
