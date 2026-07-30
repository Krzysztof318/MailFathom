# Email content

MailMcp serves one email's content from its local copy. `EmailContentReader` is the second read use case: it takes the
stable local identifier a listing returned and answers with normalized headers, the body as plain text, optionally a
sanitized HTML representation, per-attachment metadata without any bytes, the source account and folder alias, and the
remote flag snapshot.

It reaches no mail server. That is structural rather than a rule someone keeps: the use case is constructed from a
summary reader, a content store, a renderer, a repair-request store, and the account catalog, and none of them can open
an IMAP session. Reading an email therefore cannot download it and cannot set the remote `\Seen` flag, whether or not
the local copy turns out to be usable.

The protocol adapter is not part of this. `EmailContentReader` is an application use case; the `get_email_content` MCP
tool that maps onto it is specification 17.

## The request contract

`GetEmailContentRequest` carries two values.

| Field | Meaning | Absent means |
|---|---|---|
| `StoredEmailId` | The email to read, named by the identity a listing returned | — |
| `IncludeSanitizedHtml` | Whether to also produce the sanitized HTML representation | plain text only |

The HTML representation is opt-in because it costs a sanitization pass over untrusted markup and because plain text is
what most callers want: a model reading mail is better served by the words than by the layout around them.

## What a result carries

`GetEmailContentResult` is the most sensitive projection MailMcp publishes. It is message content in full and inherits
every classification, retention, access, and erasure constraint of the mail it was read from. Nothing in it is logged.

| Field | Meaning |
|---|---|
| `StoredEmailId`, `AccountId`, `FolderAlias` | Where the email is, in MailMcp's own names |
| `SizeOctets` | The size the mail server reported for the whole message |
| `Headers` | Subject, sent and received timestamps, every participant under its header role, and the thread identifiers |
| `Body` | The representations, or the reason there are none |
| `AttachmentSummary` | The counts for what the message carries besides its body, absent when nobody has counted them |
| `Attachments` | One entry per attachment, re-derived from the stored raw MIME, with no bytes |
| `RemoteFlags` | The flags a server last showed, and when they were read |

### Headers come from the message, not from the row

The headers are read during the same parse that produces the body rather than from the columns a listing is served out
of. The row keeps only the comparison forms a filter needs, so display names, a `Bcc` a message carries for its own
recipient, and the `Sender` header exist nowhere else — a reader shown the listing's copy would be shown a narrower
message than the one that arrived.

### Attachments are re-derived, never stored

