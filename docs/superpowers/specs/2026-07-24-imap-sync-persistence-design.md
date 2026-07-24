# IMAP Synchronization and Persistence Design

## Goal

Implement the first vertical slice for read-only IMAP synchronization with local PostgreSQL persistence, application-owned abstractions, and a periodic background worker.

## Approved scope

This slice implements periodic reconciliation only. It records account, folder, remote occurrence identity, metadata, raw MIME content, and synchronization checkpoints while bounding metadata batches and raw MIME size. It does not implement IMAP IDLE, NOTIFY, SMTP outbox, RAG indexing, MCP tools, integration tests, or production startup migrations.

## Architecture

`Domain` owns pure identifiers and invariants for mail accounts, folders, IMAP UIDVALIDITY, UIDs, message occurrence identity, and synchronization checkpoints. `Application` owns use-case orchestration and all persistence/content/IMAP ports. `Infrastructure` owns EF Core PostgreSQL mappings, repository implementations, raw MIME storage, and the MailKit adapter. `Host` owns typed configuration, dependency injection, and the hosted worker.

## Data flow

The hosted worker wakes on a configured interval, creates a scope per account/folder work unit, resolves the application synchronizer, isolates failures per work unit, and runs one account/folder synchronization operation at a time. The synchronizer opens an IMAP folder read-only through the application-owned session port, asks for messages after the stored checkpoint, persists metadata and raw MIME idempotently, then advances the checkpoint only after storage succeeds.

## Safety and privacy

All content-fetch APIs are named around preserving the remote `\\Seen` flag, and tests verify application code uses only that safe path. Logs include counts and stable technical identifiers but never credentials, raw MIME, message bodies, attachment content, or full recipient payloads.

## Testing

Unit tests cover domain invariants, application idempotency/checkpoint behavior, bounded batches, oversized-message skipping, and use of seen-preserving IMAP fetches. ADR 001 leaves EF mapping/constraint verification to future PostgreSQL integration tests. Real IMAP/PostgreSQL integration remains out of scope for this slice.
