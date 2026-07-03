# Trusted Networks

Trusted networks control which devices, printers, applications, or local systems can submit SMTP to Relaywright.

Open:

```text
Settings -> Trusted IPs
```

## Trust Model

Relaywright trusts SMTP clients by source IP or CIDR, not by SMTP AUTH.

A trusted network entry can be:

- a single IP address;
- an IPv4 or IPv6 CIDR range.

Examples:

```text
192.168.1.25
192.168.1.0/24
10.10.20.15/32
```

Keep trusted ranges narrow. Avoid trusting whole office networks if only a few devices need relay access.

## Device Profile Fields

Each trusted network can include:

- description;
- owner;
- location;
- enabled or disabled state;
- maximum message size;
- maximum recipients per message;
- hourly rate limit;
- allowed and blocked senders;
- allowed and blocked recipient domains.

Owner and location help operators identify a device before disabling, tightening, or deleting it.

## Sender Rules

Sender entries can be:

- exact email address, such as `scanner@example.com`;
- domain pattern, such as `@example.com`;
- wildcard domain pattern, such as `*@example.com`.

Blocked senders take precedence over allowed senders.

## Recipient Domain Rules

Recipient-domain entries can be:

- `example.com`;
- `*.example.com`.

Recipient rules match domains only. Blocked domains take precedence over allowed domains.

## Practical Setup

1. Add one device or small CIDR at a time.
2. Record owner and location.
3. Start with tighter sender and recipient-domain rules.
4. Use [[Diagnostics|Diagnostics]] Flow Checker before letting the device submit real mail.
5. Watch [[Queue Operations|Queue-Operations]] and Logs after enabling the device.

To temporarily stop a device, disable the trusted network entry instead of deleting it.
