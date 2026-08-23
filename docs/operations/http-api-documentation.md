# The HTTP API document and the explorer

<!-- describes: backend/src/Host/Api/Documentation/**, backend/src/Host/Hosting/Startup/SurfaceIsolation.cs -->

A development instance publishes one OpenAPI document describing every operation MailFathom serves over HTTP, and an
API explorer to read it in. Both exist only while the process is running in the `Development` environment.

This is developer tooling. It answers the question *what does this deployment serve over HTTP* without reconstructing
it from route registrations, and it lets a call be tried against a running instance before any client exists to make
it.

## The two addresses

| Address | What answers there |
|---|---|
| `/openapi/v1.json` | The generated document, as OpenAPI 3.1 JSON |
| `/scalar` | [Scalar](https://scalar.com/), which loads that document and renders it as an explorer |

`/scalar` redirects to `/scalar/v1`, which is the same page addressed by document name. The explorer's script, its
icon, and everything else the page loads are served by the instance itself from the `Scalar.AspNetCore` assembly, so
the page works on a machine with no route to the internet and no third party sits between a developer and their own
deployment. The one asset the package would otherwise fetch from a content delivery network is its typeface, and the
host turns that off.

## Development, and nowhere else

Outside `Development` neither the generator nor the routes are registered. Both addresses then answer `404` because no
endpoint was mapped there — not `401`, which would still confirm that a catalogue exists and merely refuse to hand it
over.

The rule reads the environment name exactly, so it holds for `Production`, for `Staging`, and for any custom name a
deployment invents. `ASPNETCORE_ENVIRONMENT` is what sets it, and a deployment that sets nothing is not in
`Development`: the container image, the Helm chart, the Compose file, and the Quadlet units all run in `Production`.

## One document for both surfaces

MailFathom serves two HTTP API surfaces, and the document contains both:

- the **administrative** surface beneath `/api/admin`, which [`mfctl` reaches](admin-endpoint.md); and
- the **client** surface beneath `/api/client`, which [the MailFathom client reaches](client-endpoint.md).

They are described together because a developer's question is about the deployment rather than about one listener, and
two documents would answer it only when both were opened. Nothing about the separation changes: each surface keeps its
own listener, its own credentials, and its own permission vocabulary.

The document describes what this instance mapped, so a surface its operator did not enable contributes no operations to
it. That is the same answer the instance gives to a request — a route on a disabled surface is served nowhere — rather
than a limitation of the document.

Everything else this process maps stays out, because none of it is an HTTP operation with a contract to publish here:
the MCP protocol route, whose tools are described by the protocol itself, the attachment download a signed link admits,
the [health probes](health-endpoints.md), and the two RFC 9728 metadata documents, which exist for a client that holds
no credential yet.

## Reading it does not open anything

Both documentation routes admit an anonymous caller. Neither carries an authorization requirement, a rate-limiting
policy, or a request timeout from either API surface, because both are mapped outside the groups those are attached to.

That says nothing about the operations described. Every request the explorer makes travels the ordinary pipeline and
meets the same authentication, authorization, limits, and timeouts as any other client's — invoking a protected
operation without its credential receives the ordinary refusal. The document records that faithfully: an operation the
running host would demand a credential for is published carrying a `Bearer` security requirement, and one the host
would serve to anybody is published without one. Both surfaces are unauthenticated until an operator configures a
credential for them, and the document then describes them that way rather than describing a lock that is not on the
door.

To call a protected operation from the explorer, use its authentication control and supply the credential the surface
you are calling accepts — an API key, an access token from a configured authorization server, or a client assertion.
All three are presented in the same `Authorization: Bearer` header, which is why the document publishes one scheme.
Credentials do not cross surfaces: one provisioned for the administrative endpoint authenticates nothing on the client
endpoint, and the reverse.

> The explorer keeps whatever credential you type in the browser. Give it a development credential rather than one that
> reaches a real mailbox.

## Which listener answers

The documentation addresses belong to no surface, so every listener this process bound serves them — including one
carrying only the administrative endpoint, which is an ordinary shape for a development instance. That is the one
exception to the rule that a path is served only where the surface owning it is served; the
[endpoint configuration](configuration-endpoints.md) page describes the rule itself.

## What the document does not carry

Operation and schema descriptions are not derived from the XML documentation comments in the source. The OpenAPI
package ships a source generator for that, and it cannot run in this host: it builds one cache keyed by documentation
identifier across the host's own compilation and every assembly it references, and `StampedAssemblyVersion` is compiled
into two of them from `backend/src/shared/`, which makes the cache throw on a duplicate key the first time a document
is requested. `Host.csproj` removes the generator and records the two upstream issues. Paths, verbs, parameters,
request bodies, responses, and schemas are unaffected.
