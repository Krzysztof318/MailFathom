# MCP tools

MailMcp publishes its read side as Model Context Protocol tools over the Streamable HTTP transport. This page records the
conventions every tool follows, the contract of the tools that exist, and what a client reads when a call fails.

The endpoint is disabled by default and, until the OAuth 2.1 work lands, carries no transport authentication.
`docs/operations/mcp-endpoint.md` records that posture and how to enable the endpoint; this page describes the surface it
serves.

## Implemented behavior

`ModelContextProtocol.AspNetCore` 2.0.0 hosts the server. The `Mcp` project owns the tool descriptors, the conversion of
protocol arguments into the domain identities a use case is expressed in, and the mapping from a use case's result back
onto the published contract. It holds no query, no persistence, and no mail-protocol code: `list_emails` calls the
`MailboxTimelineReader` use case and nothing else, and `get_email_content` calls the `EmailContentReader` use case and
nothing else.

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
  return it, which today is `get_email_content` alone; `list_emails` returns summaries and no body text at all.

## Descriptor conventions

Every tool is declared with the same deliberate metadata, because a client decides whether a tool is safe to call before
it calls anything:

| Element | Convention |
|---|---|
| `name` | Snake case, as the MCP tool ecosystem spells tool names — `list_emails`, `get_email_content` |
| `title` | A human-readable label for display — `List emails`, `Get email content` |
| `description` | States what the tool reads, that it reads the local copy only, that it changes nothing, and what it bounds |
| `inputSchema` | Every argument is a top-level property carrying its own description, unit, and absence meaning |
| `outputSchema` | Generated from the result type, whose properties carry descriptions of their own |
| `readOnlyHint` | `true` |
| `destructiveHint` | `false` |
| `idempotentHint` | `true` |
| `openWorldHint` | `false` — every tool is confined to MailMcp-controlled local state |

The four annotations are contract metadata rather than documentation, so `Mcp.UnitTests` asserts the advertised
`tools/list` output: the name, the title, the description, every input property, the descriptions on them, the output
schema, and each annotation. A descriptor that drifts fails the build.

Enumerations travel as their names, camel-cased — `newestFirst`, `exceededSizeLimit` — never as ordinals. Each one is a
type this boundary owns rather than the domain enumeration describing the same states, because the member names *are* the
published wire values: sharing the domain's type would make a rename inside the domain a silent change to the protocol.
Timestamps are ISO 8601 and property names are camel-cased, both of which follow from the single `JsonSerializerOptions`
every tool registration is given, so the schema that was advertised and the payload that is serialized cannot diverge.

Sizes are published in bytes and named for it — `sizeBytes`, `totalSizeBytes` — even though the application and the
stored schema call the same quantity octets. The two words mean one thing here, and the protocol uses the one a client
reads without pausing.

## Error reporting

Expected failures are reported as a tool result with `isError` set, whose text is the one shape every tool uses:

```text
MailMcp error 53001: Mail account 'shared-billing' is not accessible.
```

The five-digit code is the machine-readable part and is stable: it is what a runbook, an alert, or a log search matches
on. The sentence after it is the one the use case wrote, republished rather than restated here, so a client and an
operator read the same wording and there is no second text to drift. It names the filter and, where there is one, its
limit — never the value that was refused, because a filter value is itself sensitive and a boundary that reflects input
back has started returning content. An account identifier is the exception the rule allows: it is MailMcp's own
configured name for an account and carries nothing the caller did not already write.

