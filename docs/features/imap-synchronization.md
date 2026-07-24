# IMAP synchronization

MailMcp now includes the first vertical slice for read-only IMAP synchronization. The implemented slice is intentionally limited to periodic reconciliation so the persistence model, authenticated IMAP adapter seam, application ports, and safety invariants can be reviewed before adding long-lived IDLE or NOTIFY workers.

## Implemented behavior

- `Domain` models stable IMAP message occurrence identity as `(account, folder, UIDVALIDITY, UID)`.
- `Application` owns IMAP, metadata repository, content store, and checkpoint ports.
- `MailboxSynchronizer` opens folders through a read-only session port, requests bounded metadata batches, skips messages above the configured raw MIME limit, fetches message content through a seen-preserving method, and opens the local persistence session only after remote content reads complete so EF transactions do not span IMAP network I/O. It advances checkpoints only when the mailbox adapter reports a non-speculative UID cursor known safe from the opened folder state.
- `Infrastructure` provides EF Core PostgreSQL mappings for accounts, folders, message metadata, raw message content, and synchronization checkpoints. The MailKit adapter caps UID progress with the opened folder UIDNEXT value, normalizes message sent dates to UTC before persistence, and disposes IMAP clients when session setup fails before ownership is transferred to a returned session.
- `Host` provides typed `MailSynchronization` options, startup validation for enabled account connection settings, and a periodic scoped background worker that isolates failures per account/folder work unit.

## Configuration

Synchronization is disabled by default:

```json
{
  "MailSynchronization": {
    "Enabled": false,
    "Interval": "00:05:00",
    "MaxMetadataBatchSize": 100,
    "MaxRawMimeBytes": 26214400,
    "MaxUidWindowsPerRun": 10,
    "Accounts": []
  }
}
```

When enabled, at least one account with a non-blank `AccountId`, host, user name, and password must be configured. If an account omits `Folders`, the worker applies the post-binding default `INBOX`; explicit folder lists replace that default. Account secrets and concrete IMAP connection settings are intentionally not committed in ordinary configuration files.

## Safety assumptions

The application layer exposes only `FetchMessageContentWithoutSettingSeenAsync` for content retrieval during synchronization. This name is part of the contract: implementations must use IMAP read-only selection and BODY.PEEK-equivalent behavior so remote `\\Seen` flags are not changed. Metadata requests are bounded by `MaxMetadataBatchSize`, each run is bounded by `MaxUidWindowsPerRun`, empty unassigned UID ranges are not checkpointed speculatively, and raw MIME fetches are skipped or rejected above `MaxRawMimeBytes`. Logs record counts and account/folder identifiers only; raw MIME, message bodies, attachments, credentials, and tokens remain sensitive and must not be logged.

## Pending work

- Deployment-specific secret binding for IMAP passwords and reviewed operational examples for external secret stores.
- IMAP IDLE and NOTIFY support.
- Explicit EF Core migrations after schema review.
- Integration tests with PostgreSQL and a real IMAP server in the later integration-test phase, including EF mapping/constraint verification required by ADR 001.
- MCP read tools, RAG indexing, and SMTP outbox integration.
