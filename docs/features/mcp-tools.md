# MCP tools

<!-- describes: src/Mcp/** -->

MailFathom publishes its read side as Model Context Protocol tools over the Streamable HTTP transport. This page records the
conventions every tool follows, the contract of the tools that exist, and what a client reads when a call fails.

The endpoint is disabled by default, and enabling it requires stating whether a client presents an API key or nothing at all.
`docs/operations/mcp-endpoint.md` records that posture and how to enable the endpoint; this page describes the surface it
serves.

## Implemented behavior

`ModelContextProtocol.AspNetCore` 2.0.0 hosts the server. The `Mcp` project owns the tool descriptors, the conversion of
protocol arguments into the domain identities a use case is expressed in, and the mapping from a use case's result back
onto the published contract. It holds no query, no persistence, and no mail-protocol code: `list_emails` calls the
`MailboxTimelineReader` use case and nothing else, `get_email_content` calls the `EmailContentReader` use case and
nothing else, and `search_emails` calls the `MailboxSearchReader` use case and nothing else.

It holds no AI code either, and cannot. The project references `Domain` and `Application` and no other MailFathom assembly,
which `Mcp.UnitTests` asserts against the compiled reference list rather than against a convention — so no tool on this
surface can embed a query, rewrite it, or hand anything to a chat model, and a package that would make one able to has
to be added and reviewed before that changes.

The division is deliberate and is what keeps a second entrypoint from bypassing anything. Every filter bound, the
page-size range, the account authorization, and the cursor's authenticity belong to the use case, so this boundary
re-states no limit of its own; [Mailbox queries](mailbox-queries.md) documents them once, where they are enforced. What
the boundary owns is the one thing a use case cannot: turning a caller's text into an account identifier or a folder
alias, and refusing text that names neither.

Three properties hold for every tool and are proven by test rather than asserted here:

- A call reads the local mailbox copy only. Nothing in a tool request reaches a mail server, so a request cannot wait on
  IMAP and cannot set the remote `\Seen` flag.
- No error and no log line carries a filter value, a mailbox address, a subject, body text, raw MIME, an exception type,
  a stack trace, or an internal identifier. What a boundary withholds is not lost: the detail is logged on the server,
  correlated by the trace the request already carries.
- No result carries raw MIME or attachment bytes. Message content itself is a result only where the tool exists to
  return it: `get_email_content` returns bounded bodies, `search_emails` returns bounded extracts of one, and
  `list_emails` returns summaries and no body text at all.
- Every tool bounds how much mail one call can draw out of a mailbox, in the count of items and in their volume alike:
  `list_emails` pages at 100 summaries, `search_emails` windows at 50 ranked matches, and `get_email_content` reads at
  most 10 emails under a shared character budget. A caller can never raise any of them.

## Descriptor conventions

Every tool is declared with the same deliberate metadata, because a client decides whether a tool is safe to call before
it calls anything:

| Element | Convention |
|---|---|
| `name` | Snake case, as the MCP tool ecosystem spells tool names — `list_emails`, `get_email_content`, `search_emails` |
| `title` | A human-readable label for display — `List emails`, `Get email content`, `Search emails` |
| `description` | States what the tool reads, that it reads the local copy only, that it changes nothing, and what it bounds |
| `inputSchema` | Every argument is a top-level property carrying its own description, unit, and absence meaning |
| `outputSchema` | Generated from the result type, whose properties carry descriptions of their own |
| `readOnlyHint` | `true` |
| `destructiveHint` | `false` |
| `idempotentHint` | `true` |
| `openWorldHint` | `false` — every tool is confined to MailFathom-controlled local state |

The four annotations are contract metadata rather than documentation, so `Mcp.UnitTests` asserts the advertised
`tools/list` output: the name, the title, the description, every input property, the descriptions on them, the output
schema, and each annotation. A descriptor that drifts fails the build.

Enumerations travel as their names, camel-cased — `newestFirst`, `exceededSizeLimit`, `lexical` — never as ordinals. Each one is a
type this boundary owns rather than the domain enumeration describing the same states, because the member names *are* the
published wire values: sharing the domain's type would make a rename inside the domain a silent change to the protocol.
Timestamps are ISO 8601 and property names are camel-cased, both of which follow from the single `JsonSerializerOptions`
every tool registration is given, so the schema that was advertised and the payload that is serialized cannot diverge.

These stay plain C# enumerations rather than the closed enumerations the repository requires of a value that publishes an
identity, and the reason is what that rule is about. A closed enumeration exists where the identity and the member name
are different things — a SASL mechanism spelled `PLAIN`, a failure numbered `51002` — so the type has to carry the
identity because the name cannot produce it. Here the name *is* the identity, converted by one shared policy, and a
`readonly record struct` would add a second serialization path without adding a fact. What the rule protects against is
a rename changing the contract in silence, and that is closed by assertion instead: `Mcp.UnitTests` pins the advertised
spellings of every enumeration this surface publishes, on the input and the output side alike, so a rename fails the
build. An enumeration added here without that assertion is the actual defect the rule is warning about.

Sizes are published in bytes and named for it — `sizeBytes`, `totalSizeBytes` — even though the application and the
stored schema call the same quantity octets. The two words mean one thing here, and the protocol uses the one a client
reads without pausing.

## Error reporting

Expected failures are reported as a tool result with `isError` set, whose text is the one shape every tool uses:

```text
MailFathom error 53001: Mail account 'shared-billing' is not accessible.
```

The five-digit code is the machine-readable part and is stable: it is what a runbook, an alert, or a log search matches
on. The sentence after it is the one the use case wrote, republished rather than restated here, so a client and an
operator read the same wording and there is no second text to drift. It names the filter and, where there is one, its
limit — never the value that was refused, because a filter value is itself sensitive and a boundary that reflects input
back has started returning content. An account identifier is the exception the rule allows: it is MailFathom's own
configured name for an account and carries nothing the caller did not already write.

| Code | Meaning | Typical cause |
|---|---|---|
| `51001` | A page size outside the range the query serves | A page size of 0 or above 100, refused rather than clamped |
| `51002` | A filter carries a value, a count, or a length the query does not accept | An unusable address, a subject fragment over 256 characters or carrying a control character, a received range that ends before it starts, more than 64 accounts or folder aliases, an account identifier or folder alias that is blank, over 256 characters, or carrying a control character, a search query that is blank, over 512 characters, or carrying a control character |
| `51003` | A search asked for more ranked results than a search serves | A `resultLimit` of 0 or above 50, refused rather than clamped |
| `51004` | The call named an email with text that is no identifier this system issues | A `storedEmailIds` element that is blank, not a UUID, or the all-zero UUID, refused before anything is looked up |
| `51005` | A content read named no emails, or more than one call serves | A `storedEmailIds` list that is empty or holds more than 10 entries, refused rather than truncated |
| `51006` | A content read named the same email more than once | A `storedEmailIds` list carrying one identifier twice, in any spelling, refused rather than served twice or collapsed |
| `52001` | A continuation cursor is not one this system issued | A truncated, hand-written, or foreign cursor |
| `52002` | A continuation cursor was issued for different filters | A cursor reused after a filter or the reading direction changed |
| `53001` | The call named a mail account this deployment does not serve | An account identifier nobody configured, or one belonging to someone else — the two are deliberately one answer |
| `53002` | The call named an email the local mailbox copy holds no row for | An email never synchronized, one expunged and collected, or one of an account this deployment stopped serving — deliberately one answer |
| `54001` | The call failed for a reason the boundary deliberately does not describe | Anything undiagnosed; the detail is in the server log |
| `55001` | The email exists locally and its stored content is missing, damaged, or unreadable | A local copy being repaired; the call is worth repeating once repair has run |

Codes `51001` through `53002` and `55001` are the use cases' own, allocated in the MCP-boundary category because that is
where they surface, and every one of them is written for a caller to read. That is the whole rule the boundary applies: a
failure whose code belongs to that category is published as it stands, and a failure from any other category — a schema
mismatch, an IMAP authentication refusal, a concurrency conflict — describes MailFathom's internals to whoever asked and
collapses into `54001`. Stating the rule as a category rather than as a list of exception types is what stops a failure
added later from reaching a client because nobody remembered to add it to a list.

Two of those codes also appear inside a *successful* result. `get_email_content` answers per email, so `53002` and
`55001` reach a caller as a `failure` on the entry they belong to rather than as a failed call — one email this
deployment cannot serve must not discard the content of the nine beside it. The code and the message are the same ones a
failed call would have carried, so a client matches on one set of numbers either way, and `isError` continues to mean
that the *request* was refused.

`54001` is therefore the only answer an unexpected failure ever produces, and a failure the MCP SDK itself raises —
while binding an argument to the advertised schema, for instance — collapses into it too. Those messages are the SDK's,
not written to the rule above, and may name a rejected value or a CLR type; what a client loses is a description of a
request it can already compare against the published input schema.

One call-tool filter wraps the whole surface: it records the tool name, the outcome, the error code where there is one,
and the duration of every call, and it logs any undiagnosed exception in full on the server, correlated by the trace the
request already carries. Cancellation and protocol-level failures are recorded and then rethrown rather than converted,
because a cancelled call is the caller's own doing and a JSON-RPC error has to be reported as one.

The tool name a call arrived with is recorded only when it is spelled the way a MailFathom tool name is; anything else is
recorded as one fixed placeholder. On an unknown tool that name is unvalidated caller input on its way into a retained
log, and a log is not a place to let a caller write.

## `list_emails`

Returns a bounded page of summaries from the local mailbox copy, newest received first by default.

### Arguments

Every argument is optional.

| Argument | Type | Meaning |
|---|---|---|
| `accountIds` | `string[]` | Accounts to read. Omitted reads every account this deployment serves; an account it does not serve is refused with `53001` |
| `folderAliases` | `string[]` | MailFathom folder aliases such as `INBOX`. Omitted reads every folder of the accounts in scope. Case is normalized, so a repeated spelling names one folder |
| `senderAddress` | `string` | The whole address the sender must carry, in any case — not a fragment |
| `recipientAddress` | `string` | The whole address a `To` or `Cc` recipient must carry. `Reply-To` is stored and filterable through the use case but not searched here |
| `subjectFragment` | `string` | Text the subject must contain, case-insensitively, up to 256 characters. Wildcards a caller writes match themselves |
| `receivedOnOrAfter` | `date-time` | Inclusive start of the received range |
| `receivedBefore` | `date-time` | Exclusive end, so consecutive ranges built from one instant neither overlap nor leave a gap |
| `isRemotelySeen` | `boolean` | The remote seen state to require. Listing never changes it |
| `hasAttachments` | `boolean` | Whether to match only emails with attachments or only those without |
| `direction` | `newestFirst` \| `oldestFirst` | Which end of the timeline to read from |
| `pageSize` | `integer` | 1 to 100. Omitted takes the default of 25; a value outside the range is refused rather than clamped |
| `cursor` | `string` | The `nextCursor` of a previous call, reused with the same filters and direction |

An unbounded date range is deliberately legal; only an unbounded page is not. The page size stops at 100, and the scope
stops at 64 accounts and 64 folder aliases counted while the caller's list is read, so a request that repeats one
identifier a million times is refused after the value that crosses the limit rather than after the list has been
materialized. Every one of those bounds lives in the use case rather than here, which is what makes them hold for an
entrypoint added later; naming one served account repeatedly is legal and is read once.

Both identifier lists are converted to domain values at this boundary, and their counts are checked against the query's
own limits *before* any element is converted — a ceiling applied after the trimming and upper-casing it exists to prevent
has already run over a million-element array is not a ceiling. Text that names no identifier this system issues is then
refused with `51002`, and the refusal never repeats the value.

The boundary applies one rule to both lists: at most 256 characters and no control characters. The domain types differ on
that point, since a folder alias refuses control characters and an account identifier does not, and the stricter rule is
applied to both because an identifier travels even when it matches nothing — an account this deployment does not serve is
named back in the `53001` refusal a client reads, so an unbounded string carrying newlines would otherwise be a way to
write arbitrary text into that contract and into the log beside it.

### Result

`emails` carries the page, `nextCursor` reads the following one and is absent on the last page, and `folderFreshness`
states how current the local copy of each covered folder is.

Each summary carries the stable local identifier a content read is performed by, the account and folder alias, the message
identifier, the subject, the sender address and display name, the `To` addresses, the sent and received timestamps, the
size in bytes, the attachment summary, the remote flags with the time they were observed, and whether raw content is
available locally. It is the use case's projection published as it stands, not narrowed a second time here — a boundary
that re-decided what a listing may carry would put the privacy rule in two places and leave the one a client reads
untested. [Mailbox queries](mailbox-queries.md#what-a-summary-carries) records what it carries and why.

Three parts of it are worth reading before a caller writes against them:

- **`toAddresses` and nothing beyond it.** `Cc` and `Reply-To` are searchable but not listed, and recipient display names
  are not returned at all. A listing exists to let a reader recognize a message; the full participant set belongs to
  reading one.
- **`attachments` as a group rather than one flag.** `attachmentCount` and `inlineResourceCount` are separate values
  beside the total size and the encrypted, unverified-signature, and unexpanded-TNEF markers, because MailFathom's
  classification rule does not count an embedded logo or a signature part as an attachment. Without the second count a
  caller could not tell an email carrying a document from one carrying a picture in its signature block.
- **`contentAvailability` rather than a bare flag.** An email deliberately stored without its MIME because it exceeded the
  configured size limit reports `exceededSizeLimit`, so a caller sees why a later content read will not succeed instead of
  discovering it by making the call.

The remote flags carry `wasObserved` beside `observedAt`. Reconciliation has not landed, so every row currently reports
flags nobody has read, and a caller that ignored the distinction would read "no flag set" where the truth is "nobody has
looked".

### Freshness

Every result carries one `folderFreshness` entry per folder in the request's scope, each stating when synchronization last
committed progress for that folder or that it never has. A tool never contacts a mail server, so without it a caller
cannot tell a folder that holds no matching mail from one whose synchronization has been failing for a week.

Per folder rather than aggregated: an entry whose `wasSynchronized` is false is the folder whose staleness a caller most
needs to see, and collapsing the scope into one timestamp would hide which folder it belonged to. A folder the scope names
but no run has ever reached is reported with no timestamp rather than omitted.

### Authorization

The use case resolves the accounts this deployment serves and refuses anything outside them before it reads, so a second
entrypoint cannot reach the query without the same check. Ownership is the configured account list today, read through the
`IMailAccountCatalog` application port. OAuth 2.1 decides *who* reaches a tool at all — a token has to name a subject the
deployment authorized — and leaves that port unchanged, so every admitted caller still resolves the same configured
accounts. Deriving the account set from the authenticated identity is the later step, and it replaces the implementation
behind that port rather than introducing authorization for the first time.

An unknown account identifier is refused with `53001` rather than answered with an empty page, so a listing cannot be used
to discover which account identifiers exist; "no such account" and "not yours" are deliberately one answer. A request that
names no account is narrowed to the served accounts rather than left unrestricted, because removing an account from
configuration leaves its stored rows in place.

## `get_email_content`

Returns up to ten emails from the local mailbox copy in one call: for each one its normalized headers, the plain-text
body, optionally a sanitized HTML body, how many attachments it carries, and optionally one entry per attachment.
[Email content](email-content.md) documents the use case behind it — the representations, the sanitization policy, the
two bounds, the attachment default, and the consistency behavior — where they are enforced. This section describes the
surface.

### Arguments

| Argument | Type | Meaning |
|---|---|---|
| `storedEmailIds` | `string[]` | **Required.** The `storedEmailId` values a listing or a search returned, 1 to 10 of them, each named at most once. Each is a UUID; anything else is refused with `51004` |
| `includeSanitizedHtml` | `boolean` | Whether to also return the sanitized HTML body of each email. Omitted returns plain text alone |
| `includeAttachmentDetails` | `boolean` | Whether to describe each attachment rather than only count them. Omitted returns the counts alone |

Naming several emails is what the tool exists for: a call that has just listed or searched routinely wants the top few
results, and one round trip per email spends the protocol overhead, the rate-limit budget, and a turn of the model's own
attention on a read that touches nothing outside the local copy.

The identifiers are the one argument this boundary converts, and it converts them before anything is looked up. Text that
is blank, longer than any UUID form, is not a UUID, or is the all-zero UUID names no email this system could have issued,
so it is refused with `51004` rather than looked up and reported as absent — a typo and a deleted message are different
findings. The count is checked before the first parse and each identifier's length before that identifier's parse,
because a parse scans whatever it is handed and a caller nobody vouches for decides both how long each one is and how
many there are. No refusal repeats the text or says which position carried it, because that is caller input on its way
into a client-readable result and the log line beside it — and the caller holds the list it sent.

Three refusals end the call rather than one entry, because none of them leaves an email to report an outcome against: a
list of more than ten or of none at all is `51005`, a repeated identifier is `51006`, and text that names no email is
`51004`. A list is refused rather than truncated or de-duplicated, so a caller never has to compare what came back
against what it asked for to find out what it did not receive.

`51004` and `53002` are therefore deliberately distinct, as are `53002` and `55001`: the first pair separates "you named
no email" from "that email is not here", and the second separates "not here" from "here and currently unservable". Only
the last is worth repeating.

### Result

The result is one `emails` entry per named email, in the order the call named them. Each entry names the email and
carries exactly one of two things.

| Field | Meaning |
|---|---|
| `storedEmailId` | The email this entry answers for, present whether or not there was content |
| `content` | The email as it was read, or `null` when it could not be served |
| `failure` | The stable `code` and `message` saying why there is no content, or `null` when there is |

One email this deployment cannot serve therefore costs the caller that email rather than the whole call — which is the
reason the tool answers per email at all. The codes on `failure` are the same ones a failed call reports, `53002` and
`55001`, so a client matches on one set of numbers whether the finding was about the request or about one of the emails
in it.

`content` carries what a read produced.

| Field | Meaning |
|---|---|
| `accountId`, `folderAlias` | Where the email is, in MailFathom's own names |
| `sizeBytes` | The size of the whole email as the mail server reported it |
| `headers` | Subject, sent and received timestamps, every participant with its header role, and the three threading identifiers |
| `body` | The representations, or the reason there are none |
| `attachments` | One entry per attachment when the call asked for them: normalized file name, media type, decoded size — never bytes |
| `attachmentCounts` | What the email carries besides its body, returned either way, or `null` when nothing has ever read its parts |
| `remoteFlags` | The flags a server last showed, and when they were read |

Five parts of it are worth reading before a caller writes against them:

- **Truncation travels inside each representation, and names the bound.** `plainText` and `sanitizedHtml` each carry
  `text`, `originalCharacterCount`, and `truncatedBy`, because a body and the fact that it is incomplete are never useful
  apart: a model handed only the text would summarize a cut message as a whole one. `truncatedBy` is `none`,
  `bodyCharacterLimit` when this email alone is longer than one call returns, or `readCharacterBudget` when the emails
  named before it had already spent the call's total budget — the one case where naming fewer emails at once returns
  more. A message can exceed a bound in one representation and not in the other, which is why the metadata is not shared
  between them.
- **`availability` rather than an empty body.** `readable` means the text is the message, and an empty body under it
  means the message displayed nothing. `encryptedNotReadableLocally` is mail this deployment cannot decrypt, and
  `notStoredExceededSizeLimit` is mail whose bytes the configured limit deliberately kept out of storage. The last two
  return an empty text because nothing could be read, and a caller that ignored the distinction would report an empty
  message.
- **`attachments` is `null` unless it was asked for, which is not an empty list.** A file name is text the sender chose
  and is often the most identifying string an email carries, so an ordinary read of a body publishes none. `null` means
  the call did not ask; `[]` means the email carries none. `attachmentCounts` answers how many either way, so a caller
  can tell that asking again would describe something.
- **`attachments` carries no content, in any shape.** Nothing reachable from the published result can hold bytes, which
  `Mcp.UnitTests` asserts structurally over the whole contract rather than response by response. Attachment download and
  message export are out of scope for the first release, and withholding file names by default narrows what is described
  rather than beginning to publish what is not.
- **File names are normalized and may say so.** A file name is attacker-controlled text that reaches a model directly, so
  what is published is the domain's normalized form: a bare name, never a path or a traversal segment, never a control
  character or a bidirectional override, at most 200 characters. `wasFileNameNormalized` states whether MailFathom had to
  rewrite what the message wrote, and a part left with nothing usable is reported as unnamed rather than given an
  invented name.

`attachmentCounts` is `null`, rather than zero, for an email whose content the size limit kept out of storage. Nothing
has ever read that message's parts — synchronization recorded what the server's envelope reported, and an envelope does
not describe attachments — so publishing zeros would claim the email carries nothing attached, which no local state
supports.

### Reading changes nothing, locally or remotely

The tool holds one use case and that use case holds no mailbox port, so no branch of a content read can open an IMAP
session. A missing local copy is answered with `55001` and a durable repair request the synchronizer acts on later,
never with a fetch; reading mail through MailFathom therefore cannot download it and cannot set the remote `\Seen` flag.
The `remoteFlags` a result carries are an observation from the last synchronization run, with `wasObserved` stating
whether any run has looked.

Authorization is the use case's, as it is for `list_emails`: an email of an account this deployment does not serve is
reported as `53002`, the same answer an email that was never stored gets, so a read cannot be used to discover which
identifiers exist. In a call naming several emails that answer is one entry's, and the emails the deployment does serve
come back beside it.

## `search_emails`

Searches the local mailbox copy for text and returns one bounded window of matches ranked by relevance, each carrying
the summary a listing would show, a relevance rank, and bounded extracts of the body around what matched.
[Lexical email search](lexical-email-search.md) documents the use case behind it — what is indexed, what the rank means,
how the extracts are cut, and why there is no cursor — where those are enforced. This section describes the surface.

### Arguments

| Argument | Type | Meaning |
|---|---|---|
| `queryText` | `string` | **Required.** The text to search for, up to 512 characters. Blank is refused with `51002`, because a search with no text is a listing |
| `accountIds` | `string[]` | Accounts to search. Omitted searches every account this deployment serves; an account it does not serve is refused with `53001` |
| `folderAliases` | `string[]` | MailFathom folder aliases such as `INBOX`. Omitted searches every folder of the accounts in scope |
| `senderAddress` | `string` | The whole address the sender must carry, in any case — not a fragment |
| `recipientAddress` | `string` | The whole address a `To` or `Cc` recipient must carry |
| `subjectFragment` | `string` | Text the subject must contain, case-insensitively, up to 256 characters |
| `receivedOnOrAfter` | `date-time` | Inclusive start of the received range |
| `receivedBefore` | `date-time` | Exclusive end of the received range |
| `isRemotelySeen` | `boolean` | The remote seen state to require. Searching never changes it |
| `hasAttachments` | `boolean` | Whether to match only emails with attachments or only those without |
| `resultLimit` | `integer` | 1 to 50. Omitted takes the default of 20; a value outside the range is refused with `51003` rather than clamped |

The structured filters are `list_emails`' own and mean exactly the same things, because both read models apply one
validated selection; the identifier lists are converted and bounded by the same code, so the `51002` refusals described
above hold here word for word. `subjectFragment` and `queryText` are unrelated and the argument descriptions say so: the
fragment narrows which emails are eligible, and the query text is what the eligible ones are matched and ranked against.

There is deliberately no cursor, no offset, and no argument that widens how much of a message an extract may show. The
first is unsound over a relevance order that moves as mail is indexed; the second would let a caller lift a privacy
control that belongs to the deployment. `Mcp.UnitTests` asserts the absence of the second as part of the descriptor,
because an argument added later would be the one thing that quietly changes what a search can draw out of a mailbox.

### Result

| Field | Meaning |
|---|---|
| `matches` | The matched emails, most relevant first, ties broken by the newest received. Empty when nothing matched |
| `retrievalMode` | How the results were retrieved — `lexical` today |
| `folderFreshness` | How current the local copy of each covered folder is, exactly as a listing reports it |

Each match carries `summary`, which is the same shape `list_emails` publishes and is documented above, together with
`relevanceRank` and `snippets`.

- **`relevanceRank` is comparable within one response and nowhere else.** It is computed for the query that produced it,
  so storing it or comparing it with a rank from another call compares two different scales.
- **`snippets` are message text and are returned as data.** Each matched run is wrapped in `**` and nothing else is
  added: no interpretation, no summary, and no formatting that would let mail somebody else wrote read as instruction or
  as one of MailFathom's own fields. A caller passing them to a model treats them as untrusted input, as it would any other
  message content.
- **A match can carry no snippets at all.** An email that matched on its subject or a participant address carries none,
  because the summary publishes both whole, and an email with no indexed body text — encrypted mail, or mail whose
  content lives inside an attachment — carries none either.

### The retrieval mode is a field from the first release

`retrievalMode` reports `lexical` and is the only value it can report today. It exists anyway, because retrieval becomes
hybrid when the RAG work lands and a client given no way to tell the two apart would either infer it from a server
version or discover the change by reasoning wrongly about the results. Publishing it now costs one field and makes the
later work widen an enumeration rather than reshape a response.

Lexical means what it says on the descriptor: the words a query contains are matched against the words the mail is
written in, so a query term that appears nowhere in a message will not find it however close its meaning. Words that
appear only inside an attachment payload are not searchable at all, which is a limit of the index rather than of this
tool.

### What the boundary bounds, and why it bounds it again

Every limit a caller can name belongs to the use case, as it does for `list_emails`: the query length, the filter
bounds, the result-count range, and the account authorization are checked there and this boundary re-states none of
them.

Two bounds it does apply again, on what it is about to publish: at most the configured number of extracts per email, and
at most the greatest number of ranked results a search serves. Neither is request input and neither is a caller's to
widen — they are the control on how much mail content one call draws out of a mailbox, and this is the last place that
content passes before it reaches a model. The read model already applies them against what PostgreSQL returned for the
same reason, and a control a defective adapter could widen is not one.

The character bound on a single extract is applied here as a ceiling rather than reproduced exactly. The use case counts
the characters of the message and deliberately does not count the highlight markers, which are MailFathom's own; once those
markers are `**` they are indistinguishable from a message that writes `**` itself, so this boundary cannot repeat that
count and does not pretend to. It cuts at a ceiling derived from that bound instead — three times it, plus the one
character the use case's own truncation mark contributes — which is above every extract the use case can produce, since
a marked run needs a character of its own and a character separating it from the next and markup can therefore at most
double an extract, and far below a body.

### Empty results, and what a search does not reveal

A query that matches nothing returns an empty `matches` array with the same `retrievalMode` and the same
`folderFreshness` a window that matched would carry. It is an ordinary response rather than an error, so a search cannot
be used to establish that an account or a folder holds mail the caller was not already entitled to see. An account this
deployment does not serve is still refused with `53001` before anything is read, for the reason a listing refuses one:
an empty result would confirm the identifier.

What that guarantee covers is worth stating exactly, because it is narrower than "a search reveals nothing". It covers
everything outside the served scope: the account authorization is resolved before any read, and a folder alias is only
ever matched within the accounts already resolved, so no query — matching or empty — reports on an account this
deployment does not serve for this caller. It deliberately does not hide the folder names *inside* a served account.
`folderFreshness` publishes one entry per folder in scope, and a request that names no folder therefore lists every
folder those accounts have; that is the field doing its job rather than leaking, since a caller who cannot see which
folders are stale cannot tell an empty result from an unsynchronized one. A caller who guesses an alias and receives no
freshness entry has learned that no such folder exists in their own mailbox, which the unscoped call would have told
them outright.

The query text is never logged and no failure message repeats it. What somebody is searching their own mailbox for is
personal data of a particularly revealing kind, and the refusals this tool raises name the filter and its limit rather
than the value.

## Pending

`ask_mail`, and the retrieval-augmented generation work behind it, is a later stage. `list_emails`,
`get_email_content`, and `search_emails` are complete: the PostgreSQL read models, the lexical index, and the content
store they read through landed with their use cases, so a call against a synchronized mailbox answers from the local
copy.
