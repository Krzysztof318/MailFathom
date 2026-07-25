# Lexical Email Search

**Roadmap group:** D — read side and MCP
**Draft delivery stage:** 4
**Depends on:** 08, 13
**Estimated change size:** ~600 lines including tests and documentation

## Goal

Implement the `SearchEmails` application use case over the PostgreSQL full-text index from specification 08, returning ranked snippets with stable identifiers, as draft section 13.3 specifies.

## Current state

The `tsvector` column and GIN index exist after specification 08, and `ListEmails` exists after specification 13, but nothing queries the index and nothing produces snippets.

## Approved scope

The use case takes a free-text query plus the same structured filters as `ListEmails` — accounts, folder aliases, participants, date range, attachment presence — and returns a bounded, ranked result set. Each result carries the stable local message identifier, the summary fields needed to make it useful without a second call, a relevance rank, and one or more highlighted snippets.

User query text is turned into a full-text query through provider-supported parameterization only. No part of the query string is concatenated into SQL, and the text search configuration is the validated application-owned setting from specification 08, never a value taken from the request.

Ranking must be deterministic. Full-text rank alone produces ties, so the ordering contract appends the ordering key from specification 07 as a tiebreaker, giving a stable order for pagination and for reproducible tests.

Result count is bounded by a validated maximum. This use case returns a ranked window rather than a keyset-paginated stream, because relevance ordering is not stable across index changes in the way a timeline is; the bound is the control, and the specification states this explicitly instead of implying a cursor that would not be sound.

## Safety and privacy

Snippets are the data-minimization boundary for search: they are bounded in length and count per message, and the bounds are validated configuration. A search result never contains raw MIME, attachment bytes, or a complete body. A query that matches nothing returns an empty result rather than an error, so search cannot be used to probe for the existence of accounts or folders beyond what the caller is already authorized to see. Query text is not logged, because a search query over a mailbox is itself sensitive.

## Testing

`Application.UnitTests` cover: query parameterization including inputs containing SQL metacharacters and full-text operators, structured filters combined with text, deterministic ordering under tied ranks, snippet bounds, result-count bounds, the empty-result case, and rejection of an unbounded requested result count. Verification that the GIN index is actually used belongs to specification 20.

## Out of scope

Semantic search, hybrid ranking, and reciprocal rank fusion, which belong to the RAG stages after the read-only tools land. The MCP tool mapping, which specification 18 owns.

## Definition of done

- A search combining text and structured filters returns bounded ranked snippets with stable identifiers.
- Query text reaches PostgreSQL only through parameterization, proven by test.
- Ordering is reproducible under tied relevance ranks.
- `docs/features/` documents the query contract, snippet bounds, and the bounded-window rationale.
- `dotnet msbuild eng/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
