# Admin HTTPS And Certificates

Relaywright's admin UI is HTTPS-first. Production installs should keep HTTP disabled unless there is a deliberate local-only reason to enable it.

## Web Interface Listener

Open:

```text
System -> Web Interface
```

Configure:

- HTTPS listener port;
- optional HTTP listener;
- listener URLs and restart-required state.

Admin web listener changes require an application restart. Relaywright records restart-required state when a restart is needed.

## Certificate Settings

Open:

```text
System -> Certificate
```

Options:

- generate a self-signed certificate;
- upload a `.pfx` or `.p12` bundle;
- upload PEM certificate and private key files.

Use self-signed certificates for labs and private test machines. Use a trusted certificate for shared deployments.

## Certificate Passwords

Certificate passwords are sensitive. Store them in your normal infrastructure credential store. Do not put certificate passwords into screenshots, Wiki pages, logs, tickets, or shell history.

## After Certificate Changes

1. Save the certificate configuration.
2. Restart Relaywright if prompted.
3. Open the admin UI over HTTPS.
4. Confirm the browser shows the expected certificate.
5. Update monitoring or reverse proxy configuration if needed.
