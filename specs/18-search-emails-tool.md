# The `search_emails` Tool

**Roadmap group:** D — read side and MCP
**Draft delivery stage:** 4
**Depends on:** 15, 16
**Estimated change size:** ~450 lines including tests and documentation

## Goal

Expose the lexical search use case from specification 15 as an MCP tool, completing the read-only tool set that this roadmap segment targets.

## Approved scope

The tool accepts a free-text query plus the structured filters the use case supports, and returns bounded ranked results with stable local message identifiers, summary fields, relevance rank, and highlighted snippets. It does not call a chat model, which draft section 13.3 states explicitly, so its annotations are identical to the other read-only tools including `openWorldHint` false.

The tool advertises that its results are lexical. When the later RAG work makes search hybrid, the result shape must not change in a way that breaks clients, so the response includes a retrieval-mode field from the start rather than having one added later.

## Safety and privacy

Search results are the retrieval surface a model will most often consume, so the untrusted-input rule from draft section 12.4 applies here even before the agent work: snippets are message content and are returned as data, with no interpretation and no formatting that would let message text pose as instruction or metadata. Snippet length and count stay bounded by the use-case configuration and are re-enforced at the boundary.

Query text is not logged. An empty result is a normal response, not an error, so search cannot be used to enumerate accounts or folders.

## Testing

`Mcp.UnitTests` cover: advertised metadata and annotations including `openWorldHint` false, query and filter mapping, snippet and result-count bounds enforced at the boundary, the empty-result response shape, the retrieval-mode field being present and reporting lexical, rejection of an unbounded requested result count, and rejection of accounts outside the owner's scope.

## Out of scope

Semantic and hybrid retrieval, `ask_mail`, and any chat-model invocation.

## Definition of done

- The tool returns bounded ranked snippets with stable identifiers and a retrieval-mode field.
- No chat model is reachable from this tool, proven by the absence of any AI dependency in the `Mcp` project.
- Advertised metadata matches the read-only annotation conventions.
- `docs/features/` documents the tool contract and the retrieval-mode field.
- `dotnet msbuild eng/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
