# Extracted Text and Full-Text Index

**Roadmap group:** B — message data enrichment
**Draft delivery stage:** 5, pulled forward ahead of stage 4
**Depends on:** 06, 07
**Estimated change size:** ~500 lines including tests and documentation

## Goal

Derive searchable plain text from stored messages and index it with PostgreSQL full-text search, so the `search_emails` use case in specification 15 has a lexical index to query.

This work sits at stage 5 in the draft's delivery order but is scheduled before the stage 4 search tool, because a search tool without an index has nothing to search.

## Current state

Raw MIME is stored and, after specification 06, parsed for metadata. No body text is extracted, and there is no `tsvector` column or GIN index.

## Approved scope

Text extraction follows draft section 12.1 steps 1 through 3. The reader prefers a genuine `text/plain` part; when only HTML exists it derives text by stripping markup, and that derivation is treated as lossy and marked as such on the record so a future chunking design can decide whether to trust it. Quoted history and signatures are removed conservatively, and the original extracted text is retained alongside the trimmed form rather than replaced, because an over-aggressive trim would otherwise destroy content permanently.

Persistence adds the extracted text column, a lossy-derivation marker, and a generated `tsvector` column covering subject, normalized participant addresses, and the trimmed body text, with the GIN index draft section 9.2 requires. The text search configuration is an explicit, validated setting rather than a database default, because changing it silently invalidates the index contents.

A bounded backfill operation re-derives text for messages stored before this change, running in the background with the same batch bounds and cancellation behavior as synchronization. Backfill is idempotent and restartable from a persisted position.

## Safety and privacy

Extracted text is mail content and is classified accordingly: it never appears in logs, and no error message includes a fragment of it. HTML is treated as untrusted input; extraction neither resolves nor fetches remote images, linked resources, or external entities, per draft section 10. The extracted text and the `tsvector` are derived data inheriting the retention and deletion obligations of the source message per draft section 16.1, so specification 10's deletion path must reach them.

## Testing

Unit tests cover plain-text preference, HTML derivation and its lossy marker, conservative quote and signature trimming with a case that must not trim, retention of the untrimmed original, and rejection of an unknown text search configuration. Backfill tests cover boundedness, idempotency, restart from a persisted position, and cancellation. Verification that the generated column and GIN index behave as expected in PostgreSQL belongs to specification 20.

## Out of scope

Chunking, embeddings, hybrid ranking, and snippet generation, which belong to the RAG stages after the read-only MCP tools land.

## Definition of done

- Every newly synchronized message gets extracted text and an indexed `tsvector`.
- Backfill completes for pre-existing messages and is safe to interrupt and resume.
- No extracted text appears in any log or error message.
- `docs/features/` documents the extraction rules, the lossy-HTML marker, and the text search configuration setting.
- `dotnet msbuild eng/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
