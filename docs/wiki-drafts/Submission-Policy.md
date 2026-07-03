# Submission Policy

Submission policy sets global acceptance rules for trusted devices before SMTP DATA is accepted.

Open:

```text
Settings -> Submission Policy
```

## What It Controls

Global policy can define:

- maximum accepted message size;
- maximum recipients per message;
- allowed senders;
- blocked senders;
- allowed recipient domains;
- blocked recipient domains.

Device-specific rules on [[Trusted Networks|Trusted-Networks]] are combined with global rules. When both layers define numeric limits, the stricter limit wins.

## Sender Entries

Allowed and blocked sender entries support:

- exact addresses, such as `scanner@example.com`;
- domain entries, such as `@example.com`;
- wildcard domain entries, such as `*@example.com`.

If allowed senders is empty, any sender from a trusted device is allowed unless blocked by another rule.

Blocked senders take precedence over allowed senders.

## Recipient Domain Entries

Allowed and blocked recipient domains support:

- exact domains, such as `example.com`;
- wildcard domains, such as `*.partner.example`.

Blocked domains take precedence over allowed domains.

## Operating Guidance

- Start strict and expand only when a real device needs it.
- Prefer device-specific policy when one printer or app has unusual needs.
- Use global policy for organization-wide boundaries.
- Test policy decisions with [[Diagnostics|Diagnostics]] Flow Checker.

Rejected submissions happen before message content is spooled.
