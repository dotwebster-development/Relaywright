# Relaywright Operator Manual

Relaywright is a self-hosted SMTP relay gateway for trusted devices, apps, and internal systems that need a controlled path to one upstream smart host.

Use this Wiki for day-to-day operation:

- install Relaywright on Windows or Linux;
- create the first administrator account;
- configure trusted devices and submission policy;
- connect Relaywright to an upstream SMTP provider;
- watch the queue, logs, diagnostics, alerts, and backups;
- update, recover, and troubleshoot the service.

Relaywright is not a public MX, open relay, mailing-list manager, or spam filter. It is designed for controlled internal submission from known devices.

## Start Here

1. [[Install Relaywright|Install-Relaywright]]
2. [[First Run Setup|First-Run-Setup]]
3. [[Configure Upstream SMTP|Configure-Upstream-SMTP]]
4. [[Trusted Networks|Trusted-Networks]]
5. [[Submission Policy|Submission-Policy]]
6. [[Diagnostics|Diagnostics]]

## Important Safety Model

Relaywright only accepts SMTP from trusted IPs or CIDRs. Accepted message DATA is written to the spool and queue metadata before the SMTP client receives success. Rejected submissions are rejected before message content is spooled.

Keep admin HTTPS, runtime data, Data Protection keys, backup files, certificate passwords, OAuth secrets, and message spool files protected as infrastructure data.

## Current Stable Line

The current stable line is `1.0.x`. Use release candidates only for validation unless you intentionally choose a test build.

Downloads:

- https://github.com/dotwebster-development/Relaywright/releases/latest

Repository docs remain the source of truth for release validation, architecture, and security policy.
