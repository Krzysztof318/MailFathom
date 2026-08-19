# What a credential may do

<!-- describes: src/Domain/Access/**, src/Application/Access/**, src/Host/Configuration/Access/TransportAuthenticationOptions.cs, src/Host/Security/Endpoints/**, src/Host/Security/Transport/**, src/Mcp/Tools/PublishedTools.cs -->

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
dot-separated, and always a valid OAuth scope token so the same string can travel in a token's `scope` claim. The two
surfaces draw from disjoint halves, and the prefix after `mailfathom.` says which half a name belongs to.

| Permission | Surface | What it covers |
| --- | --- | --- |
| `mailfathom.mail.read` | MCP | The tools that read the local mailbox copy: `list_accounts`, `list_emails`, `get_email_content`, `search_emails`. Where semantic retrieval is configured, searching places the caller's own query text with the embedding provider, so this is not an egress-free grant |
| `mailfathom.mail.ask` | MCP | `ask_mail`, which answers from mail content by sending it to a model provider. It does not imply `mailfathom.mail.read`, and granting it is granting access to mail |
| `mailfathom.mail.flags.write` | MCP | `set_mail_flags`, which marks mail read or unread, stars or unstars it, and writes its keywords. It is the one MCP grant whose effect reaches the owner's mail server, and it does not follow from reading mail: a deployment that lets an agent read has not thereby let it change anything |
| `mailfathom.mail.send` | MCP | Asking this deployment to send mail from an account it holds. It is the one grant here whose effect leaves the deployment and cannot be recalled, which is why it follows from nothing: reading a mailbox is not writing from it, and marking mail reaches the owner's own server rather than a stranger's. It covers `send_email`, which queues a message for a mailbox this deployment holds, and `reply_to_email` and `forward_email`, which queue one anchored to mail it already holds — those two also need `mailfathom.mail.read`, because an answer is derived from the message it answers. It is asked for again by the use cases behind all three and by the outbox beneath them, so it governs the act rather than only the tool |
| `mailfathom.mail.contacts.read` | MCP | `list_contacts` and `get_contact`, which read the deployment's own contact book: names, addresses, and the notes an owner wrote about identified third parties |
| `mailfathom.mail.contacts.write` | MCP | `create_contact`, `update_contact`, `delete_contact`, and `promote_contact`, which record, amend, erase, and take on a person in that book. The erasure is here rather than apart, because a grant that cannot edit the book cannot be trusted to take somebody out of it |
| `mailfathom.admin.read` | administrative | The reads reporting the deployment's own state and no mail: what synchronization is doing per account and per folder, embedding status and the activation preview, the loaded rules, a run's progress, what a rewind would cost, where a re-derivation has got to, the stopped-job list |
| `mailfathom.admin.audit.read` | administrative | Everything derived from somebody's mail: the mailbox-mutation audit, the answering audit, the rules history, the spam classifications, and reading the contact book — a listing, one person, or their export |
| `mailfathom.admin.operate` | administrative | Asking the deployment to do work it can already do: running rules over an account, classifying an account, retrying or dropping a stopped job, cancelling a reindex, rewinding synchronization, re-deriving stored mail, and writing to the contact book |
| `mailfathom.admin.credentials.write` | administrative | Storing a mailbox refresh token |
| `mailfathom.admin.spend` | administrative | Activating the declared embedding model, which is the one operation that starts a provider bill |
| `mailfathom.admin.erase` | administrative | Disposing of what this deployment holds: the mail stored for a folder an account no longer mirrors, and one person and everything the contact book derived from them |

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

The contact permissions are separate from the mailbox ones and from each other, because the book is a different body of
personal data from the mail: an assembled record about identified third parties rather than correspondence that
arrived, and a credential that may look somebody up is not thereby one that may erase them. On the administrative half
the six names are allocated so that the separations an operator would plausibly want are the ones they can make:
reading state, reading what was derived from mail, causing work, placing a credential, starting a bill, and destroying
what the deployment holds.

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
| `ask_mail` | `mailfathom.mail.ask` |
| `list_contacts` | `mailfathom.mail.contacts.read` |
| `get_contact` | `mailfathom.mail.contacts.read` |
| `create_contact` | `mailfathom.mail.contacts.write` |
| `update_contact` | `mailfathom.mail.contacts.write` |
| `delete_contact` | `mailfathom.mail.contacts.write` |
| `promote_contact` | `mailfathom.mail.contacts.write` |

The three sending rows are the ones worth reading twice. `mailfathom.mail.send` is the only grant here whose effect
leaves this deployment and cannot be recalled, and the mapping is what withholds those tools from every other
credential: a caller without the name is not offered the descriptor and is answered as if the tool did not exist. Two
further checks stand behind it — the use case, and the outbox it writes through — so an entrypoint added later meets the
same refusal without passing this table.

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

**Six `mfctl` commands make two requests and therefore need two permissions**, because what such a command reads is
what it puts in front of you or amends from:

| Command | Needs |
| --- | --- |
| `mfctl contact update`, `mfctl contact add-address`, `mfctl contact remove-address` | `mailfathom.admin.audit.read` beside `mailfathom.admin.operate` |
| `mfctl contact delete` | `mailfathom.admin.audit.read` beside `mailfathom.admin.erase` |
| `mfctl mailbox rewind` | `mailfathom.admin.read` beside `mailfathom.admin.operate` |
| `mfctl embedding activate` | `mailfathom.admin.read` beside `mailfathom.admin.spend` |

A credential granted only the permission the operation itself is published under meets the refusal at the first request
and nothing is done — the safe half of it, and still not what the operator intended.

**`GET /api/admin/session` sits outside the model and needs no permission.** It reports the credential the caller
already presented, the version this deployment already publishes, and the permissions that credential holds, all of
which the caller brought or may always ask about itself; and it is what every command reads first, `mfctl login`
included. Requiring a permission for it would make that permission a component of every administrative grant, so a
credential granted only the spend permission could not sign in to use it. A credential granted nothing therefore still
answers here and nowhere else; an operator who wants nothing answered at all removes the entry.

## The contact book draws from both halves

The book is reachable from both surfaces, and it needed a name of its own on neither. On the administrative surface,
reading the book, reading one person, and exporting them are `mailfathom.admin.audit.read`, because what the book holds
is derived from mail; recording, amending, and promoting are `mailfathom.admin.operate`; erasing somebody is
`mailfathom.admin.erase`. What the book needed was not a seventh name but for each of its routes to be placed against
the separations the six already draw.

Recording, amending, and erasing a person are performed by the contact tools as well as by those routes, so the use case
behind them admits the administrative name *or* `mailfathom.mail.contacts.write`. The two halves are disjoint, so
requiring one of them would leave the act reachable from `mfctl` and refused from every agent. It is an alternative
rather than a widening, and it stops where the act does: promoting a collected contact and exporting one are named for
the administrative surface alone, because taking a record on is the owner's judgement about somebody collection
inferred, and an export answers a data-subject request.
[MCP tools](../features/mcp-tools.md#the-contact-book-on-this-surface) is what the other half publishes.

## Writing a grant

An `Authentication` entry states what it grants, as `Permissions` — a list of published names, on either endpoint.
[Endpoint configuration](configuration-endpoints.md#the-accepted-credentials--mcpendpointauthenticationn) is where that
key and the entry's other keys are specified.

**The grant belongs to the entry, not to the block inside it.** An entry may carry an `ApiKey`, a `PublicKey`, and an
`OAuth` block at once, and `Permissions` applies to every credential it admits — so two credentials to be granted
differently are two entries, which is what turns grouping from a matter of tidiness into a decision.

**An absent `Permissions` key and an empty list are opposites.** Writing no key at all leaves the entry holding
everything its surface publishes, which is what makes a first deployment work before it is governed. The key's absence
means *this surface* rather than the names published the day the file was written, so a permission added in a later
release reaches an unrestricted entry on its own — the contact tools are the worked example, since an entry that wrote
no key gained `mailfathom.mail.contacts.read` and `mailfathom.mail.contacts.write` on upgrade alone, and with the
second of those a credential that can record, amend, and irreversibly erase what this deployment holds about identified
third parties. `mailfathom.mail.send` is the same shape and the sharpest case of it: an entry that wrote no key gains it on upgrade,
and with it a credential that can send mail from the deployment's mailboxes to anybody.
Writing `Permissions: []` grants nothing, which is how a credential is retired without deleting its
entry: it still authenticates, and on the administrative surface it still reads `GET /api/admin/session`, which is
where an operator reads that the credential now holds nothing.

**A value writing `*` as a whole segment grants every published name the pattern reaches.** `mailfathom.admin.*` grants
every administrative permission and `mailfathom.mail.contacts.*` grants both contact permissions, so a grant states the
boundary you mean rather than a list to revisit whenever a name is added. The wildcard stands for **one or more whole
segments**, at whatever position it is written and more than once if you like: `mailfathom.admin.*` reaches
`mailfathom.admin.credentials.write` a level deeper than itself, and `mailfathom.*.read` reaches both depths of the
reading half — `mailfathom.mail.read` and `mailfathom.mail.contacts.read` on the MCP endpoint,
`mailfathom.admin.read` and `mailfathom.admin.audit.read` on the administrative one. It stands for at least one segment,
so a pattern never reaches the name it was written around and `mailfathom.mail.read.*` reaches nothing. A `*` inside a
segment is no wildcard and no pattern: `mailfathom.mail.c*` fails startup as the name nothing publishes that it is,
which is the refusal that tells you a pattern was never written from one that matched nothing. A pattern is resolved
against the published set on every start rather than frozen at the version it was written under, **which carries the
same upgrade consequence the absent key does**: a permission added where a written pattern reaches, in a later release,
comes to the entry on upgrade alone, with nobody editing the grant. A wildcard before the last segment widens that:
`mailfathom.*.read` reaches a reading name published at any depth rather than only beneath one prefix.
Where that would be wrong, write the names out. `mailfathom.mail.flags.write` is the first case of it and the one to
check a written grant against: an entry reading `mailfathom.mail.*` used to reach nothing that leaves this deployment,
and on upgrade it reaches the tool that writes to the owner's mail server. `mailfathom.mail.send` is the second and the
sharper one, since the same pattern now also carries the grant to send from the owner's address to anybody, through
`send_email` and through the two tools that answer stored mail. Everything that reads a grant back states
what a pattern resolved to and never the pattern — the startup line, `GET /api/admin/session`, and `scopes_supported` —
so no reader has to expand one by hand.

**A pattern grants the surface it is written on, and only that.** `mailfathom.*.read` names two permissions on each
surface, and an entry guards one — so written on the MCP endpoint it grants `mailfathom.mail.read` and
`mailfathom.mail.contacts.read`, and written on the administrative endpoint it grants `mailfathom.admin.read` and
`mailfathom.admin.audit.read`. The other half is dropped rather than granted, because no check on the endpoint you wrote
it on reads a name of the other surface, and what the startup line and `GET /api/admin/session` report is what the entry
actually holds. A pattern reaching *only* the other surface is a different thing and still fails startup, since an
operator who wrote one meant something the entry cannot do.

**A surface with no `Authentication` entry at all grants that surface's whole half** to every caller it serves, because
there is no entry for a grant to be written on. That is the unauthenticated posture the startup warning already
reports.

**`PermissionsFromTokenScopes` makes the list a ceiling rather than a grant.** With it, a token holds the published
names its scopes carry *and* the entry lists, so the authorization server decides per subject within a bound the
deployment fixed. A scope naming anything else — `openid`, `offline_access`, another resource's scope — is ignored, and
a scope naming a permission the entry never listed grants nothing. It is available only where the entry's sole block is
`OAuth`: neither a key nor a public key can carry a scope, so startup refuses the combination rather than asking a
credential a question it cannot answer. Every such entry's ceiling is published in `scopes_supported`, which is what an
operator creates in their authorization server;
[connecting an MCP client through your identity provider](mcp-client-oauth.md) walks that setup, and an entry granting
from configuration publishes none of its permissions, because no client can ask for one.

**Startup refuses a grant that says something impossible**, naming the entry and quoting what was written: a name
nothing publishes, a name belonging to the other surface, a name the same grant already carries, a pattern matching
nothing this repository publishes, a pattern matching only the other surface's half, a pattern covering a name the
grant already carries explicitly or through another pattern, and a bare `*` or `mailfathom.*` — which reach both
surfaces *entirely*, so they are no shorthand for a part of either and grant exactly what leaving the key out grants;
they are refused rather than accepted as a second spelling of it. A pattern reaching part of the other surface beside
part of this one is not among them: it grants what it reaches here, as the paragraph above says. A permission name or a pattern written into `RequiredScopes` or
`AdvertisedScopes` is refused as well: requiring a permission at the door would close it on a caller the deployment
meant to serve less, the grant that reads one advertises it already, and a scope is compared byte for byte at an
authorization server, which can mint no pattern.

**That last refusal is the other half of what publishing a name costs an upgrade**, and it is the one that stops a
deployment rather than widening it. A name nothing publishes is an ordinary scope token, so an operator who minted a
scope of their own with that spelling could write it in `RequiredScopes` or `AdvertisedScopes` and start; the release
that publishes the name turns the same value into a permission, and startup refuses it by name. The action is the one
the refusal states: take the value out of `RequiredScopes` or
`AdvertisedScopes` and write it in `Permissions` on the entry — with `PermissionsFromTokenScopes` where the point was
for the token's own scope to decide.

**Startup records what every entry resolved to**, one line per entry, so the posture is read on the first run rather
than inferred later. An entry that wrote no grant says so rather than being reported as though somebody had chosen what
it holds, an entry with `PermissionsFromTokenScopes` is reported as what it grants *at most*, and an entry granted
nothing as `nothing`. Nothing in the report names a key, a public key, a token, an authorization server, or a subject: a
grant is what the deployment wrote, never who presented something.
[The MCP endpoint](mcp-endpoint.md#what-a-credential-may-do) and
[the administrative endpoint](admin-endpoint.md#what-a-credential-may-do) each carry the lines their surface produces.

## What a refused caller is told

The two surfaces enforce the same grant and answer differently, because their callers are different.

**The MCP endpoint says nothing.** A caller is offered exactly the tools its grant permits — `tools/list` omits the
rest, composed per request and never cached — and a call naming one of the omitted tools is answered as a call naming a
tool that does not exist: the same error, the same code, and nothing about the caller, the credential, the permission,
or what a different caller would have been served. A message a client could tell apart would disclose the capability the
listing just withheld, and there is no `insufficient_scope` challenge either, even where the grant came from a token
whose authorization server could in principle mint one.

**The administrative endpoint names the permission and nothing else.** A caller the grant does not admit is refused
`403` in the endpoint's ordinary problem shape, stating the permission that would have sufficed and carrying it as a
`permission` member, so `mfctl` can say what to grant — [what a refusal says](admin-endpoint.md#what-a-refusal-says)
carries both shapes. The caller there is an operator at their own terminal rather than an agent, and a route publishing
no decision is refused to everyone, which makes an omission a visible failure rather than an open route.

**Both surfaces record every refusal, and neither answer is the record.** A refusal is counted by
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

**Which mailboxes a caller reaches.** Every tool call resolves the accounts the configured owner controls and refuses
anything outside them, whichever credential got the caller in, and no setting narrows that. Two admitted callers see
the same mailboxes — which is exactly why a token has to name an authorized subject, since admitting a colleague of the
same tenant would admit them to the owner's mail rather than to their own.

**Whether a capability exists.** A grant composes with availability rather than replacing it: a tool may be
unavailable, unauthorized, or both, and no grant makes a capability this deployment does not have appear. An endpoint
whose chat provider is unconfigured withholds `ask_mail` from a caller granted `mailfathom.mail.ask` exactly as it does
from one granted nothing.
