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

Text extraction follows draft section 12.1 steps 1 through 3. The reader prefers a genuine `text/plain` part; when only HTML exists it derives text from the markup, and that derivation is treated as lossy and marked as such on the record so a future chunking design can decide whether to trust it. Quoted history and signatures are removed conservatively, and the original extracted text is retained alongside the trimmed form rather than replaced, because an over-aggressive trim would otherwise destroy content permanently.

The HTML-to-text derivation uses `MimeKit.Text.HtmlTokenizer`, which is already available through the pinned MailKit package, so this specification adds no dependency. That matters beyond convenience: specification 14 selects `HtmlSanitizer`, which pins AngleSharp to an exact old version and therefore forecloses referencing AngleSharp 1.x directly. Deriving text with MimeKit keeps a second HTML stack out of the solution entirely rather than trading one version conflict for another.

Persistence adds the extracted text column, a lossy-derivation marker, and a generated `tsvector` column covering subject, normalized participant addresses, and the trimmed body text, with the GIN index draft section 9.2 requires. The text search configuration is an explicit, validated setting rather than a database default, because changing it silently invalidates the index contents.

Text comes from the message body only. Attachment payloads are never opened, so a PDF or an office document contributes nothing to the index; a message whose information lives entirely in an attachment is indexed on its subject and participants alone. That is a deliberate limitation rather than an oversight — draft section 21.5 names attachment extraction as an unbounded-cost path, and document parsers are a far larger hostile-input surface than MIME parsing. It is recorded in the feature documentation so the search behavior is not surprising.

A message that specification 06 recorded as encrypted has no readable body. It is marked as having no extractable text for that reason and is not indexed as though its body were empty, so an encrypted message stays distinguishable from a genuinely empty one and does not become a permanent silent gap in search. The backfill re-evaluates such messages if decryption is ever enabled (#75).

A bounded backfill operation re-derives text for messages stored before this change, running in the background with the same batch bounds and cancellation behavior as synchronization. Backfill is idempotent and restartable from a persisted position.

## Safety and privacy

Extracted text is mail content and is classified accordingly: it never appears in logs, and no error message includes a fragment of it. HTML is treated as untrusted input; extraction neither resolves nor fetches remote images, linked resources, or external entities, per draft section 10. The extracted text and the `tsvector` are derived data inheriting the retention and deletion obligations of the source message per draft section 16.1, so specification 10's deletion path must reach them.

## Testing

Unit tests cover plain-text preference, HTML derivation and its lossy marker, conservative quote and signature trimming with a case that must not trim, retention of the untrimmed original, and rejection of an unknown text search configuration. Backfill tests cover boundedness, idempotency, restart from a persisted position, and cancellation. Verification that the generated column and GIN index behave as expected in PostgreSQL belongs to specification 20.

## Out of scope

Chunking, embeddings, hybrid ranking, and snippet generation, which belong to the RAG stages after the read-only MCP tools land. Extracting text from attachment payloads, including PDF, office document, and image OCR extraction, which needs its own bounded-cost design and its own parser-hardening review. Decrypting encrypted messages so their bodies become indexable (#75).

## Definition of done

- Every newly synchronized message gets extracted text and an indexed `tsvector`.
- Backfill completes for pre-existing messages and is safe to interrupt and resume.
- No extracted text appears in any log or error message.
- `docs/features/` documents the extraction rules, the lossy-HTML marker, and the text search configuration setting.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