| Code | Meaning | Typical cause |
|---|---|---|
| `51001` | A page size outside the range the query serves | A page size of 0 or above 100, refused rather than clamped |
| `51002` | A filter carries a value, a count, or a length the query does not accept | An unusable address, a subject fragment over 256 characters or carrying a control character, a received range that ends before it starts, more than 64 accounts or folder aliases, an account identifier or folder alias that is blank, over 256 characters, or carrying a control character |
| `51004` | The call named an email with text that is no identifier this system issues | A `storedEmailId` that is blank, not a UUID, or the all-zero UUID, refused before anything is looked up |
| `52001` | A continuation cursor is not one this system issued | A truncated, hand-written, or foreign cursor |
| `52002` | A continuation cursor was issued for different filters | A cursor reused after a filter or the reading direction changed |
| `53001` | The call named a mail account this deployment does not serve | An account identifier nobody configured, or one belonging to someone else — the two are deliberately one answer |
| `53002` | The call named an email the local mailbox copy holds no row for | An email never synchronized, one expunged and collected, or one of an account this deployment stopped serving — deliberately one answer |
| `54001` | The call failed for a reason the boundary deliberately does not describe | Anything undiagnosed; the detail is in the server log |
| `55001` | The email exists locally and its stored content is missing, damaged, or unreadable | A local copy being repaired; the call is worth repeating once repair has run |

Codes `51001` through `53002` and `55001` are the use cases' own, allocated in the MCP-boundary category because that is
where they surface, and every one of them is written for a caller to read. That is the whole rule the boundary applies: a
failure whose code belongs to that category is published as it stands, and a failure from any other category — a schema
mismatch, an IMAP authentication refusal, a concurrency conflict — describes MailMcp's internals to whoever asked and
collapses into `54001`. Stating the rule as a category rather than as a list of exception types is what stops a failure
added later from reaching a client because nobody remembered to add it to a list.

`54001` is therefore the only answer an unexpected failure ever produces, and a failure the MCP SDK itself raises —
while binding an argument to the advertised schema, for instance — collapses into it too. Those messages are the SDK's,
not written to the rule above, and may name a rejected value or a CLR type; what a client loses is a description of a
request it can already compare against the published input schema.

One call-tool filter wraps the whole surface: it records the tool name, the outcome, the error code where there is one,
and the duration of every call, and it logs any undiagnosed exception in full on the server, correlated by the trace the
request already carries. Cancellation and protocol-level failures are recorded and then rethrown rather than converted,
because a cancelled call is the caller's own doing and a JSON-RPC error has to be reported as one.

The tool name a call arrived with is recorded only when it is spelled the way a MailMcp tool name is; anything else is
recorded as one fixed placeholder. On an unknown tool that name is unvalidated caller input on its way into a retained
log, and a log is not a place to let a caller write.

## `list_emails`

Returns a bounded page of summaries from the local mailbox copy, newest received first by default.

### Arguments

Every argument is optional.

| Argument | Type | Meaning |
|---|---|---|
| `accountIds` | `string[]` | Accounts to read. Omitted reads every account this deployment serves; an account it does not serve is refused with `53001` |
| `folderAliases` | `string[]` | MailMcp folder aliases such as `INBOX`. Omitted reads every folder of the accounts in scope. Case is normalized, so a repeated spelling names one folder |
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
  beside the total size and the encrypted, unverified-signature, and unexpanded-TNEF markers, because MailMcp's
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
`IMailAccountCatalog` application port; the OAuth 2.1 work replaces the implementation behind that port with one that
derives the set from the authenticated identity rather than introducing authorization for the first time.

An unknown account identifier is refused with `53001` rather than answered with an empty page, so a listing cannot be used
to discover which account identifiers exist; "no such account" and "not yours" are deliberately one answer. A request that
names no account is narrowed to the served accounts rather than left unrestricted, because removing an account from
configuration leaves its stored rows in place.

## `get_email_content`

Returns one email from the local mailbox copy: its normalized headers, the plain-text body, optionally a sanitized HTML
body, and one entry per attachment. [Email content](email-content.md) documents the use case behind it — the
representations, the sanitization policy, the truncation rule, and the consistency behavior — where they are enforced.
This section describes the surface.

### Arguments

| Argument | Type | Meaning |
|---|---|---|
| `storedEmailId` | `string` | **Required.** The `storedEmailId` a listing or a search returned. A UUID; anything else is refused with `51004` |
| `includeSanitizedHtml` | `boolean` | Whether to also return the sanitized HTML body. Omitted returns plain text alone |

