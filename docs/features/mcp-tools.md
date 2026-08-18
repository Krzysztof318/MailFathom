# MCP tools

<!-- describes: src/Mcp/** -->

MailFathom publishes Model Context Protocol tools over the Streamable HTTP transport: the mailbox read side, and the
contact book, which is the one part of this surface a call can write to. This page records the conventions every tool
follows, the contract of the tools that exist, and what a client reads when a call fails.

The endpoint is disabled by default, and enabling it requires stating whether a client presents an API key or nothing at all.
`docs/operations/mcp-endpoint.md` records that posture and how to enable the endpoint; this page describes the surface it
serves.

## Implemented behavior

`ModelContextProtocol.AspNetCore` 2.0.0 hosts the server. The `Mcp` project owns the tool descriptors, the conversion of
protocol arguments into the domain identities a use case is expressed in, and the mapping from a use case's result back
onto the published contract. It holds no query, no persistence, and no mail-protocol code: `list_accounts` calls the
`MailAccountDirectoryReader` use case and nothing else, `list_emails` calls the `MailboxTimelineReader` use case and
nothing else, `get_email_content` calls the `EmailContentReader` use case and nothing else, `search_emails` calls the
`MailboxSearchReader` use case and nothing else, `ask_mail` calls the `MailboxQuestionReader` use case and nothing else,
`list_contacts` and `get_contact` call the `ContactBookReader` use case and nothing else, and `create_contact`,
`update_contact`, `delete_contact`, and `promote_contact` call the `ContactBookWriter` use case and nothing else.

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

Where a table below says a tool reads every folder of the accounts in scope, "every folder" means every folder the
deployment lets tools read: a folder configuration maps and does not withhold. A folder mapped with
`VisibleToTools: false` and a folder **no mapping names** are both outside all four mailbox tools and are never
mentioned by one — a request naming such an alias comes back empty rather than refused, and an email of it reads as not
found. The decision is made once, where the scope a read is expressed in is resolved, and what it carries is the list of
folders that may be read, so it holds for a tool added later without that tool doing anything, and an account whose
configuration maps no folder reads as empty;
[folders withheld from tools](mailbox-queries.md#folders-withheld-from-tools) states what a caller sees and why nothing
says the folder exists.

Four properties hold for every tool and are proven by test rather than asserted here:

- A call reaches no mail server. Nothing in a tool request speaks IMAP or SMTP, so a request cannot wait on a mailbox
  and cannot set the remote `\Seen` flag, and the mailbox tools read the local copy only. `ask_mail` reaches a chat
  provider, which is a different thing and the one exception to "a call reaches nothing outside this process": it still
  reads mail from the local copy alone and still speaks to no mail server. The three contact writes change local state
  and reach nothing outside the process at all.
- No error and no log line carries a filter value, a mailbox address, a subject, body text, raw MIME, an exception type,
  a stack trace, or an internal identifier. What a boundary withholds is not lost: the detail is logged on the server,
  correlated by the trace the request already carries.
- No result carries raw MIME. Message content itself is a result only where the tool exists to return it:
  `get_email_content` returns bounded bodies and, for a call that asked to describe the attachments, the files under
  bounds of their own; `search_emails` returns bounded extracts of a body; `list_emails` returns summaries and no body
  text at all; and `ask_mail` returns prose written about mail plus the subjects of the emails it cites. Attachment
  content reaches exactly one property of one result, and no other tool publishes any.
- Every tool bounds how much one call can draw out of the database, in the count of items and in their volume alike:
  `list_emails` pages at 100 summaries, `search_emails` windows at 50 ranked matches, `get_email_content` reads at
  most 10 emails under a shared character budget and a shared attachment-byte budget, `ask_mail` publishes by
  default at most 20 000 characters of
  answer citing at most 20 emails, having read at most 20 000 characters of mail to write it, and `list_contacts` pages
  at 200 people. A caller can never raise any of them, and the `ask_mail` set is the operator's to lower or raise in
  [`MailAnswering`](../operations/configuration-ai.md#mailanswering).

One property holds for ten of the eleven and is stated where it stops. `list_accounts`, `list_emails`,
`get_email_content`, `search_emails`, and the six contact tools are within reach of every deployment, because local
state is all they need. `ask_mail` needs two AI providers an operator configures separately, so it is advertised only
while both are configured and working; the [`ask_mail`](#ask_mail) section records what decides that and what a call
meets when it arrives anyway. Whether any of the eleven is offered to a particular caller is a second question, which the
next section answers.

## What a caller is offered

**A caller is served only the tools it may call.** Each tool declares the permission required to reach it, `tools/list`
omits every tool the caller's grant does not permit, and a call naming one of the omitted tools is answered exactly as a
call naming a tool that does not exist: the same JSON-RPC error, the same code, and nothing about the caller, the
credential, the permission, or what a different caller would have been served.

[Which tool each name covers](../operations/permissions.md#which-tool-each-name-covers) is the mapping, one row per
tool, and the page around it is the model those four names belong to: what each one reaches, and why no permission here
implies another. Which grant a credential holds is written on the entry that admits it, and
[the MCP endpoint](../operations/mcp-endpoint.md#what-a-credential-may-do) is where that is configured; a deployment
whose entries write no grant serves every permission to every caller, which is what makes this invisible until an
operator narrows something. An entry that writes no grant but sets `PermissionsFromTokenScopes` is the one exception:
its whole surface is a ceiling rather than a grant, and each token holds only the permission names its own scopes carry
— so a token whose client received none is served an empty listing on an entry nobody narrowed.

The protocol has no field on a tool descriptor for a required permission, and it expressly allows the returned tool set
to vary by the authorization presented on the request — so the listing is where the decision is stated, and no extension
field is invented to say it instead. Nothing caches a listing, so one caller's answer never serves another.

This composes with the availability rule rather than replacing it: a tool may be unavailable, unauthorized, or both, and
no grant makes a capability the deployment does not have appear. A caller granted `mailfathom.mail.ask` is not offered
`ask_mail` on a deployment that answers no questions.

The check runs twice. The endpoint refuses before a use case is reached, and the use case behind each tool asks for the
same permission on its own — so an entrypoint added later reaches the same refusal without passing any of this.

Either of them refusing is recorded, which on this surface is the only place the decision is visible at all: the caller
is told nothing it could report, so a client that stopped working is diagnosed from the deployment's own counter and
warning rather than from what it received. A tool merely withheld from a listing is not recorded, because nothing was
refused. [Telemetry](../operations/telemetry.md#what-an-authorization-refusal-records) holds what each channel carries.

## Descriptor conventions

Every tool is declared with the same deliberate metadata, because a client decides whether a tool is safe to call before
it calls anything:

| Element | Convention |
|---|---|
| `name` | Snake case, as the MCP tool ecosystem spells tool names — `list_accounts`, `list_emails`, `get_email_content`, `search_emails`, `ask_mail`, `list_contacts`, `get_contact`, `create_contact`, `update_contact`, `delete_contact`, `promote_contact` |
| `title` | A human-readable label for display — `List accounts`, `List emails`, `Get email content`, `Search emails`, `Ask about mail`, `List contacts`, `Get contact`, `Create contact`, `Update contact`, `Delete contact` |
| `description` | States what the tool reads or changes, that it reaches no mail server, and what it bounds |
| `inputSchema` | Every argument is a top-level property carrying its own description, unit, and absence meaning |
| `outputSchema` | Generated from the result type, whose properties carry descriptions of their own |
| `openWorldHint` | `false` — every tool is confined to MailFathom-controlled local state |

The remaining three annotations are what a client reads before it decides whether a call needs a human, so they differ
per tool rather than per surface:

| Tool | `readOnlyHint` | `destructiveHint` | `idempotentHint` |
|---|---|---|---|
| `list_accounts`, `list_emails`, `get_email_content`, `search_emails`, `ask_mail` | `true` | `false` | `true` |
| `list_contacts`, `get_contact` | `true` | `false` | `true` |
| `create_contact` | `false` | `false` | `false` |
| `update_contact` | `false` | `true` | `true` |
| `delete_contact` | `false` | `true` | `true` |
| `promote_contact` | `false` | `false` | `true` |

Each of those three values is a fact about the tool rather than a posture. `create_contact` is not idempotent because
the book mints the identity: calling it twice with one person records them once and then answers
`addressHeldByAnotherContact`. `update_contact` is idempotent because an amendment states the whole record, so the
second identical call writes what the first one already wrote — and destructive for that same reason, because stating
the whole record removes an address the caller left out and clears a note it omitted. `delete_contact` is idempotent
too and destructive all the same: erasing somebody twice leaves the state the caller asked for, and the first call
removed a record nothing here can bring back. `create_contact` is the one write that is neither, because it mints a
record where none was held and so has nothing to drop. `promote_contact` is idempotent and not destructive: nothing
about the person is rewritten, what moves is which half of the book they are in, and the second call answers
`alreadyAsserted`. Nothing on this surface is `openWorld`, contact writes included, because a write
here reaches MailFathom's own database and no third party.

The annotations are contract metadata rather than documentation, so `Mcp.UnitTests` asserts the advertised
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
| `51002` | A filter carries a value, a count, or a length the query does not accept | An unusable address, a subject fragment over 256 characters or carrying a control character, a received range that ends before it starts, more than 64 accounts or folders, an account identifier or folder alias that is blank, over 256 characters, or carrying a control character, a keyword over 64 characters or carrying a control character, a search query that is blank, over 512 characters, or carrying a control character |
| `51003` | A search asked for more ranked results than a search serves | A `resultLimit` of 0 or above 50, refused rather than clamped |
| `51004` | The call named an email with text that is no identifier this system issues | A `storedEmailIds` element that is blank, not a UUID, or the all-zero UUID, refused before anything is looked up |
| `51005` | A content read named no emails, or more than one call serves | A `storedEmailIds` list that is empty or holds more than 10 entries, refused rather than truncated |
| `51006` | A content read named the same email more than once | A `storedEmailIds` list carrying one identifier twice, in any spelling, refused rather than served twice or collapsed |
| `51007` | A content read named both ways of selecting what to read, or neither | A call carrying `storedEmailIds` and `threadId` together, or omitting both, refused rather than resolved by precedence |
| `51008` | The call named a conversation with text that is no identifier this system issues | A `threadId` that is blank, not a UUID, or the all-zero UUID, refused before anything is looked up |
| `51009` | A contact listing carries a page size, an origin, or a search the book does not serve | A `pageSize` of 0 or above 200, refused rather than clamped; an `origin` that is neither published name; a `search` over 320 characters or carrying a character that renders as nothing |
| `51010` | The call named a contact with text that is no identifier and no usable address | A `contactId` that is blank, not a UUID, or the all-zero UUID; an `address` that is no address; or a `get_contact` call naming both or neither |
| `51011` | A contact record breaks a rule the book holds | No name or one over 256 characters, no address or more than 32, an address that is not one, a preferred address the record does not name, or a note over 4000 characters — the message names the rule and never the value |
| `52001` | A continuation cursor is not one this system issued | A truncated, hand-written, or foreign cursor |
| `52002` | A continuation cursor was issued for different filters | A cursor reused after a filter or the reading direction changed |
| `52003` | A contact listing's cursor is not one this system issued | A truncated, hand-written, or foreign `cursor`; a contact cursor is not bound to the filters, so changing `search` or `origin` mid-walk is not what produces it |
| `53001` | The call named a mail account this deployment does not serve | An account identifier nobody configured, or one belonging to someone else — the two are deliberately one answer |
| `53002` | The call named an email the local mailbox copy holds no row for | An email never synchronized, one expunged and collected, or one of an account this deployment stopped serving — deliberately one answer |
| `53003` | The call named a folder by a role no folder in scope is mapped with | A `folders` element written `role:Junk` on a deployment whose accounts map no junk folder; naming the alias, or mapping the role, is what answers it |
| `54001` | The call failed for a reason the boundary deliberately does not describe | Anything undiagnosed; the detail is in the server log |
| `55001` | The email exists locally and its stored content is missing, damaged, or unreadable | A local copy being repaired; the call is worth repeating once repair has run |
| `56001` | This deployment cannot answer questions about mail, either at all or for now | `ask_mail` called on a server that declared no chat endpoint or embeds no mail, or one whose chat provider is currently refusing; the message says which |
| `57001` | Answering would cost more than this deployment allows | `ask_mail` on a server whose current period has spent its allowance, or a run that reached what one question may spend; the message says which, and only the first becomes answerable by waiting |

Codes `51001` through `53003`, `55001`, `56001`, and `57001` are the use cases' own, allocated in the MCP-boundary
category because that is
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

The same filter publishes what it measured as instruments, from the one measurement rather than from a second timing
path, so how often each tool is called and how long it takes are readable as a rate and a distribution rather than as a
pile of records. Those are stricter about the tool name still: a name is used as a dimension only when this surface
publishes a tool answering to it, because a dimension a caller can choose is a time series a caller can create.
[Telemetry](../operations/telemetry.md#what-mailfathom-publishes-under-its-own-name) names both instruments and every
outcome they distinguish.

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
| `folders` | One entry per folder this deployment maps and lets tools read, in the same shape `folderFreshness` takes elsewhere: the alias, when synchronization last committed progress for it, and whether it ever has |

**Either name may be used to select the account.** The identifier is matched exactly and the display name without regard
to case, and configuration refuses a display name that another account's identifier or display name already carries, so
a name always names one mailbox. Both spellings resolve to one identity before a query runs, which is why a continuation
cursor issued for one stays valid for the other.

**`synchronizationMode` states what was asked for, not what a folder is getting.** Whether push is served is decided per
folder against what the mail server advertises and how recent attempts went, which is an observation about a run rather
than a property of the account.

**An empty `folders` list is a statement.** It says synchronization has never reached a folder this account lets tools
read — or that it lets them read none — which means its mail may be absent entirely rather than merely out of date, a
distinction an empty listing cannot make for itself. `synchronizationEnabled` answers the other half: `false` means the
timestamps below it are as current as any answer will get, because nothing is advancing them.

**The list is the same set every other tool reads.** It is resolved through the one scope every mailbox read is
expressed in, so a folder mapped with `VisibleToTools: false` and a folder no mapping names are both absent from it —
naming a folder here is publishing that it exists, which is the whole of what this answer does. The account's junk
folder is present, because withholding that one is about not returning its mail unasked and no mail is returned here.

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
| `folders` | `string[]` | Folders to read, each named by its MailFathom alias such as `INBOX` or by the role it plays, written `role:Junk`. Omitted reads every folder of the accounts in scope. Case is normalized, so a repeated spelling names one folder; a role no folder of an account in scope carries is refused with `53003` |
| `senderAddress` | `string` | The whole address the sender must carry, in any case — not a fragment |
| `recipientAddress` | `string` | The whole address a `To` or `Cc` recipient must carry. `Reply-To` is stored and filterable through the use case but not searched here |
| `subjectFragment` | `string` | Text the subject must contain, case-insensitively, up to 256 characters. Wildcards a caller writes match themselves |
| `receivedOnOrAfter` | `date-time` | Inclusive start of the received range |
| `receivedBefore` | `date-time` | Exclusive end, so consecutive ranges built from one instant neither overlap nor leave a gap |
| `isRemotelySeen` | `boolean` | The remote seen state to require. Listing never changes it |
| `isRemotelyFlagged` | `boolean` | The remote `\Flagged` state to require, which is the star a mail client shows. Unrelated to the `Flagged` folder role, which names a folder rather than a flag |
| `keyword` | `string` | One keyword the email must carry, matched whole and without regard to case, up to 64 characters. A value no stored keyword could be is refused with `51002` |
| `hasAttachments` | `boolean` | Whether to match only emails with attachments or only those without |
| `includeJunkMail` | `boolean` | Whether the account's junk folder takes part. Omitted leaves it out, and the result says which of the two answers it gave |
| `direction` | `newestFirst` \| `oldestFirst` | Which end of the timeline to read from |
| `pageSize` | `integer` | 1 to 100. Omitted takes the default of 25; a value outside the range is refused rather than clamped |
| `cursor` | `string` | The `nextCursor` of a previous call, reused with the same filters and direction |

An unbounded date range is deliberately legal; only an unbounded page is not. The page size stops at 100, and the scope
stops at 64 accounts and 64 folders counted while the caller's list is read, so a request that repeats one
identifier a million times is refused after the value that crosses the limit rather than after the list has been
materialized. Every one of those bounds lives in the use case rather than here, which is what makes them hold for an
entrypoint added later; naming one served account repeatedly is legal and is read once.

Both lists are converted to domain values at this boundary, and their counts are checked against the query's own limits
*before* any element is converted — a ceiling applied after the trimming and upper-casing it exists to prevent has
already run over a million-element array is not a ceiling. Text that could name nothing this system issues is then
refused with `51002`, and the refusal never repeats the value.

**The junk folder is left out unless it is asked for.** Mail a filter already set aside is mail written to be read by
somebody who did not ask for it, and a model reasoning over a page of summaries cannot tell it from correspondence, so
the default is the safe one and `includeJunkMail` is how a caller looking for a message a filter took reaches it. The
answer is reported back in `includedJunkMail`, and it takes part in the continuation cursor, so a page and its follower
are always one walk. Which folder that is comes from the account's `Junk` mapping;
[spam classification](spam-classification.md#the-junk-folder-is-left-out-of-listing-and-search) records why the override
can never reveal a folder an operator withheld.

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

`emails` carries the page, `nextCursor` reads the following one and is absent on the last page, `includedJunkMail`
states whether the account's junk folder took part, and `folderFreshness` states how current the local copy of each
covered folder is.

Each summary carries the stable local identifier a content read is performed by, the account identifier and the display
name it is published under, the folder alias, the message identifier, the conversation identifier, the subject, the sender address and display name, the sender verdict, the machine-authorship reading, the `To` addresses, the sent and received timestamps, the
size in bytes, the attachment summary, the remote flags with the time they were observed, and whether raw content is
available locally. It is the use case's projection published as it stands, not narrowed a second time here — a boundary
that re-decided what a listing may carry would put the privacy rule in two places and leave the one a client reads
untested. [Mailbox queries](mailbox-queries.md#what-a-summary-carries) records what it carries and why.

Six parts of it are worth reading before a caller writes against them:

- **`senderVerification` is two answers, never one.** `senderAddress` beside it is a claim the email wrote about itself,
  and nothing on the way to a listing verified it. `authorAuthentication` is what the receiving mail server established
  about the author the email displays — `authenticated`, `failed`, or `notEstablished` — and `deploymentTrust` is
  whether this deployment's own trusted-sender configuration names that author — `trusted` or `unknown`. Neither is
  derived from the other and no field merges them, because **`authenticated` beside `unknown` is the ordinary state of
  legitimate mail from a correspondent nobody has named** and must not read as a finding against the message. `unknown`
  is also what an email whose author failed carries, which is why the pair is read together. Both values are read from
  what synchronization stored; a listing evaluates nothing and contacts no mail server. [Sender
  authentication](sender-authentication.md#what-the-read-tools-publish) records what each value means and what it
  deliberately does not claim.
- **`machineAuthorship` is about the text, not about the sender.** `state` is how much the email's own text reads as
  machine written — `likely`, `possible`, `unlikely`, or `notAssessed` — and `likelihood` is the number that reading
  came from. It is **a heuristic estimate rather than a measured probability, and it is informational rather than a
  safety signal**: `likely` is not a finding against the email or its sender, warrants no action on its own, and says
  nothing about whether the email is wanted, honest, or safe — a great deal of ordinary correspondence is drafted with
  a text generator by people who mean every word of it. It is independent of `senderVerification` and neither is derived
  from the other: that one is about who sent the email and this one is about how its text was written. `notAssessed` is
  what an email with no readable body carries, what a deployment that turned the reading off records, and what mail
  stored before this deployment assessed anything carries; `likelihood` is `0` there and means nothing, so read `state`
  first. [Machine authorship](machine-authorship.md#what-the-read-tools-publish) records what each signal behind it is
  and what the value deliberately does not claim.
- **`toAddresses` and nothing beyond it.** `Cc` and `Reply-To` are searchable but not listed, and recipient display names
  are not returned at all. A listing exists to let a reader recognize a message; the full participant set belongs to
  reading one.
- **`attachments` as a group rather than one flag.** `attachmentCount` and `inlineResourceCount` are separate values
  beside the total size and the encrypted, unverified-signature, and unexpanded-TNEF markers, because MailFathom's
  classification rule does not count an embedded logo or a signature part as an attachment. Without the second count a
  caller could not tell an email carrying a document from one carrying a picture in its signature block.
- **`threadId`, or nothing.** The conversation the message belongs to, which
  [`get_email_content`](#the-conversation-a-message-belongs-to) reads a whole exchange by. It is absent for a message no
  pass has assembled yet, which is what a mailbox synchronized before this release holds until `mfctl mailbox rederive`
  reaches it — never an identifier naming an empty conversation.
- **`contentAvailability` rather than a bare flag.** An email deliberately stored without its MIME reports why, so a
  caller sees that a later content read will not succeed instead of discovering it by making the call — and sees whether
  that is permanent. `exceededSizeLimit` is an email larger than the configured per-message limit, which every later run
  will refuse in the same way; `awaitingStorageHeadroom` is one that arrived while local storage stood at its ceiling,
  whose content a later synchronization run fetches once there is room.

The remote flags carry `wasObserved` beside `observedAt`, because a row a reconciliation window has not reached yet
reports every flag unset and no keyword at all. A caller that ignored the distinction would read "no flag set" where the
truth is "nobody has looked".

Beside the five booleans they carry `keywords`, the flags the protocol leaves to whoever set them — `$Junk`, a label a
mail client wrote. Flag names are compared without regard to case, so they are published in one case rather than in the
one a server happened to write, and `keyword` on both tools folds a caller's value the same way before matching.

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
that fetches each of those attachments. Every email it returns also carries the conversation it belongs to, and a call
may name a conversation instead of naming emails. [Email content](email-content.md) documents the use case behind it —
the representations, the sanitization policy, the two bounds, the attachment default, and what a link authorizes — where
they are enforced. This section describes the surface.

### Arguments

| Argument | Type | Meaning |
|---|---|---|
| `storedEmailIds` | `string[]` | The `storedEmailId` values a listing or a search returned, 1 to 10 of them, each named at most once. Each is a UUID; anything else is refused with `51004` |
| `threadId` | `string` | The conversation to read instead of naming its messages. A UUID; anything else is refused with `51008` |
| `includeSanitizedHtml` | `boolean` | Whether to also return the sanitized HTML body of each email. Omitted returns plain text alone |
| `includeAttachmentDownloadLinks` | `boolean` | Whether to mint a link for fetching each attachment, rather than only describing it. Omitted still returns every attachment's file name, media type, and size |

**Exactly one of `storedEmailIds` and `threadId` is given.** A call carrying both, or neither, is refused with `51007`
rather than resolved by precedence: either reading of a call carrying both returns mail the caller did not ask for —
honouring the list ignores a conversation somebody wanted, and honouring the conversation returns messages nobody named
— and which was meant is the caller's to say. Neither is marked required in the advertised schema, because marking
either one would advertise the other as unusable.

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

Five refusals end the call rather than one entry, because none of them leaves an email to report an outcome against: a
list of more than ten or of none at all is `51005`, a repeated identifier is `51006`, text that names no email is
`51004`, a call naming both selections or neither is `51007`, and text that names no conversation is `51008`. A list is refused rather than truncated or de-duplicated, so a caller never has to compare what came back
against what it asked for to find out what it did not receive.

`51004` and `53002` are therefore deliberately distinct, as are `53002` and `55001`: the first pair separates "you named
no email" from "that email is not here", and the second separates "not here" from "here and currently unservable". Only
the last is worth repeating.

### Result

The result is one `emails` entry per named email, in the order the call named them — or, for a call naming a
conversation, one entry per message it served in the conversation's own order. `unreadThreadMessages` beside them names
the conversation's remaining messages, in that same order, and is empty for a call that named its emails itself. Each
entry names the email and carries exactly one of two things.

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
| `senderVerification` | The same verdict pair a listing publishes: what was established about the displayed author, and what this deployment made of them |
| `machineAuthorship` | The same reading a listing publishes: how much the email's own text reads as machine written, as a band and a number |
| `authorshipEvidence` | What that reading was computed from — the signals the text carried, strongest first, and the weighting they were judged under |
| `headers` | Subject, sent and received timestamps, every participant with its header role, the three threading identifiers, and `senderAuthentication` — the evidence the verdict was reached from |
| `body` | The representations, or the reason there are none |
| `attachments` | One entry per attachment, always: normalized file name, media type, and decoded size, plus a short-lived address to fetch it from when the call asked for one |
| `attachmentCounts` | What the email carries besides its body, returned either way, or `null` when nothing has ever read its parts |
| `remoteFlags` | The flags a server last showed, and when they were read |
| `thread` | The conversation this email belongs to, or `null` when nothing has assembled one for it |

Nine parts of it are worth reading before a caller writes against them:

- **The verdict is beside the headers and its evidence is inside them.** `senderVerification` is the pair a listing, a
  search match, and a citation all publish, in one shape, so a client reads one thing everywhere. What only this read
  adds is `headers.senderAuthentication`: `authenticatedDomain`, the domain that actually authenticated;
  `displayedAuthorDomain`, the domain the `From` header wrote; `authenticatedBy`, the check that established the first —
  `dkim`, `spf`, or `none`; and `dmarc`, the result the trusted server reported. Both domains are published in the
  comparison form MailFathom stores — upper-cased, and an internationalized name in its ASCII form. **A difference
  between them is not by itself a spoofed author:** `authenticatedDomain` is whichever identity authenticated the
  transport, and `dkim` is reported where both checks produced one, so an email sent through a provider that signs as
  itself while `spf` passes for the author's own domain differs here and is authenticated exactly as it appears.
  `senderVerification.authorAuthentication` is the conclusion, and it is reached against every identity that
  authenticated rather than against the one published here. A `null` domain is an ordinary
  outcome rather than missing data: nothing authenticated, or the email wrote no usable `From` mailbox. Nothing here is
  evaluated on the read path, and an email whose raw MIME was never stored carries the same stored verdict as any other.
- **The authorship reading is beside its evidence, and only this read carries the evidence.** `machineAuthorship` is the
  band and the number a listing, a search match, and a citation all publish. What only this read adds is
  `authorshipEvidence`: `signals`, naming what the text carried, strongest first; and `profileRevision`, an opaque
  identifier for the weighting the number was computed under — two likelihoods carrying the same value are directly
  comparable, and two carrying different values are not, so it is read before the numbers are. **The signals divide into
  two kinds worth very different things.** `tagCharacters`, `variationSelectorRun`, `hiddenCharacters`, and
  `bidirectionalOverrides` are facts about the email's characters — it carries text no mail client renders — and are
  close to unambiguous; `formulaicFraming`, `unspacedEmDashes`, `listScaffolding`, and `uniformTypography` are
  observations about style that a careful writer also produces and that mean nothing individually. The list names which
  signals fired and nothing else: no position, no count, and no matched text, so no part of the message reaches a caller
  through it. `signals` is empty and `profileRevision` is `null` on an email nothing assessed.
- **Truncation travels inside each representation, and names the bound.** `plainText` and `sanitizedHtml` each carry
  `text`, `originalCharacterCount`, and `truncatedBy`, because a body and the fact that it is incomplete are never useful
  apart: a model handed only the text would summarize a cut message as a whole one. `truncatedBy` is `none`,
  `bodyCharacterLimit` when this email alone is longer than one call returns, `readCharacterBudget` when the emails
  named before it had already spent the call's total budget — the one case where naming fewer emails at once returns
  more — or `sensitiveContentScanCeiling` when a switched-on scanner analyzed as much of the body as it may and the
  remainder is withheld rather than served unscanned, which no call returns more of. A message can exceed a bound in one
  representation and not in the other, which is why the metadata is not shared between them.
- **The content may come back redacted.** Where the deployment scans mail for sensitive content, what the message's
  author wrote — both body representations, the subject, and the display names of at most the first 40 named
  participants of the email — is scanned on every call and returned with each detection replaced by
  `[redacted:<category>]`. The marker means material of that kind stood there and was withheld; it is never text the
  message contained, and the same call returns the same marker. Every participant past that fortieth name is published
  with no display name at all rather than with one nothing scanned, so on such a deployment an absent `displayName` can
  mean either that the sender wrote none or that the bound was reached. Addresses, identifiers, sizes, flags, and the
  two domains `headers.senderAuthentication` publishes are never redacted, nothing stored is rewritten, and a detector
  that cannot answer fails the call rather than returning unfiltered content. [Sensitive-content
  scanning](sensitive-content-scanning.md#reading-a-message-is-scanned-in-flight) is the whole contract.
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

### The conversation a message belongs to

Every served email carries `thread`, which answers what a reader asks next — what else is in this exchange, and where
does what I am reading sit in it — without returning any of it.

| Field | Meaning |
|---|---|
| `threadId` | The conversation's identifier. Pass it back as `threadId` to read the conversation's messages |
| `position` | The zero-based place this email holds in the conversation's order, or `null` when the conversation was longer than one read assembles and this email fell outside what was assembled |
| `inReplyToStoredEmailId` | The `storedEmailId` of the message this one answers, or `null` when it is a root of what the caller is shown |
| `messageCount` | How many of the conversation's messages the caller may see, this one included |
| `otherMessages` | The conversation's other messages in its own order — each with its `storedEmailId`, `position`, `inReplyToStoredEmailId`, subject, sent timestamp, and sender address |
| `moreMessagesNotNamed` | Whether the conversation holds messages `otherMessages` does not name |

Four things about it are worth reading before a caller writes against them:

- **The other messages are named, never reproduced.** No body, no attachment, and no participant list travels in
  `otherMessages`: it is what a reader picks the next message to open from, and reading one is still a call. The list is
  bounded and `moreMessagesNotNamed` says when it stopped short, which is when reading the conversation itself by
  `threadId` is the call to make.
- **A conversation is assembled from identifiers alone.** Membership follows `Message-ID`, `In-Reply-To`, and
  `References`, and nothing else — never a subject, an address, or a timestamp. So a reply whose sender rewrote the
  subject stays in the conversation, and two unrelated messages sharing a subject never join one.
  [Bringing stored mail up to a later
  release](imap-synchronization.md#bringing-stored-mail-up-to-a-later-release) is what assembles a mailbox stored before
  this release; until it runs, `thread` is absent rather than wrong.
- **Order is the reply relation first, and a timestamp only between siblings.** A reply is published after the message
  it answers whatever the two clocks say, because a sender's clock is not something MailFathom can check; two replies to
  one message are ordered by their sent timestamps, and messages that still tie are settled on their local identity. The
  same conversation read twice comes back in the same order.
- **Withheld mail is absent from all of it.** A message in a folder withheld from tools appears in no `otherMessages`
  list, is in no `messageCount`, and is returned by no call naming its conversation — and a message whose parent is
  withheld is published as a root naming no ancestor, rather than pointing at something the caller may not see. A
  conversation whose messages are all withheld is published nowhere, so asking about its identifier returns no email
  rather than a refusal: telling the two apart would let a caller learn which conversations exist by asking about them.

A call naming `threadId` reads the conversation's messages in that order, bounded by the same ten a caller's own list is
held to, and names the identifiers it did not carry in `unreadThreadMessages`. A second call passing those in
`storedEmailIds` reads the rest.

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
| `queryText` | `string` | **Required.** The text to search for, up to 512 characters, worded in the language the mail was written in. Blank is refused with `51002`, because a search with no text is a listing |
| `accounts` | `string[]` | Accounts to search, each named by its configured account identifier or by the display name it is published under. Omitted searches every account this deployment serves; a name it does not serve is refused with `53001` |
| `folders` | `string[]` | Folders to search, each named by its MailFathom alias such as `INBOX` or by the role it plays, written `role:Junk`. Omitted searches every folder of the accounts in scope; a role no folder of an account in scope carries is refused with `53003` |
| `senderAddress` | `string` | The whole address the sender must carry, in any case — not a fragment |
| `recipientAddress` | `string` | The whole address a `To` or `Cc` recipient must carry |
| `subjectFragment` | `string` | Text the subject must contain, case-insensitively, up to 256 characters |
| `receivedOnOrAfter` | `date-time` | Inclusive start of the received range |
| `receivedBefore` | `date-time` | Exclusive end of the received range |
| `isRemotelySeen` | `boolean` | The remote seen state to require. Searching never changes it |
| `isRemotelyFlagged` | `boolean` | The remote `\Flagged` state to require |
| `keyword` | `string` | One keyword the email must carry, matched whole and without regard to case |
| `hasAttachments` | `boolean` | Whether to match only emails with attachments or only those without |
| `includeJunkMail` | `boolean` | Whether the account's junk folder takes part. Omitted leaves it out, and the result says which of the two answers it gave |
| `resultLimit` | `integer` | 1 to 50. Omitted takes the default of 20; a value outside the range is refused with `51003` rather than clamped |

The structured filters are `list_emails`' own and mean exactly the same things, because both read models apply one
validated selection; the identifier lists are converted and bounded by the same code, so the `51002` refusals described
above hold here word for word. `subjectFragment` and `queryText` are unrelated and the argument descriptions say so: the
fragment narrows which emails are eligible, and the query text is what the eligible ones are matched and ranked against.

The query text is matched rather than translated, and the argument description says so, because the caller writing it is
the only party that knows which languages a question could be about. One text search configuration serves the whole
index — `simple` by default, which neither stems a word nor drops a stop word — so a mailbox holding several languages
is reached by a search per language rather than by one search in the language of the request. [Mail answering § A
question in one language, mail in another](mail-answering.md#a-question-in-one-language-mail-in-another) records what
`ask_mail` does about the same fact on a caller's behalf.

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
| `includedJunkMail` | Whether the account's junk folder took part in this search |
| `folderFreshness` | How current the local copy of each covered folder is, exactly as a listing reports it |

Each match carries `summary`, which is the same shape `list_emails` publishes and is documented above, together with
`relevanceRank` and `snippets`. `senderVerification` and `machineAuthorship` therefore arrive with the summary rather
than as shapes of their own, so a client written against a listing reads a match's sender verdict and its authorship
reading with nothing new.

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
| `folders` | `string[]` | Folders the answer may be drawn from, each named by its MailFathom alias such as `INBOX` or by the role it plays, written `role:Junk`. Omitted draws on every folder of the accounts in scope. Case is normalized, so a repeated spelling names one folder; a role no folder of an account in scope carries is refused with `53003` |

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
| `answer` | The answer, in prose, written in the language the question was asked in, whatever language the mail behind it was written in |
| `citations` | The emails the run retrieved, one entry per email, in the order it first reached each |
| `answerTruncated` | Whether the answer was cut to the length one response carries |
| `citationsTruncated` | Whether the run reached more emails than `citations` names |
| `retrievalTruncated` | Whether the run hit this deployment's ceiling on how much mail one question may read |

Each citation carries the `storedEmailId` a content read is performed by, the account identifier and the display name it
is published under, the folder alias, the subject, the received time, `senderVerification`, and `machineAuthorship`. It deliberately carries
no extract: the passage the run retrieved has already reached a model, and returning it here would put mail content into
a response whose purpose is an answer. The subject and the received time
are what let a reader recognize a message before fetching it.

`senderVerification` is the same pair a listing publishes, in the same shape and without the evidence, so an answer says
what was established about the author of each message it was drawn from. It is what a reader weighs a claim by: an
answer is worth what the mail behind it is worth, and a claim traced to a message whose displayed author failed
authentication is worth reading differently from one traced to a correspondent this deployment recognizes. The evidence
behind the verdict stays with the single-email read the citation points at.

`machineAuthorship` travels beside it on the same terms and answers a different question: how the text of the cited
message reads rather than who sent it. It is informational and is not a reason to discount a citation — mail drafted
with a text generator is as citable as any other, and what it is good for here is the same thing it is good for on a
listing, which is knowing what kind of text a claim came out of. The signals behind it stay with the single-email read.

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
direction.

What it is not is authorization, which is the second and separate reason a listing may withhold this tool. Availability
says what this deployment can do and the grant says what this caller may ask of it; the deployment's own switch is the
authority over the first, so no grant makes a capability it does not have appear. The two are answered differently when
a call arrives anyway: a caller whose grant does not permit `ask_mail` is answered as though no such tool existed, while
one whose grant permits it on a deployment that cannot answer reaches the use case and is refused with `56001`, whose
message says whether this deployment answers no questions at all or answers them and currently cannot.

## The contact book on this surface

Six tools reach MailFathom's own contact book: `list_contacts` and `get_contact` read it, and `create_contact`,
`update_contact`, `delete_contact`, and `promote_contact` write it. [Contacts](contacts.md) is the record and every rule
a writer of it obeys — what identifies a person, when two addresses are the same address, who may amend what, and what
an erasure removes. Nothing of that is restated here; what this section holds is what the tools publish and refuse.

The book is why an agent can answer "who is this from" without being handed a list in a prompt, and the reason the write
half exists rather than only the read half is that a book nobody can add to is one that stays empty. A caller writes as
**asserted** — somebody writing a person down — so a record this deployment collected from arriving mail is not an
agent's to amend in place; `promote_contact` is what it does instead.

**Everything these tools return is personal data about third parties**, and a note is free text somebody typed about
somebody else. A client passes a name, an address, and a note into a model as data, exactly as it does message content.
Nothing on this surface logs any of it, records it as a metric dimension, or writes it into a failure message: what a
contact failure names is the rule that refused it, and the identity, which is MailFathom's own and not the person's.

### Two permissions divide the book

`mailfathom.mail.contacts.read` and `mailfathom.mail.contacts.write` are held the way every other permission is, and
[§ What a caller is offered](#what-a-caller-is-offered) is what a caller not holding one meets — the tool absent from
`tools/list`, and a call to it answered as a call naming a tool that does not exist. What is worth reading twice is how
this book divides: a read-only credential sees `list_contacts` and `get_contact` and no write tool, and one granted
neither sees no contact tool at all, which is also what a deployment looks like to a credential narrowed to
`mailfathom.mail.read`. The mailbox tools are unaffected either way; a grant over the book is not a grant over mail, and
neither is a grant over mail a grant over the book.

Both are on the mail surface, so a credential granted that surface without narrowing holds them already — including an
entry written before this release, which gains them on upgrade because an absent `Permissions` key means the surface
rather than the names published the day the file was written.

Each contact use case asks for the same permission itself, so an entrypoint arriving another way is refused there too —
as `54001`, since a use case does not know what a protocol calls an unknown tool.

## `list_contacts`

Returns a bounded page of the contact book, ordered by name, with the addresses each person uses.

It is the tool for reading the book as a book — who is in it, what this deployment picked up, who matches a fragment of
a name. Resolving one address to the person using it is `get_contact`, which is an index lookup rather than a page of
the book.

### Arguments

| Argument | Meaning |
|---|---|
| `search` | Text a contact must carry in its name or in one of its addresses, matched anywhere in the value and without regard to case, at most 320 characters. A wildcard character matches itself. Omitted, or empty, lists the whole book |
| `origin` | `asserted` or `collected`, narrowing the page to one half of the book. Omitted lists both |
| `pageSize` | From 1 to 200. Omitted takes the default of 50 |
| `cursor` | The `nextCursor` of a previous call |

There is no mode that returns the whole book in one call. A page size outside the range is refused rather than clamped,
which is the same rule every listing on this surface follows.

### Result

| Field | Meaning |
|---|---|
| `contacts` | The page, ordered by the name's comparison form and then by identity, each entry carrying `contactId`, `displayName`, `addresses`, `preferredAddress`, `note`, `origin`, `recordedAt`, and `amendedAt` |
| `nextCursor` | The cursor of the following page, or absent when the walk is done |

**A contact cursor is not bound to the filters.** The book is walked in one order whatever narrows it, so continuing a
walk after changing `search` or `origin` is defined rather than refused — unlike a mailbox cursor, which carries the
filters it was issued for because the ordering it continues is the filtered one. A cursor this deployment did not issue
is `52003`.

**The addresses are the ones somebody wrote**, preferred first and the rest in comparison order. What is matched is the
comparison form; what is published is the spelling the record holds.

## `get_contact`

Returns one person, named either by the identifier the book gave them or by any address they use.

The address form is the question the book exists to answer: a message names an address, and the caller wants the person.
It is served from the unique index over addresses rather than from a search, so at most one contact can answer.

### Arguments

| Argument | Meaning |
|---|---|
| `contactId` | The identifier a listing or a write returned |
| `address` | Any address the person uses, written as the address alone and matched without regard to case |

**Exactly one of the two is named.** Naming neither asks nothing, and naming both can name two different people, leaving
a caller unable to tell which of its questions was answered; either is `51010`.

The address is the addr-spec alone: `anna@example.test` rather than `Anna Kowalska <anna@example.test>`. A header copied
whole is refused rather than read leniently, because the write tools read the addresses of a record against the same
domain rule, and one form across the five tools is what a caller learns once.

### Result

`contact` carries the person, in the shape `list_contacts` publishes, or is absent when the book holds nobody of that
identity or address. Somebody this deployment has no record of is an answer rather than a failure — the same reading
every "not found" on this surface takes when the question was well formed.

## `create_contact`

Records a person the book does not yet hold, and returns the record as written.

### Arguments

| Argument | Meaning |
|---|---|
| `displayName` | The name to record, at most 256 characters |
| `addresses` | Every address this person uses, at least one and at most 32, each at most 320 characters |
| `preferredAddress` | Which of them to use by default. Must be one of `addresses` |
| `note` | What to record about this person, at most 4000 characters, or omitted for none |

Nothing picks a preferred address for the caller, including where the record names a single address: which address is
preferred is the owner's choice rather than an ordering accident, and a record naming one the contact does not hold is
`51011`.

### Result

| Field | Meaning |
|---|---|
| `state` | `written`, or `addressHeldByAnotherContact` |
| `contact` | The record as the book now holds it, present only on `written` |
| `addressHolderContactId` | The identity of one contact already holding an address this write claimed, present only on that state |

**A refusal publishes an identity and never a record.** Reading the book and writing to it are separate grants, so a
caller holding only the writing one must not learn what this deployment holds about somebody by being refused. The
answer names a contact; reading that contact is a `get_contact` call, which the caller makes only if it holds the
reading grant.

## `update_contact`

Amends one contact to the record the caller states, and returns the record as amended.

An amendment is the whole record rather than the difference from the one held — the name, every address, which one is
preferred, and the note. An address the new record does not name is removed, and an omitted note clears the one held, so
a caller reads the contact first and sends it back with the change rather than sending only what moved.

### Arguments

`contactId`, and then the same four the record is stated with: `displayName`, `addresses`, `preferredAddress`, and
`note`.

### Result

The same shape `create_contact` answers with, and two more states it can carry:

| State | What it means |
|---|---|
| `written` | The book holds the record, published in `contact` |
| `notFound` | No contact of that identifier is in the book |
| `addressHeldByAnotherContact` | One of the addresses belongs to somebody else, named by `addressHolderContactId` |
| `contactWasCollected` | The record came from mail that arrived rather than from somebody writing it down |

`contactWasCollected` is the origin rule rather than a failure: the record is taken on first — with `promote_contact`
here, or `mfctl contact promote` at a terminal — and is amendable afterwards.

## `delete_contact`

Erases one person from the book and removes every address recorded with them.

It is destructive and the annotation says so, as `update_contact`'s does for the part of a record an amendment drops.
What is different here is that nothing of the person survives: the record is deleted rather than marked, and nothing
here brings it back. It removes the contact record alone: no mail is deleted, and no mail server is
contacted.

### Arguments

`contactId`, and nothing else. A caller that needs to be sure who it is erasing reads them with `get_contact` first,
because the answer afterwards carries no name, address, or note.

### Result

| Field | Meaning |
|---|---|
| `contactId` | The identity the erasure was asked for, echoed back |
| `wasHeld` | Whether the book held that contact when the erasure ran |
| `addressesErased` | How many of the person's addresses went with them |

The counts are the point rather than a courtesy: erasure is a data-subject obligation, so whoever asked for one is
entitled to an answer saying what was removed. Erasing somebody the book does not hold is a completed erasure with
`wasHeld` false, not a failure — the state the caller asked for is the state the book is in, and reporting it as an
error would only say whether somebody had already erased that person.

## `promote_contact`

Takes on one person MailFathom collected from arriving mail, so the record becomes one the owner asserted.

It is the only path between the two origins and it runs one way. It is also what unlocks `update_contact` on a record
that answered `contactWasCollected`: an agent that read the book and found somebody the deployment picked up takes the
record on for the same owner an operator at a terminal would, and a promotion reachable from only one of the two
surfaces would leave an amendment permanently refused here for every record collection produced. Nothing about the
person is rewritten, and no mail server is contacted.

### Arguments

`contactId`, and nothing else.

### Result

The same shape `create_contact` answers with, and it carries the state alone. `written` means the record is now under
the `asserted` origin; `notFound` means the book holds no contact of that identifier; `alreadyAsserted` means the
promotion had nothing left to do, which is the state a first call left the record in.

**No record comes back, deliberately.** The caller supplied an identifier rather than a person, so answering with the
promoted contact would hand the whole of what `get_contact` serves — the name, every address, the note — to a caller
holding `mailfathom.mail.contacts.write` and nothing else, and no permission here implies another. A caller that also
holds `mailfathom.mail.contacts.read` reads the person through the tool published for reading them. The administrative
route behind the same use case answers the same way and for the same reason.

## Pending

The five mailbox tools are complete. `list_accounts` publishes the accounts a caller may name,
`list_emails`, `get_email_content`, and `search_emails` read the PostgreSQL read models, the lexical index, and the
content store that landed with their use cases, and `ask_mail` answers over the retrieval and the agent composition
above them, under the ceilings an operator sets on what one question and one period may spend. The six contact tools
are complete as well, and are the whole of what this surface writes.

What is still pending is the run trace an operator reads after a question, its own change, and any tool that sends mail:
SMTP delivery is deliberately absent from this tool set, and a contact write is not a step towards it — it changes a
row in MailFathom's own database and reaches nothing outside the process.
