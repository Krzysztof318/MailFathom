# Mailbox Query Read Models

**Roadmap group:** D — read side and MCP
**Draft delivery stage:** 4
**Depends on:** 07
**Estimated change size:** ~700 lines including tests and documentation

## Goal

Implement the `ListEmails` application use case with the filters, bounded page size, and keyset pagination that draft section 13.1 specifies, independent of any protocol adapter.

## Current state

The application layer has a write path only. There is no query use case, no read model, no cursor, and no pagination contract. `MailMcpDbContext` is used exclusively by the synchronizer's repositories.

## Approved scope

A `ListEmails` use case takes an explicit request contract carrying account identifiers, folder aliases, sender and recipient filters, a subject fragment, a received date range, remote seen state, attachment presence, sort direction, page size, and an optional continuation cursor. It returns a bounded page of summaries plus the next cursor, and never returns raw MIME or attachment bytes.

Attachment presence means the attachment count from the specification 06 classification rule is greater than zero. A message whose only non-body parts are inline resources or cryptographic signature parts does not match, so filtering for mail with attachments does not return every signed message and every message with a logo in its signature block. The summary carries the attachment count and the inline-resource count as separate values for the same reason.

Pagination is keyset-based over the ordering contract fixed in specification 07. The cursor is an opaque string encoding the ordering key and the filter fingerprint. The fingerprint matters: a client that changes filters but reuses a cursor would otherwise receive an arbitrary window, so a cursor whose fingerprint does not match the current request is rejected with a stable error rather than silently honored. The cursor carries no secret and requires no signing, because it encodes only values the caller already supplied and already received.

Page size has a validated maximum of 100 per the draft, with a smaller default. The query projects directly into the read model with `AsNoTracking`, selecting only the columns the summary needs, so no entity graph and no `bytea` column is ever loaded.

Every result carries the synchronization freshness information draft section 17 requires, so a caller can tell how current the local copy is while IMAP is unavailable.

## Safety and privacy

The result is a bounded projection, which is the data-minimization control draft section 16.2 names for list operations. Filters are validated at the use-case boundary: an unbounded date range is allowed but an unbounded page size is not, and an unknown account identifier produces an authorization-shaped failure rather than an empty page, so a caller cannot probe which account identifiers exist. Ordering is deterministic even for equal timestamps, because a non-deterministic order silently drops or duplicates rows across pages.

## Testing

`Application.UnitTests` cover each filter in isolation and in combination against an in-memory fake repository, page-size clamping and rejection, cursor round-tripping, the filter-fingerprint mismatch rejection, deterministic ordering with duplicate timestamps and with null timestamps, the last-page cursor being absent, and freshness reporting. Query-plan and index-usage verification belongs to specification 20.

## Out of scope

The MCP tool mapping, which specification 16 owns. Full-text and semantic ranking, which specifications 15 and the later RAG stages own.

## Definition of done

- Paging through a result set with stable filters visits every row exactly once, including across equal and null timestamps.
- A cursor reused with different filters is rejected with a stable error code.
- No query path loads raw MIME or tracks entities.
- `docs/features/` documents the request contract, cursor semantics, and freshness reporting.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
