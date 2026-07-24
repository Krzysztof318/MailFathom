# IMAP Synchronization Review Hardening Design

## Goal

Keep the complete IMAP synchronization and PostgreSQL persistence scope of pull request 13 while correcting the merge-blocking resource, memory, asynchronous I/O, ownership-validation, and coverage-documentation issues found during review.

## Approved scope

- Keep the PostgreSQL repositories, EF Core model, Unit of Work, hosted worker, and existing application ports.
- Align the pre-migration EF Core model with the repository architecture draft instead of preserving the provisional table layout.
- Keep temporary coverage exclusions for provider-bound persistence code that cannot be exercised by the current unit-test-only policy.
- State on each temporary exclusion that PostgreSQL integration tests will cover the behavior later, and place a concrete TODO beside the exclusion so removal remains visible.
- Do not add an integration-test project or a fake EF Core provider in this change.

## Persistence model

The pre-migration schema uses the architecture draft's core table names and ownership:

- `mailbox_accounts` owns `mail_folders`;
- `stored_emails` gives each remote occurrence a local UUIDv7 and enforces uniqueness on `(mail_folder_id, uid_validity, uid)`;
- `email_message_contents` uses the stored-email UUID as both primary key and foreign key and records raw MIME, byte length, SHA-256, and storage time;
- `synchronization_checkpoints` is separate from folder identity and uses the folder key as its primary key and foreign key.

The application metadata port returns the stable local `StoredEmailId` from its idempotent upsert. The content port accepts that identifier, allowing metadata and raw MIME to share one transaction without exposing EF Core entities outside Infrastructure. The metadata write occurs before the content write so the required principal is present in the same unit of work.

EF Core sessions clear tracked state when disposed. This is required because one scoped `DbContext` can create several short transactions during a synchronization run and must not retain raw MIME arrays or stale tracked entities between them.

New stored-email UUIDv7 values use the injected `TimeProvider`, keeping time-dependent identity generation predictable. The PostgreSQL content adapter reuses a complete array-backed `ReadOnlyMemory<byte>` buffer and copies only sliced or non-array memory, avoiding a second full-size MIME allocation in the normal MailKit path.

## Synchronization transaction and memory model

`MailboxSynchronizer` fetches at most one raw MIME payload at a time. After a seen-preserving IMAP fetch completes, it opens a short persistence session, saves that message's metadata and content atomically, commits, and releases the payload before fetching the next message.

After every message in the inspected UID window has either been durably stored or deliberately skipped because it exceeds the configured limit, the synchronizer opens a separate short persistence session and advances the checkpoint. A process failure before checkpoint commit may cause already stored messages to be fetched again, but the existing idempotent occurrence-key writes make the retry safe. No database transaction spans IMAP network I/O.

## MailKit session safety

Folder resolution uses MailKit's asynchronous `GetFolderAsync` API. The adapter retains read-only folder selection and seen-preserving content retrieval.

Failed session setup always attempts both disconnect and disposal. Cleanup failures must never replace the original connect, authenticate, folder-resolution, or folder-open failure. Normal session disposal also attempts disposal even if graceful disconnect fails, while reporting the first cleanup failure to the caller.

`FetchMessageContentWithoutSettingSeenAsync` validates that the requested occurrence belongs to the session's account, folder, and current UIDVALIDITY before issuing an IMAP fetch. This prevents content fetched from one session from being persisted under another remote occurrence identity.

## Configuration and operational behavior

The worker scope remains unchanged. Startup validation rejects duplicate normalized account identifiers, duplicate normalized folders, and ports outside the IMAP range before a worker can run. Account lookup uses the same normalized identifier as the domain value object. The TLS mode is named `UseSslOnConnect`: `true` selects implicit TLS and `false` selects mandatory STARTTLS, so neither mode permits clear-text authentication.

The direct EF Core Relational package reference belongs to `Infrastructure`, which owns relational persistence, rather than to the Host and infrastructure test project.

The branch incorporates the current `main` rules for draft pull requests and ignored local worktrees. The PR must be returned to draft state before the final push because the owner has not requested that it be marked ready for review.

## Tests

Application unit tests prove that:

- the next remote content fetch starts only after the previous message's persistence session has committed and been disposed;
- the checkpoint session starts only after all accepted messages have committed;
- the metadata upsert supplies the local stored-email identifier used for the raw MIME write;
- retry-safe checkpoint ordering remains unchanged.

Domain unit tests prove that `StoredEmailId` rejects an empty UUID and preserves valid UUIDv7 values.

Infrastructure unit tests prove that:

- folder resolution uses the asynchronous client method;
- failed setup preserves the primary exception when disconnect or disposal also fails;
- session disposal still disposes the client when disconnect fails;
- a foreign account, folder, or UIDVALIDITY occurrence is rejected before `GetStreamAsync`.

The full repository verification remains restore, build without restore, unit tests without build, formatting verification, aggregate coverage collection, and final diff inspection.
