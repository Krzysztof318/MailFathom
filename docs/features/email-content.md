# Email content

MailFathom serves the content of the emails one call names, from its local copy. `EmailContentReader` is the second read
use case: it takes the stable local identifiers a listing returned and answers for each of them with normalized headers,
the body as plain text, optionally a sanitized HTML representation, attachment counts, optionally per-attachment
metadata without any bytes, the source account and folder alias, and the remote flag snapshot.

It reaches no mail server. That is structural rather than a rule someone keeps: the use case is constructed from a
summary reader, a content store, a renderer, a repair-request store, and the account catalog, and none of them can open
an IMAP session. Reading an email therefore cannot download it and cannot set the remote `\Seen` flag, whether or not
the local copy turns out to be usable.

The protocol adapter is not part of this. `EmailContentReader` is an application use case; the `get_email_content` MCP
tool that maps onto it is documented in [MCP tools](mcp-tools.md#get_email_content).

## The request contract

`GetEmailContentRequest` carries three values and is built through `Create`, which enforces the two refusals below.

| Field | Meaning | Absent means |
|---|---|---|
| `StoredEmailIds` | The emails to read, named by the identities a listing returned, in the order to read them | — |
| `IncludeSanitizedHtml` | Whether to also produce the sanitized HTML representation of each body | plain text only |
| `IncludeAttachmentDetails` | Whether to describe each attachment rather than only count them | counts only |

Both flags govern the whole call rather than one email each. A caller asking for markup or for attachment descriptions
wants them for what it is about to read, and a flag per identifier would make the argument list grow with the batch while
answering a question no caller asks per email.

The HTML representation is opt-in because it costs a sanitization pass over untrusted markup and because plain text is
what most callers want: a model reading mail is better served by the words than by the layout around them. The attachment
descriptions are opt-in for a different reason, recorded under [Attachments](#attachments-are-counted-always-and-described-on-request).

### Reading several emails, and what bounds it

A listing returns up to 100 summaries and a search up to 50 ranked matches, so a caller can always name more emails than
one read serves. Two bounds close that gap, and they answer different questions.

| Bound | Value | What it limits |
|---|---|---|
| `GetEmailContentRequest.MaximumEmails` | 10 | How many emails one call may name |
| `EmailContent:MaxCharactersPerRead` | 2 000 – 2 000 000, default 200 000 | How many body characters one call returns in total |

The count is a `const` rather than configuration, because it bounds a protocol call's shape rather than a deployment's
appetite; the volume is configured, because how much mail a response can usefully carry depends on what a deployment's
mail looks like. Neither replaces the other: without the count one call could name a mailbox, and without the budget ten
emails could each return `MaxBodyCharacters` in full.

Two refusals are decided before anything is read, and both refuse rather than repair:

- **More than ten emails, or none at all**, is `51005 EmailContentReadCountOutOfRange`. A truncated answer would leave a
  caller comparing what came back against its own list to find out which emails it did not receive.
- **The same email named twice** is `51006 EmailContentReadDuplicateEmail`. Serving it twice spends the read's character
  budget on content the caller already holds and displaces an email it has not read; collapsing it returns fewer entries
  than were named, which a caller reading results positionally cannot detect.

Both are the request's own invariant, enforced in `Create`, so an entrypoint added later cannot reach the use case with a
list nobody counted.

## What a result carries

`GetEmailContentResult` is the most sensitive projection MailFathom publishes. It is message content in full and inherits
every classification, retention, access, and erasure constraint of the mail it was read from. Nothing in it is logged.

It carries one `EmailContentReadOutcome` per named email, in the order they were named. That order is the contract twice
over: it is how a caller pairs an outcome with what it asked for, and it is the order the character budget was spent in.

Each outcome names the email it answers for and carries exactly one of two things.

| Field | Meaning |
|---|---|
| `StoredEmailId` | The email this outcome answers for, present whether or not there was content |
| `Content` | The email as a reader receives it, or absent when it could not be served |
| `Failure` | The stable code and message saying why there is no content, or absent when there is |

An email this deployment cannot serve therefore costs the caller that email and nothing else it asked about. That is why
the two per-email findings became results rather than exceptions: the reader that discovers them keeps going, and the
repository's failure rules reserve an exception for a fact that must travel past the code able to decide what it means.
The codes are the ones a single-email read already published, so a caller matching on `53002` or `55001` reads the same
fact whether it named one email or ten.

`ReadEmailContent` is the content half.

| Field | Meaning |
|---|---|
| `StoredEmailId`, `AccountId`, `FolderAlias` | Where the email is, in MailFathom's own names |
| `SizeOctets` | The size the mail server reported for the whole message |
| `Headers` | Subject, sent and received timestamps, every participant under its header role, and the thread identifiers |
| `Body` | The representations, or the reason there are none |
| `AttachmentSummary` | The counts for what the message carries besides its body, absent when nobody has counted them |
| `Attachments` | One entry per attachment when the request asked for them, re-derived from the stored raw MIME, with no bytes |
| `RemoteFlags` | The flags a server last showed, and when they were read |

### Headers come from the message, not from the row

The headers are read during the same parse that produces the body rather than from the columns a listing is served out
of. The row keeps only the comparison forms a filter needs, so display names, a `Bcc` a message carries for its own
recipient, and the `Sender` header exist nowhere else — a reader shown the listing's copy would be shown a narrower
message than the one that arrived.

Both header lists a sender controls the length of are bounded where the parse produces them: at most 256 participants
per header role, and at most 256 thread references, of which an over-long path keeps its root and its most recent
ancestors. The reference bound is applied while the header is read rather than to the list it produced, so a sender who
writes a hundred thousand ancestors costs the parse the memory of the ones it keeps. Each thread identifier is bounded
in itself as well, at the 998 octets RFC 5322 allows a header line: a longer one is refused rather than cut, because a
prefix of a message identifier is an identifier another message may legitimately carry. The persisted columns bound the
same values more narrowly, deliberately — one bound is about what a parse publishes to a reader and the other about
what a column stores.

### Attachments are counted always, and described on request

How many attachments an email carries is answered by every read. What each one is *called* is answered only when
`IncludeAttachmentDetails` asks: a file name is text the sender chose, it is frequently the most identifying string a
message carries, and a read that only wanted the body never asked for it. `list_emails` already publishes counts and
never names, so withholding the names here makes the two read models agree about the same data rather than disagree for
no reason a caller stated.

The default is never silently lossy. `AttachmentSummary` states how many attachments exist, their total decoded size,
the inline-resource count, and the encryption, signature, and TNEF flags whichever way the flag is set, so a caller can
tell that asking again would describe something rather than concluding the message carries nothing. `Attachments` is
absent rather than empty in that case, which keeps "you did not ask" and "there are none" apart.

Attachment bytes are unchanged by any of this: they were never returned under any setting and still are not.

### The descriptions are re-derived, never stored

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

"Left nothing behind" is measured on what the message wrote rather than on what a call returned. A read's character
budget can empty the representation of such a message for a reason belonging to the call rather than to the mail — the
emails named before it spent the budget — and reporting that as the encrypted state would tell a caller the message can
never be read locally when naming it alone returns the readable alternative in full. It stays readable, with
`readCharacterBudget` saying what cut it.

`NotStoredExceededSizeLimit` is not a defect and schedules no repair: synchronization recorded the occurrence and
deliberately stored no content for it, and asking for repair would ask a later run to store what the same limit refuses
again. Everything answerable is still answered — the headers from the stored row, the attachment counts from the summary
written when the occurrence was recorded — and only the per-attachment list is absent, because nothing local can derive
it.

Plain text is the default representation and is always present, empty in each of the states where nothing could be read.
A genuine `text/plain` part wins over every HTML alternative; HTML is read only when the message offered no plain-text
one. Unlike the text the lexical index covers, nothing is trimmed: quoted history and a signature block are part of the
message a person asked to read.

### Truncation is always explicit, and names the bound that cut

Each representation carries its own `EmailBodyRepresentation`: the text as returned, the number of characters its source
held, and which bound removed something. A caller therefore never has to guess whether it received a whole message, and a
message can exceed a bound in one representation without affecting the other.

`EmailBodyTruncation` names the bound rather than merely reporting that there was one, because the two lead a caller to
different actions.

| `Truncation` | Meaning | What a caller does about it |
|---|---|---|
| `None` | The text is the whole of what the message displayed in this representation | Nothing |
| `BodyCharacterLimit` | The per-representation bound cut it | Nothing; this message is longer than any single call returns |
| `ReadCharacterBudget` | The call's total budget cut it, because the emails named before it had already spent it | Name this email in a call of its own, or fewer emails at once |

`EmailContent:MaxBodyCharacters` sets the per-representation bound, defaults to 100,000, and is validated at startup
within 1,000–1,000,000. `EmailContent:MaxCharactersPerRead` sets the whole call's budget, defaults to 200,000, and is
validated within 2,000–2,000,000 and at no less than twice `MaxBodyCharacters`. Together they decide how much of a body a
caller is handed; what bounds this process is `MailSynchronization:MaxRawMimeBytes`, which no stored message is above.

```json
{
  "EmailContent": {
    "MaxBodyCharacters": 100000,
    "MaxCharactersPerRead": 200000
  }
}
```

The budget is at least twice the per-body bound because one email asking for both representations may return that bound
twice. A smaller budget would cut a one-email call by a limit that exists for calls naming several, and the truncation it
reported would send a caller to split a call it cannot split further. Startup is where that is caught, because the
alternative is a deployment discovering it one read at a time.

The budget is spent in the order the emails were named, and it counts both representations, because both are message
content the caller received. Within it, each representation of each email is still bounded by `MaxBodyCharacters`, and
the plain text is bounded before the markup so the representation every caller receives is never starved by the one it
opted into. An email reached after the budget has run out returns an empty text that says `ReadCharacterBudget` cut it,
rather than failing the call: the emails already read are what the caller keeps.

The section is one of its own rather than a value inside the synchronization settings, because it bounds a read rather
than a fetch: it applies whether or not synchronization is enabled, and changing it changes no stored data. The section
is bound strictly, so a misspelled key fails startup instead of being replaced by the default.

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

In every case the read records a durable repair request and reports that email as `55001 EmailContentUnavailable`. The
request is recorded first, so the finding survives whether or not the caller acts on what comes back; performing the
repair belongs to the synchronizer and is out of scope here. The request is idempotent per email — PostgreSQL resolves
the collision itself — so a caller retrying a damaged message leaves one row with an accurate count rather than a row per
attempt. The other emails of the same call are read and returned as usual.

The three fetch-again defects stay distinct from `Unreadable` because they say different things to whoever repairs them:
a second fetch fixes the first three and may well reproduce the fourth. Which one was found is named in the failure's
message.

An email the local copy holds no row for, or one belonging to an account this deployment no longer serves, is reported as
`53002 StoredEmailNotFound`. One failure covers both, for the reason `53001 MailAccountNotAccessible` covers both of its
cases: a caller that could tell them apart could learn which identifiers exist by asking.

The two codes are distinct on purpose. `StoredEmailNotFound` names an email that was never stored here;
`EmailContentUnavailable` names one that is stored and whose body cannot currently be served, and only the second is
worth retrying.

Both are per-email outcomes rather than raised failures, so neither ends a call. What does end a call is a refusal of the
request itself — a count outside the bound, a repeated identifier, or text that names no email at all — because none of
those leaves an email to report an outcome against.

## Where the pieces live

- `MailFathom.Application.Emails.GetEmailContent` — the use case, its request, its per-email outcome and failure, and the
  two refusals a request itself can earn.
- `MailFathom.Application.EmailContent` — the content store port, the renderer port, the repair-request port, the body
  representations with their bounds, and the headers.
- `MailFathom.Infrastructure.Mail.Mime` — `MimeKitEmailContentRenderer` and `EmailHtmlSanitizer`, which own the MIME parser
  and the HTML sanitizer respectively. Neither type escapes that namespace.
- `MailFathom.Infrastructure.Persistence` — `StoredEmailSummaryReader`, the content store's integrity-bearing read, and
  `EmailContentRepairRequestStore`.

`MimeMessageHeaderReader` is shared with the extraction that fills the lexical index, so a message is indexed under
exactly the headers it is displayed under.
