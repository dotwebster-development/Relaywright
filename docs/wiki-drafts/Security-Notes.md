# Security Notes

Relaywright handles SMTP traffic, message spool files, admin credentials, OAuth client secrets, certificate passwords, protected configuration, and backup files. Treat Relaywright hosts as infrastructure systems.

## Operator Rules

- Restrict admin UI ports to trusted management networks.
- Restrict SMTP listener ports to devices that need relay access.
- Keep trusted IP/CIDR ranges narrow.
- Keep `App_Data`, Data Protection keys, SQLite database files, spool files, certificates, and backups out of source control.
- Use HTTPS for the admin UI.
- Replace self-signed certificates with trusted certificates for shared deployments.
- Encrypt backups before moving them off the host.
- Store passwords, OAuth secrets, and certificate passwords in an infrastructure credential system.

## Secret Handling

Do not put these values into Wiki pages, screenshots, tickets, or public logs:

- admin passwords;
- upstream SMTP passwords;
- OAuth client secrets or tokens;
- certificate passwords;
- Data Protection keys;
- protected secret blobs;
- raw message bodies;
- customer data;
- private SMTP transcripts.

## Reporting Vulnerabilities

Report suspected vulnerabilities privately to:

```text
security@relaywright.com
```

Include:

- affected Relaywright version;
- deployment platform and install method;
- clear reproduction steps;
- whether message contents, passwords, OAuth secrets, Data Protection keys, or backup files may have been exposed.

Do not open public issues for suspected vulnerabilities until a fix or mitigation is available.
