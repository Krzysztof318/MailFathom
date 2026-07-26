# IMAP synchronization

MailMcp now includes the first vertical slice for read-only IMAP synchronization. The implemented slice is intentionally limited to periodic reconciliation so the persistence model, authenticated IMAP adapter seam, application ports, and safety invariants can be reviewed before adding long-lived IDLE or NOTIFY workers.

## Implemented behavior

- `Domain` models stable IMAP email occurrence identity as `EmailOccurrenceId`, keyed by `(account, folder, UIDVALIDITY, UID)`. `Email` is the repository-wide term for the mail artifact; `Message` is reserved so it stays unambiguous once AI conversation types exist.
- `Application` owns IMAP, metadata repository, content store, and checkpoint ports, plus the `IPersistenceSession` write-transaction port in `MailMcp.Application.Persistence`. The persistence session is named separately from `IMailboxSession` because both would otherwise be "the session" at a call site.
- `MailboxSynchronizer` opens folders through a read-only session port and requests bounded metadata batches. It retains at most one fetched MIME payload at a time: each seen-preserving remote fetch finishes before a short local session atomically upserts that occurrence's metadata, uses the returned local stored-email identifier for its content, and commits and disposes before the next remote fetch starts. After the inspected batch finishes, a separate short session advances the checkpoint only when the mailbox adapter reports a non-speculative UID cursor known safe from the opened folder state.
- Batches are bounded by email count, not by UID-space width. The adapter searches the whole remaining assigned UID range — a UID SEARCH returns identifiers only — and then fetches envelopes for at most `MaxMetadataBatchSize` emails. A folder whose UIDs are sparse after deletions therefore still advances a full batch per iteration instead of crawling the UID space, which keeps an initial backfill practical.
- An email that exceeds `MaxRawMimeBytes` is never silently dropped. Its occurrence metadata is committed with `ContentAvailability = ExceededSizeLimit` before the checkpoint moves past it, so the gap stays queryable and auditable instead of existing only as a counter in a log line. The same applies when the advertised size understated the payload and the bounded stream read rejects it mid-fetch.
- Committing occurrences before the window checkpoint means a process failure may cause a later run to fetch an already stored occurrence again. Content and metadata writes use the stable remote occurrence identity and are idempotent, so this retry does not create duplicate stored emails.
- `Infrastructure` maps the pre-migration PostgreSQL model to `mailbox_accounts`, `mail_folders`, `stored_emails`, `email_message_contents`, and separate `synchronization_checkpoints`. Each stored email has a local UUIDv7; its raw MIME row uses the same UUID as both primary key and foreign key and records byte length, SHA-256, and storage time. Each stored email also records a `ContentAvailability` value as text so a metadata-only occurrence is distinguishable from one whose raw MIME is present. Persistence sessions clear tracked state after cleanup so one scoped context does not retain MIME arrays between per-email transactions, and re-synchronizing an occurrence that is already stored overwrites its payload with a set-based update rather than reading the existing `bytea` back into the change tracker.
- A write repository takes its EF Core context from the `IPersistenceSession` it is handed, and injects none of its own. The write is therefore always issued on that session's own context, whichever scope the session came from, so "this write joined the caller's transaction" is structurally true instead of being an effect of both objects happening to resolve from the same DI scope. A session backed by a different persistence provider cannot supply a context at all and is rejected outright. Read methods take no session and use the scoped context, because a read joins no transaction.
- Lookups that must see an insert still pending in the open session use the change tracker before the database, since EF Core never flushes pending changes before a query. Primary-key lookups rely on `FindAsync`, which already does this; alternate-key lookups go through one shared two-pass helper driven by a single predicate expression. The one hand-written exception is the raw MIME row, where materializing the existing `bytea` is precisely the cost being avoided.
- Mutable tracked email metadata and synchronization checkpoints carry an infrastructure-only `ConcurrencyVersion`. It is a `uint` row version, which is how Npgsql maps a property onto the PostgreSQL `xmin` system column, so the token is server-generated and no concurrency column exists in either table. A stale tracked update is translated from `DbUpdateConcurrencyException` into an application-owned commit result at the session boundary, which is the only place a conflict is an ordinary branch: its consumer is the retry policy's loop. Synchronization retries a complete idempotent metadata/content write in a fresh persistence session, never repeats the preceding IMAP fetch, and uses cancellation-aware exponential backoff with jitter between bounded attempts. Checkpoint writes are attempted once and only when their durable UIDVALIDITY and last-seen UID still equal the progress read at the start of the run; timestamp precision differences are ignored, while the later synchronization timestamp is retained. `xmin` detects a later race before commit, and a concurrent first-checkpoint primary-key collision is treated narrowly as the same conflict.
- Once bounded attempts are spent, or a checkpoint moved under the run, the conflict leaves `SynchronizeAsync` as `PersistenceConcurrencyConflictException` instead of being restated as a result value by each layer it passes. Progress the run already committed stays durable. The worker catches it per folder, logs a deferral with the reason, and continues with the remaining folders; the next interval rereads the last committed checkpoint. The attempt bound is one deployment-wide setting, not a synchronization option, because writers compete for shared rows rather than for anything a single service owns.
- The MailKit adapter resolves folders asynchronously, caps UID progress with the opened folder UIDNEXT value, normalizes email sent dates to UTC before persistence, and rejects occurrence identities that do not belong to the open account, folder, and UIDVALIDITY scope.
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

