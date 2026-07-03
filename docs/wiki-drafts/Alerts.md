# Alerts

Alerts monitor operational risks and can notify recipients through the configured upstream mail path.

Open:

```text
System -> Alerts
```

## Alert Areas

Relaywright includes built-in operational alert rules for conditions such as:

- queue depth;
- oldest queued message age;
- failed or expired messages;
- listener or worker health;
- disk space;
- certificate expiry;
- repeated upstream failures.

Rules have thresholds and cooldowns so operators can tune signal volume.

## Notification Recipients

Alert notifications send directly through the configured upstream relay path. They do not go through the queued message pipeline.

Use operational mailboxes or distribution groups that are monitored by the people responsible for the relay host.

## Operating Guidance

- Keep cooldowns long enough to avoid repeated noise during a known outage.
- Keep queue and age thresholds low enough to catch broken delivery quickly.
- Test upstream connectivity after changing alert recipients or relay credentials.
- Do not include secrets or private message content in alert names or notes.

Use Logs and [[Queue Operations|Queue-Operations]] to investigate alert causes.
