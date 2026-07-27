# The `get_email_content` Tool

**Roadmap group:** D — read side and MCP
**Draft delivery stage:** 4
**Depends on:** 14, 16
**Estimated change size:** ~450 lines including tests and documentation

## Goal

Expose the content read model from specification 14 as an MCP tool, following the conventions established in specification 16.

## Approved scope

The tool accepts one stable local message identifier and an optional flag requesting the sanitized HTML representation. It returns normalized headers, the plain-text body, attachment metadata without bytes, source account and folder alias, the remote flag snapshot, and truncation metadata, mapped from the use-case result.

The descriptor carries the same read-only annotation set as `list_emails`, since this tool reads local state and reaches nothing outside MailMcp. The consistency error for missing or corrupt content maps to its own stable code, distinct from a not-found code, because a caller needs to distinguish a message that does not exist from one whose local copy is missing and is being repaired.

## Safety and privacy

The tool returns message content, which makes it the most sensitive surface in this roadmap segment. Three properties are enforced and tested: attachment bytes are never included in any shape, truncation is always explicit so a caller cannot mistake a partial body for a complete one, and the response cannot trigger an IMAP fetch, which is the acceptance criterion draft section 23 states directly.

Body content is never logged, and the tool's error responses carry stable codes and safe messages only.

## Testing

`Mcp.UnitTests` cover: advertised metadata and annotations, mapping of the full result including truncation metadata, the HTML flag requested and absent, the not-found code, the consistency-error code being distinct from not-found, attachment metadata containing no byte content, attachment file names returned in the normalized form specification 06 defines and never as a path, an encrypted message surfacing the not-readable state rather than an empty body, rejection of a message belonging to an account the owner does not control, and an invalid identifier being rejected at the boundary.

## Out of scope

Attachment download, message export, and any mutation of remote state.

## Definition of done

- The tool returns bounded content with explicit truncation and no attachment bytes, and every returned file name is normalized before it reaches a model.
- Missing local content produces a distinct stable code and never an IMAP fetch.
- Advertised metadata matches the read-only annotation conventions.
- `docs/features/` documents the tool contract and its error codes.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