Optimistic concurrency is configured once for the whole deployment, outside the synchronization section, because it bounds every local writer rather than this feature:

```json
{
  "Persistence": {
    "MaximumConcurrencyCommitAttempts": 2
  }
}
```

When enabled, at least one account with a non-blank `AccountId`, host, user name, and password must be configured. If an account omits `Folders`, the worker applies the post-binding default `INBOX`; explicit folder lists replace that default. Account secrets and concrete IMAP connection settings are intentionally not committed in ordinary configuration files.

Account identifiers and folder names must be unique after domain normalization, IMAP ports must be between 1 and 65535, and `MaximumConcurrencyCommitAttempts` must be between 1 and 10. The default of two attempts covers the single lost race that a rare conflict represents; a folder deferred after that is retried by the next interval anyway. `UseSslOnConnect` defaults to `true` for implicit TLS; setting it to `false` selects mandatory STARTTLS, not clear-text transport.

## Safety assumptions

The application layer exposes only `FetchEmailContentWithoutSettingSeenAsync` for content retrieval during synchronization. This name is part of the contract: implementations must use IMAP read-only selection and BODY.PEEK-equivalent behavior so remote `\Seen` flags are not changed. The MailKit adapter satisfies both halves — it selects the folder with `FolderAccess.ReadOnly` and retrieves content through `GetStreamAsync(uid)`, which issues `UID FETCH <uid> (BODY.PEEK[])`. A regression test exercises a successful fetch and asserts that neither `StoreAsync`, the only `IMailFolder` member able to change flags, nor a read-write reselection was requested. Metadata requests are bounded by `MaxMetadataBatchSize`, each run is bounded by `MaxMetadataBatchesPerRun`, empty unassigned UID ranges are not checkpointed speculatively, and raw MIME above `MaxRawMimeBytes` is recorded as metadata-only. Logs record counts and account/folder identifiers only; raw MIME, email bodies, attachments, credentials, and tokens remain sensitive and must not be logged.

## Pending work

- Deployment-specific secret binding for IMAP passwords and reviewed operational examples for external secret stores.
- IMAP IDLE and NOTIFY support.
- Explicit EF Core migrations after schema review.
- Integration tests with PostgreSQL and a real IMAP server in the later integration-test phase, including EF mapping, `xmin` conflict detection across transactions, same-transaction token semantics, PK/FK, integrity-metadata, and uniqueness-constraint verification required by ADR 001. Temporary provider-bound coverage exclusions carry adjacent TODOs for removal at that point.
- MCP read tools, RAG indexing, and SMTP outbox integration.