The identifier is the one argument this boundary converts, and it converts it before anything is looked up. Text that is
blank, is not a UUID, or is the all-zero UUID names no email this system could have issued, so it is refused with
`51004` rather than looked up and reported as absent — a typo and a deleted message are different findings. The refusal
never repeats the text, because it is caller input on its way into a client-readable result and the log line beside it.

`51004` and `53002` are therefore deliberately distinct, as are `53002` and `55001`: the first pair separates "you named
no email" from "that email is not here", and the second separates "not here" from "here and currently unservable". Only
the last is worth repeating.

### Result

| Field | Meaning |
|---|---|
| `storedEmailId`, `accountId`, `folderAlias` | Where the email is, in MailMcp's own names |
| `sizeBytes` | The size of the whole email as the mail server reported it |
| `headers` | Subject, sent and received timestamps, every participant with its header role, and the three threading identifiers |
| `body` | The representations, or the reason there are none |
| `attachments` | One entry per attachment: normalized file name, media type, decoded size — never bytes |
| `attachmentCounts` | What the email carries besides its body, or `null` when nothing has ever read its parts |
| `remoteFlags` | The flags a server last showed, and when they were read |

Four parts of it are worth reading before a caller writes against them:

- **Truncation travels inside each representation.** `plainText` and `sanitizedHtml` each carry `text`,
  `originalCharacterCount`, and `wasTruncated`, because a body and the fact that it is incomplete are never useful apart:
  a model handed only the text would summarize a cut message as a whole one. A message can exceed the bound in one
  representation and not in the other, which is why the metadata is not shared between them.
- **`availability` rather than an empty body.** `readable` means the text is the message, and an empty body under it
  means the message displayed nothing. `encryptedNotReadableLocally` is mail this deployment cannot decrypt, and
  `notStoredExceededSizeLimit` is mail whose bytes the configured limit deliberately kept out of storage. The last two
  return an empty text because nothing could be read, and a caller that ignored the distinction would report an empty
  message.
- **`attachments` carries no content, in any shape.** Nothing reachable from the published result can hold bytes, which
  `Mcp.UnitTests` asserts structurally over the whole contract rather than response by response. Attachment download and
  message export are out of scope for the first release.
- **File names are normalized and may say so.** A file name is attacker-controlled text that reaches a model directly, so
  what is published is the domain's normalized form: a bare name, never a path or a traversal segment, never a control
  character or a bidirectional override, at most 200 characters. `wasFileNameNormalized` states whether MailMcp had to
  rewrite what the message wrote, and a part left with nothing usable is reported as unnamed rather than given an
  invented name.

`attachmentCounts` is `null`, rather than zero, for an email whose content the size limit kept out of storage. Nothing
has ever read that message's parts — synchronization recorded what the server's envelope reported, and an envelope does
not describe attachments — so publishing zeros would claim the email carries nothing attached, which no local state
supports.

### Reading changes nothing, locally or remotely

The tool holds one use case and that use case holds no mailbox port, so no branch of a content read can open an IMAP
session. A missing local copy is answered with `55001` and a durable repair request the synchronizer acts on later,
never with a fetch; reading mail through MailMcp therefore cannot download it and cannot set the remote `\Seen` flag.
The `remoteFlags` a result carries are an observation from the last synchronization run, with `wasObserved` stating
whether any run has looked.

Authorization is the use case's, as it is for `list_emails`: an email of an account this deployment does not serve is
refused with `53002`, the same answer an email that was never stored gets, so a read cannot be used to discover which
identifiers exist.

## Pending

`search_emails` is specified but not implemented — the `MailboxSearchReader` use case it will map onto has landed, the
tool has not — and `ask_mail` with the RAG work behind it is a later stage. `list_emails` and `get_email_content` are
complete: the PostgreSQL read models and the content store they read through landed with their use cases, so a call
against a synchronized mailbox answers from the local copy.
