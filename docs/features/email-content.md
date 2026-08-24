# Email content

<!-- describes: backend/src/Application/EmailContent/**, backend/src/Application/Emails/GetEmailContent/**, backend/src/Application/Emails/DownloadAttachment/**, backend/src/Infrastructure/Mail/Mime/**, backend/src/Infrastructure/Mail/Attachments/**, backend/src/Infrastructure/Persistence/Emails/**, backend/src/Infrastructure/ObjectStorage/**, backend/src/Host/Api/EmailAttachmentDownloadEndpoint.cs, backend/src/Host/Configuration/Persistence/AttachmentDownloadOptions.cs -->

MailFathom serves the content of the emails one call names, from its local copy. `EmailContentReader` is the second read
use case: it takes the stable local identifiers a listing returned and answers for each of them with normalized headers,
the body as plain text, optionally a sanitized HTML representation, attachment counts, every attachment described and
optionally carrying a short-lived link that fetches it, the source account and folder alias, and the remote flag
snapshot.

It reaches no mail server. That is structural rather than a rule someone keeps: the use case is constructed from a
summary reader, a content store, a renderer, a repair-request store, the account catalog, and the link issuer, and none
of them can open an IMAP session. Reading an email therefore cannot download it and cannot set the remote `\Seen` flag, whether or not
the local copy turns out to be usable.

The protocol adapter is not part of this. `EmailContentReader` is an application use case; the `get_email_content` MCP
tool that maps onto it is documented in [MCP tools](mcp-tools.md#get_email_content).

## The request contract

`GetEmailContentRequest` carries four values and is built through `Create`, `CreateForThread`, or `CreateForSelection`,
which enforce the refusals below.

| Field | Meaning | Absent means |
|---|---|---|
| `StoredEmailIds` | The emails to read, named by the identities a listing returned, in the order to read them | a conversation was named instead |
| `ThreadId` | The conversation whose messages to read, in the conversation's own order | the emails were named directly |
| `IncludeSanitizedHtml` | Whether to also produce the sanitized HTML representation of each body | plain text only |
| `IncludeAttachmentDownloadLinks` | Whether to mint a link for fetching each attachment, rather than only describe it | descriptions only |

The first two are alternatives and exactly one of them is given. `CreateForSelection` is what a boundary offering both
builds through, and it refuses a request carrying both or neither with `51007 EmailContentReadSelectionInvalid` rather
than resolving it by precedence: honouring the list would ignore a conversation somebody wanted, and honouring the
conversation would return messages nobody named. A conversation is counted where it resolves rather than where it is
named, because how many messages it holds is what reading it answers — the same ten bound applies to the resolved order,
and the identities beyond it come back in `UnreadThreadEmails` so a second call asks for them directly. Neither argument
is resolved until that refusal has been decided, so a call carrying a list beside an empty one or a misspelled
conversation identifier is told which of the two to drop rather than that the argument it will not be read by is too
short or malformed.

Both flags govern the whole call rather than one email each. A caller asking for markup or for the attached files wants
them for what it is about to read, and a flag per identifier would make the argument list grow with the batch while
answering a question no caller asks per email.

The HTML representation is opt-in because it costs a sanitization pass over untrusted markup and because plain text is
what most callers want: a model reading mail is better served by the words than by the layout around them. The links
are opt-in for a different reason, recorded under
[Attachments](#attachments-are-described-always-and-fetched-through-a-link).

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

Attachments are subject to neither, and have no byte bound of their own, because no response carries their octets. What
a caller receives for a file is a link, whose size is the same few hundred characters whatever the file weighs — so a
message carrying a video costs a response exactly what one carrying a note does.

Two refusals are decided before anything is read, and both refuse rather than repair:

- **More than ten emails, or none at all**, is `51005 EmailContentReadCountOutOfRange`. A truncated answer would leave a
  caller comparing what came back against its own list to find out which emails it did not receive.
- **The same email named twice** is `51006 EmailContentReadDuplicateEmail`. Serving it twice spends the read's character
  budget on content the caller already holds and displaces an email it has not read; collapsing it returns fewer entries
  than were named, which a caller reading results positionally cannot detect.

Both are the request's own invariant, enforced in `Create`, so an entrypoint added later cannot reach the use case with a
list nobody counted. A conversation is not counted there, because nothing is resolved yet when the request is built; the
same bound is applied to the order it resolves to, and what falls outside it is named rather than dropped.

## What a result carries

`GetEmailContentResult` is the most sensitive projection MailFathom publishes. It is message content in full and inherits
every classification, retention, access, and erasure constraint of the mail it was read from. Nothing in it is logged.

It carries one `EmailContentReadOutcome` per named email, in the order they were named — or, for a request naming a
conversation, one per message it served in the conversation's own order, with `UnreadThreadEmails` naming the
identities the bound left out. That order is the contract twice over: it is how a caller pairs an outcome with what it
asked for, and it is the order the character budget was spent in.

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
| `Attachments` | One entry per attachment, re-derived from the stored raw MIME, each carrying a link to fetch it or the reason it carries none |
| `RemoteFlags` | The flags a server last showed, and when they were read |
| `Thread` | The conversation this email belongs to, with the other messages named rather than reproduced, or absent when nothing has assembled one |
| `SenderVerification` | What was established about the author the message displays, and what this deployment made of them |
| `SenderAuthenticationEvidence` | What that conclusion was reached from: the authenticated domain, the displayed author's domain, the check that established the first, the DMARC result, and which reading produced all of it |

### The sender verdict is read from the row, and only here is its evidence published

Both values come from the summary the read already loaded rather than from the parse, because both are conclusions
reached when the message was stored. A read evaluates nothing: it resolves no DNS, verifies no signature, and does not
re-read the `Authentication-Results` header the verdict came from. That is worth stating twice now that extraction may
do all three — verifying a message's own DKIM signatures where its server wrote no verdict is a step of storing a
message, never of answering a call, so no read this page describes puts anything on the wire. The evidence names which
of the two readings produced the verdict, so a caller weighing one never has to infer it from what is missing. That
holds for a message whose raw MIME was never stored too — the body says why there is none, and the verdict beside it is the same one a listing publishes.

The verdict pair is what every read tool publishes. The evidence is what only this one does, because it is how a reader
judges a verdict rather than what a reader acts on, and a listing exists to let somebody recognize a message rather than
to weigh one they have already found. The authenticated domain and the displayed author's domain are read beside the
verdict rather than against each other: the first is whichever identity authenticated the transport, so the two differ
on ordinary mail a provider relayed and signed as itself, and `authorAuthentication` is what says the displayed author
was not established. Each value states its own absence, since a message nothing authenticated names no authenticated
domain and one displaying no usable `From` mailbox names no displayed one. [Sender authentication](sender-authentication.md#what-the-read-tools-publish) holds what
each value means and what it deliberately does not claim.

The machine-authorship reading is published on the same terms and split the same way: the band and the number reach
every read tool, and only this one adds the signals the text carried and the weighting they were judged under. It is a
second, independent answer rather than a refinement of the verdict above — that one is about who sent the message and
this one is about how its text was written — and it is informational rather than a warning about either. It too is read
from the row: a content read weighs no text and re-reads no body to produce it. [Machine
authorship](machine-authorship.md#what-the-read-tools-publish) holds what each signal is and what the reading
deliberately does not claim.

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

### Attachments are described always, and fetched through a link

Every read answers what a message carries: how many attachments, what each is called, what it declares itself to be, and
how large it is. **No response carries a file's octets, in any encoding and at any size.** What
`IncludeAttachmentDownloadLinks` adds is a short-lived signed URL per attachment, which the caller fetches over HTTP on
its own.

The line falls there rather than around the descriptions because of what a caller does with them. Deciding whether a
file is worth fetching *is* reading its name, its type, and its size; a read that answered with a count alone would
leave a caller nothing to decide on and force a second call to learn what the first was about. A link is what costs
something — it is a bearer capability over the message's most sensitive part — so it is the part that is asked for.

`list_emails` still counts and never names, and that disagreement between the two read models is deliberate rather than
an oversight. A listing is a browse over a mailbox, where a file name would be sender-chosen identifying text about mail
the caller has not opened; a content read has already returned the body in full, so a file name adds nothing about that
message a caller does not already hold.

A read that asks for no link mints none, and one whose message carries no attachment never reaches the issuer at all. Both
matter because minting resolves the deployment's key material: an ordinary read of ordinary mail touches the key ring
zero times. The entry says which case it was.

| `Availability` | Meaning | What a caller does about it |
|---|---|---|
| `NotRequested` | The call asked for no link, so the file was described and no capability was minted | Ask again with `IncludeAttachmentDownloadLinks` |
| `Issued` | A link is present and fetches the whole file until it expires | Fetch it |
| `Unavailable` | This deployment issues no attachment links at all | Nothing; only its operator can change that |

Inline resources and cryptographic parts carry no link here for the same reason they carry no description — they never
enter the list at all.

### What a download link is, and what bounds it

A link is `https://<declared address>/attachments/<capability>`, where the capability is one opaque value carrying a
format marker, the key it was signed with, the email, the attachment's position in the message's walk order, the expiry
instant, and 128 bits of cryptographically secure randomness, followed by an HMAC-SHA256 tag over all of it. The tag is
compared in constant time; the randomness is what makes two links for one file unrelated values rather than a function
of what they name.

| Setting | Value | What it decides |
|---|---|---|
| `Deployment:PublicBaseAddress` | absolute, no default | Where a link points, and whether any is issued at all |
| `EmailContent:AttachmentDownloads:LinkLifetime` | 1 – 30 minutes, default 10 minutes | How long a minted link stays redeemable |

**The address is declared, never derived from the request.** A URL composed from a `Host` header would let whoever
called the tool decide where the link it receives points. It sits under `Deployment` rather than beside the lifetime
because it is a fact about the installation rather than about attachments: anything that later hands a caller an
absolute address asks the same question, and an operator should answer it once. A deployment that declares none serves
every other part of a read and issues no link, which the attachment reports as `Unavailable`; so does one that
configures no [data-encryption key ring](../operations/secret-provisioning.md#the-data-encryption-key), because the
signing key is derived from that ring rather than from a secret of its own.

**The lifetime is the whole of a link's revocation model**, which is why both ends of its range belong to the product
rather than to the operator. Below a minute nothing could reliably be redeemed — the URL still has to cross a protocol
response, a client, and often a separate process before anything fetches it. Above half an hour a URL copied into a
proxy log, a browser history, or a chat transcript stops being a capability and becomes a credential this deployment
cannot revoke. A configured value outside the range fails startup rather than being clamped, and expiry is decided
against the injected `TimeProvider`.

A link is **redeemable repeatedly until it expires**. Single use would need durable, replicated, pruned server-side
state, and it breaks the ordinary behaviour of the things that fetch files: a range retry, a redirect, or a proxy
prefetch would each spend it. The window is the control, not the count.

### Redeeming a link

`GET /attachments/<capability>` is served on the MCP endpoint's own listeners and **requires no credential**. The
signature is the whole of the access control, deliberately: a link exists to be handed to whatever actually fetches
files — a browser, a downloader, a client's HTTP stack — and none of those can attach an MCP credential, so requiring
one would make the capability unusable by its only callers. What stands beside the signature is the ten-minute window,
the scope of one attachment of one email, the MCP surface's own transport, its per-caller rate limit and its
process-wide concurrency limit — the route belongs to that surface for exactly this reason — and the resolution below.

Redemption reads the attachment through the same store, the same integrity check, and the same MIME walk
`get_email_content` reads it through, then streams that one part's decoded octets to the response. Reading afresh is
what makes a link unable to outlive the deletion of its own message: an attachment is mail content in full and inherits
every retention, access, and erasure constraint of the message it belongs to.

**Every refusal is one refusal.** An expired capability, a forged one, one naming an email this deployment no longer
serves, one whose local copy is damaged, and one naming a position the message does not carry are all `404` with the
same body — telling them apart would let whoever holds a capability learn what became of mail they can no longer read. A
damaged or missing local copy records a repair request first, exactly as a read of the same message would, because the
finding is about the stored copy rather than about who asked for it.

The response states the attachment's own media type and file name, both of which are text a sender wrote: the media type
is parsed before it is echoed and falls back to `application/octet-stream` when it is not a media type, and the file name
travels through the header type that applies RFC 5987 encoding. It is always served as `Content-Disposition: attachment`
with `X-Content-Type-Options: nosniff`, because these are sender-controlled bytes on the address the operator publishes
MailFathom at, and with `Cache-Control: no-store`, because an intermediary that stored the response would keep serving
the file for that URL after the capability expired — which would take the expiry out of the revocation model it is the
whole of. Neither the URL, the capability, the file name, nor any octet reaches a log.

### The descriptions are re-derived, never stored

The per-attachment list — the normalized file name, the media type, and the decoded size — is produced by the parse
this read already performs, following the classification rule
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

Content leaves the process through that one list and nowhere else. Nothing else reachable from the published result can
hold bytes at all, and the single property that carries a file is named in a unit test that fails when a second one
appears — so a payload cannot be added beside it and quietly inherit none of the bounds above.

## The body, and the three ways there is none

`EmailContentBody` states which case a reader is in.

| `Availability` | Meaning |
|---|---|
| `Readable` | The body was read; an empty one means the message displayed nothing |
| `EncryptedNotReadableLocally` | The body arrived inside a cryptographic envelope and nothing here can read it |
| `NotStoredExceededSizeLimit` | The raw MIME exceeded `MailSynchronization:MaxRawMimeBytes`, so it was never stored |
| `NotStoredAwaitingStorageHeadroom` | Local content storage was at `MailSynchronization:MaxStoredContentBytes`, or the message's owner was at `MailSynchronization:MaxStoredContentBytesPerOwner`, when it arrived, so its content is not stored yet |

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

Neither of the two unstored states is a defect and neither schedules a repair: synchronization recorded the occurrence
and deliberately stored no content for it, so asking for repair would ask a later run to store what it already decided
not to. Everything answerable is still answered — the headers from the stored row — and everything about the message's
parts is absent, because nothing local can derive it: the attachment list is empty and the counts beside it are `null`
rather than zero, for the reason [Attachments](#attachments-are-described-always-and-fetched-through-a-link) gives. The
empty list is about the parts never having been read rather than about the message carrying no files, and the absent
counts are what say which of the two it is.

What separates them is whether asking again is worth anything. `NotStoredExceededSizeLimit` is permanent: the same limit
refuses the same message on every run. `NotStoredAwaitingStorageHeadroom` is a queue — the message was discovered while
content storage stood at its ceiling, and the [refill pass](imap-synchronization.md#the-storage-ceiling-degrades-ingestion-rather-than-failing-it)
of a later run fetches it as soon as there is room, after which this same read returns the body. A caller that collapses
the two would either give up on mail that is arriving or keep asking about mail that never will.

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
| `SensitiveContentScanCeiling` | A switched-on scanner analyzed as much of the body as it may, and the rest is withheld rather than served unscanned | Nothing a call can do; only raising `SensitiveContent:MaximumAnalyzedCharacters` returns more |

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

### A scanned deployment redacts what it returns

Where [sensitive-content scanning](sensitive-content-scanning.md#reading-a-message-is-scanned-in-flight) is switched on,
what the message's author wrote is scanned on every read and returned with each detection replaced by
`[redacted:<category>]`: both body representations, the subject, and each participant's display name. The addresses
beside those names, the identifiers, the sizes, the flags, the two domains the sender verdict's evidence publishes, and
every attachment's file name are left as they are, on the line that page draws between a routing identity and free text.

The display names of the first 40 named participants of a message are scanned, and past that the address is published
with no display name at all. A scan is a round trip where the personal-data analyzer runs in a container of its own, and
a parse publishes up to 256 addresses per header role, so a list expansion would otherwise turn one read into thousands
of sequential requests holding the scan permits every listing and answering run shares. Losing a name past the fortieth
participant is the cheaper side of that bound, and a withheld name is never a name nothing scanned.

The scan is what the read hands over rather than what it stored: nothing rewrites the raw MIME or the extracted text,
and no span, offset, or finding location for a stored message is written anywhere. That is why it is paid per call.

Three consequences reach this contract. The redaction runs over the text this read would have returned, so every
character a caller receives is one a scanner saw, and the placeholders can carry a representation slightly past the
bound that cut it — the same property re-serialized markup already has, and the reason `Truncation` is stated rather
than derived from the two lengths. A body longer than the scan's own ceiling comes back cut at it and says
`SensitiveContentScanCeiling`, over whichever bound had cut it already, because that is where the returned text now
ends. And a detector that cannot answer fails the call rather than serving the message unscanned: the server log records
`81001` naming the scanner while the caller receives `54001`, under the category rule
[MCP tools § error reporting](mcp-tools.md#error-reporting) states.

**A ceiling cut lands on a UTF-16 boundary rather than a text-element one.** It never hands back an unpaired surrogate —
the cut steps back off a high surrogate before it is taken — but a combining sequence, a ZWJ emoji, or a
regional-indicator pair standing exactly at the ceiling is split, so a body can end in a bare base letter or half a flag.
The text-element guarantee above therefore holds for the two call bounds alone, which are the ones applied to text the
message wrote rather than to text a scan had already stopped reading.

**That ceiling is also the one place the balanced-markup guarantee above stops applying.** It cuts what the sanitizer had
already serialized rather than the source it was serialized from, so a `sanitizedHtml` representation reporting
`SensitiveContentScanCeiling` can end inside an element — the fragment the source-first cut exists to avoid. The
alternative is worse in the way this whole feature is written against: sanitizing again would hand back markup the scan
never analyzed. A caller that renders the markup treats this truncation as it would a broken document; the plain text
beside it is unaffected, since nothing re-serializes it.

With both switches off none of this happens: no detector is constructed, nothing is scanned, and the read is
byte-identical to the one the same message produced before the feature existed.

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

An email the local copy holds no row for, one belonging to an account this deployment no longer serves, and one stored in
a folder mapped with `VisibleToTools: false` or in a folder no mapping names at all are all reported as
`53002 StoredEmailNotFound`. One failure covers them, for the reason `53001 MailAccountNotAccessible` covers both of its
cases: a caller that could tell them apart could learn which identifiers exist by asking. An attachment link minted
before the folder was withheld — or before its mapping was removed — stops serving the same way, because the question is
asked where the download is served;
[folders withheld from tools](mailbox-queries.md#folders-withheld-from-tools) states both cases and what they withhold.

The two codes are distinct on purpose. `StoredEmailNotFound` names an email that was never stored here;
`EmailContentUnavailable` names one that is stored and whose body cannot currently be served, and only the second is
worth retrying.

Both are per-email outcomes rather than raised failures, so neither ends a call. What does end a call is a refusal of the
request itself — a count outside the bound, a repeated identifier, or text that names no email at all — because none of
those leaves an email to report an outcome against.

## Where a payload is kept

Raw MIME lives in one of two places, and which one is a deployment's decision rather than a message's. A deployment that
configures nothing keeps every payload in the PostgreSQL table beside the metadata; one that configures
`ContentStorage:ObjectStorage` writes new payloads to that S3-compatible endpoint instead.
[Configuration](../operations/configuration-runtime.md) holds the keys.

**The setting decides only where the next write goes.** Every content row states which store holds its own payload, so
turning the object backend on moves nothing and turning it off re-encodes nothing: mail written to the database stays
readable from the database, and mail written to an endpoint stays readable from that endpoint. A read resolves the
backend from the row it is reading rather than from the setting, which is what makes both true at once.

What a reader of the schema sees is one shape per row across all four tables that hold raw MIME —
`email_message_contents`, `outgoing_email_contents`, `mail_draft_contents`, and `recurring_send_drafts`:

- **`Backend`** names the store, `Database` or `ObjectStorage`. Its column default names the database, which is what
  makes every row written before the discriminator existed read as the thing it is, and what keeps an ordinary
  database-backed insert from having to state anything.
- **`ObjectLocator`** carries the whole key an object was written under, and is empty for a database-backed row. Nothing
  ever recomputes it — see below.
- **The payload column** carries the bytes for a database-backed row and is empty for an object-backed one. A payload is
  never in both places: a second copy would be mail nobody agreed to keep.
- **The byte length and the SHA-256 digest are on the row either way**, so the integrity check a read performs is the
  same one under both backends. Under the object backend the digest is also what the endpoint was asked to verify the
  upload against, so a row carrying it describes an object the endpoint agreed it received intact rather than one this
  process merely believes it sent.

A check constraint pairs those three, so none of the three ways of getting it wrong can be written at all: a row that
names the object backend and carries no locator, one that names the database and carries no bytes, and one that names
the object backend and carries bytes anyway — which would be the second copy of a message that the "never in both
places" rule above forbids.

### The object write happens before the transaction, and every placement mints its own key

Writing to an endpoint is a remote call, and
[ADR 0001](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md)
does not allow one inside a database transaction — a network that stalls would otherwise hold a transaction open for as
long as it stalled. So a write is two acts rather than one. The payload is **placed** first, before the caller opens its
unit of work, and what comes back says where it went; the caller then passes that along to the write that stages the
row, inside its transaction, alongside everything else that has to commit with it.

Two consequences are worth reading off that, because both are observable:

- **Every placement mints a fresh key.** Nothing derives a key from the identity of the row that will point at it,
  because at the moment the key is minted that row does not exist yet. A draft revised three times therefore leaves
  three objects and one row pointing at the newest, and re-synchronizing a message already stored points its row at a
  newer object rather than overwriting the one under it.
- **A replay writes no second object.** The persistence layer replays a whole unit of work when a concurrent writer wins
  the row, and the placement happened before that unit of work began — so every attempt stages the same locator over the
  same object, and the endpoint sees one write however many attempts the commit took.

**An object the row stopped pointing at is left where it is.** Nothing here deletes one, which is a deliberate omission
rather than an oversight: what removes content is erasure, retention, and the data-subject workflows those belong to,
and a store that quietly dropped a payload the moment a row moved would decide that question in the wrong place.

### Losing the endpoint is a readiness condition

A deployment that stored mail through an endpoint and then lost the configuration keeps those rows intact and
unreadable, and nothing about a listing or a timeline says so — both answer from the database. The readiness probe is
what says so instead, reporting unhealthy while such rows exist and no endpoint is named, and becoming ready by itself
once one is. [Health endpoints](../operations/health-endpoints.md) records both halves of that: the check that asks the
configured bucket whether it answers, and the check that asks the stored content whether a bucket is still needed.

## Where the pieces live

- `MailFathom.Application.Emails.GetEmailContent` — the use case, its request, its per-email outcome and failure, and the
  two refusals a request itself can earn.
- `MailFathom.Application.EmailContent.Storage` — the content store port and what a read of it returns, remote and
  stored. The port covers mail leaving as well as mail arriving: an outgoing message is stored against the record of the
  send it was composed for rather than against a local occurrence, and is written once so a retry transmits the bytes an
  earlier attempt may already have begun transmitting. A repeated send's draft is a third kind, written once beside the
  declaration each occasion is composed from. A draft's message is the fourth and the one exception to writing once: it
  is stored against the draft and **rewritten** with each revision, because what it holds is a message somebody is
  still editing rather than bytes a later attempt has to reproduce. One port is what keeps raw MIME behind one seam,
  which is what let the object backend arrive as one adapter's concern rather than four — no use case above it knows
  which store answered. [Mail delivery](mail-delivery.md) holds why the send's write may never be repeated and why the
  draft's must be, and [where a payload is kept](#where-a-payload-is-kept) holds what the two backends are.
- `MailFathom.Application.EmailContent.Rendering` — the renderer port, the body representations with their bounds, and
  the headers.
- `MailFathom.Application.EmailContent.Repair` — the repair-request port, the request it carries, and the defect that
  raises one.
- `MailFathom.Infrastructure.Mail.Mime` — `MimeKitEmailContentRenderer` and `EmailHtmlSanitizer`, which own the MIME parser
  and the HTML sanitizer respectively. Neither type escapes that namespace.
- `MailFathom.Infrastructure.Persistence.Emails` — `StoredEmailSummaryReader`, the content store's integrity-bearing read,
  and `EmailContentRepairRequestStore`.

`MimeMessageHeaderReader` is shared with the extraction that fills the lexical index, so a message is indexed under
exactly the headers it is displayed under.
