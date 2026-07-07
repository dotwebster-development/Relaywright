# Troubleshooting

Use this page for first-pass checks before deeper investigation.

## Admin UI Does Not Open

Check:

- Relaywright service is running.
- HTTPS port is correct.
- Host firewall allows the selected admin port from your management network.
- Browser is using `https://`, not `http://`, unless HTTP was intentionally enabled.
- Certificate warning is expected if using a self-signed certificate.

## Device Cannot Submit Mail

Check:

- device source IP is covered by an enabled [[Trusted Network|Trusted-Networks]];
- SMTP listener port is reachable from the device;
- sender address is allowed by global or device policy;
- recipient domain is allowed and not blocked;
- message size and recipient count are under limits;
- rate limit has not been reached.

Use [[Diagnostics|Diagnostics]] Flow Checker with the device IP, sender, recipients, and size.

## Messages Stay Pending Or RetryScheduled

Check:

- upstream host and port;
- TLS mode;
- upstream authentication;
- DNS from the Relaywright host;
- outbound firewall rules;
- upstream provider rate limits or outages.

Run upstream connectivity diagnostics and inspect message delivery attempts.

## Backups Are Not Ready

Check:

- backup directory is writable;
- data volume has enough space;
- previous backup validation result;
- encryption password if validating an encrypted backup;
- whether the install uses SQLite or a server database.

Built-in backup and restore is SQLite-only. SQL Server and MySQL require platform backup tooling.

## Alerts Are Not Sending

Check:

- alert recipients are valid;
- alert rule is enabled;
- cooldown has elapsed;
- upstream configuration works;
- the upstream provider accepts mail from the configured Relaywright account.

## What Not To Publish

Do not paste secrets, raw message bodies, OAuth client secrets, certificate passwords, protected blobs, or private SMTP transcripts into public issues, Wiki pages, screenshots, or shared logs.
