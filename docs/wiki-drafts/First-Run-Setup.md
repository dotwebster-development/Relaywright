# First Run Setup

Relaywright shows the first-run setup page when no administrator account exists and no bootstrap admin was configured during install.

## Create The First Admin

Open the admin UI, usually:

```text
https://localhost:5443
```

Create the first administrator account. This account is required before the web interface, relay settings, and diagnostics can be used.

Use a strong password and store it in your normal infrastructure credential system.

## HTTPS Certificate Setup

During first-run setup, Relaywright can configure a managed HTTPS certificate for the admin web interface.

Options:

- generate a self-signed certificate for lab and test machines;
- upload a `.pfx` or `.p12` certificate bundle;
- upload PEM certificate and private key files;
- skip certificate setup and keep the current deployment certificate.

Certificate changes are picked up after the service restarts.

## Next Steps

1. Review [[Admin HTTPS And Certificates|Admin-HTTPS-And-Certificates]].
2. Configure [[Upstream SMTP|Configure-Upstream-SMTP]].
3. Add [[Trusted Networks|Trusted-Networks]].
4. Set a global [[Submission Policy|Submission-Policy]].
5. Run [[Diagnostics|Diagnostics]] before pointing devices at the relay.
