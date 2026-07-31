# Message Metadata Persistence and Indexes

**Roadmap group:** B — message data enrichment
**Draft delivery stage:** 3
**Depends on:** 06
**Estimated change size:** ~600 lines including tests and documentation

## Goal

Persist the metadata extracted in specification 06 with the indexes draft section 9.2 requires, and establish the deterministic ordering that keyset pagination in specification 13 depends on.

## Current state

`StoredEmailEntity` stores identity, message identifier, subject, sent timestamp, size, and content availability. `MailFathomDbContext` configures those mappings. There are no participant columns, no attachment summary, no remote flag snapshot, and none of the timeline or full-text indexes the draft specifies.

## Approved scope

`stored_emails` gains normalized sender columns, recipient arrays for the to, cc, and reply-to roles, the received timestamp, thread reference columns, the attachment summary, and a remote flag snapshot. Recipient arrays use PostgreSQL array columns rather than a join table, because every planned query filters by containment rather than joining to recipient rows, and the draft's index list assumes array containment indexes.

The persisted attachment summary is the indexable part of the specification 06 record and only that: the attachment count, the total decoded size, the inline-resource count, and the encrypted, unverified-signature, and unexpanded-TNEF markers. The signature marker is named for presence rather than for verification, because specification 06 verifies nothing; a column called "signed" would be read as an authenticity result by every query that later touches it. **The per-attachment list of file names, media types, and sizes is deliberately not persisted.** File names are mail content and personal data, so a second copy widens the access, export, and erasure surface for no query benefit — no planned query in specification 13 or 15 filters or sorts on a file name, and specification 14 already parses the stored raw MIME to render a body, so it re-derives the list in a pass it is making anyway. Persisting it would mean maintaining a second representation that can drift from the raw MIME it was derived from. If a future query genuinely needs to filter on attachment media type, that is a narrow addition decided then, not a reason to store the whole list now.

Indexes follow draft section 9.2: the unique constraint on folder plus UIDVALIDITY plus UID that already expresses occurrence identity, the account timeline index on received timestamp descending with the identifier as tiebreaker, the folder timeline index with the same shape, and indexes over the normalized sender and recipient arrays. The full-text index is deliberately deferred to specification 08, which introduces the column it covers.

The timeline index shape is the ordering contract for keyset pagination: received timestamp descending, then identifier descending. Because a message can have a null received timestamp, the ordering must define where those messages sort and the index must match that definition, otherwise pagination silently skips rows. This specification fixes that decision explicitly rather than leaving it to the query implementation.

Because EF Core migrations are deliberately deferred to specification 19, this change also adds a Development-only schema bootstrap: the host creates the schema from the EF Core model when the environment is Development and the operator has opted in, and fails startup if that path is reached in any other environment. The bootstrap is temporary scaffolding with a single owner — specification 19 removes it — and it is marked as such in code and documentation.

## Safety and privacy

Participant columns hold personal data and inherit the classification in draft section 16.1. No projection introduced here selects raw MIME, and the entity configuration keeps the content relationship unloaded by default so a mailbox query cannot accidentally pull a `bytea` value into the change tracker. The remote flag snapshot is stored as an observation of server state and is never writable through any application path.

## Testing

Unit tests cover the ordering contract, including the null received timestamp placement, and the mapping from the extracted metadata record to the persistence model. Per ADR 0001, verification of the generated PostgreSQL schema, constraint enforcement, and index usage belongs to the integration suite that specification 20 introduces; this specification records those checks as its acceptance criteria there rather than asserting them from unit tests.

## Out of scope

Full-text columns and indexes, embedding storage, and the migration files themselves.

## Definition of done

- Every extracted field that this specification persists is mapped, and the deliberately unpersisted per-attachment list is documented as such rather than silently dropped.
- The ordering contract is documented and expressed in both the index definition and the entity configuration.
- The Development-only bootstrap fails startup outside Development and carries an explicit removal reference to specification 19.
- `docs/architecture/` documents the table shape, index list, and ordering contract.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
