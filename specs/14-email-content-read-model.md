# Email Content Read Model

**Roadmap group:** D — read side and MCP
**Draft delivery stage:** 4
**Depends on:** 06, 08, 13
**Estimated change size:** ~700 lines including tests and documentation

## Goal

Implement the `GetEmailContent` application use case per draft section 13.2: normalized headers, plain-text body, optional sanitized HTML, attachment metadata without bytes, and explicit truncation metadata, served entirely from local storage.

## Current state

`IMessageContentStore` can read stored raw MIME, but nothing renders it for a caller. There is no sanitization, no truncation policy, and no handling for the case where metadata exists but content does not.

## Approved scope

The use case takes one stable local message identifier and returns normalized headers, the plain-text body, attachment metadata, source account and folder alias, the remote flag snapshot, and truncation metadata. Plain text is the default representation. Sanitized HTML is returned only when the caller asks for it and the message actually has an HTML part.

Bodies are bounded by a validated maximum size. When a body exceeds it, the result is truncated at a character boundary and the truncation metadata states the original length and that truncation occurred, so a caller never has to guess whether it received a complete message.

Missing or corrupt content is an expected failure, not an exception path: the use case returns a stable consistency error and schedules background repair. It must never trigger a synchronous IMAP fetch, which draft sections 10 and 17 both require and which the acceptance criteria in draft section 23 name explicitly. The repair request is recorded durably so the synchronizer can act on it; the use case does not wait for it.

## Safety and privacy

HTML sanitization treats message HTML as hostile input. The sanitized output permits a conservative element and attribute allow-list, strips scripts, event handlers, embedded objects, and form elements, neutralizes external references so no remote image or linked resource can be fetched by a client rendering the output, and rewrites or removes URLs that would leak a read receipt. The allow-list approach is required rather than a deny-list, because a deny-list cannot be proven complete.

Sanitization needs a library rather than hand-written parsing; the chosen component must be verified for a permissive license, .NET 10 compatibility, and active maintenance, pinned centrally, and recorded in `LICENSES.md` in the same change. If no acceptable component is found, HTML support is dropped from this specification and only plain text is returned — degrading the feature is the correct outcome, not shipping a hand-rolled sanitizer.

Errors returned by this use case carry stable codes and safe messages, never exception types, stack traces, provider payloads, or internal identifiers.

## Testing

`Application.UnitTests` cover: plain-text preference, HTML requested and absent, truncation metadata at and beyond the boundary, missing content producing the consistency error and a recorded repair request, corrupt content detected through the stored length and hash, and the absence of any IMAP call on every path. Sanitization tests cover script removal, event-handler attributes, external reference neutralization, nested and malformed markup, and an encoding-confusion case.

## Out of scope

Returning attachment bytes, which draft section 3.2 excludes from the first release. Actually performing the repair, which belongs to the synchronizer.

## Definition of done

- No code path in this use case can reach an IMAP session, proven by test.
- Sanitized HTML contains no script, no event handler, and no external reference.
- Truncation is always explicit in the result.
- `docs/features/` documents the representations, the sanitization policy, and the consistency-error behavior.
- `dotnet msbuild eng/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
