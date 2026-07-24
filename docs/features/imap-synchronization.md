# IMAP synchronization

MailMcp now includes the first vertical slice for read-only IMAP synchronization. The implemented slice is intentionally limited to periodic reconciliation so the persistence model, authenticated IMAP adapter seam, application ports, and safety invariants can be reviewed before adding long-lived IDLE or NOTIFY workers.

## Implemented behavior

- `Domain` models stable IMAP message occurrence identity as `(account, folder, UIDVALIDITY, UID)`.
- `Application` owns IMAP, metadata repository, content store, checkpoint, explicit synchronization unit-of-work, and synchronization settings reader ports.
- `MailboxSynchronizer` opens folders through a read-only session port, requests bounded metadata batches, skips messages above the configured raw MIME limit, fetches message content through a seen-preserving method, stores raw MIME before metadata, and advances checkpoints only after each bounded UID window is inspected. Content, metadata, and checkpoint writes for a bounded UID window participate in one explicit application-owned unit-of-work session so transaction participation remains visible without exposing EF Core to `Application`.
- `Infrastructure` provides EF Core PostgreSQL mappings for accounts, folders, message metadata, raw message content, and synchronization checkpoints.
- `Host` binds provider-shaped `MailSynchronization` options, rejects unknown keys during binding, maps them through `MailSynchronizationSettingsReader` into immutable application settings for new operations, and runs a periodic scoped background worker that isolates failures per account/folder work unit.

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

When enabled, at least one account must be configured. If an account omits `Folders`, the worker applies the post-binding default `INBOX`; explicit folder lists replace that default. Unknown `MailSynchronization` keys are rejected at binding time. Account secrets and concrete IMAP connection settings are intentionally not committed in ordinary configuration files.

## Reload and safety assumptions

The first synchronization configuration group is classified as reloadable for new operations only: each worker execution captures an immutable application settings snapshot before starting scoped account/folder work. Invalid mapped reload candidates are rejected by the host-side reader and the previous validated application snapshot remains active. Programmatic configuration mutation is not implemented.

## ADR alignment map

- ADR 001: `IMailSynchronizationUnitOfWorkFactory` and `IMailSynchronizationUnitOfWorkSession` are application-owned ports, while EF Core transactions remain inside the PostgreSQL adapter.
- ADR 002: `ISynchronizationSettingsReader` is the application-owned settings access point. Host-bound options stay in `Host`, where they are validated and mapped to immutable application settings snapshots.

## Safety assumptions

The application layer exposes only `FetchMessageContentWithoutSettingSeenAsync` for content retrieval during synchronization. This name is part of the contract: implementations must use IMAP read-only selection and BODY.PEEK-equivalent behavior so remote `\\Seen` flags are not changed. Metadata requests are bounded by `MaxMetadataBatchSize`, each run is bounded by `MaxUidWindowsPerRun`, and raw MIME fetches are skipped or rejected above `MaxRawMimeBytes`. Logs record counts and account/folder identifiers only; raw MIME, message bodies, attachments, credentials, and tokens remain sensitive and must not be logged.

## Pending work

- Deployment-specific secret binding for IMAP passwords and reviewed operational examples for external secret stores.
- IMAP IDLE and NOTIFY support.
- Explicit EF Core migrations after schema review.
- Integration tests with PostgreSQL and a real IMAP server in the later integration-test phase, including EF mapping, transaction, migration, and constraint verification required by ADR 001.
- MCP read tools, RAG indexing, and SMTP outbox integration.