The per-attachment list — the normalized file name, the media type, and the decoded size — is produced by the parse this
read already performs, following the classification rule
[MIME metadata extraction](imap-synchronization.md#mime-metadata-extraction) defines. It is not persisted, because file
names are mail content and [the stored schema](../architecture/stored-email-schema.md) deliberately keeps only the
indexable summary. Re-deriving costs nothing extra and guarantees the list cannot drift from the message it describes.

Inline resources and cryptographic parts never appear in the list. They are reported as counts on `AttachmentSummary`
instead, so a signed message and a message with a logo in its signature block do not look like mail with files attached.

Those counts come from the same parse as the list whenever the stored MIME could be read, so the two can never disagree.
They would if the row answered for them: a message stored before extraction ran records no attachments until the
backfill reaches it, while the message it describes has them.

Where there is nothing to parse — content the size limit kept out of storage — the summary is **absent rather than
zero**. Nothing has ever read that message's parts: synchronization recorded what the server's envelope reported, and an
envelope describes no attachments, so the row's zero counts are unset defaults rather than a finding. Publishing them
would tell a caller that every oversized message carries no attachments, which is a claim nothing here is in a position
to make.

Each header role contributes at most 256 participants. Nothing between a sender and this system bounds how many
addresses a header may carry, so without it one message could decide how large every result derived from it becomes.
The persisted columns carry a bound of their own, deliberately: this one bounds what a parse publishes, that one bounds
what a column stores.

File names arrive normalized: path structure, control characters, and bidirectional overrides are removed when the name
is read, and a name is never returned as a path or resolved against one.

The result type has nowhere to put attachment content. That is a property of the contract rather than of a caller's
discipline, and a unit test asserts it.

## The body, and the three ways there is none

`EmailContentBody` states which case a reader is in.

| `Availability` | Meaning |
|---|---|
| `Readable` | The body was read; an empty one means the message displayed nothing |
| `EncryptedNotReadableLocally` | The body arrived inside a cryptographic envelope and nothing here can read it |
| `NotStoredExceededSizeLimit` | The raw MIME exceeded `MailSynchronization:MaxRawMimeBytes`, so it was never stored |

An encrypted body is a state rather than an empty string, because merging the two would make mail this deployment holds
and cannot decrypt indistinguishable from mail that genuinely said nothing. Decrypting it is out of scope and is tracked
by #75.

The state means what it says: nothing could read the body. A `multipart/alternative` may offer a readable `text/plain`
member beside an encrypted one, and the message then has a body a reader can be shown — so it is reported as readable,
even though the attachment summary still records that the message carries encrypted content somewhere. The unreadable
state is reserved for a body that left nothing behind.

`NotStoredExceededSizeLimit` is not a defect and schedules no repair: synchronization recorded the occurrence and
deliberately stored no content for it, and asking for repair would ask a later run to store what the same limit refuses
again. Everything answerable is still answered — the headers from the stored row, the attachment counts from the summary
written when the occurrence was recorded — and only the per-attachment list is absent, because nothing local can derive
it.

Plain text is the default representation and is always present, empty in each of the states where nothing could be read.
A genuine `text/plain` part wins over every HTML alternative; HTML is read only when the message offered no plain-text
one. Unlike the text the lexical index covers, nothing is trimmed: quoted history and a signature block are part of the
message a person asked to read.

### Truncation is always explicit

Each representation carries its own `EmailBodyRepresentation`: the text as returned, the number of characters its source
held, and whether the bound removed anything. A caller therefore never has to guess whether it received a whole message,
and a message can exceed the bound in one representation without affecting the other.

`EmailContent:MaxBodyCharacters` sets the bound, defaults to 100,000, and is validated at startup within 1,000–1,000,000.
It decides how much of a body a caller is handed; what bounds this process is `MailSynchronization:MaxRawMimeBytes`,
which no stored message is above.

```json
{
  "EmailContent": {
    "MaxBodyCharacters": 100000
  }
}
```

It is a section of its own rather than a value inside the synchronization settings, because it bounds a read rather than
a fetch: it applies whether or not synchronization is enabled, and changing it changes no stored data. The section is
bound strictly, so a misspelled key fails startup instead of being replaced by the default.

The cut falls on a text-element boundary, so a body ending in an emoji or a combining sequence is never handed over as a
lone surrogate that a JSON writer would replace and PostgreSQL would reject.

Plain text is read in full and then cut, so the reported original length is the length that actually existed. Its edges
are left exactly as the sender wrote them — a leading indent can be the first line of a code block and a trailing blank
line can be the shape of a signature. Text *derived* from HTML is trimmed, because its edge whitespace belongs to the
derivation rather than to the message: a body opening with a block element emits a line break before its first word.

Markup is cut before it is parsed: sanitizing is the expensive step, and there is nothing to learn from parsing what
will not be returned. The sanitizer's parse then closes what the cut left open, so a truncated HTML representation is
still balanced markup, and its truncation is measured against the source it was cut from.

Closing those elements adds characters, so a source that fits the bound can serialize past it — deeply nested markup can
spend its whole allowance on opening tags and then need as much again to close them. Rather than cut the result, which
would hand back exactly the unbalanced fragment the source-first cut avoids, the source is shrunk and sanitized again
until the result fits. The retry terminates because a shorter prefix opens no more elements than a longer one, and
ordinary mail never reaches a second pass.

## HTML sanitization

Message HTML is treated as hostile input. The policy is an allow-list at every level the sanitizer offers — elements,
attributes, CSS properties, CSS at-rules, and URI schemes — because a deny-list cannot be proven complete.

- **No URI scheme is allowed at all.** Every `href`, `src`, and other reference is removed rather than filtered. Nothing
  here can prove which attributes a given client resolves without being asked, so no reference survives to find out: no
  remote image is fetched, no linked resource is loaded, and no tracking URL is left for a renderer to open.
- **`cid:` references fall with them, deliberately.** They point at parts of the same message, this read never returns
  part bytes, and a client resolving content identifiers against something other than the message would follow one
  somewhere unintended. The inline-resource count is what a caller reports instead — that the message contained embedded
  images, rather than a gap where one was.
- **Style is removed entirely**, both the `style` attribute and `<style>` elements with their contents. That is where a
  body hides a reference behind a `url()` and where an at-rule imports one.
- **Scripts, event handlers, embedded objects, forms, and inputs are removed**, each with its contents.
- **`alt` and `title` survive**, because what a stripped image was is the only thing a reader is left with. So do
  `colspan`, `rowspan`, `dir`, and `lang`.
- **`template` is not on the allow-list and must never be added.** Its contents were the subject of CVE-2026-25543
  (GHSA-j92c-7v7g-gj3f), fixed in the pinned version and only ever exploitable where the element had been allowed
  explicitly.

A disallowed element is removed with its content rather than unwrapped. Unwrapping would keep the text a `<script>`
element carries, which is inert but indistinguishable from the message's own words. The element allow-list is therefore
generous about the presentational elements mail actually uses — `font`, `center`, `big` — whose attributes are stripped
anyway, so removing an element is rare.

One consequence follows from that choice and is worth stating: an unclosed disallowed container takes with it whatever
the parser nested inside it. A message ending in an unclosed `<iframe>` loses the text after it, which is the same text
a browser would not display either.

## When the local copy is unusable

Missing or damaged content is an expected outcome, not a crash. The read verifies what is stored against the length and
SHA-256 digest recorded beside it when it was written, and four things can be wrong.

| Defect | What it means |
|---|---|
| `Missing` | The row says content is stored and none is |
| `ByteLengthMismatch` | The payload is not as long as was recorded, which is what a partial write leaves |
| `HashMismatch` | The payload is the right length and its bytes changed |
| `Unreadable` | The payload is intact and still yields no message a parser can render |

In every case the read records a durable repair request and then fails with `54001 EmailContentUnavailable`. The request
is recorded first, so the finding survives whether or not anything catches the failure; performing the repair belongs to
the synchronizer and is out of scope here. The request is idempotent per email — PostgreSQL resolves the collision
itself — so a caller retrying a damaged message leaves one row with an accurate count rather than a row per attempt.

The three fetch-again defects stay distinct from `Unreadable` because they say different things to whoever repairs them:
a second fetch fixes the first three and may well reproduce the fourth.

An email the local copy holds no row for, or one belonging to an account this deployment no longer serves, is refused
with `53002 StoredEmailNotFound`. One failure covers both, for the reason `53001 MailAccountNotAccessible` covers both
of its cases: a caller that could tell them apart could learn which identifiers exist by asking.

The two codes are distinct on purpose. `StoredEmailNotFound` names an email that was never stored here;
`EmailContentUnavailable` names one that is stored and whose body cannot currently be served, and only the second is
worth retrying.

## Where the pieces live

- `MailMcp.Application.Emails.GetEmailContent` — the use case, its request, and its result.
- `MailMcp.Application.EmailContent` — the content store port, the renderer port, the repair-request port, the body
  representations, the headers, and the two failures.
- `MailMcp.Infrastructure.Mail.Mime` — `MimeKitEmailContentRenderer` and `EmailHtmlSanitizer`, which own the MIME parser
  and the HTML sanitizer respectively. Neither type escapes that namespace.
- `MailMcp.Infrastructure.Persistence` — `StoredEmailSummaryReader`, the content store's integrity-bearing read, and
  `EmailContentRepairRequestStore`.

`MimeMessageHeaderReader` is shared with the extraction that fills the lexical index, so a message is indexed under
exactly the headers it is displayed under.
