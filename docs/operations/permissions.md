# What a credential may do

<!-- describes: backend/src/Domain/Access/**, backend/src/Application/Access/**, backend/src/Host/Configuration/Access/TransportAuthenticationOptions.cs, backend/src/Host/Configuration/Access/OwnerFacingAuthenticationOptions.cs, backend/src/Host/Api/Client*.cs, backend/src/Host/Security/Endpoints/**, backend/src/Host/Security/Transport/**, backend/src/Mcp/Tools/PublishedTools.cs -->

Authentication decides whether a caller reaches a surface at all. What it may then do is a **permission**: a named
capability MailFathom publishes, written on the `Authentication` entry that admitted the caller, checked by the use
case behind every operation, and counted under its own name when a caller is refused.

This page is the whole model. The names, what each one reaches, how a grant is written, what an unwritten grant means,
and what a refused caller is told are all here; the pages that configure a listener, publish a tool, or serve a route
link back to this one rather than restating it.
[ADR 0012](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0012-authorization-model-named-permissions-and-where-they-are-enforced.md)
is the record of why the model has this shape.

## The published set

The set is **closed**: every name a grant can carry has a check behind it, so a misspelling fails startup rather than
reading as a narrower grant than you meant. A name is `mailfathom.<surface>[.<subject>].<verb>`, lowercase and
dot-separated, and always a valid OAuth scope token so the same string can travel in a token's `scope` claim. It has
two disjoint halves, and the prefix after `mailfathom.` says which half a name belongs to.

| Permission | Half | What it covers |
| --- | --- | --- |
| `mailfathom.mail.read` | mail | The tools that read the local mailbox copy: `list_accounts`, `list_emails`, `get_email_content`, `search_emails`. On the client endpoint it also reaches [`GET /api/client/accounts`](client-endpoint.md#the-accounts-route) and [`GET /api/client/folders`](client-endpoint.md#the-folders-route), which name the signed-in owner's own accounts and folders and how current each of them is, and [`GET /api/client/emails`](client-endpoint.md#the-mail-list-route) with [`GET /api/client/emails/search`](client-endpoint.md#the-mail-search-route) [`GET /api/client/threads/{threadId}`](client-endpoint.md#the-conversation-route), [`GET /api/client/messages/{storedEmailId}`](client-endpoint.md#the-message-route), [`GET /api/client/messages/{storedEmailId}/body`](client-endpoint.md#the-message-body-route), and [`GET /api/client/messages/{storedEmailId}/attachments/{position}`](client-endpoint.md#the-attachment-route), which are the owner's mail itself — subjects, correspondents, the opening of each message's text, the extracts a search cuts around what matched, one whole conversation at a time, one message's headers with what was established about its author and what it carries, one message's whole body as the document a reading pane draws and as the words beside it, and the octets of any file it carries.It also reaches [`GET /api/client/record`](client-endpoint.md#the-record-routes), which is that owner's own record as the deployment holds it, secrets redacted — reading which mailboxes are read for you is part of reading your mail, and changing them is [`mailfathom.mail.accounts.write`](#the-published-set) below. It is also the whole of what the two [`/api/client/preferences`](client-endpoint.md#the-preferences-routes) routes ask, the write included: those hold what a person set about their own client rather than what this deployment reads for them, so somebody whose mail accounts an administrator maintains — and who therefore does not hold `mailfathom.mail.accounts.write` — still turns their own telemetry off. Where semantic retrieval is configured, searching places the caller's own query text with the embedding provider, so this is not an egress-free grant |
| `mailfathom.mail.ask` | mail | `ask_mail`, which answers from mail content by sending it to a model provider. It does not imply `mailfathom.mail.read`, and granting it is granting access to mail |
| `mailfathom.mail.flags.write` | mail | `set_mail_flags`, which marks mail read or unread, stars or unstars it, and writes its keywords. It is the one MCP grant whose effect reaches the owner's mail server, and it does not follow from reading mail: a deployment that lets an agent read has not thereby let it change anything |
| `mailfathom.mail.drafts.write` | mail | `save_draft`, `update_draft`, and `delete_draft`, which write a message into the owner's own drafts folder, replace it, and take it back out. It is the safe half of authoring mail and is its own name because that half is worth granting on its own: a draft is delivered to nobody, is withdrawn by deleting it, and lands in a folder the owner already reads, so an agent holding this and nothing else can prepare mail whose worst failure is a message in Drafts. It does not imply `mailfathom.mail.send` and is not implied by it — `send_draft` is admitted under the sending grant, so a caller holding this name alone cannot make a draft leave. Its effect does reach the owner's own mail server, which is `mailfathom.mail.flags.write`'s reach rather than a send's. A draft answering stored mail needs `mailfathom.mail.read` beneath it as well, because an answer is derived from the message it answers. On the client endpoint it reaches [the drafts routes](client-endpoint.md#the-drafts-routes) — listing, opening, composing, revising, giving up, and staging or removing a file — which are the same acts on the same drafts in the same folder, so a person's client and an agent draw on one name rather than two |
| `mailfathom.mail.send` | mail | Asking this deployment to send mail from an account it holds. It is the one grant here whose effect leaves the deployment and cannot be recalled, which is why it follows from nothing: reading a mailbox is not writing from it, and marking mail reaches the owner's own server rather than a stranger's. It covers `send_email`, which queues a message for a mailbox this deployment holds, and `reply_to_email` and `forward_email`, which queue one anchored to mail it already holds — those two also need `mailfathom.mail.read`, because an answer is derived from the message it answers. It covers `get_outgoing_email` and `cancel_outgoing_email` as well, which report what became of a queued send and stop one that is still waiting: a caller may read back and withdraw exactly what it was allowed to queue, and what this mailbox has written to whom is not something the reading grant confers. It covers `send_draft`, which queues the message a draft holds, and the promotion beneath it, because promoting a draft is asking for mail to leave. On the client endpoint it reaches [`POST /api/client/drafts/{draftId}/send`](client-endpoint.md#the-drafts-routes) and [the outbox routes](client-endpoint.md#the-outbox-routes), which are the same act and the same reading back and withdrawal the tools above are, plus a paging over the caller's own queued sends that no tool offers — an enumeration is a person's screen rather than an agent's. It is asked for again by the use cases behind all six and by the outbox beneath the four that send, so it governs the act rather than only the tool |
| `mailfathom.mail.accounts.write` | mail | Maintaining the signed-in owner's own record: [`POST /api/client/record`](client-endpoint.md#the-record-routes), [`POST /api/client/record/mail-accounts`](client-endpoint.md#the-record-routes), and [`POST /api/client/record/mail-accounts/removal`](client-endpoint.md#the-record-routes), which save the whole record, declare one more mailbox in it, and stop it declaring one. It is separate from `mailfathom.mail.read` for the reason every write here is: reading somebody's mail is not deciding which mailboxes this deployment reads for them. It reaches that owner's record and no other — the routes name nobody, so the caller's own identity is the whole of whose record is changed — and it withdraws no mail: a mailbox a record stops declaring keeps everything already stored for it. Nothing under this name reaches the roster: recording an owner, relabelling one, erasing one, and adopting one are administrative, and no owner-facing grant lists who else this deployment serves. It publishes no MCP tool |
| `mailfathom.mail.contacts.read` | mail | `list_contacts` and `get_contact`, which read the deployment's own contact book: names, addresses, and the notes an owner wrote about identified third parties |
| `mailfathom.mail.contacts.write` | mail | `create_contact`, `update_contact`, `delete_contact`, and `promote_contact`, which record, amend, erase, and take on a person in that book. The erasure is here rather than apart, because a grant that cannot edit the book cannot be trusted to take somebody out of it |
| `mailfathom.admin.read` | administrative | The reads reporting the deployment's own state and no mail: what synchronization is doing per account and per folder, embedding status and the activation preview, the loaded rules, a run's progress, what a rewind would cost, where a re-derivation has got to, the stopped-job list, the outbox counted by stage and listed without naming anybody, and which owners this deployment holds records for, what each of them signs in with, what each one's record declares, and what adopting one would move out of the files. The roster and the owner records are here and nowhere else: no owner-facing grant reads them, so a person signed in to this deployment cannot learn who else it serves. It also reads the deployment's **own configuration** — the three routes under `/api/admin/configuration`, which report every setting the deployment composed with the layer that decided each one, the persisted document an editing session opens, and what an adoption would copy. Read without a prefix that is the whole composed configuration: authorization-entry names, OAuth issuers and authorized subjects, provisioned file paths, mail account addresses, and folder mappings. A secret-bearing setting and a bootstrap-only one report the redaction marker rather than their value, and so does a variable of the host process that no MailFathom section names — but the rest is read in full, so a credential holding this name is one that may read how the deployment is put together |
| `mailfathom.admin.audit.read` | administrative | Everything derived from somebody's mail: the mailbox-mutation audit, the answering audit, the rules history, the spam classifications, one queued message with the addresses it is offered to, and reading the contact book — a listing, one person, or their export |
| `mailfathom.admin.operate` | administrative | Asking the deployment to do work it can already do: running rules over an account, classifying an account, retrying or dropping a stopped job, cancelling a reindex, rewinding synchronization, re-deriving stored mail, carrying stored content into the object backend, withdrawing or re-queueing one queued message, and writing to the contact book |
| `mailfathom.admin.credentials.write` | administrative | Minting and withdrawing the ways into a mailbox: storing a mailbox refresh token, and provisioning, rotating, suspending, or removing [an owner's username and password](admin-endpoint.md#owner-credentials). Reading which credentials exist is `mailfathom.admin.read`, so a credential granted the state reads has not thereby been given one that can sign in as somebody |
| `mailfathom.admin.spend` | administrative | Activating the declared embedding model, which is the one operation that starts a provider bill |
| `mailfathom.admin.erase` | administrative | Disposing of what this deployment holds: the mail stored for a folder an account no longer mirrors, one person and everything the contact book derived from them, the database copies a finished move left beside the objects it verified, and **an owner together with every message, folder, attachment, and derived index this deployment holds for them**. That last one is why erasing an owner is not `mailfathom.admin.configuration.write`: recording somebody decides what the deployment reads next, and removing them destroys what it already read |
| `mailfathom.admin.configuration.write` | administrative | Changing the deployment's own [persisted configuration](configuration-sources.md#changing-a-persisted-setting): persisting a setting, stopping the document carrying one, saving an edited document, and adopting what the files decide beneath a path. It also covers **who this deployment serves and what it reads for them** — recording an owner, relabelling one, saving one owner's record, declaring or withdrawing a mailbox in it, and [adopting an owner](admin-endpoint.md#owners-and-their-records) so their mailboxes stop being decided by the files. Reading the settings and the records is `mailfathom.admin.read`, so a credential may be told where a value is decided without being able to decide it |

**Three surfaces draw on those two halves.** The MCP endpoint and the client endpoint each draw on the mail half — the
client reads the mail an agent reads, and a second vocabulary for one authority would be two things to keep in step —
and the administrative endpoint draws on its own. Every rule below that says a grant belongs to *the surface it is
written on* means the half that surface draws on, so a name written on one mail-half endpoint is refused on the
administrative one and accepted on the other.

**No permission implies another**, so a credential that needs to read state and to run rules is granted both names. An
implication table would be a second set of rules to keep true, and writing two names is cheaper than remembering which
one carries which. `mailfathom.mail.ask` in particular is not the weaker of the mail pair: a cited answer returns mail
content. What withholding it stops is mail content going to a *chat* provider on a caller's behalf, which is not the
same as making `mailfathom.mail.read` egress-free — a deployment with semantic retrieval configured places the caller's
own query text with the embedding provider before anything is read back.

`mailfathom.mail.flags.write` is separate from `mailfathom.mail.read` for the reason no permission here implies
another, and the separation is the useful one on this surface: reading a mailbox and changing it are different acts with
different consequences, and only the second is visible to the owner in the client they open. It carries no read of its
own either — a credential granted it and nothing else can change an email it can name and cannot list one.

`mailfathom.mail.send` is separate from both for a stronger version of the same reason. A flag change reaches the
owner's own mail server and is undone in their own client with the gesture that would have made it; a send reaches
somebody else's mailbox and nothing here can take it back. So the grant that reads a mailbox does not carry it, the
grant that writes to one does not either, and it carries no read of its own: a credential granted it and nothing else
can send from an account it cannot list.

`mailfathom.mail.drafts.write` sits between the two and is the reason the pair is not one name. Writing a draft is
authoring mail and is the half that reaches nobody: the message goes into the owner's own drafts folder, the owner
reads it in the client they already open, and deleting it takes it back. So a deployment that wants an agent to prepare
mail for a person to send grants this name and withholds `mailfathom.mail.send`, and the agent is then offered
`save_draft`, `update_draft`, and `delete_draft` and is not offered `send_draft` at all. It carries no read of its own
either, which matters more here than elsewhere: a draft answering stored mail is composed from that mail, so a
deployment that means an agent to draft replies grants `mailfathom.mail.read` beside it, and one that grants this name
alone gets an agent that can write a message of its own and cannot answer anything.

The contact permissions are separate from the mailbox ones and from each other, because the book is a different body of
personal data from the mail: an assembled record about identified third parties rather than correspondence that
arrived, and a credential that may look somebody up is not thereby one that may erase them. On the administrative half
the seven names are allocated so that the separations an operator would plausibly want are the ones they can make:
reading state, reading what was derived from mail, causing work, placing a credential, starting a bill, destroying
what the deployment holds, and changing what the deployment itself is.

**A name is published when the capability exists.** Adding one is a configuration-schema change under
[ADR 0004](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md),
made when there is an operation for it to govern rather than ahead of one, because a grant naming a capability the
deployment does not have is a grant that means nothing.

The operation rather than the tool is what that rule counts, and `mailfathom.mail.send` is the worked example of it.
It was published while no tool required it, because the use case that writes a send down already existed, was reached,
and refused a caller that did not hold the name; `send_email` then changed what the same grant reaches rather than what
an operator has to write. A name allocated that way governs something from the day it is published, which a name
reserved for a capability nobody has written would not.

## Which tool each name covers

Each MCP tool declares the one permission required to reach it, and a caller is served only the tools its grant
permits. [MCP tools](../features/mcp-tools.md#what-a-caller-is-offered) describes what that does to a listing and to a
call.

| Tool | Permission it requires |
|---|---|
| `list_accounts` | `mailfathom.mail.read` |
| `list_emails` | `mailfathom.mail.read` |
| `get_email_content` | `mailfathom.mail.read` |
| `search_emails` | `mailfathom.mail.read` |
| `set_mail_flags` | `mailfathom.mail.flags.write` |
| `send_email` | `mailfathom.mail.send` |
| `reply_to_email` | `mailfathom.mail.send`, and `mailfathom.mail.read` beneath it |
| `forward_email` | `mailfathom.mail.send`, and `mailfathom.mail.read` beneath it |
| `get_outgoing_email` | `mailfathom.mail.send` |
| `cancel_outgoing_email` | `mailfathom.mail.send` |
| `save_draft` | `mailfathom.mail.drafts.write`, and `mailfathom.mail.read` beneath a draft that answers stored mail |
| `update_draft` | `mailfathom.mail.drafts.write`, and `mailfathom.mail.read` beneath a draft that answers stored mail |
| `delete_draft` | `mailfathom.mail.drafts.write` |
| `send_draft` | `mailfathom.mail.send` |
| `ask_mail` | `mailfathom.mail.ask` |
| `list_contacts` | `mailfathom.mail.contacts.read` |
| `get_contact` | `mailfathom.mail.contacts.read` |
| `create_contact` | `mailfathom.mail.contacts.write` |
| `update_contact` | `mailfathom.mail.contacts.write` |
| `delete_contact` | `mailfathom.mail.contacts.write` |
| `promote_contact` | `mailfathom.mail.contacts.write` |

The six rows naming `mailfathom.mail.send` are the ones worth reading twice. It is the only grant here whose effect
leaves this deployment and cannot be recalled, and the mapping is what withholds those tools from every other
credential: a caller without the name is not offered the descriptor and is answered as if the tool did not exist. Two
further checks stand behind it — the use case, and the outbox it writes through — so an entrypoint added later meets the
same refusal without passing this table.

**`get_outgoing_email` and `cancel_outgoing_email` take the sending grant rather than the reading one, and that is the
disclosure decision rather than a convenience.** What a read of a queued send answers is who this mailbox wrote to and
when a server accepted or refused each of them, which is a fact about the mailbox's outgoing correspondence rather than
about mail it received — so a credential given a mailbox to read is not thereby one that learns it, and there is no
listing on that surface to reach it through in bulk either. Withdrawing takes the same name from the other direction:
what a caller may stop is exactly what it was allowed to start, so no grant of its own is minted for taking back what
one grant already permitted. Both are additionally confined to what the calling principal queued, which is a scope
rather than a permission: a send another caller queued is answered exactly as one nobody queued, and no grant widens
that.

**`reply_to_email` and `forward_email` are the two rows that name a second grant, and it is a requirement rather than a
note.** The sending grant is what the descriptor declares and what narrows the listing, because sending is the effect
that leaves. What the use case beneath asks for as well is `mailfathom.mail.read`, since an answer is derived from the
message it answers — it quotes it, threads it, addresses it from its headers, and carries its files. No permission here
implies another, so a credential holding only the sending grant is offered both tools and refused by the use case behind
them: a deployment that means an agent to reply grants both names on the same entry. Which of the two refused is not a
distinction the caller is given, and the deployment's own counter and warning are where it is read.

## Which administrative route each name covers

Every administrative route publishes the one permission it requires as endpoint metadata, and
[what the endpoint serves](admin-endpoint.md#what-the-endpoint-serves) states it route by route beside what each route
does. Two consequences of that mapping belong here rather than to any one route.

**Twelve `mfctl` commands make two requests and therefore need two permissions**, because what such a command reads is
what it puts in front of you or amends from:

| Command | Needs |
| --- | --- |
| `mfctl contact update`, `mfctl contact add-address`, `mfctl contact remove-address` | `mailfathom.admin.audit.read` beside `mailfathom.admin.operate` |
| `mfctl contact delete` | `mailfathom.admin.audit.read` beside `mailfathom.admin.erase` |
| `mfctl mailbox rewind` | `mailfathom.admin.read` beside `mailfathom.admin.operate` |
| `mfctl content move` | `mailfathom.admin.read` beside `mailfathom.admin.operate` |
| `mfctl content release` | `mailfathom.admin.read` beside `mailfathom.admin.erase` |
| `mfctl embedding activate` | `mailfathom.admin.read` beside `mailfathom.admin.spend` |
| `mfctl config set`, `mfctl config unset`, `mfctl config edit`, `mfctl config adopt` | `mailfathom.admin.read` beside `mailfathom.admin.configuration.write` |

A credential granted only the permission the operation itself is published under meets the refusal at the first request
and nothing is done — the safe half of it, and still not what the operator intended.

The four configuration writes read for a reason of their own: what they read carries the version the change is composed
over, and a version fetched apart from the values it describes is the lost update the deployment's version guard exists
to refuse. `mfctl config get` and `mfctl config show` make one request and need `mailfathom.admin.read` alone.

**A command acting for an owner makes one more request when the invocation names none.** Every `mfctl owner` command
but `list` and `add` takes `--owner`, and does not need it on a deployment holding one person: without it the command
reads the roster and acts for the sole owner it finds, which is one `mailfathom.admin.read` request beside the
permission the act itself is published under. Passing `--owner` skips that lookup and nothing else — the named owner is
sent as written, and the deployment refuses one it holds no record for.

Skipping the lookup is not the same as making the command need one permission. Six of them read something else
unconditionally, whether or not `--owner` was passed, so each needs `mailfathom.admin.read` beside its own name in every
invocation:

| Command | Reads, before the act |
| --- | --- |
| `mfctl owner show` | the record it prints |
| `mfctl owner account add`, `mfctl owner account remove` | the record the change is composed over, and the version it is composed at |
| `mfctl owner adopt` | what an adoption would move, which is what it asks you to confirm |
| `mfctl owner remove` | the roster, so the confirmation names the person being erased |
| `mfctl owner rename` | the roster, so a label a declaration will rewrite at the next start is reported as one that lasts until then |

`mfctl credential delete` reads the credential listing too, to name what it is about to delete, and is the one command
that goes on without it: a caller refused that read is told which credential it holds no name for and deletes it
anyway.

**`GET /api/admin/session` sits outside the model and needs no permission.** It reports the credential the caller
already presented, the version this deployment already publishes, and the permissions that credential holds, all of
which the caller brought or may always ask about itself; and it is what every command reads first, `mfctl login`
included. Requiring a permission for it would make that permission a component of every administrative grant, so a
credential granted only the spend permission could not sign in to use it. A credential granted nothing therefore still
answers here and nowhere else; an operator who wants nothing answered at all removes the entry.

## The outbox is split by what a reading names

Its five routes take three of the names, and the line between the first two is drawn by what the answer contains rather
than by what the request asks for. Counting the stages and listing what is queued are `mailfathom.admin.read`, because
neither answer names a recipient or a subject; reading one queued message is `mailfathom.admin.audit.read`, because it
reports the addresses the message is offered to and what each of their servers said; withdrawing one and offering one
again are `mailfathom.admin.operate`, because both change what leaves the deployment and the second may put a copy in
somebody's mailbox.

A monitoring credential granted `mailfathom.admin.read` therefore watches an outbox it is never served an address from,
which is the arrangement the split exists for.

## The contact book draws from both halves

The book is reachable from both surfaces, and it needed a name of its own on neither. On the administrative surface,
reading the book, reading one person, and exporting them are `mailfathom.admin.audit.read`, because what the book holds
is derived from mail; recording, amending, and promoting are `mailfathom.admin.operate`; erasing somebody is
`mailfathom.admin.erase`. What the book needed was not a name of its own but for each of its routes to be placed
against the separations the others already draw.

Recording, amending, and erasing a person are performed by the contact tools as well as by those routes, so the use case
behind them admits the administrative name *or* `mailfathom.mail.contacts.write`. The two halves are disjoint, so
requiring one of them would leave the act reachable from `mfctl` and refused from every agent. It is an alternative
rather than a widening, and it stops where the act does: promoting a collected contact and exporting one are named for
the administrative surface alone, because taking a record on is the owner's judgement about somebody collection
inferred, and an export answers a data-subject request.
[MCP tools](../features/mcp-tools.md#the-contact-book-on-this-surface) is what the other half publishes.

## Writing a grant

**Where a grant is written follows from what the credential names.** An owner's credential reaches that owner's mail, so
what it may do is a fact about the credential and is recorded beside it; the deployment's own credential answers for the
deployment, so what it may do is configured with it. The two halves of the published set are therefore written in two
places, and neither surface accepts the other's shape.

### On an owner's credential

The grant is named where the credential is provisioned, once per permission:

```console
$ mfctl credential create --method api-key --owner 6f1c… \
    --permission mailfathom.mail.read \
    --permission mailfathom.mail.contacts.read
```

[Owner credentials](admin-endpoint.md#owner-credentials) is where the command and its other options are specified.
`mfctl credential list` reads back what each credential holds.

**Naming no permission grants everything the mail surface publishes**, which is what makes a first deployment work
before it is governed. That means *this surface* rather than the names published the day the credential was provisioned,
so a permission added in a later release reaches an ungoverned credential on its own — the contact tools are the worked
example, since a credential that named nothing gained `mailfathom.mail.contacts.read` and `mailfathom.mail.contacts.write`
on upgrade alone, and with the second of those the ability to record, amend, and irreversibly erase what this deployment
holds about identified third parties. `mailfathom.mail.send` is the same shape and the sharpest case of it: a credential
that named nothing gains it on upgrade, and with it the ability to send mail from the owner's address to anybody.
`mailfathom.mail.drafts.write` arrived the same way and is milder for the reason it exists: what it adds is the ability to
put a message in the owner's own Drafts folder, which the owner sees and can delete.

**`--no-permissions` grants nothing**, which is how a credential is retired without deleting it: it still authenticates,
and it is served an empty tool list. `mfctl credential disable` is the other way to close one, and it is the one to reach
for when the reason may turn out to be nothing.

**There is no pattern here, deliberately.** A grant on a credential is written once, by somebody deciding what one client
of one owner may do, and read back from a listing that states names — so a shorthand that quietly widens on the next
release would be answering a question nobody asked at the moment they provisioned. Where the whole surface is meant,
name no permission; where part of it is, write the part out.

**Provisioning refuses a grant that says something impossible**, naming what was written: a name nothing publishes, and a
name belonging to the administrative half — which grants nothing to a credential that reaches one owner's mail, and is
refused with the mail half's own names written back.

**A surface accepting a method it has no provisioned credential for admits nobody**, which is not the same as an
unauthenticated one. A surface with no `Authentication` entry at all grants the whole of the mail half to every caller it
serves, because there is no credential for a grant to be recorded on; that is the unauthenticated posture the startup
warning already reports.

**`PermissionsFromTokenScopes` makes the recorded grant a ceiling rather than a grant.** Written on an entry accepting
`oauth-subject`, a token then holds the published names its scopes carry *and* its credential records, so the
authorization server decides per session within a bound the provisioning fixed. A scope naming anything else — `openid`,
`offline_access`, another resource's scope — is ignored, and a scope naming a permission the credential does not hold
grants nothing. It is written only on that entry, since no other method carries a token to read a scope from. What the
metadata document advertises there is the surface's whole published vocabulary rather than a configured ceiling, because
there is no ceiling in the section to read — those are the scope names to create in the authorization server, and
advertising them widens nothing, since a token holds only the intersection with its own credential's grant.
[Connecting an MCP client through your identity provider](mcp-client-oauth.md) walks that setup.

### On the deployment's own credential

`AdminEndpoint:Authentication` states each credential and what it grants, as `Permissions` — a list of published names
from the administrative half. [Endpoint configuration](configuration-endpoints.md#adminendpoint) is where that key and
the entry's other keys are specified.

**The grant belongs to the entry, not to the block inside it.** An entry may carry an `ApiKey`, a `PublicKey`, and an
`OAuth` block at once, and `Permissions` applies to every credential it admits — so two credentials to be granted
differently are two entries, which is what turns grouping from a matter of tidiness into a decision.

**An absent `Permissions` key and an empty list are opposites.** Writing no key at all leaves the entry holding
everything this surface publishes. `mailfathom.admin.configuration.write` is the sharpest case of what that costs on an
upgrade: an administrative entry that wrote no key gains it, and with it a credential that can change what the
deployment *is* rather than what it does next — widen another credential's grant, repoint a model provider, or turn a
surface off. An entry that wrote `mailfathom.admin.*` gains it on the same upgrade and for the same reason, since a
pattern is resolved against the published set on every start; that is the shape to check first, because it reads as a
deliberate grant rather than as an omission. An operator who granted an administrative credential the operating work and
meant to withhold the power to redefine the deployment narrows that entry to the names it actually needs, because
neither the absent key nor the covering pattern withholds it. Writing `Permissions: []` grants nothing, which is how a
credential is retired without deleting its entry: it still authenticates, and it still reads
`GET /api/admin/session`, which is where an operator reads that the credential now holds nothing.

**A value writing `*` as a whole segment grants every published name the pattern reaches.** `mailfathom.admin.*` grants
every administrative permission, so a grant states the boundary you mean rather than a list to revisit whenever a name is
added. The wildcard stands for **one or more whole segments**, at whatever position it is written and more than once if
you like: `mailfathom.admin.*` reaches `mailfathom.admin.credentials.write` a level deeper than itself, and
`mailfathom.*.read` reaches both depths of the reading half — `mailfathom.admin.read` and `mailfathom.admin.audit.read`.
It stands for at least one segment, so a pattern never reaches the name it was written around and
`mailfathom.admin.read.*` reaches nothing. A `*` inside a segment is no wildcard and no pattern:
`mailfathom.admin.c*` fails startup as the name nothing publishes that it is, which is the refusal that tells you a
pattern was never written from one that matched nothing. A pattern is resolved against the published set on every start
rather than frozen at the version it was written under, **which carries the same upgrade consequence the absent key
does**: a permission added where a written pattern reaches, in a later release, comes to the entry on upgrade alone, with
nobody editing the grant. A wildcard before the last segment widens that: `mailfathom.*.read` reaches a reading name
published at any depth rather than only beneath one prefix. Where that would be wrong, write the names out.
Everything that reads a grant back states what a pattern resolved to and never the pattern — the startup line and
`GET /api/admin/session` — so no reader has to expand one by hand.

**A pattern grants the administrative half, and only that.** `mailfathom.*.read` names two permissions in each half, and
this entry guards one — so it grants `mailfathom.admin.read` and `mailfathom.admin.audit.read`. The mail half is dropped
rather than granted, because no check on this endpoint reads a name of the other surface, and what the startup line and
`GET /api/admin/session` report is what the entry actually holds. A pattern reaching *only* the mail surface is a
different thing and still fails startup, since an operator who wrote one meant something the entry cannot do.

**Startup refuses a grant that says something impossible**, naming the entry and quoting what was written: a name
nothing publishes, a name belonging to the other surface, a name the same grant already carries, a pattern matching
nothing this repository publishes, a pattern matching only the other surface's half, a pattern covering a name the grant
already carries explicitly or through another pattern, and a bare `*` or `mailfathom.*` — which reach both surfaces
*entirely*, so they are no shorthand for a part of either and grant exactly what leaving the key out grants; they are
refused rather than accepted as a second spelling of it. A pattern reaching part of the mail surface beside part of this
one is not among them: it grants what it reaches here, as the paragraph above says. A permission name or a pattern
written into `RequiredScopes` or `AdvertisedScopes` is refused as well: requiring a permission at the door would close it
on a caller the deployment meant to serve less, the grant that reads one advertises it already, and a scope is compared
byte for byte at an authorization server, which can mint no pattern.

**That last refusal is the other half of what publishing a name costs an upgrade**, and it is the one that stops a
deployment rather than widening it. A name nothing publishes is an ordinary scope token, so an operator who minted a
scope of their own with that spelling could write it in `RequiredScopes` or `AdvertisedScopes` and start; the release
that publishes the name turns the same value into a permission, and startup refuses it by name. The action is the one the
refusal states: take the value out of `RequiredScopes` or `AdvertisedScopes` and write it in `Permissions` on the entry.

### What startup records

**Startup records what every entry resolved to**, one line per entry, so the posture is read on the first run rather than
inferred later. An administrative entry that wrote no grant says so rather than being reported as though somebody had
chosen what it holds, and one granted nothing as `nothing`. A mail-serving entry reports the method it accepts and says
where the grants behind it are read — `mfctl credential list` — because there is none in that section to report. Nothing
in the report names a key, a public key, a token, an authorization server, or a subject: what it states is what the
deployment configured, never who presented something.
[The MCP endpoint](mcp-endpoint.md#what-a-credential-may-do) and
[the administrative endpoint](admin-endpoint.md#what-a-credential-may-do) each carry the lines their surface produces.

### Which names are published where

A grant on an owner's credential draws from the mail half, and a grant on the administrative endpoint from the
administrative half. `mailfathom.mail.flags.write` and `mailfathom.mail.send` are the two worth naming rather than
leaving to the ungoverned default: the first writes to the owner's real mail server, and the second is the only name in
either half whose effect leaves the deployment and cannot be recalled.

## What a refused caller is told

The three surfaces enforce the same grants and answer differently, because their callers are different.

**The MCP endpoint says nothing.** A caller is offered no tool its grant does not permit — `tools/list` omits the rest,
composed per request and never cached — and a call naming one of the omitted tools is answered as a call naming a
tool that does not exist: the same error, the same code, and nothing about the caller, the credential, the permission,
or what a different caller would have been served. A message a client could tell apart would disclose the capability the
listing just withheld, and there is no `insufficient_scope` challenge either, even where the grant came from a token
whose authorization server could in principle mint one. A grant is not the only reason a tool is missing: a tool whose
category this deployment does not publish is withheld from the same listing and refused with the same answer, so
nothing tells a caller which of the two reasons applied —
[what this endpoint publishes](mcp-endpoint.md#what-this-endpoint-publishes).

**The administrative endpoint names the permission and nothing else.** A caller the grant does not admit is refused
`403` in the endpoint's ordinary problem shape, stating the permission that would have sufficed and carrying it as a
`permission` member, so `mfctl` can say what to grant — [what a refusal says](admin-endpoint.md#what-a-refusal-says)
carries both shapes. The caller there is an operator at their own terminal rather than an agent, and a route publishing
no decision is refused to everyone, which makes an omission a visible failure rather than an open route.

**The client endpoint answers the same way, and for a reason of its own.** Its caller is a page holding this person's
own credential, and [`GET /api/client/session`](client-endpoint.md#the-session-route) already answers that same caller
with the whole of its grant — so naming what is missing from that list discloses nothing the caller could not already
read about itself, while withholding it would leave somebody unable to tell a credential that needs re-issuing from a
deployment that is broken. A route publishing no decision is refused to everyone here too, which is the property that
matters most on the surface that returns mail.

**Every surface records its refusals, and no answer is the record.** A refusal is counted by
`mailfathom.authorization.refusals` — by surface, by the tool or route refused, and by the permission that would have
sufficed — with a warning beside it naming the credential the work was admitted as. On the MCP surface that record is
the only place the boundary is visible at all, so a client that stopped working is diagnosed from it rather than from
what the client received. A tool merely withheld from a listing is not recorded, because nothing was refused.
[Telemetry](telemetry.md#what-an-authorization-refusal-records) holds what each channel carries.

## Where the check runs

The grant is enforced twice, and neither check replaces the other. The transport refuses cheaply, before a use case is
reached, and it is the only place a decision can be reflected in what a caller is *offered* rather than only in what it
is told. The use case behind each operation then asks the same question on its own, so an entrypoint added later — a
rule action, a worker, a command, a second protocol — reaches the same refusal without passing any middleware.
[Who a use case is running for](../architecture/authorized-principal.md) is how the grant reaches the code that acts on
it, including the two principals that hold no permission at all: the process's own identity, and the signed capability
an attachment download link carries.

## What a permission does not decide

**Which mailboxes a caller reaches.** Every tool call resolves the accounts the credential's own owner controls and
refuses anything outside them, whichever method got the caller in, and no permission narrows or widens that. Which owner
it is follows from the credential rather than from the grant — which is exactly why a token has to resolve a mapped
subject, since admitting a colleague of the same tenant on the strength of a valid signature would admit them to
somebody else's mail.

**Whose mail a caller is acting on.** That is a second axis, and no name in the table above is on it. A caller admitted on the MCP
surface or the client surface is admitted to act for one owner, and every mailbox read is resolved against the accounts
that owner owns before any grant is consulted; a caller admitted on the administrative surface acts for no owner at all,
so an owner-scoped use case refuses it however broad its grant. That is why no permission names an account and adding
one would be the wrong repair: a grant says what may be done, and whose mail it may be done to is decided by what
admitted the caller. A deployment may declare several owners and serve each of them the mail accounts they own, and
every credential a mail-serving surface accepts now names the owner it admits — but a deployment serving several still
refuses to come up while any of the three surfaces is enabled, because the reads that resolve an owner from the
deployment rather than from the caller have not moved yet.
[Who a use case is running for](../architecture/authorized-principal.md) records the whole of it.

**Whether a capability exists.** A grant composes with availability rather than replacing it: a tool may be
unavailable, unauthorized, or both, and no grant makes a capability this deployment does not have appear. An endpoint
whose chat provider is unconfigured withholds `ask_mail` from a caller granted `mailfathom.mail.ask` exactly as it does
from one granted nothing.
