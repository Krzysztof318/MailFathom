# The client endpoint

<!-- describes: backend/src/Host/Configuration/Endpoints/ClientEndpointOptions.cs, backend/src/Host/Configuration/Endpoints/ClientApplicationOptions.cs, backend/src/Host/Api/ClientApiEndpoints.cs, backend/src/Host/Api/ProtectedResourceMetadataEndpoint.cs, backend/src/Host/Security/Endpoints/ClientTransportSecurityExtensions.cs, backend/src/Host/Hosting/ClientApplicationFiles.cs, backend/src/Host/Hosting/Warnings/ClientTransportSecurityWarning.cs -->

Where the MailFathom client reaches the service, what a deployment has to enable before it answers, and what a person's
mail client presents to get in.

It is a third transport surface beside the two that already exist, and it is deliberately not either of them with more
routes on it. An agent's key, an operator's key, and the credential somebody signs their own mail client in with are
three things to provision and three things to revoke; keeping them on separate listeners with separate credentials is
what makes that a fact rather than a convention.

What it draws on is the mailbox rather than a vocabulary of its own. A grant written here comes from the same half of
[the published set](permissions.md#the-published-set) the MCP endpoint's grants come from, because the client reads the
mail an agent reads. Only the transport is new.

## The endpoint is off unless you turn it on

A deployment that configures nothing serves no client surface, so adopting a release opens no new network door onto a
mailbox. Enabling it opens a listener of its own:

```jsonc
{
  "ClientEndpoint": {
    "Enabled": true,
    "Port": 8080,
    "Cors": { "AllowedOrigins": [ "https://mail.example.test" ] },
    "Authentication": [
      {
        "OAuth": {
          "Resource": "https://mail.example.test/api/client",
          "AuthorizationServers": [
            {
              "Name": "workforce",
              "Issuer": "https://sso.example.test/realms/mailfathom",
              "AuthorizedSubjects": [ "11111111-2222-3333-4444-555555555555" ]
            }
          ]
        }
      }
    ]
  }
}
```

**The listener is its own, and that is the point.** Client routes answer on the client listener and nowhere else, and a
request for `/api/client` arriving on the MCP or the administrative port is refused before it reaches any credential
check — answered `404`, because the honest answer is that nothing is served there. The default port is the other
surfaces' own, so enabling several without stating a port publishes one socket serving each of them at its own prefix
rather than three sockets; [sharing a socket](configuration-endpoints.md#sharing-a-socket) states what that couples and
what stays each surface's own.

Every key this section takes is in
[endpoint configuration](configuration-endpoints.md#clientendpoint), with its default and its constraint.

**A local `aspire run` turns it on for you**, on the port the MCP endpoint already binds and with the browser head's own
origin as the one origin it answers, because that run starts a head that has to reach something. It configures no
credential, which is the posture every surface has locally.
[The client resource](local-development.md#the-client-resource) is what does it and what to state to stop it.

## What it serves

One route, and deliberately one:

```http
GET /api/client/session
```

It answers with three fields: `service`, which is always `MailFathom`; `version`, the running release; and
`permissions`, the published names the credential just presented carries, in the order this project publishes them.

That is what a client needs before it has drawn a single message: that this is MailFathom rather than something else
answering the port, which contract it speaks, and what the rest of the surface will serve it. It is also what lets
sign-in be built and proven end to end before a screen exists — a client that reached here with a token it had just been
issued knows the token works.

**It names no credential**, which is the one way it differs from what
[`GET /api/admin/session`](admin-endpoint.md) answers. That surface's reader is `mfctl` in an operator's own hands, and
the deployment's configured name for the credential that authenticated is what tells them which of their own entries let
them in. This surface's reader is a page holding a token, which brought no name and has nothing to do with one; a
response echoing a deployment's configured identity for a credential would be a way to read configuration back out of
the service from a browser.

A caller granted nothing reads an empty `permissions` list rather than a refusal, because "nothing" is the accurate
answer to what such a caller may do, and because a credential retired by narrowing its entry to nothing should be
distinguishable from one that no longer works.

**The mail-reading routes are a separate change.** A transport surface is where a misordered middleware, a scheme that
authenticates the wrong caller, or a listener bound where it should not be fails silently and arrives as a working
deployment answering the wrong way, so the surface is published with nothing on it but proof of life. There is no
version segment in the path either: the major version is `0` and
[ADR 0004](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md)
permits breaking the contract outright, so `/v1` would be scaffolding for a promise this project has not made.

## Credentials do not cross surfaces

A key configured under `McpEndpoint` or `AdminEndpoint` authenticates nothing here, and one configured here
authenticates nothing there. The separation is mechanical rather than conventional: each surface registers its own
authentication schemes and its own authorization policy, and a policy consults only its own schemes.

`Authentication` takes the same entries the other two sections take — one entry per credential, each carrying an
`ApiKey` block, a `PublicKey` block, an `OAuth` block, or any combination of them — and every one of them is this
endpoint's own. Each method is documented once, under [the MCP endpoint](mcp-endpoint.md#authentication). What differs
here is the audience a signed assertion names, `urn:mailfathom:client`, which is what keeps a credential minted to read
a mailbox as an agent from signing in as somebody's mail client even where one client is registered on both.

A grant written on an entry draws from the mailbox half of the published set, so a name or a pattern reaching only the
administrative half fails startup naming the entry's index. [Writing a grant](permissions.md#writing-a-grant) is the
whole of that rule; nothing about it is particular to this surface.

## Signing a person in

The credential this surface is built around is an access token a public client obtained under the authorization code
flow with PKCE. MailFathom is a protected resource only — it signs nobody in, holds no user, and issues no token — so
what a deployment configures here is which authorization server's tokens are believed and which subjects they may be
issued to.

**Every `OAuth` entry must name a `Resource` ending in `/api/client`.** Startup refuses anything else, naming the
setting. The reason is discovery rather than OAuth: the client is configured with an address and finds the
protected-resource metadata document by appending the prefix it is about to call, which reaches the document's RFC 9728
location exactly when the resource names the same prefix. It matters more here than on the administrative surface,
because the reader is a page that cannot be told the address by hand.

The document is published at the root, beneath `/.well-known/oauth-protected-resource`, and it follows its surface: it
answers on the client listener and is `404` on any other. It is served to a caller holding nothing, which is the whole
point of publishing one — its reader has not authorized yet and is trying to find out where to.

```http
GET /.well-known/oauth-protected-resource/api/client
```

Behind a reverse proxy, write the public URL and keep the path: `https://mail.example.test/api/client`. What a token
must prove, and why the advertised scope list is longer than the checked one, is
[under the MCP endpoint](mcp-endpoint.md#oauth); none of it differs here.

## Browser origins

This is the one control the administrative endpoint has no use for. Its clients are command-line tools with no origin to
be told anything, while a WebAssembly head calls this surface from a page — and a preflight this endpoint cannot answer
is a client that never starts.

| `ClientEndpoint:Cors:AllowedOrigins` | What the endpoint answers |
| --- | --- |
| absent | Every browser origin |
| `["*"]` | Every browser origin, stated |
| `["https://mail.example.test"]` | Exactly the origins listed |
| `[]` | No browser origin, which still serves every client that sends no `Origin` |

The permissive default is deliberate, for the reason [the MCP endpoint's is](mcp-endpoint.md#cors-and-the-origin-header):
a surface is protected by the credential a caller presents rather than by which page it was called from, and a first run
that failed a preflight would look like a broken deployment. Narrow it to the origin your own head is served from once
you know what that is. The empty list is the third posture rather than an oversight — it advertises nothing to a
browser, which is what a deployment whose client is a desktop or mobile head wants, since neither is subject to CORS at
all.

The policy allows `GET`, the `Authorization`, `Content-Type`, and `Accept` request headers, and exposes
`WWW-Authenticate` so a page can read the challenge that tells it where to authorize. **Credentials are never allowed
under any of the postures above**: a browser that could attach an ambient cookie would let a page act as whoever is
signed in somewhere else, and this surface's credential is a bearer token the client sets deliberately.

## What bounds the traffic

`ClientEndpoint:RateLimiting` and `ClientEndpoint:RequestTimeout` carry the same keys, defaults, and validation the
other two surfaces carry, and are configured independently of them: neither one's traffic reaches another's limits. Both
apply whether or not anyone wrote a number, which is what stops a surface reachable from a page from serving unbounded
key guessing.

**Both are attached to this surface's routes rather than applied as the process's default policy**, which is what keeps
the health probes answering while the client endpoint is refusing. A default limiter would count a readiness probe
against the same capacity a browser is spending, and a deployment under load would start failing the probe that decides
whether it is taken out of service.

## Transport security

The surface takes the same `Transport` setting the other two do, with the same three modes and the same clear-text
redirect — see [`Transport`](configuration-endpoints.md#transport). It is `Http` by default, which is right behind a
TLS-terminating reverse proxy and wrong anywhere else, so startup says so:

- an enabled endpoint with no `Authentication` entry warns that anything reaching the address is served the mailbox, and
  names the section that fixes it;
- an enabled endpoint terminating no TLS warns that any credential a client presents is readable on the path, and names
  its clear-text port;
- an endpoint serving [the page](#serving-the-client-from-the-deployment) over clear text an operator explicitly
  permitted reports that separately, at every startup, naming the permission rather than assuming it is still true.

Both are warnings rather than refusals, because a loopback bind, a private network, and a proxy that terminates TLS are
each a deployment where one of them is the right answer, and only an operator knows which they have. Serving the page
is the one part that is refused rather than warned about, for the reason the next section gives.

## Serving the client from the deployment

The MailFathom container image carries the client's browser head, and one setting serves it from this endpoint's own
listeners:

```jsonc
{
  "ClientEndpoint": {
    "Enabled": true,
    "Application": { "Enabled": true }
  }
}
```

It is off in every default — in the configuration, in the chart's `values.yaml`, in the Compose file, and in the Quadlet
unit — so adopting a release publishes no page. Each deployment asset turns it on with one decision of its own:
`client.enabled` in the chart and `MAILFATHOM_CLIENT` in Compose each write both keys below, while the Quadlet unit
carries them as two adjacent commented `Environment=` lines, because a `.container` file has no variable to write two
keys through. [Kubernetes](deployment-kubernetes.md), [Compose](deployment-compose.md), and
[Quadlet](deployment-quadlet.md) each state theirs.

**What it adds is static files and nothing else.** The bundle answers the root of the listeners this endpoint is served
on, and the routes beneath `/api/client` are unchanged: same credentials, same grants, same limits. The page itself
carries no credential and needs none — a browser has to load the application before it can obtain one — and what that
application then calls is authorized exactly as any other caller is. Turning this on grants nobody anything; it puts
the client in front of the sign-in the endpoint already required.

Serving it from the same origin as the surface it calls is the point of serving it here at all: the page then needs no
cross-origin permission and `Cors:AllowedOrigins` has nothing to say about it. A client downloaded and installed rather
than served — the desktop head — reaches the same routes from its own origin and does need one.

**Clear text is refused rather than warned about**, and that is the one difference from every other posture on this
page. A page is what a person types their credential into, so a deployment that serves it over a socket this process
opened in the clear fails at startup naming the two ways out: terminate TLS here, with `ClientEndpoint:Transport` set
to `HttpsOnly` and a `ClientEndpoint:Https:Endpoints` profile; or state that something in front of this process already
did, by writing `ClientEndpoint:Application:AllowClearText: true`. Nothing here can tell an ingress terminating TLS
from a socket published to a network, which is why the second one is a declaration an operator makes rather than
something MailFathom infers. A socket that only redirects to HTTPS serves nothing and needs no permission.

Two more refusals belong to the same setting. Writing `Application:Enabled` while `ClientEndpoint:Enabled` is off fails
at startup rather than being ignored, and so does enabling it on a host that carries no bundle — the files travel
inside the container image, so a service run straight from the sources serves the API surfaces alone and says so
instead of answering a page of 404s.

There is no client-certificate profile here. The trust question a certificate answers is a second one this endpoint does
not yet ask; where it is served is stated in exactly the settings the existing endpoints use, so the day it does ask,
the answer arrives as a profile on a listener already shaped to carry one.

## Publishing it

Behind an ingress, publish `/api/client` as its own path to the same backend the other surfaces are on and keep the
prefix — the client appends the rest to the address it was configured with, and the `Resource` written above has to be
the public URL a browser actually reaches. The chart's `ingress.hosts[].paths` is where that goes;
[deploying on Kubernetes](deployment-kubernetes.md) is the page.

An ingress answers no preflight on the application's behalf, so `Cors:AllowedOrigins` still has to name the origin the
page is served from even where the ingress and the page share a host.

Serving the page needs one more path published: `/`, to the same backend, so the bundle and the routes it calls arrive
on one origin. Publish that only where the page is actually enabled — a path routed to a deployment that serves no
client is a `404` with an ingress in front of it.
