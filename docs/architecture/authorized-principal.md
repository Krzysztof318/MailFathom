# Who a use case is running for

<!-- describes: src/Application/Access/**, src/Host/Security/Transport/TransportAuthorizedPrincipalSource.cs, src/Host/Security/Transport/TransportCallerIdentity.cs, src/Host/Api/EmailAttachmentDownloadEndpoint.cs -->

A use case can be reached by more than one thing. Today an MCP tool, an administrative route, a background worker, and
the attachment download link all end at application-layer code, and each of them arrives by a different path with
different checks in front of it. This page describes what that code is told about whoever reached it, and why the check
that acts on it lives there rather than only in the middleware a request happened to pass through.

The vocabulary of named permissions itself — what a permission is, which ones exist, and how a credential comes to hold
one — is
[ADR 0012](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0012-authorization-model-named-permissions-and-where-they-are-enforced.md)
and [the configuration reference](../operations/configuration-reference.md). What follows is only how that grant reaches
the code that acts on it.

## Two checks, and why neither replaces the other

**The transport refuses cheaply.** A check at the endpoint turns a request away without reaching a use case at all, and
it is the only place a decision can be reflected in what a caller is offered rather than only in what it is told. It is
where admission is decided today, and where a grant will be weighed as well.

**The use case is the authority.** An entrypoint added later — a rule action, a worker, a command, a second protocol —
reaches the same code without passing that middleware, so a check that lived only there is one the new entrypoint
forgets. Asking inside the use case makes the answer a property of the operation rather than of the route somebody
happened to arrive by, which is what leaves a new entrypoint safe by default instead of safe by the author remembering.

The two are not redundant, because they are answering for different sets of callers. A use case never relies on the
transport having already asked, and the transport never relies on the use case being the only way in.

## What the application layer is told

`AuthorizedPrincipal` is the whole of it: the identity the work was admitted under, and the permissions that identity
holds. Nothing from `System.Security.Claims`, ASP.NET Core, or the MCP SDK crosses that line, and no use case learns
*which* credential admitted a caller — that is a question the transport already answered. `Boundaries.UnitTests` holds a
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

The signed capability is what `GET /attachments/{capability}` runs under. That route authenticates nobody by design: the
URL carries a ticket verified against the deployment's key ring, and what it names is one attachment of one email rather
than a surface. So the capability *is* the authorization, already bounded to a single object and a lifetime, and the use
case behind it admits that kind and asks for no permission beside it. It is a principal kind rather than an exception,
so the next capability-authorized route does not have to argue for its own.

## How it gets there

The host composes one `IAuthorizedPrincipalSource` per scope, which for a served request is that request:

- **A request an authentication scheme validated** becomes a caller, named by what this deployment authorized — an API
  key's name, a client public key's name, or the issuer and subject the access policy checked against the configured
  authorization servers. The permissions travel as claims the scheme wrote when the credential was judged, so nothing
  per request re-reads a configuration section.
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

## What a refusal is, and what each boundary does with it

A use case refuses by raising `PrincipalNotAuthorizedException`, which carries error code `14001` and, where the
requirement was a grant rather than a kind, the permission that would have sufficed. It is an application failure rather
than a status code or a protocol result, because the same refusal is meant to reach two boundaries that answer it
differently — and a use case that raised either shape directly would have decided both.

A use case reached under no principal at all is refused the same way. That is the case an entrypoint produces by
omission — it never said what admitted the work — and refusing it is what "fails rather than defaulting to permitted"
means in the one place the decision is taken.

**No use case requires a permission yet.** What a caller may do on either surface is still decided by the credential it
authenticated with, exactly as
[the configuration reference](../operations/configuration-reference.md) states under `Permissions`:
the grant is read, validated, carried, and reported, and nothing acts on it. The one requirement in force today is the
download route's, which admits a signed capability and nothing else. So the boundary mappings a refusal will need — what
the MCP surface tells a caller, and what the administrative surface names — do not exist yet, and neither surface can
produce this refusal.
