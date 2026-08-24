# Mailbox queries

<!-- describes: backend/src/Application/Accounts/**, backend/src/Application/Emails/ListEmails/**, backend/src/Application/Emails/Mailboxes/**, backend/src/Application/Emails/Summaries/**, backend/src/Application/Folders/**, backend/src/Application/Synchronization/Checkpoints/**, backend/src/Infrastructure/Persistence/Emails/**, backend/src/Infrastructure/Persistence/Synchronization/** -->

MailFathom answers a mailbox listing from its local copy. `ListEmails` is the first read use case: it takes structured
filters, returns a bounded page of email summaries, issues the cursor that continues the walk, and reports how current
the local copy is. It reaches no mail server, so a listing behaves the same whether or not IMAP is available — and it
never touches the remote `\Seen` flag, because it speaks no mail protocol at all.

The protocol adapter is not part of this: `MailboxTimelineReader` is an application use case, and the `list_emails` MCP
tool that publishes it is described by [MCP tools](mcp-tools.md).

[Email search](email-search.md) is the second read use case and applies the same structured filters
this page documents, so nothing about what a filter means is restated there.

## The request contract

`ListEmailsRequest` carries what a caller asked for, unvalidated. `MailboxTimelineReader` turns it into a validated
`EmailTimelineFilter`, so no adapter can reach a query with a filter that skipped validation.

The filters themselves live on `MailboxEmailSelection`, which search applies too; `EmailTimelineFilter` adds the reading
direction and the cursor fingerprint, which belong to a timeline alone. One type and one SQL predicate is what keeps an
attachment filter or a recipient filter from coming to mean one thing in a listing and another in a search — a
divergence neither copy would look wrong for on its own.

| Field | Meaning | Absent means |
|---|---|---|
| `Accounts` | The accounts to list from, each named by its identifier or by its display name | every account this deployment serves |
| `Folders` | The folders to list from, each named by its alias or by the role it plays | every folder those accounts map and let tools read |
| `SenderAddress` | The address the sender must carry, in any case | any sender |
| `RecipientAddress` | The address a `To` or `Cc` recipient must carry | any recipient |
| `SubjectFragment` | Text the subject must contain, compared without regard to case | any subject |
| `ReceivedOnOrAfter` | Inclusive start of the received range | no start |
| `ReceivedBefore` | Exclusive end of the received range | no end |
| `IsRemotelySeen` | The remote `\Seen` state to require | either state |
| `IsRemotelyFlagged` | The remote `\Flagged` state to require | either state |
| `Keyword` | One keyword the email must carry, compared without regard to case | any keyword |
| `HasAttachments` | Whether attachments are required | either |
| `IncludeJunkMail` | Whether the account's junk folder takes part | it does not |
| `Direction` | Which end of the timeline to read from | `NewestFirst` |
| `PageSize` | How many emails the page returns | the default of 25 |
| `Cursor` | The cursor a previous page returned | the first page |

Accounts and folders are named by their domain identities rather than as text, so an adapter converts a caller's strings
once at its own boundary. A folder alias is MailFathom's own name for a folder and is normalized to upper case, which is why
naming `archive` and `ARCHIVE` is naming one folder.

A folder may instead be named by the [role it plays](imap-synchronization.md#what-a-role-says-beside-how-a-folder-is-found),
written `role:Junk`, and the role becomes an alias here rather than at the boundary above: a role means a different
folder on each account, so it is expanded once the accounts in scope are settled and contributes each of their own
folders. A role no account in scope maps is refused with `53003 MailFolderRoleUnmapped`, unlike an alias nothing maps,
which selects nothing — an alias is already a name the caller chose to use, while a role that reached no folder anywhere
would leave a caller reading an empty page as an empty folder.

A listing that names no folder reads every folder the account maps and lets tools read, so a message that exists in two
of them — because it was copied, by
the mailbox owner or by MailFathom — is two entries, one per folder. Nothing collapses them, because a stored row is one
occurrence and no identity spans two;
[what a message MailFathom copied becomes locally](imap-synchronization.md#what-a-message-mailfathom-copied-becomes-locally)
states what that costs and why.

### What each filter accepts, and what it refuses

Refusing beats absorbing throughout: a filter that was truncated or silently dropped would run as a query nobody wrote,
and its result would read as an answer about the mailbox.

- **Page size** is validated, not clamped. A request that names one outside 1–100 is refused with
  `51001 MailboxQueryPageSizeOutOfRange`, because a page clamped to a hundredth of what a client planned for looks
  exactly like the page it asked for. Naming none is a different input and takes the default.
- **Blank text names no filter.** A text filter that arrives empty or as whitespace alone is read as one the request did
  not name, rather than as a value to match: it takes the "absent means" column above. This holds for the addresses, the
  subject fragment, and a keyword alike, so a client that sends a field it left empty gets the unfiltered read it meant
  rather than a refusal. The free-text search query is the one exception, and refuses blank, because a search with no text is a
  listing rather than an unfiltered search.
- **Addresses** are normalized by the domain into the comparison form the persistence layer indexes, so a filter and a
  stored participant are compared in one form by construction. An unusable address is refused with
  `51002 MailboxQueryFilterInvalid` rather than kept as a value that can match no row, and so is one longer than the 320
  characters RFC 5321 allows a forward path — the same bound the stored columns carry.
- **A subject fragment** is bounded at 256 characters and matched anywhere in the subject, case-insensitively. Wildcards a
  caller writes are escaped, so a fragment of `%` matches the character rather than every subject. A control character is
  refused: PostgreSQL text cannot hold a zero byte, so a fragment carrying one would leave the query as a provider
  exception rather than as a stable failure, and no subject anyone searches part of contains one.
- **A keyword** is folded the way the stored keywords were, so a caller writing `$Junk` matches mail whose server
  reported `$junk`, and is matched whole rather than as a fragment. A value no stored keyword could be — longer than 64
  characters, or carrying a control character — is refused with `51002 MailboxQueryFilterInvalid` rather than kept as a
  filter that matches nothing, for the reason an unusable address is. Blank is not such a value: it names no keyword
  filter at all, as the bullet above says of every text filter but the search query.
- **A received range** may be unbounded at either end; only an unbounded page is disallowed. A range whose end is not
  after its start selects nothing and is refused. An email whose received timestamp is unknown falls inside neither
  bound, so naming either one excludes undated mail. Each bound names an instant, so it may be written at any UTC offset
  and the offset chosen changes neither what is selected nor which walk a cursor belongs to: `2026-07-01T10:00:00+02:00`
  and `2026-07-01T08:00:00Z` are one range asked for twice.
- **The scope** accepts at most 64 accounts and 64 folders, counting what a request names rather than what is left
  after deduplication — that is what lets the limit be enforced while the caller's list is read instead of after it has
  been materialized. Both lists are then deduplicated and ordered, so two spellings of one scope are one query with one
  cursor.
- **An account nobody serves** is refused with `53001 MailAccountNotAccessible` before anything is read. One failure
  covers both "no such account" and "not yours", and an empty page is deliberately not the answer: it would confirm the
  name and turn a listing into a way to enumerate accounts. Text matching neither an identifier nor a display name meets
  that same failure, so a caller cannot learn from it which of the two spellings it was holding.
- **A role nothing carries** is refused with `53003 MailFolderRoleUnmapped`, once the accounts in scope are settled and
  before anything is read. One account of several lacking the folder is not the case: that account contributes nothing
  and the read narrows to the others, so the refusal means no account in scope maps it at all. It names the role and no
  account, because a request naming several accounts asked about all of them and naming one would say the refusal was
  about that one.

### Naming an account

An account may be named two ways, and a caller is not required to know which it is holding. The configured `AccountId`
is matched exactly, because it is a key everything else compares exactly; the `DisplayName` it is published under is
matched without regard to case, because it is prose an operator wrote for a person to retype. Neither is ever matched as
a fragment, so naming one account can never select another whose name contains it.

The two are resolved into one identity before a query runs, which is what keeps the rest of this page true: the scope a
cursor is fingerprinted from holds identifiers, so naming the same account both ways is one account, and a cursor issued
for one spelling stays valid for the other. Configuration is what makes this unambiguous — a display name another
account's identifier or display name already carries fails startup — so resolution never has to choose between two
matches. [`list_accounts`](mcp-tools.md#list_accounts) is where a caller learns both names.

### Which accounts an unscoped request reads

Naming no account means every account this deployment serves, and the request is narrowed to that set before anything is
read rather than left without an account predicate. The two are not the same: removing an account from configuration
leaves its stored rows in place, so an absent predicate would keep publishing mail from an account MailFathom no longer
serves. Switching `MailSynchronization:Enabled` off is a different matter and hides nothing — it stops runs from fetching
mail, and the copy already stored stays readable. Switching a single folder's `Synchronize` off is a third thing again:
that folder's stored mail is kept and withheld from every reader rather than erased, which
[what a mapping decides beyond where the folder is](imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is)
states.

A deployment that serves no account at all is a configuration the options accept, and a listing then returns an empty
page with no freshness entries. Every filter is still validated first, so what a request is refused for never depends on
how many accounts happen to be configured.

Because the resolved accounts are what the query runs with, they are also what the cursor's fingerprint covers. A cursor
issued while three accounts were served is therefore refused after one of them is removed, which is correct: the result
set it named no longer exists.

### Folders withheld from tools

A folder mapped with `VisibleToTools: false` is outside every read a tool performs, whatever the request named. It is not
listed, not searched, not answered from, and not readable by identifier: naming its alias in `Folders` narrows the
listing to a folder that is then excluded, so the page is empty rather than refused, and asking for one of its emails by
identifier reports the email as not found rather than as withheld. An attachment link minted before the switch was set
stops serving for the same reason, because the question is asked where the download is served rather than where the link
was issued. None of those answers says the folder exists, because a refusal naming it would publish exactly what the
switch withholds.

**The same list decides what a tool may write.** `set_mail_flags` resolves the email it was given through this scope
before anything is recorded, so a folder the switch withholds is one no caller can mark, star, or label, and what it
meets is the not-found answer above rather than one that says the folder exists. A write surface that reached what a
read withholds would be a way round the switch rather than a second capability.

A folder **no mapping names** gives the same answers, and for a stronger reason: the scope carries the folders a mapping
names and leaves visible, so a read admits that list and nothing else. Mail stored under an alias whose mapping was
later removed is therefore mail no page reaches, no search matches, and no identifier resolves — the rows stay in the
table and stop being readable the moment configuration stops naming their folder. An account whose configuration maps no
folder at all reads as empty rather than as unrestricted, which is what "a folder MailFathom does not have" has to mean
for it to mean anything.

The decision is made once, where the scope every read model is expressed in is resolved, and it narrows by the account
and the alias **together** — one account's mapping never admits or withholds another account's folder of the same name.
It deliberately takes no part in the cursor's fingerprint: a cursor stays valid across the change, and the pages after it
simply stop admitting the folder, which is what an operator withholding it or removing its mapping meant.

Freshness follows the same rule, so a folder a mapping withholds or does not name is absent from the entries a listing
reports rather than present with a timestamp nothing may read behind it.
[What a mapping decides beyond where the folder is](imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is)
states the switch beside the other two, what an unmapped folder is instead, and how to empty one.

### The junk folder, withheld by default and reachable on request

A folder mapped to the `Junk` special use is outside a listing and a search unless the request asks for it. The default
is the one that matters: mail a filter already set aside is mail written to be read by somebody who did not ask for it,
and an agent reasoning over a timeline has no way to tell it from correspondence. `IncludeJunkMail` is the caller's
override, and the result reports which of the two answers it gave, so a reader is never left guessing whether an absent
message was missing or withheld.

This is not the switch above, and the difference is the point. `VisibleToTools: false` is an operator's decision that no
tool may read a folder; the junk exclusion is a default a caller may lift. The two are held apart and a query is handed
their union, so asking for junk can never reveal a folder the operator withheld — a folder that is both is still outside
every read.

The override is the caller's own filter rather than configuration, so it **does** take part in the cursor's fingerprint:
including junk adds rows in the middle of an ordering, and a walk resumed under the other answer would skip or repeat.
The junk folders themselves stay out of the fingerprint, exactly as the withheld folders above do, so mapping one does
not invalidate an outstanding cursor.

`get_email_content` is unaffected: a message reached by its identifier is a message somebody already has in hand.
`ask_mail` excludes junk and offers no override, for the reason
[spam classification](spam-classification.md#the-junk-folder-is-left-out-of-listing-and-search) gives.

### Attachment presence

`HasAttachments` follows the classification rule MIME extraction applies, not a `Content-Disposition` header. A message
whose only non-body parts are inline resources or a cryptographic signature carries no attachments and does not match, so
filtering for mail with attachments does not return every signed message and every message with a logo in a signature
block.

For the same reason the summary reports the counts separately: `AttachmentCount` and `InlineResourceCount` are distinct
values, beside the total attachment size and the encrypted, unverified-signature, and unexpanded-TNEF markers.
[MIME metadata extraction](imap-synchronization.md#mime-metadata-extraction) describes how each part is classified, and
[Stored email schema](../architecture/stored-email-schema.md) which of it the row keeps.

## What a summary carries

`EmailSummary` is the bounded projection a listing returns: the stable local identifier every later request names the
email by, its account and folder alias, the message identifier, the identifier of the
[conversation](../architecture/stored-email-schema.md#the-conversation-a-message-belongs-to) it belongs to, subject,
sent and received timestamps, size, the sender's display name and address, the sender verdict, the machine-authorship
reading, the `To` addresses, the attachment summary, whether raw MIME is stored locally, and the remote flag snapshot.

The conversation identifier is absent for a message nothing has assembled one for, which is what a mailbox stored before
threading existed holds until `mfctl mailbox rederive` reaches it. It is MailFathom's own identifier and carries nothing
a sender wrote, so it is published beside the identifiers rather than scanned with the text.

It carries no raw MIME, no body, and no attachment bytes, and the query that produces it selects only the columns it
publishes — a privacy control before a performance one, because it makes the stored content unreachable through this
contract rather than merely unused by it. What it does carry is personal data and inherits the classification of the mail
it summarizes.

`Cc` and `Reply-To` are stored and filterable but not listed. A listing exists to let a reader recognize a message; the
full participant set belongs to reading it.

The subject and the sender's display name are what a summary carries that a message's author wrote, so they are what a
switched-on sensitive-content scanner redacts before a listing is served. The display name goes with the subject rather
than with the address it accompanies: an address is a routing identity a server issued, while the name in front of it is
free text whoever sent the message chose. The identifiers, addresses, timestamps, and flags beside them are what a
caller acts on, and are protected by who may reach this deployment rather than by redaction. A scanner that cannot
answer refuses the listing. Both switches are off by default, and nothing on this path is scanned then.
[Sensitive-content scanning § the guarded egress
points](sensitive-content-scanning.md#the-guarded-egress-points) holds the contract.

### The sender verdict a summary carries

The sender's address is what the message wrote about itself, so the summary carries beside it what somebody else
established: `SenderVerification`, the pair of *what was concluded about the displayed author* and *whether this
deployment recognizes that author*. Both are read from the columns synchronization wrote. A listing evaluates no policy,
resolves no DNS, and re-reads no header — a query that reached a verdict of its own would answer a different question
from the one the message was stored with. That holds unchanged now that a verdict may have been reached here rather than
by the receiving server: verifying a signature is a step of storing a message, never of answering a query.

The summary carries a second value the listing does not publish: `SenderAuthenticationEvidence`, the domain that
authenticated, the domain the `From` header displayed, which check established the first, the DMARC result, and which of
the two readings produced all of it — the receiving server's `Authentication-Results` header, or MailFathom's own
verification of the message's DKIM signatures where no such header was found. One projection reads both because the
single-email read is built from this same summary and is where the evidence is published; a second query for it would let the two reads disagree about one message. [Sender
authentication](sender-authentication.md#what-the-read-tools-publish) holds what each value means, and
[MCP tools](mcp-tools.md#list_emails) holds the published shape.

Every domain either value names is personal data on the same footing as an address, and neither reaches a log line, a
metric label, or an exception message.

### The machine-authorship reading a summary carries

`MachineAuthorshipAssessment` sits beside the sender verdict and answers a different question: not who sent the message
but how its text was *written*. It carries a band and a likelihood, and — for the single-email read, exactly as the
sender evidence is — the signals the text carried and the weighting they were judged under. Every part of it is read
from the columns extraction wrote, so a listing reads no body, weighs nothing, and re-derives nothing.

It is **informational and never a finding against the message**. Nothing in the query path filters, sorts, ranks, or
refuses on it, and it takes no part in the freshness reporting or the bounds around a page. [Machine
authorship](machine-authorship.md) holds what the reading is made of and what it deliberately does not claim.

### The remote flag snapshot

`RemoteEmailFlagSnapshot` reports the flags a server last showed, together with when they were read. The timestamp is
what separates "the server reported none of these flags" from "nobody has looked yet", which no combination of the
booleans can express on its own. Reconciliation refreshes the snapshot one bounded window per run, so a row it has not
reached yet still carries the never-observed value and matches the unseen side of `IsRemotelySeen` — and `WasObserved` is
how a caller tells the two apart.

Beside the five system flags the snapshot carries `RemoteEmailKeywords`, the flags the protocol leaves to whoever set
them. RFC 9051 compares flag names without regard to case, so they are held in one case, deduplicated, and ordered: two
observations that found the same keywords produce the same value whatever order the server listed them in, which is what
keeps the stored mirror from being rewritten by a server that reorders its own answer. What one email keeps is bounded —
at most 64 keywords of at most 64 characters each — and the excess is discarded rather than failing the window, because
a window exists to record what the server said about mail that is already stored.

The snapshot travels in one direction only. Nothing in any read path turns a flag into an IMAP `STORE`.

### Mail the server no longer holds

Every query here — the timeline, search, and the single-email lookup a content read starts from — excludes an email
reconciliation found gone from its remote folder. The exclusion is written once, and no filter can opt out of it: an
email the server deleted is not part of any mailbox a reader may see. An account configured to erase local copies leaves
no row to exclude at all.

One case is deliberately not excluded, and it is the reason the rule reads two columns rather than one. Where MailFathom
itself deleted the message on your instruction and the account keeps the local copy, the row stays in every query above
although the server no longer holds it — [the authored-delete disposition](imap-synchronization.md#what-becomes-of-a-message-mailfathom-deleted-itself)
is what chooses that, and nothing else produces it.

`IsRemotelyDeleted` is a different question and is not that exclusion. It is reported on every result and filtered on by
nothing, because it is the server's `\Deleted` flag on a message the folder still holds and still serves — a message a
reader can legitimately ask for, and one whose flag they can then read.

## Ordering and pagination

### The order

`EmailTimelinePosition` is the single statement of the order a timeline is read and paged over: received timestamp, then
the stable local identifier as the tiebreaker. The identifier is part of the position rather than a decoration on it,
because a mail server can record several messages within the same instant and a page boundary computed from a
non-deterministic order silently skips or repeats rows.

Undated mail sorts at the far end of whichever direction is read: last when the newest is read first, first when the
oldest is. `OldestFirst` is `NewestFirst` reversed exactly rather than a second decision, which is what makes a cursor
taken in one direction name the same boundary in the other.

The timeline indexes on `stored_emails` reproduce that order column for column, including the `NULLS LAST` PostgreSQL
would otherwise invert; [Stored email schema](../architecture/stored-email-schema.md) records the index definitions. EF
Core publishes no way to state a null sort order in a query, so the read model expresses the same placement as a leading
ordering key. Whether PostgreSQL can then serve that expression from the timeline indexes without a sort step is a
query-plan question the integration suite answers, and the answer there is a matching expression index rather than a different
order here.

**A scope naming several accounts is read as a walk per account, appended.** The timeline index leads with the account,
so it serves one account as an ordered walk and a keyset page costs the page. Across several accounts that ordering is
not something a containment over the account list preserves, so one predicate over the list is planned as a read of
everything that matched followed by a sort of all of it — which is what a mailbox of tens of thousands of messages per
account turns a bounded page into. So each account is walked on its own index, bounded to the page size, and the walks
are appended and ordered together: what the outer ordering sorts is at most one page per account rather than everything
that matched. Each walk takes the whole page rather than a share of it, because the page may come entirely from one
account. A scope naming one account composes no merge at all and issues the query it always has.

### The cursor

Pagination is keyset-based, never offset-based: the next page asks for rows beyond a known boundary, so mail arriving
between two requests neither shifts a window nor causes a row to be skipped or repeated. Paging with stable filters
therefore visits every row exactly once, including across equal timestamps and across undated mail.

The cursor is an opaque string pairing that boundary with a fingerprint of the filters and reading direction it was
issued for. It carries no secret and needs no signature, because every value in it is one the caller already supplied or
already received; encoding it is about opacity — a client that cannot read a cursor cannot build one, and building one is
how a caller would end up asking for a boundary this system never computed.

The fingerprint is computed over a text in which every value carries its own length in front of it. A folder alias may
contain a comma, so joining a list with one would let `["ARCHIVE,SENT", "TRASH"]` and `["ARCHIVE", "SENT,TRASH"]` produce
the same text — and a cursor accepted across those two scopes names a real row in the wrong result set, which is the one
failure the fingerprint exists to prevent.

- A cursor presented against **different filters or a different direction** is refused with
  `52002 MailboxQueryCursorFilterMismatch`. It would still name a row, which is exactly why honoring it would be wrong:
  the caller would receive an arbitrary window of the new result set and would have no way to notice.
- A cursor presented with a **different page size** continues the same walk. Page size moves no boundary and is
  deliberately not part of the fingerprint.
- Anything else — truncated, hand-written, or from a build whose cursor format differs — is refused with
  `52001 MailboxQueryCursorMalformed` rather than interpreted as far as it parses.
- A **blank** cursor is the first page rather than a malformed one.
- Two requests that select the same emails in the same order share a fingerprint, including when they name the same
  accounts in a different order or write a subject fragment in a different case.

The absence of a cursor in a result is the end of the result set rather than a hint: the reader establishes it by asking
storage for one row beyond the page and finding none. A present cursor never promises that the next page is non-empty —
mail can be expunged between two requests — but continuing from it can never skip or repeat a row.

## Freshness reporting

Every result carries one `MailboxFolderFreshness` entry per folder in the request's scope, each reporting when
synchronization last durably committed progress for that folder or that it never has. Without it a caller cannot tell a
folder that holds no matching mail from one whose synchronization has been failing for a week, and both look like an
answer about the mailbox.

A folder with no checkpoint is reported with no timestamp rather than omitted, because it is the folder whose staleness a
caller most needs to see. An alias that has been bound to several remote folders over time reports the most recent
progress of any of those bindings, which is what "how current is this alias" means to a reader.

PostgreSQL performs that aggregate, so the rows crossing the boundary number one per alias in scope. The count of
historical bindings behind an alias grows every time a server recreates the folder, and grouping them in process would
make an ordinary listing pay for that history; the result itself is bounded by the folders of the accounts in scope, which
configuration bounds.

### The account-level reading

A reader deciding whether to trust a mailbox at all needs the same fact one size larger, and `MailAccountFreshnessReader`
is where the per-folder entries are reduced to it: one instant per account and one state. The instant is the newest of
the account's folders, because it answers "when did this mailbox last take anything in" — the oldest would hold a whole
account at the beginning of time over one folder that has been empty since it was mapped. The state is
`MailAccountSynchronizationState`, which says whether this deployment's most recent finished run of the account failed,
succeeded, or has never happened; that comes from the run ledger rather than from a checkpoint, so it describes the
running process and resets with it, exactly as
[the administrative status route's](../operations/admin-endpoint.md) phase and failure count do.

The two facts stay separate because they answer different halves of one question, and the separation is what a reader
needs: an account failing since yesterday and an account nobody has written to since yesterday carry the same instant.
The result is one line per account and grows with nothing else — not the folders, not the messages, not the runs. It is
what [`GET /api/client/accounts`](../operations/client-endpoint.md#the-accounts-route) publishes.

## Where the pieces live

- `MailFathom.Application.Emails.ListEmails` — the use case, its request, and its result.
- `MailFathom.Application.Emails.Mailboxes` — `MailboxEmailSelection` and the timeline filter that wraps it, the cursor,
  the page size, and the query failures shared with the other read models. `MailboxScopeResolver` is here too: it
  resolves the accounts a read runs against and refuses one this deployment does not serve, and it is a collaborator
  rather than a step inside the use case because the refusal is an access decision every read model has to make
  identically.
- `MailFathom.Application.Emails.Summaries` — the summary a read model publishes and the two reader ports that produce
  it.
- `MailFathom.Application.Accounts` — the two account catalogs, `MailAccountDirectoryReader`, the one use case that
  publishes an account set rather than bounding a read with it, and `MailAccountFreshnessReader`, which reduces that
  answer to one line per account for a reader that wants the mailbox list rather than the folders. The second is composed
  over the first rather than written beside it, so whose accounts these are, which folders count, the grant that is
  required, and the read that is recorded are decided in one place. `IDeploymentMailAccountCatalog` describes every account
  this deployment serves, and `ConfiguredMailAccountCatalog` implements it over the bound mail section, so that answer
  comes from the configuration that defines the accounts. `ICallerMailAccountCatalog` describes the accounts the caller
  in hand *owns*, and it is the one every caller-facing read resolves through: its single member answers both questions
  asked of it, whether the account a request named is accepted and which accounts an unscoped request is narrowed to.
  The two are told apart by the members they publish rather than by a flag, so a read model reaching the wrong one names
  something the right one does not have.
- `MailFathom.Application.Synchronization.Checkpoints` — the freshness port and its read model, kept separate from the
  readers that return mail because every read model attaches freshness.
- `MailFathom.Infrastructure.Persistence.Emails` — `StoredEmailTimelineReader`, which evaluates every filter, the keyset
  boundary, the ordering, and the row limit in PostgreSQL and tracks no entities. `StoredEmailSelectionPredicate` is the
  filter predicate, shared with search, and `StoredEmailSummaryRow` carries the column list and the mapping, shared with
  search and with the single-email lookup. Both are written once because each is a control that decides what a mailbox
  read can return at all: a second copy would have to be found and read before anyone could say what that is.
- `MailFathom.Infrastructure.Persistence.Synchronization` — `SynchronizationFreshnessReader`, which answers the freshness
  the timeline attaches from the same database and under the same no-tracking rule.
