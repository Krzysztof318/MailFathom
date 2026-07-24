# IMAP Synchronization Review Hardening Design

## Goal

Keep the complete IMAP synchronization and PostgreSQL persistence scope of pull request 13 while correcting the merge-blocking resource, memory, asynchronous I/O, ownership-validation, and coverage-documentation issues found during review.

## Approved scope

- Keep the PostgreSQL repositories, EF Core model, Unit of Work, hosted worker, and existing application ports.
- Keep temporary coverage exclusions for provider-bound persistence code that cannot be exercised by the current unit-test-only policy.
- State on each temporary exclusion that PostgreSQL integration tests will cover the behavior later, and place a concrete TODO beside the exclusion so removal remains visible.
- Do not add an integration-test project or a fake EF Core provider in this change.

## Synchronization transaction and memory model

`MailboxSynchronizer` fetches at most one raw MIME payload at a time. After a seen-preserving IMAP fetch completes, it opens a short persistence session, saves that message's content and metadata atomically, commits, and releases the payload before fetching the next message.

After every message in the inspected UID window has either been durably stored or deliberately skipped because it exceeds the configured limit, the synchronizer opens a separate short persistence session and advances the checkpoint. A process failure before checkpoint commit may cause already stored messages to be fetched again, but the existing idempotent occurrence-key writes make the retry safe. No database transaction spans IMAP network I/O.

## MailKit session safety

Folder resolution uses MailKit's asynchronous `GetFolderAsync` API. The adapter retains read-only folder selection and seen-preserving content retrieval.

Failed session setup always attempts both disconnect and disposal. Cleanup failures must never replace the original connect, authenticate, folder-resolution, or folder-open failure. Normal session disposal also attempts disposal even if graceful disconnect fails, while reporting the first cleanup failure to the caller.

`FetchMessageContentWithoutSettingSeenAsync` validates that the requested occurrence belongs to the session's account, folder, and current UIDVALIDITY before issuing an IMAP fetch. This prevents content fetched from one session from being persisted under another remote occurrence identity.

## Configuration and operational behavior

The existing configuration and worker scope remain unchanged. The branch incorporates the current `main` rules for draft pull requests and ignored local worktrees. The PR must be returned to draft state before the final push because the owner has not requested that it be marked ready for review.

## Tests

Application unit tests prove that:

- the next remote content fetch starts only after the previous message's persistence session has committed and been disposed;
- the checkpoint session starts only after all accepted messages have committed;
- retry-safe checkpoint ordering remains unchanged.

Infrastructure unit tests prove that:

- folder resolution uses the asynchronous client method;
- failed setup preserves the primary exception when disconnect or disposal also fails;
- session disposal still disposes the client when disconnect fails;
- a foreign account, folder, or UIDVALIDITY occurrence is rejected before `GetStreamAsync`.

The full repository verification remains restore, build without restore, unit tests without build, formatting verification, aggregate coverage collection, and final diff inspection.
