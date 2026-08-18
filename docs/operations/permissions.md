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
| `ask_mail` | `mailfathom.mail.ask` |
| `list_contacts` | `mailfathom.mail.contacts.read` |
| `get_contact` | `mailfathom.mail.contacts.read` |
| `create_contact` | `mailfathom.mail.contacts.write` |
| `update_contact` | `mailfathom.mail.contacts.write` |
| `delete_contact` | `mailfathom.mail.contacts.write` |
| `promote_contact` | `mailfathom.mail.contacts.write` |

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
third parties. Writing `Permissions: []` grants nothing, which is how a credential is retired without deleting its
entry: it still authenticates, and on the administrative surface it still reads `GET /api/admin/session`, which is
where an operator reads that the credential now holds nothing.

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

**Four things fail startup**, each naming the entry and the position in the list: a name nothing publishes, a name
belonging to the other surface, a name the same grant already carries, and a permission name written into
`RequiredScopes` or `AdvertisedScopes`. The last is refused because requiring a permission at the door would close it on
a caller the deployment meant to serve less, and because the grant that reads one advertises it already.

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
