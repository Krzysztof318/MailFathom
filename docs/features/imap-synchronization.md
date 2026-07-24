# IMAP synchronization

MailMcp now includes the first vertical slice for read-only IMAP synchronization. The implemented slice is intentionally limited to periodic reconciliation so the persistence model, authenticated IMAP adapter seam, application ports, and safety invariants can be reviewed before adding long-lived IDLE or NOTIFY workers.

## Implemented behavior

- `Domain` models stable IMAP message occurrence identity as `(account, folder, UIDVALIDITY, UID)`.
- `Application` owns IMAP, metadata repository, content store, and checkpoint ports.
- `MailboxSynchronizer` opens folders through a read-only session port and requests bounded metadata batches. It retains at most one fetched MIME payload at a time: each seen-preserving remote fetch finishes before a short local session atomically upserts that occurrence's metadata, uses the returned local stored-email identifier for its content, and commits and disposes before the next remote fetch starts. After the inspected batch finishes, a separate short session advances the checkpoint only when the mailbox adapter reports a non-speculative UID cursor known safe from the opened folder state.
- Batches are bounded by message count, not by UID-space width. The adapter searches the whole remaining assigned UID range — a UID SEARCH returns identifiers only — and then fetches envelopes for at most `MaxMetadataBatchSize` messages. A folder whose UIDs are sparse after deletions therefore still advances a full batch per iteration instead of crawling the UID space, which keeps an initial backfill practical.
- A message that exceeds `MaxRawMimeBytes` is never silently dropped. Its occurrence metadata is committed with `ContentAvailability = ExceededSizeLimit` before the checkpoint moves past it, so the gap stays queryable and auditable instead of existing only as a counter in a log line. The same applies when the advertised size understated the payload and the bounded stream read rejects it mid-fetch.
- Committing occurrences before the window checkpoint means a process failure may cause a later run to fetch an already stored occurrence again. Content and metadata writes use the stable remote occurrence identity and are idempotent, so this retry does not create duplicate stored messages.
- `Infrastructure` maps the pre-migration PostgreSQL model to `mailbox_accounts`, `mail_folders`, `stored_emails`, `email_message_contents`, and separate `synchronization_checkpoints`. Each stored email has a local UUIDv7; its raw MIME row uses the same UUID as both primary key and foreign key and records byte length, SHA-256, and storage time. Each stored email also records a `ContentAvailability` value as text so a metadata-only occurrence is distinguishable from one whose raw MIME is present. Persistence sessions clear tracked state after cleanup so one scoped context does not retain MIME arrays between per-message transactions, and re-synchronizing an occurrence that is already stored overwrites its payload with a set-based update rather than reading the existing `bytea` back into the change tracker.
- The MailKit adapter resolves folders asynchronously, caps UID progress with the opened folder UIDNEXT value, normalizes message sent dates to UTC before persistence, and rejects occurrence identities that do not belong to the open account, folder, and UIDVALIDITY scope.
- Failed MailKit session setup attempts both disconnect and disposal without replacing the primary setup failure. Normal session disposal also attempts both operations and reports the first cleanup failure.
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
    "MaxMetadataBatchesPerRun": 10,
    "Accounts": []
  }
}
```

When enabled, at least one account with a non-blank `AccountId`, host, user name, and password must be configured. If an account omits `Folders`, the worker applies the post-binding default `INBOX`; explicit folder lists replace that default. Account secrets and concrete IMAP connection settings are intentionally not committed in ordinary configuration files.

Account identifiers and folder names must be unique after domain normalization, and IMAP ports must be between 1 and 65535. `UseSslOnConnect` defaults to `true` for implicit TLS; setting it to `false` selects mandatory STARTTLS, not clear-text transport.

## Safety assumptions

The application layer exposes only `FetchMessageContentWithoutSettingSeenAsync` for content retrieval during synchronization. This name is part of the contract: implementations must use IMAP read-only selection and BODY.PEEK-equivalent behavior so remote `\Seen` flags are not changed. The MailKit adapter satisfies both halves — it selects the folder with `FolderAccess.ReadOnly` and retrieves content through `GetStreamAsync(uid)`, which issues `UID FETCH <uid> (BODY.PEEK[])`. A regression test exercises a successful fetch and asserts that neither `StoreAsync`, the only `IMailFolder` member able to change flags, nor a read-write reselection was requested. Metadata requests are bounded by `MaxMetadataBatchSize`, each run is bounded by `MaxMetadataBatchesPerRun`, empty unassigned UID ranges are not checkpointed speculatively, and raw MIME above `MaxRawMimeBytes` is recorded as metadata-only. Logs record counts and account/folder identifiers only; raw MIME, message bodies, attachments, credentials, and tokens remain sensitive and must not be logged.

## Pending work

- Deployment-specific secret binding for IMAP passwords and reviewed operational examples for external secret stores.
- IMAP IDLE and NOTIFY support.
- Explicit EF Core migrations after schema review.
- Integration tests with PostgreSQL and a real IMAP server in the later integration-test phase, including EF mapping, PK/FK, integrity-metadata, and uniqueness-constraint verification required by ADR 001. Temporary provider-bound coverage exclusions carry adjacent TODOs for removal at that point.
- MCP read tools, RAG indexing, and SMTP outbox integration.
