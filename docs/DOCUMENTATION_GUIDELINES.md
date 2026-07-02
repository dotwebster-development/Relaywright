# Documentation Guidelines

Relaywright keeps release-sensitive documentation in the repository and uses the GitHub Wiki for practical operator guidance.

## Source Of Truth

- Repository docs are the source of truth for release-specific behavior, install and upgrade commands, validation evidence, architecture, security policy, and troubleshooting that must be reviewed with code.
- The GitHub Wiki is the operator manual: task-focused pages for installing, configuring, operating, updating, backing up, restoring, diagnosing, and recovering Relaywright.
- The public website is the short public overview: what Relaywright is, what it supports, where to download it, and where operators can find deeper docs.

## Docs Impact Check

Every change touching these areas must include a docs impact check before merge:

- installation, update, uninstall, service hosting, firewall, or release artifacts;
- admin UI labels, workflows, settings, navigation, screenshots, or screenshots used by the website;
- trusted networks, submission policy, queue behavior, retry/expiry, delivery, diagnostics, alerts, backups, restore, account security, certificates, or version checks;
- security posture, secret handling, public URLs, supported platforms, or release validation.

If behavior changed, update the relevant repo docs first. If the behavior is operator-facing, also update or draft the matching Wiki page.

## Wiki Sync Rules

- Keep Wiki pages practical and version-aware. Avoid claims that are broader than the current stable release.
- Check Wiki install commands, screenshots, feature names, URLs, supported platforms, and troubleshooting steps before each stable release.
- Do not put secrets, production hostnames, customer data, raw message bodies, SMTP transcripts, tokens, protected blobs, or private infrastructure details in Wiki pages or screenshots.
- When a Wiki page needs substantial changes, draft the source text in the repository first under a tracked docs change or release PR so it can be reviewed.
- If a Wiki page intentionally differs from repo docs because it is simplified for operators, keep the repo docs authoritative and link from the Wiki to the source document.

## Release Checklist

Before tagging a stable release:

- review the website against the release branch;
- review the Wiki against the release branch;
- confirm screenshots match the current admin UI;
- confirm install/update commands point at the intended release repository;
- record any documentation deviations in the release record.
