# Queue Operations

The queue shows accepted messages and their delivery state.

Open:

```text
Operations -> Queue
```

## Queue States

Relaywright uses these states:

- `Pending`
- `InProgress`
- `RetryScheduled`
- `Delivered`
- `Failed`
- `Expired`

Accepted SMTP DATA is written to the spool and queue metadata before the SMTP client receives success.

## Common Actions

Use the queue page to:

- search messages;
- filter by status;
- inspect recipients and delivery attempts;
- retry eligible messages;
- purge terminal messages when retention cleanup should not wait.

Bulk actions preserve queue-state guards. Messages are not removed from the spool before matching queue metadata is safely removed or terminal retention cleanup is complete.

## Pause And Resume Delivery

Outbound delivery can be paused from the runtime status area. Pausing delivery stops claiming new outbound work. SMTP intake can still accept and spool mail from trusted devices.

Resume delivery when the upstream provider, network, or configuration issue is fixed.

## What To Check When Messages Pile Up

1. Confirm the upstream host, port, TLS mode, and authentication in [[Configure Upstream SMTP|Configure-Upstream-SMTP]].
2. Run [[Diagnostics|Diagnostics]].
3. Check Logs for delivery classifications.
4. Confirm DNS and outbound firewall rules from the Relaywright host.
5. Verify the upstream provider is accepting the configured mailbox or credentials.
