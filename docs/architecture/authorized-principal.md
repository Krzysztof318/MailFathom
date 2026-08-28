# Who a use case is running for

<!-- describes: backend/src/Application/Access/**, backend/src/Host/Security/Transport/TransportAuthorizedPrincipalSource.cs, backend/src/Host/Security/Transport/TransportCallerIdentity.cs, backend/src/Host/Security/Transport/TransportCallerOwner.cs, backend/src/Host/Api/EmailAttachmentDownloadEndpoint.cs, backend/src/Application/Emails/DownloadAttachment/** -->

A use case can be reached by more than one thing. Today an MCP tool, an administrative route, a background worker, and
the attachment download link all end at application-layer code, and each of them arrives by a different path with
different checks in front of it. This page describes what that code is told about whoever reached it, and why the check
that acts on it lives there rather than only in the middleware a request happened to pass through.

The vocabulary of named permissions itself — what a permission is, which ones exist, and how a credential comes to hold
one — is [what a credential may do](../operations/permissions.md), and
[ADR 0012](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0012-authorization-model-named-permissions-and-where-they-are-enforced.md)
is the record behind it. What follows is only how that grant reaches the code that acts on it.

## Two checks, and why neither replaces the other

**The transport refuses cheaply.** A check at the endpoint turns a request away without reaching a use case at all, and
it is the only place a decision can be reflected in what a caller is offered rather than only in what it is told. The
MCP surface uses both halves of that: admission is decided there, and the grant is weighed there too, which is what lets
a `tools/list` carry only the tools this caller may call.

**The use case is the authority.** An entrypoint added later — a rule action, a worker, a command, a second protocol —
reaches the same code without passing that middleware, so a check that lived only there is one the new entrypoint
forgets. Asking inside the use case makes the answer a property of the operation rather than of the route somebody
happened to arrive by, which is what leaves a new entrypoint safe by default instead of safe by the author remembering.

The two are not redundant, because they are answering for different sets of callers. A use case never relies on the
transport having already asked, and the transport never relies on the use case being the only way in.

## What the application layer is told

`AuthorizedPrincipal` is the whole of it: the identity the work was admitted under, the permissions that identity holds,
and — where the work is somebody's mail rather than the deployment's — the owner it is acting for. Nothing from
`System.Security.Claims`, ASP.NET Core, or the MCP SDK crosses that line, and no use case learns *which* credential
admitted a caller — that is a question the transport already answered. `Boundaries.UnitTests` holds a
rule keeping claims types out of `Application` and `Domain`, because those are the one family of types the project
references cannot refuse on their own.

The identity is not one shape. Where an operator wrote the credential it is that entry's own name; where a token brought
it, it is the issuer and subject the deployment authorized — a host name and that authorization server's identifier for
a person; and where the surface configures no credential at all it is the fixed word `anonymous`, because there is
nothing to tell one caller apart from another. So a boundary that names the caller to its own readers reads the identity
and decides for itself what it may show, and a refusal never carries it.

There are three kinds of principal, and none of them is a weaker version of another:

| Kind | What it is | What it holds |
| --- | --- | --- |
| Caller | Somebody who presented a credential a configured entry admits, or — where the surface configures no entry at all — somebody who presented nothing | Whatever that entry's grant resolved to, and everything the surface publishes where there is no entry |
| Process identity | MailFathom itself, running work no caller requested | Nothing, by construction |
| Signed capability | A ticket this deployment signed for one object | Nothing; the ticket is the authorization |

The process identity is a kind of its own rather than a caller holding everything, and that is the point of modelling
it. A principal that could be admitted by holding a permission would be reachable by whoever an operator granted that
permission to — so a use case that may run without a caller admits it **by name**, and never by a permission check.

**The owner is a second axis, and it is not a permission.** A permission says which operations a caller may perform;
the owner says whose mail those operations run against, and no grant an operator writes can make one caller act for
another. So a principal carries an owner or carries none, and the two questions are asked separately: `RequirePermission`
answers the first and `RequireOwner` the second. A caller admitted on a surface that serves one owner's mail acts for
that owner; the deployment administrator and this process's own identity act for nobody, and a caller-scoped read
reached by either is refused rather than answered with an empty set — an empty answer there would publish the read to
them in the shape of an answer. Which surfaces carry an owner is settled where a request becomes a principal, one
section below.

**Work that runs for nobody still has to know whose mail it touches.** Synchronization and embedding are performed
under this process's own identity, which carries no owner by construction — so a bound stated per owner cannot be read
off the principal, and asking `RequireOwner` there would refuse the work rather than answer it. `IMailOwnership` is what
answers instead: it resolves the owner of a stored message from the mail graph, where ownership lives on the row itself,
and it is the only way to reach that fact without a caller. It has nothing to resolve an owner *from an account* with,
and deliberately so — an account identifier names one mailbox within its owner rather than across the deployment, so a
question that names the identifier alone has no single answer, and whoever holds the account already holds the pair. Reading it is not an authorization
decision and grants nothing — a worker was already admitted by name — and it is deliberately not a way for a caller to
act for somebody else, because nothing published to a caller consults it.

**Not everything owned is caller-scoped, and the difference is a second reading rather than a second rule.** The contact
book belongs to an owner exactly as mail does, and it is reached by the administrator and by MailFathom's own
synchronization as well as by a caller — so requiring an owner there would refuse the two principals that legitimately
maintain it. `ActingOwner` is the reading those paths take: it answers with the owner the principal acts for, or with
nothing when the principal acts for none, and one resolution then falls back to the owner this deployment serves. It
refuses work reached under no principal exactly as `RequireOwner` does, because that is the case where nothing said what
admitted the work at all. `ContactBookOwnership` is the only reader, and [Contacts § A book belongs to one
owner](../features/contacts.md#a-book-belongs-to-one-owner) is where what it decides is described.

The signed capability is what `GET /attachments/{capability}` runs under. That route authenticates nobody by design: the
URL carries a ticket verified against the deployment's key ring, and what it names is one attachment of one email rather
than a surface. So the capability *is* the authorization, already bounded to a single object and a lifetime, and the
entrypoint behind that route admits that kind and asks for no permission beside it. It is a principal kind rather than
an exception, so the next capability-authorized route does not have to argue for its own.

The use case those two share serves the client surface's own attachment route as well, and the second entrypoint admits
a caller granted `mailfathom.mail.read` and no capability at all — a reader who authenticated needs nothing minted for
them. The two are disjoint in both directions rather than one being a relaxation of the other: a caller however broadly
granted cannot redeem a link's entrypoint, and a redeemed capability cannot reach the client's. Everything either of
them establishes afterwards is the same, which is why they are two authorizations over one resolution rather than two
use cases.

## How it gets there

The host composes one `IAuthorizedPrincipalSource` per scope, which for a served request is that request:

- **A request an authentication scheme validated** becomes a caller, named by what this deployment authorized — the
  identifier of the owner credential the presented value resolved to on a mail-serving surface, and on the
  administrative surface the configured name of an API key or a client public key, or the issuer and subject the access
  policy checked against the configured authorization servers. The permissions travel as claims the scheme wrote when
  the credential was judged, so nothing per request re-reads a configuration section or the credential's row.
- **A scope with no request behind it** is the process's own identity. Work reached outside a request in this process is
  work no caller asked for.
- **A route that verified a capability** states that principal onto its own scope before it reaches the use case. The
  download route is the one that does.

**A request that authenticated nothing depends on what the surface it reached configures.** Where that surface
configures no `Authentication` entry at all, the caller holds everything the surface publishes — there is no entry for a
grant to hang on, which is the posture ADR 0012 settled and the startup record already states, so reporting no principal
there would have a use case refuse every call on a deployment whose own record says it grants everything. Where the
surface does configure a credential, such a request is none of the three.

**The download route is withheld from that grant on either posture.** The MCP surface serves it beside the protocol
route, so the paragraph above would otherwise admit it out of the transport wherever the deployment configures no MCP
credential — a second and weaker way into an attachment than the signature the route exists to check, and one holding on
one posture only. The adapter therefore answers no principal for a path that route serves before it asks either surface,
and the route's own statement, made once the ticket verifies, is the only thing that authorizes it.

**Two of the three surfaces serve one owner's mail, and the adapter is where that is decided.** A caller admitted on
the MCP surface or on the client surface is admitted to act for the owner this deployment serves; a caller admitted on
the administrative surface is not, because an administrator is admitted to a deployment rather than to somebody's
mailbox. The split holds on either posture — a caller the surface authenticated and a caller admitted by the surface
configuring no credential are both admitted the same way — because it follows from the path the request arrived on
rather than from what it presented.

**Which owner that is comes from the credential wherever a credential was presented, and from a startup gate
otherwise.** Every credential a mail-serving surface accepts resolves one owner record, whichever of the four methods it
is, so a request admitted on one carries that owner as a claim and the adapter acts for it — which is what lets one
deployment serve more than one person's mail over one address, and what makes a credential resolving no owner refusable
exactly as an unknown credential is.

The gate settles the whole roster while the host starts — every owner the file declares, each with the mail accounts
they own, every owner whose record is already their own and whom no file declares, and the deployment's own
`MailSynchronization:Accounts` belonging to the sole owner such a deployment holds. A caller whose credential names no
owner needs exactly one owner to act for, so the gate refuses to come up on any other number **while `McpEndpoint` or
`ClientEndpoint` admits such a caller** — which is a surface requiring no authentication, or one any of whose methods is
a key, a public key, or an access token rather than a person's password. It is the methods rather than the enablement:
a surface accepting a password accepts whatever is configured beside it, and a caller admitted by one of those still
arrives carrying nobody, so what frees the roster is every method on both surfaces naming an owner — or the surfaces
being off.

**The administrative surface is deliberately outside that reading.** An administrator acts for the deployment rather
than for a person, and every route there that is about one owner names the owner it is about, so a roster of several
leaves nothing unanswered. That is also what makes recording a second owner reachable at all: the surface an operator
would use to correct the other two cannot be the surface the refusal closes. What it costs is the handful of
administrative reads that still resolve the sole owner — the contact book among them — and those have no answer on a
roster of several; each is a separate act to scope, and no route silently picks one owner out of several. Which mail
accounts a caller owns is not one of them: it is resolved from the owner each served account carries, so every mailbox
read answers a caller with their own half of a deployment serving several.
[The health endpoints](../operations/health-endpoints.md#the-three-probes) record what each refusal means to an
operator.

The claim is read on the two mail-serving surfaces alone. A request admitted on the administrative surface acts for no
owner whether or not it carried one, which is why the method that produces the claim
[is refused there at startup](../operations/mcp-endpoint.md#passwords): a credential naming a person would otherwise be
admitted to a surface with nowhere to put them, and what it authorized would be the deployment's own authority.

**The client surface reaches the same adapter and is no third case.** It is the third surface a request can arrive on,
and its grant is resolved exactly as the two above are — from the entry that admitted the caller, or from the whole of
the mail half where it configures none. Its session route reports that grant rather than requiring anything of it, and
its mail-reading routes reach the use cases that already ask, under the permissions they already name:
`GET /api/client/accounts` is published under `mailfathom.mail.read` and calls the same account reader an agent's
`list_accounts` reaches.

## What a refusal is, and what each boundary does with it

A use case refuses by raising `PrincipalNotAuthorizedException`, which carries error code `14001` and, where the
requirement was a grant rather than a kind, the permission that would have sufficed. It is an application failure rather
than a status code or a protocol result, because the same refusal is meant to reach two boundaries that answer it
differently — and a use case that raised either shape directly would have decided both.

A use case that acts on one owner's mail and is reached by a principal acting for no owner is refused the same way and
with the same code, because from the use case's side it is the same fact: what reached it cannot say whose mail it is
about. A deployment that cannot resolve its own owner never reaches a use case at all — it fails to start, with
`14002 DeploymentMailOwnerUnresolved`.

A use case reached under no principal at all is refused the same way. That is the case an entrypoint produces by
omission — it never said what admitted the work — and refusing it is what "fails rather than defaulting to permitted"
means in the one place the decision is taken.

**The MCP surface answers it by saying nothing.** The use cases behind its tools each require a permission —
`mailfathom.mail.read` for the four that read the local mailbox copy, `mailfathom.mail.ask` for the one that answers
from it, `mailfathom.mail.flags.write` for the one that changes a mailbox, `mailfathom.mail.drafts.write` for the three
that write a draft, and the two contact names for the book — and
the endpoint asks the same question ahead of them, from the grant the caller was admitted under. `mailfathom.mail.send`
is asked for a third time by the outbox a send is written down in, which is the case that shows which of the checks is
the authority: an entrypoint reaching that use case from anywhere else meets the same refusal without passing the
endpoint at all. A tool
the grant does not permit is absent from `tools/list`, and a call naming one is answered as a call naming a tool that
does not exist: the same error, the same code, and nothing about the caller, the credential, or the permission. So this
refusal never reaches a client in a form it could read;
[the MCP tools page](../features/mcp-tools.md#what-a-caller-is-offered) states which tool requires which permission.

**The two HTTP surfaces answer it by naming the permission and nothing else.** Every route on either publishes the one
permission it requires as endpoint metadata, decided beside the route rather than in a list a new route could be added
without joining, and a group filter reads that metadata ahead of the handler: a caller the grant does not admit is
refused `403` in the endpoint's ordinary problem shape, stating the permission that would have sufficed and carrying it
as a `permission` member so `mfctl` can say what to grant. A route publishing no decision is refused to everyone, and so
is one published under the other half's permission, which makes either omission a visible failure rather than an open
route. The use case behind the route asks the same question again and raises this refusal on its own, and the filter
answers that exception in the same shape — so the transport is a cheap first reading rather than the authority.
`GET /session` is the one route on each surface that requires no permission, because reporting what the credential is
and what it may do is what a caller holding nothing needs in order to learn that it holds nothing. Which half a route
may draw on is the group's decision rather than the route's, so the same filter serves both surfaces and each names its
own.
[The administrative endpoint page](../operations/admin-endpoint.md#what-the-endpoint-serves) and
[the client endpoint page](../operations/client-endpoint.md#what-it-serves) carry the two mappings.

**Every surface records its refusals, and no surface's answer is the record.** A refusal is counted by
`mailfathom.authorization.refusals` — by surface, by the tool or route that was refused, and by the permission that
would have sufficed — with a warning beside it naming the credential the work was admitted as, which the boundary reads
from `AccessAuthorization` rather than from the refusal, since the failure itself is barred from carrying an identity. A
tool withheld from a listing is not a refusal and is not recorded: nothing was refused, and every narrowed caller would
produce one on every listing.
[Telemetry](../operations/telemetry.md#what-an-authorization-refusal-records) holds what each channel carries.

The other requirement in force is the download route's, which admits a signed capability and nothing else.

The transport's own reading of the grant goes through the same `AccessAuthorization` the use cases ask, which reports a
verdict instead of raising where a boundary has to compose an answer rather than perform an operation. One definition of
what holding a permission means is what keeps a listing from offering a tool the use case behind it would then refuse.
