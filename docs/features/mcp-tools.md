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
onto the published contract. It holds no query, no persistence, and no mail-protocol code: `list_accounts` calls the
`MailAccountDirectoryReader` use case and nothing else, `list_emails` calls the `MailboxTimelineReader` use case and
nothing else, `get_email_content` calls the `EmailContentReader` use case and nothing else, `search_emails` calls the
`MailboxSearchReader` use case and nothing else, and `ask_mail` calls the `MailboxQuestionReader` use case and nothing
else.

It holds no AI code either, and cannot. The project references `Domain` and `Application` and no other MailFathom assembly,
which `Mcp.UnitTests` asserts against the compiled reference list rather than against a convention — so no tool on this
surface can embed a query, rewrite it, or compose an agent, and a package that would make one able to has to be added and
reviewed before that changes. `ask_mail` is not an exception to that: it calls an application use case, and everything
between that use case and a provider — the agent, its tool loop, the retrieval it is bound to — lives behind the
`IMailQuestionAnswerer` port, in the `AI` project this one cannot see.

The division is deliberate and is what keeps a second entrypoint from bypassing anything. Every filter bound, the
page-size range, the account authorization, and the cursor's authenticity belong to the use case, so this boundary
re-states no limit of its own; [Mailbox queries](mailbox-queries.md) documents them once, where they are enforced. What
the boundary owns is the one thing a use case cannot: turning a caller's text into an account identifier or a folder
alias, and refusing text that names neither.

Three properties hold for every tool and are proven by test rather than asserted here:

- A call reads the local mailbox copy only. Nothing in a tool request reaches a mail server, so a request cannot wait on
  IMAP and cannot set the remote `\Seen` flag. `ask_mail` reaches a chat provider, which is a different thing and the
  one exception to "a call reaches nothing outside this process": it still reads mail from the local copy alone and
  still speaks to no mail server.
- No error and no log line carries a filter value, a mailbox address, a subject, body text, raw MIME, an exception type,
  a stack trace, or an internal identifier. What a boundary withholds is not lost: the detail is logged on the server,
  correlated by the trace the request already carries.
- No result carries raw MIME. Message content itself is a result only where the tool exists to return it:
  `get_email_content` returns bounded bodies and, for a call that asked to describe the attachments, the files under
  bounds of their own; `search_emails` returns bounded extracts of a body; `list_emails` returns summaries and no body
  text at all; and `ask_mail` returns prose written about mail plus the subjects of the emails it cites. Attachment
  content reaches exactly one property of one result, and no other tool publishes any.
