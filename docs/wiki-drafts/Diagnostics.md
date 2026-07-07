# Diagnostics

Diagnostics help prove configuration and policy before or during real message flow.

Open:

```text
Diagnostics
```

## Upstream Connectivity

Use connectivity checks to test whether the Relaywright host can reach the upstream smart host with the selected host, port, TLS mode, timeout, and authentication settings.

Use this after:

- initial upstream setup;
- password or OAuth secret changes;
- firewall or DNS changes;
- upstream provider outages.

## Flow Checker

The Flow Checker previews whether a trusted device submission would pass:

- source IP or CIDR trust;
- sender policy;
- size policy;
- recipient-domain policy;
- recipient count policy;
- hourly rate-limit policy.

The Flow Checker does not consume device rate-limit quota.

## Test Email

Use Test Email to send a controlled message through the configured upstream path.

Keep test bodies free of secrets and customer data. Diagnostic records are designed not to persist raw SMTP transcripts, secrets, or message bodies.

## Reading Results

Use diagnostics together with:

- [[Queue Operations|Queue-Operations]] for message state;
- Logs for operational events;
- [[Configure Upstream SMTP|Configure-Upstream-SMTP]] for relay settings;
- [[Trusted Networks|Trusted-Networks]] and [[Submission Policy|Submission-Policy]] for acceptance rules.
