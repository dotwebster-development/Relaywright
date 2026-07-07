# Configure Upstream SMTP

Relaywright relays accepted mail to one configured upstream smart host.

Open:

```text
Settings -> Relay Settings
```

## Listener Settings

Configure how devices connect to Relaywright:

- bind to all interfaces, or choose a specific local IP;
- SMTP listener TCP port;
- listener host name;
- maximum accepted message size;
- optional STARTTLS and certificate path.

STARTTLS certificate paths must point at certificate files supported by the app. If STARTTLS is enabled, configure a certificate path.

## Upstream Settings

Configure the remote smart host:

- upstream host name or IP address;
- upstream TCP port;
- TLS mode;
- timeout in seconds;
- authentication mode.

Host fields should be hostnames or IP literals, not URLs or paths.

## Authentication Modes

Relaywright supports:

- no upstream authentication;
- basic username/password authentication;
- Microsoft 365 OAuth client credentials.

For Microsoft 365 OAuth, configure:

- mailbox address;
- tenant ID or tenant domain;
- client ID;
- client secret.

The selected mailbox and Microsoft Entra app must have the required Exchange SMTP OAuth permissions outside Relaywright.

## Delivery And Retention

Relay settings also control:

- delivery concurrency;
- maximum retry count;
- initial and maximum retry delays;
- message expiration;
- delivered, failed, and operational event retention.

SMTP listener changes restart the listener. Delivery changes wake the delivery worker so eligible queued work can move promptly.

## Validation

After saving:

1. Run [[Diagnostics|Diagnostics]].
2. Send a test email through the configured upstream.
3. Confirm accepted messages appear in [[Queue Operations|Queue-Operations]] and later move to `Delivered` or a retry state.
