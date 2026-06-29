# Security Policy

Relaywright handles SMTP traffic, message spool files, admin credentials, OAuth client secrets, certificate passwords, and protected configuration. Treat Relaywright hosts as infrastructure systems.

## Supported Versions

Security fixes are provided for the latest published stable release. Pre-release builds are intended for early deployments and should be upgraded promptly.

## Reporting A Vulnerability

Report vulnerabilities privately to `security@relaywright.com`. Include:

- Affected Relaywright version.
- Deployment platform and install method.
- Clear reproduction steps.
- Whether message contents, passwords, OAuth secrets, Data Protection keys, or backup files may have been exposed.

Please do not open public issues for suspected vulnerabilities until a fix or mitigation is available.

## Operator Guidance

- Restrict admin UI ports to trusted networks.
- Restrict SMTP listener ports to devices that need relay access.
- Keep `App_Data`, Data Protection keys, SQLite database files, spool files, and backups out of source control.
- Use HTTPS for the admin UI and replace self-signed installer certificates with trusted certificates for shared deployments.
- Keep backups encrypted when moving them off the host.
