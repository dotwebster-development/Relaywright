# Release Process

Relaywright uses a standard stable/default branch flow.

## Branch Flow

- `main` is the stable default branch and should stay protected.
- `development` is where normal feature and bug-fix work lands.
- Create short feature branches from `development`, then merge by PR back into `development`. See [Branch workflow](BRANCH_WORKFLOW.md) for the protected-branch PR flow and recovery steps after rejected direct pushes.
- Create release branches from `development`, for example `release/1.0.0`.
- Tag release candidates from the release branch, for example `v1.0.0-rc.6`.
- Run release validation against the RC artifacts, not local build output.
- When validation passes, merge `release/1.0.0` into `main`, tag `v1.0.0` from `main`, and merge `main` back into `development`.
- For urgent production fixes after 1.0, create `hotfix/x.y.z` from `main`, tag the hotfix from `main`, then merge `main` back into `development`.

## Release Gates

Do not tag a stable release until all required gates pass:

- Windows clean install validation.
- Windows update validation.
- Linux clean install validation.
- Linux update validation.
- At least one deterministic SMTP trusted/untrusted smoke test against a local capture relay.
- Local Debug and Release test suites.
- Package vulnerability gates.

The preferred final automation path is `full-release` on both the Windows and Linux release validation workflows, plus the SMTP smoke.

## SMTP Smoke Policy

Use a local or capture SMTP server as the primary automated SMTP test target. The smoke should prove accepted, denied, queued, and delivered-or-retried behavior without trusting broad public networks.

`relaywright.com` or a subdomain can be useful as an optional public/manual smoke target for DNS, certificate, browser reachability, controlled SMTP submission from a known trusted source, and no-open-relay behavior. It is not the primary release gate.

## Release Record

Every release should record:

- commit and tag tested,
- artifact version tested,
- VM names used,
- workflow run URLs,
- deviations from the standard process,
- deterministic SMTP smoke result,
- optional public/manual smoke result.