- Every tool bounds how much mail one call can draw out of a mailbox, in the count of items and in their volume alike:
  `list_emails` pages at 100 summaries, `search_emails` windows at 50 ranked matches, `get_email_content` reads at
  most 10 emails under a shared character budget and a shared attachment-byte budget, and `ask_mail` publishes by
  default at most 20 000 characters of
  answer citing at most 20 emails, having read at most 20 000 characters of mail to write it. A caller can never raise
  any of them, and the last set is the operator's to lower or raise in
  [`MailAnswering`](../operations/configuration-reference.md#mailanswering).

One property holds for four of the five and is stated where it stops. `list_accounts`, `list_emails`,
`get_email_content`, and `search_emails` are advertised by every deployment, because local state is all they need.
`ask_mail` needs two AI providers an operator configures separately, so it is advertised only while both are configured
and working; the [`ask_mail`](#ask_mail) section records what decides that and what a call meets when it arrives anyway.

## Descriptor conventions

Every tool is declared with the same deliberate metadata, because a client decides whether a tool is safe to call before
it calls anything:

| Element | Convention |
|---|---|
| `name` | Snake case, as the MCP tool ecosystem spells tool names — `list_accounts`, `list_emails`, `get_email_content`, `search_emails`, `ask_mail` |
| `title` | A human-readable label for display — `List accounts`, `List emails`, `Get email content`, `Search emails`, `Ask about mail` |
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
| `56001` | This deployment cannot answer questions about mail, either at all or for now | `ask_mail` called on a server that declared no chat endpoint or embeds no mail, or one whose chat provider is currently refusing; the message says which |
| `57001` | Answering would cost more than this deployment allows | `ask_mail` on a server whose current period has spent its allowance, or a run that reached what one question may spend; the message says which, and only the first becomes answerable by waiting |

Codes `51001` through `53002`, `55001`, `56001`, and `57001` are the use cases' own, allocated in the MCP-boundary category because that is
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

Every provider failure `ask_mail` can end in collapses into `54001` as well, and that is the rule working rather than an
omission. A refused chat credential is `71001`, an endpoint that did not answer within its budget is `72001`, and a call
that produced no text is `73001` — none of them in the MCP-boundary category, because each describes an endpoint the
caller neither configured nor can reach. What a client is told is that the call failed; what an operator reads, in the
server log and in the health record for the chat role, is which of the three it was.

One call-tool filter wraps the whole surface: it records the tool name, the outcome, the error code where there is one,
and the duration of every call, and it logs any undiagnosed exception in full on the server, correlated by the trace the
request already carries. Cancellation and protocol-level failures are recorded and then rethrown rather than converted,
because a cancelled call is the caller's own doing and a JSON-RPC error has to be reported as one.

The tool name a call arrived with is recorded only when it is spelled the way a MailFathom tool name is; anything else is
recorded as one fixed placeholder. On an unknown tool that name is unvalidated caller input on its way into a retained
log, and a log is not a place to let a caller write.

## `list_accounts`

Returns the mail accounts this deployment serves, with the names a request may use for each and how current the local
copy of each of their folders is.

It is the tool a client calls first. Every other tool takes an account filter, and a caller that cannot see the accounts
has no way to fill one in — the identifier an operator configured is a key they invented, not something a model can
guess. This is also the one tool that publishes the account set rather than using it as a bound; the others answer only
about an account the caller already named, and refuse a name they do not serve.

### Arguments

None. The tool answers about the deployment rather than about a request, so there is nothing for a caller to get wrong
and nothing to bound.

### Result

`accounts` carries one entry per served account, ordered by account identifier, and `synchronizationEnabled` says whether
the deployment is refreshing its local copy at all.

| Field | Meaning |
|---|---|
| `accountId` | The configured identifier. It is what every other result reports as `accountId`, and it is stable across a change of the display name |
| `displayName` | The readable name the operator gave the account |
| `synchronizationMode` | `polling` or `push`, stating what the operator asked to start the account's next pass |
| `folders` | One entry per folder local state knows of, in the same shape `folderFreshness` takes elsewhere: the alias, when synchronization last committed progress for it, and whether it ever has |

**Either name may be used to select the account.** The identifier is matched exactly and the display name without regard
to case, and configuration refuses a display name that another account's identifier or display name already carries, so
a name always names one mailbox. Both spellings resolve to one identity before a query runs, which is why a continuation
cursor issued for one stays valid for the other.

**`synchronizationMode` states what was asked for, not what a folder is getting.** Whether push is served is decided per
folder against what the mail server advertises and how recent attempts went, which is an observation about a run rather
than a property of the account.

**An empty `folders` list is a statement.** It says synchronization has never reached the account, which means its mail
may be absent entirely rather than merely out of date — a distinction an empty listing cannot make for itself.
`synchronizationEnabled` answers the other half: `false` means the timestamps below it are as current as any answer will
get, because nothing is advancing them.

### What it deliberately does not publish

Nothing about how MailFathom reaches a mailbox. The mail server, the port, the IMAP user name, and every secret
reference are absent, and the descriptor test asserts their absence rather than trusting it, so a field carrying one
cannot arrive unnoticed. The display name is what makes a mailbox recognizable to a caller; the connection detail is the
operator's, and an assistant choosing which mailbox to ask about needs none of it.

An account this deployment stopped serving is absent as well, because the read is scoped to the served accounts exactly
as every other read is. Local state still holds its folders, and that is not a reason to name it in the one answer that
lists what exists.

## `list_emails`

Returns a bounded page of summaries from the local mailbox copy, newest received first by default.

### Arguments

Every argument is optional.

| Argument | Type | Meaning |
|---|---|---|
| `accounts` | `string[]` | Accounts to read, each named by its configured account identifier or by the display name it is published under. Omitted reads every account this deployment serves; a name it does not serve is refused with `53001` |
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

Both lists are converted to domain values at this boundary, and their counts are checked against the query's own limits
*before* any element is converted — a ceiling applied after the trimming and upper-casing it exists to prevent has
already run over a million-element array is not a ceiling. Text that could name nothing this system issues is then
refused with `51002`, and the refusal never repeats the value.

**Which account a name refers to is not settled here.** An account may be named by its identifier or by its display
name, and the two are matched against the served accounts inside the use case, so text naming nothing meets exactly the
refusal an account the deployment stopped serving meets. The identifier is matched exactly and the display name without
regard to case, and neither is ever matched as a fragment; naming the same account both ways is one account in the
resolved scope, so a continuation cursor issued for one spelling stays valid for the other.

The boundary applies one rule to both lists: at most 256 characters and no control characters. It holds whatever each
domain type goes on to check for itself, because a name travels even when it matches nothing — an account this
deployment does not serve is named back in the `53001` refusal a client reads, so an unbounded string carrying newlines
would otherwise be a way to write arbitrary text into that contract and into the log beside it.

### Result

`emails` carries the page, `nextCursor` reads the following one and is absent on the last page, and `folderFreshness`
states how current the local copy of each covered folder is.

Each summary carries the stable local identifier a content read is performed by, the account identifier and the display
name it is published under, the folder alias, the message identifier, the subject, the sender address and display name, the `To` addresses, the sent and received timestamps, the
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
- **`contentAvailability` rather than a bare flag.** An email deliberately stored without its MIME reports why, so a
  caller sees that a later content read will not succeed instead of discovering it by making the call — and sees whether
  that is permanent. `exceededSizeLimit` is an email larger than the configured per-message limit, which every later run
  will refuse in the same way; `awaitingStorageHeadroom` is one that arrived while local storage stood at its ceiling,
  whose content a later synchronization run fetches once there is room.

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

A name no served account answers to is refused with `53001` rather than answered with an empty page; "no such account"
and "not yours" are deliberately one answer, and so is "that is not a name of anything". A request that names no account
is narrowed to the served accounts rather than left unrestricted, because removing an account from configuration leaves
its stored rows in place.

That refusal is about a name a caller *guessed*. Which accounts exist is published deliberately and in one place —
[`list_accounts`](#list_accounts) — because a caller that cannot see the accounts cannot fill in the filter above, and a
filter nobody can fill in is a filter nobody uses. What stays unpublished either way is everything about how MailFathom
reaches a mailbox.

## `get_email_content`

Returns up to ten emails from the local mailbox copy in one call: for each one its normalized headers, the plain-text
body, optionally a sanitized HTML body, every attachment it carries described, and — on request — a short-lived link
that fetches each of those attachments. [Email content](email-content.md) documents the use case behind it — the
representations, the sanitization policy, the two bounds, the attachment default, and what a link authorizes — where
they are enforced. This section describes the surface.

### Arguments

| Argument | Type | Meaning |
|---|---|---|
| `storedEmailIds` | `string[]` | **Required.** The `storedEmailId` values a listing or a search returned, 1 to 10 of them, each named at most once. Each is a UUID; anything else is refused with `51004` |
| `includeSanitizedHtml` | `boolean` | Whether to also return the sanitized HTML body of each email. Omitted returns plain text alone |
| `includeAttachmentDownloadLinks` | `boolean` | Whether to mint a link for fetching each attachment, rather than only describing it. Omitted still returns every attachment's file name, media type, and size |

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
| `attachments` | One entry per attachment, always: normalized file name, media type, and decoded size, plus a short-lived address to fetch it from when the call asked for one |
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
  means the message displayed nothing. `encryptedNotReadableLocally` is mail this deployment cannot decrypt,
  `notStoredExceededSizeLimit` is mail whose bytes the configured size limit deliberately kept out of storage, and
  `notStoredAwaitingStorageHeadroom` is mail that arrived while local storage stood at its ceiling and whose content a
  later synchronization run fetches once there is room. The last three return an empty text because nothing could be
  read, and a caller that ignored the distinction would report an empty message — or would give up on the one state
  where asking again later actually returns the body.
- **`attachments` is always present, and `[]` means the email carries none.** Every read describes what a message
  carries, because deciding whether a file is worth fetching *is* reading its name, its type, and its size — a result
  answering with a count alone would force a second call to learn what the first was about. `attachmentCounts` answers
  how many either way. `list_emails` still counts and never names, deliberately: a listing is a browse over mail the
  caller has not opened, while a content read has already returned the body in full.
- **No response carries a file's bytes, and `downloadState` says what it carries instead.** `downloadUrl` is an absolute
  address that returns exactly one attachment to an ordinary `GET` with no credential attached, and `downloadExpiresAt`
  is when it stops working; both are absent unless `downloadState` is `issued`. `notRequested` is the call that did not
  set `includeAttachmentDownloadLinks`, and `unavailable` is a deployment that declares no public address or no
  data-encryption key ring — asking again helps with the first and can never help with the second. Nothing reachable
  from the result can hold a raw byte array, a stream, or a base64 payload at all; `Mcp.UnitTests` asserts that
  structurally over the contract rather than response by response.
- **A link is a bearer capability, so treat the URL as a secret.** Anyone holding it can fetch that file until it
  expires, which is ten minutes by default and never more than thirty. Fetch it once, do not log it, and do not store
  it anywhere it will outlive the request; after it expires a new `get_email_content` call is what mints another, and
  there is no way to extend one.
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
[Email search](email-search.md) documents the use case behind it — what is indexed, what the rank means,
how the extracts are cut, and why there is no cursor — where those are enforced. This section describes the surface.

### Arguments

| Argument | Type | Meaning |
|---|---|---|
| `queryText` | `string` | **Required.** The text to search for, up to 512 characters. Blank is refused with `51002`, because a search with no text is a listing |
| `accounts` | `string[]` | Accounts to search, each named by its configured account identifier or by the display name it is published under. Omitted searches every account this deployment serves; a name it does not serve is refused with `53001` |
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
| `retrievalMode` | How this call's results were ranked — `lexical` or `hybrid` |
| `semanticSearch` | What this server can do with embeddings — `inactive`, `available`, or `degraded` |
| `folderFreshness` | How current the local copy of each covered folder is, exactly as a listing reports it |

Each match carries `summary`, which is the same shape `list_emails` publishes and is documented above, together with
`relevanceRank` and `snippets`.

- **`relevanceRank` is comparable within one response and nowhere else.** It is computed for the query that produced it,
  so storing it or comparing it with a rank from another call compares two different scales — and the scale itself
  depends on `retrievalMode`, a full-text rank under `lexical` and a fused rank score under `hybrid`.
- **`snippets` are message text and are returned as data.** Each matched run is wrapped in `**` and nothing else is
  added: no interpretation, no summary, and no formatting that would let mail somebody else wrote read as instruction or
  as one of MailFathom's own fields. A caller passing them to a model treats them as untrusted input, as it would any other
  message content.
- **A match can carry no snippets at all.** An email that matched on its subject or a participant address carries none,
  because the summary publishes both whole, and an email with no indexed body text — encrypted mail, or mail whose
  content lives inside an attachment — carries none either.

### The retrieval mode is read per response, not per server

`retrievalMode` names how the call in front of you was ranked, and both values are advertised by every server.

- **`lexical`** means the words a query contains were matched against the words the mail is written in, so a query term
  that appears nowhere in a message did not find it however close its meaning.
- **`hybrid`** means that ranking was combined with a search by embedding similarity, so a message can appear without
  carrying the query's words. [Email search](email-search.md#hybrid-retrieval) records what the combination does and
  what it does not promise.

It is a property of the response rather than of the deployment because the answer can differ between two calls to one
server: an instance configured for hybrid retrieval reports `lexical` while its embedding provider is unreachable, and
while it has activated no profile. Reading a server's configuration instead would leave a client concluding the wrong
thing about why a message it expected is missing.

Neither mode reaches a chat model, rewrites the query, or expands it; under `hybrid` the query is embedded and compared,
never interpreted. Words that appear only inside an attachment payload are not searchable under either mode, which is a
limit of what is indexed rather than of this tool.

### `semanticSearch` says why a lexical answer was lexical

`retrievalMode` says what happened to this call and `semanticSearch` says what the server is able to do, which is the
half a client cannot infer. A server that deliberately does not embed and a server whose embedding credential expired an
hour ago both answer `lexical`, and only the second is returning less than its operator intends.

- **`inactive`** — this server does not embed mail, so `lexical` is the intended and only mode. Nothing is wrong and
  nothing is going to change on its own.
- **`available`** — this server embeds mail and its provider is answering. An individual call can still report
  `lexical`, and it then reports `degraded` beside it, because the call that failed is the freshest evidence about the
  provider there is.
- **`degraded`** — this server embeds mail but currently cannot place a query in that vector space: a refused
  credential, an unreachable endpoint chain, or a configured model that is not the one the active profile records. The
  results are narrower than the server intends.

`degraded` is not an error and is not caused by the request, so **retrying buys nothing**. A client that surfaces it
tells the user the results may be incomplete and leaves the fix — a credential, an endpoint, a model declaration — with
the server's operator. Recovery is automatic and needs no restart: the next embedding call that succeeds restores the
state, and the search after it is `hybrid` again. [Email search](email-search.md#what-the-three-capability-states-mean)
records what each state means on the server side and how a call arrives at one.

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

## `ask_mail`

Answers a question about the local mailbox copy and names the emails the answer was drawn from. A chat model conducts
the run and looks up mail as it decides it needs context; [Mail answering](mail-answering.md) records what that run is,
what it may reach, and how much of it leaves the process. This section is the protocol surface over it.

It is the one tool that spends money on a call and the one that takes seconds rather than milliseconds. The description
says so, because the choice between asking and searching is a model's to make: ask when the answer spans several
messages, search when the messages themselves are what is wanted.

### Arguments

| Argument | Type | Meaning |
|---|---|---|
| `question` | `string` | **Required.** The question to answer, up to 1000 characters. It is not a search query: its words are not matched against the mail, and the lookups are written by the model |
| `accounts` | `string[]` | Accounts the answer may be drawn from, each named by its configured account identifier or by the display name it is published under. Omitted draws on every account this deployment serves; a name it does not serve is refused with `53001` |
| `folderAliases` | `string[]` | MailFathom folder aliases such as `INBOX`. Omitted draws on every folder of the accounts in scope. Case is normalized, so a repeated spelling names one folder |

There is no structured filter beside the scope, and that is a decision rather than an omission. A sender or a date range
supplied here would narrow every lookup the model makes without the model knowing why its searches were returning
nothing, and it would be answering a question nobody asked while reporting it as the answer to the one they did. The
model narrows its own lookups instead, with the same filters `search_emails` publishes: [Mail answering § What one
lookup may ask for](mail-answering.md#what-one-lookup-may-ask-for) records which, and which two are withheld from it.

The scope is the caller's authorization expressed as data, and it is the one part of the run the model cannot reach. It
is resolved before the run starts and bound into it, and the tool the model is offered takes no account and no folder
argument at all, so a run that has been talked into asking about another account has the caller's own scope searched for
those words. The same resolution runs again on every lookup, underneath, because the retrieval is the search
`search_emails` answers from.

The two identifier lists are converted and bounded exactly as `list_emails` converts them, by the same code and with the
same refusals; the section above records that rule once.

### Result

| Field | Meaning |
|---|---|
| `answer` | The answer, in prose |
| `citations` | The emails the run retrieved, one entry per email, in the order it first reached each |
| `answerTruncated` | Whether the answer was cut to the length one response carries |
| `citationsTruncated` | Whether the run reached more emails than `citations` names |
| `retrievalTruncated` | Whether the run hit this deployment's ceiling on how much mail one question may read |

Each citation carries the `storedEmailId` a content read is performed by, the account identifier and the display name it
is published under, the folder alias, the subject, and the received time. It deliberately carries no extract: the passage
the run retrieved has already reached a model, and returning it here would put mail content into a response whose purpose
is an answer. The subject and the received time
are what let a reader recognize a message before fetching it.

**The citations are what the run retrieved, not what the model demonstrably used.** Nothing outside the model knows which
of them it drew on, so publishing the narrower set would state something this system cannot observe. What they are good
for is the thing that makes an answer usable rather than merely fluent: every claim can be checked by reading the
messages the run had in front of it.

An empty `citations` array is an ordinary answer. The mailbox was searched and held nothing about the question, and the
answer then says so — which is a real answer rather than a failure.

The three truncation flags are part of the contract rather than diagnostics. A cut this surface made and did not report
would leave a shortened answer indistinguishable from a complete one, a claim traced to a message the response no longer
names cannot be checked, and a run that was stopped from reading further answered a narrower reading of the mailbox than
the question asked for. All three cut rather than refuse, which is the opposite of how a request bound behaves: a
request larger than a limit is the caller's to correct, while an answer larger than one has already been generated and
paid for.

`retrievalTruncated` is the one worth acting on differently. It does not mean the answer is wrong — it is complete for
what the run did read — but the mailbox holds matching messages the model was never shown, so asking a narrower question
reads a *different* part of the mailbox rather than more of it. [Mail answering § What one question may
spend](mail-answering.md#what-one-question-may-spend) records the ceiling behind it and what the model is told when it
is reached.

**The answer and the cited subjects are untrusted text.** The answer is model output written from extracts of mail
somebody else wrote, and a subject is that person's own words. A client that passes either into another model treats both
as data, as it would any other message content. What a message written to manipulate the run cannot do to it — and what
it still can do to the words of an answer — is [Mail answering § What is actually tried, and what it
settles](mail-answering.md#what-is-actually-tried-and-what-it-settles).

### When the tool is advertised

`ask_mail` appears in `tools/list` only while this deployment can answer a question, and is absent otherwise. A client
that can see a tool will call it, and a tool that exists only to answer "not configured" costs a round trip to learn
something the tool list could have said.

Two conditions decide it, and an operator configures them separately:

- **An embedding profile is active and a query can be placed in its space.** This is the same reading `search_emails`
  publishes as `semanticSearch`, so a server answering `inactive` or `degraded` there advertises no `ask_mail`.
- **The chat endpoint is declared and is not currently refusing.** A deployment that declared none never advertises the
  tool. One whose endpoint refused within the last minute withholds it, and offers it again after that so a repaired
  credential is discovered without a restart.

That recheck exists because nothing else calls the chat endpoint. The embedding provider is called by synchronization and
by the search path, so its health record renews itself; with the second retrieval pass off, answering a question is the
only thing that reaches the chat endpoint at all, and a deployment that withheld the tool for as long as the last failure
was on record would withhold it forever.

The decision is made per listing rather than at startup, so the transition is observable and needs no restart in either
direction. What it is not is authorization: a client may call a tool it was never offered, and the use case behind this
one refuses a question the same way whether or not the caller ever read a list. That refusal is `56001`, and its message
says whether this deployment answers no questions at all or answers them and currently cannot.

## Pending

The five read-only tools of this release are complete. `list_accounts` publishes the accounts a caller may name,
`list_emails`, `get_email_content`, and `search_emails` read the PostgreSQL read models, the lexical index, and the
content store that landed with their use cases, and `ask_mail` answers over the retrieval and the agent composition
above them, under the ceilings an operator sets on what one question and one period may spend. What is still pending on this surface is the run trace an operator reads afterwards,
its own change, and any tool that writes: SMTP delivery is deliberately absent from the first public tool set.
