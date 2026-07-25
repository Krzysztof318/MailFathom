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

Sanitization needs a library rather than hand-written parsing. The evaluation was completed while writing this specification and is recorded below; the selected component is pinned centrally and recorded in `LICENSES.md` in the same change as the implementation.

Errors returned by this use case carry stable codes and safe messages, never exception types, stack traces, provider payloads, or internal identifiers.

## Sanitizer selection

**Selected: `HtmlSanitizer` 9.0.967.** MIT, published 2026-07-21, 120.7 million downloads, actively maintained, and the de facto standard for this job in .NET. It parses with AngleSharp rather than regular expressions, and its model is an allow-list of tags, attributes, CSS properties, and URI schemes, which is the shape this specification requires. Its target frameworks are `net8.0`, `netstandard2.0`, and `net462`; the `net8.0` assets are compatible with .NET 10.

Three consequences of that choice have to be accepted deliberately:

- It pins AngleSharp to exactly `0.17.1` plus `AngleSharp.Css` `0.17.0`, both MIT. AngleSharp itself is at 1.5.2, so the transitive dependency is several major versions behind. **This forecloses referencing AngleSharp 1.x directly anywhere in the solution**, because the exact-version pin cannot be satisfied alongside it. Since nothing else in MailMcp needs AngleSharp, the constraint costs nothing today, but it must be recorded so a later change does not discover it by build failure.
- `LICENSES.md` needs three entries, not one: `HtmlSanitizer`, `AngleSharp`, and `AngleSharp.Css`.
- The library has a security history worth knowing: CVE-2026-25543 (GHSA-j92c-7v7g-gj3f, moderate, published 2026-02-03) was a bypass where the contents of a `<template>` element went unsanitized, exploitable when `template` and `shadowrootmode` were explicitly allowed. It is fixed in 9.0.892, so 9.0.967 carries the fix, and the default configuration was never affected because `template` is disallowed by default. The lesson for this specification is directional: the allow-list stays minimal, `template` is never added to it, and the pinned version is watched for advisories rather than pinned and forgotten.

**Rejected: AngleSharp 1.5.2 alone.** MIT, actively maintained, and the newer stack, but it is a parser, not a sanitizer. Using it would mean writing the allow-list enforcement, CSS filtering, and URI-scheme handling by hand — exactly the hand-rolled sanitizer this specification refuses.

**Rejected: HtmlAgilityPack 1.12.4.** MIT, but likewise a parser rather than a sanitizer, and its last release was 2025-10-03.

**Rejected: `Microsoft.Security.Application` (AntiXSS).** Legacy, .NET Framework only, no longer maintained.

Should the pinned version develop an unpatched advisory before this work starts, the fallback stated in the original scope still holds: drop the HTML representation and return plain text only. Degrading the feature is the correct outcome, not shipping a hand-rolled sanitizer.

## Testing

`Application.UnitTests` cover: plain-text preference, HTML requested and absent, truncation metadata at and beyond the boundary, missing content producing the consistency error and a recorded repair request, corrupt content detected through the stored length and hash, and the absence of any IMAP call on every path. Sanitization tests cover script removal, event-handler attributes, external reference neutralization, nested and malformed markup, and an encoding-confusion case.

## Out of scope

Returning attachment bytes, which draft section 3.2 excludes from the first release. Actually performing the repair, which belongs to the synchronizer.

## Definition of done

- No code path in this use case can reach an IMAP session, proven by test.
- Sanitized HTML contains no script, no event handler, and no external reference.
- Truncation is always explicit in the result.
- `HtmlSanitizer`, `AngleSharp`, and `AngleSharp.Css` are pinned in `Directory.Packages.props` and recorded in `LICENSES.md`, and the sanitizer type does not escape the adapter that owns it.
- `docs/features/` documents the representations, the sanitization policy, and the consistency-error behavior.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
