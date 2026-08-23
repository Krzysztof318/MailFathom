# Endpoint configuration

<!-- describes: backend/src/Host/Configuration/Endpoints/**, backend/src/Host/Configuration/Access/** -->

Every key deciding where each of the four surfaces is served, what a caller has to present to reach one, and how much
traffic and how long a request each will take. What an admitted caller may then do is a grant rather than a listener
setting, and [what a credential may do](permissions.md) is the whole of it. The tables read as
[the configuration reference](configuration-reference.md#how-to-read-the-tables) says they do, and that page is the map
to the rest of the sections.

## Where each surface is served

Every socket this process opens is named by the section of the surface that owns it, and by nothing else. `McpEndpoint`,
`AdminEndpoint`, and `ClientEndpoint` each state a `BindAddress`, a `Port`, and a `Transport`; `HealthEndpoints` states
the same three under a smaller set of TLS settings. Read those four sections and you have read every listener the
process binds.

**The host's own ways of naming a listener are refused at startup.** `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`,
`ASPNETCORE_HTTPS_PORTS`, `--urls`, and any endpoint under `Kestrel:Endpoints` each fail the process with a message
naming the setting that replaces them. They are refused rather than ignored because ignoring them is silent: Kestrel
drops the URL-shaped addresses as soon as a listener is bound in code — which every surface here does — and binds a
configured endpoint beside them on a socket no section describes, no credential guards, and no isolation middleware was
composed for. An operator who states a port deserves to be told it moved, not to find the surface answering somewhere
else.

A deployment that enables no surface at all is refused for the same reason. Kestrel answers zero listeners by binding
its own default address, and a development certificate on the machine would add a TLS one beside it, so the process
would hold a socket nothing configured.

### `Transport`

`McpEndpoint:Transport`, `AdminEndpoint:Transport`, and `ClientEndpoint:Transport` each decide what that surface's
clear-text socket does. The HTTPS half is the profiles under `Https:Endpoints`, each with its own domain, certificate,
TLS floor, and HTTP versions.

| Value | The socket at `BindAddress`:`Port` | `Https:Endpoints` |
| --- | --- | --- |
| `Http` | Serves the routes | Must be empty |
| `HttpAndHttps` | Redirects to the profiles, or serves the routes when `Https:Redirect:Enabled` is `false` | Bind |
| `HttpsOnly` | Not opened at all | Bind |

`Http` is the default, so adopting a release costs no certificate work, and it is the right posture behind a
TLS-terminating reverse proxy and wrong anywhere else — startup warns about it either way, because only an operator
knows which they have.

`HttpsOnly` is the posture that leaves nothing reachable in clear text. `HttpAndHttps` keeps the clear-text socket, and
the redirect is on unless a deployment turns it off, so enabling TLS does not read as an outage to a client nobody has
repointed yet; turning the redirect off is what makes that socket serve the routes, which is the deliberate
both-schemes posture rather than the migration one. `HealthEndpoints:Transport` takes the same three values, and
because the probes carry one certificate rather than profiles, its `HttpAndHttps` needs a second port of its own in
`HealthEndpoints:HttpsPort`.

### Sharing a socket

Two surfaces, or all four, may name one port. That is the posture a single-node deployment behind one ingress wants —
one socket to publish and one backend to route — and it is why all three request-serving surfaces default to `8080` for
clear text and `8443` for a profile. The port is bound once and serves each surface's own paths; which paths a request may ask
for is decided from the port it arrived on, so a surface that is not on that port is still refused there with a `404`.

**What sharing costs is exposure.** The probes answer without a credential and the administrative surface is a different
authority from the mailbox, so putting either on the endpoint's port publishes it wherever that port is published. Keep
them apart when that matters; the ports exist so the decision is yours.

#### Which settings a shared socket couples

Sharing is per socket, not per surface. Each surface declares its clear-text socket (`BindAddress` + `Port`) and one
socket per HTTPS profile (`Https:Endpoints:<n>:BindAddress` + `:Port`) separately, so two surfaces may share the
clear-text one and keep TLS sockets of their own. Every rule below applies to one socket at a time, and each failure
names both sections.

| Setting | Coupled on | Why it cannot differ |
| --- | --- | --- |
| `Transport` — whether *this* socket carries TLS | Every shared socket | One socket serves one scheme |
| `Https:Redirect:Enabled` | A shared clear-text socket | The socket either redirects or serves the routes; it cannot do both |
| The domain a redirect resolves | A shared **redirecting** socket | The client sent one host name, so two answers to it would be settled by composition order |
| Profiles by server name vs one certificate | A shared TLS socket | A socket answers a handshake one way; the probes present one certificate and the endpoints select by name |
| `ClientCertificateProfiles` — configured or not | A shared TLS socket | Whether a certificate is asked for is settled while the connection is established |
| `Https:Endpoints:<n>:Domain` — uniqueness | A shared TLS socket | One name served by two surfaces would leave composition order deciding which the client reached |
| `Https:Endpoints:<n>:HttpProtocols` | A shared TLS socket | ALPN offers what the listener was bound with, which is before any server name has been read |
| `BindAddress` — a wildcard beside a specific address | The same port | The operating system grants only one of those two sockets |

**What stays each surface's own, on a shared socket as much as on a separate one:**

- The **HTTPS ports.** Two surfaces sharing a clear-text socket may redirect to profiles on ports of their own, because a
  redirect resolves the name the client asked for: `mail.example.test` goes to the MCP endpoint's port and
  `admin.example.test` to the administrative one, from the same `8080`. Only publishing *one* name at two ports is
  refused.
- `MinimumTlsVersion`, per profile. The TLS floor is settled per connection, after the server name is known, so profiles
  on one socket may each keep their own.
- Different **domains** on one TLS socket, which is what sharing one is for.
- Everything decided per request rather than per socket: `Authentication` and every method it carries, `Cors`,
  `RateLimiting`, origin validation, and the route prefix. An API key provisioned for an agent still authenticates
  nothing under `/api/admin`, whichever port both are reached on.

Two specific addresses on one port are two sockets and are accepted; none of the rules above applies between them.

## `ReverseProxy`

Which peers this process accepts a public scheme and host from, when something in front of it terminates TLS. One
section for the whole process rather than one per surface: it runs at the front of the one request pipeline every
listener shares, so a proxy named here is trusted on each of them.
[Behind a TLS-terminating reverse proxy](mcp-endpoint.md#behind-a-tls-terminating-reverse-proxy) is the page.

`X-Forwarded-Proto` and `X-Forwarded-Host` are always read; there is no key that switches that off. What the section
carries is who they are believed from, and **an unconfigured section believes every peer.**

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ReverseProxy:TrustedProxies` | string list | empty, which trusts `0.0.0.0/0` and `::/0` | Each entry an IP address or a CIDR network whose host bits are clear — not a DNS name. What is named replaces the default rather than adding to it, and the framework's loopback default is cleared rather than inherited. Left empty, or written as `0.0.0.0/0` and `::/0`, it trusts every peer and so disables the refusal of an OAuth token that arrived without TLS — see [what the default costs](mcp-endpoint.md#behind-a-tls-terminating-reverse-proxy) | restart |
| `ReverseProxy:MaximumForwardedHops` | int | `1` | At least 1; how far right-to-left through each header a value is believed | restart |

`X-Forwarded-For` is never read, so the peer MailFathom observes stays the one that opened the connection, and the
configured OAuth `Resource` stays a value you wrote rather than anything derived from a header.

## `ConnectionLimits`

How many connections this process accepts at once, across every listener it opens. The other section that belongs to
the whole process rather than to a surface, and for a stronger reason than `ReverseProxy`: a connection is accepted
before any routing has decided which endpoint it was for, so there is no per-surface form of this question to ask.

**Read it as the process's ceiling, never as the sum of what the endpoints permit.** `McpEndpoint:RateLimiting:MaxConcurrentRequests`
bounds what one surface serves at once; this bounds what the machine accepts at all, the probe listener included. The
two numbers are deliberately far apart, because a connection is not a request — a client holds one open across several,
and an idle one survives until the keep-alive timeout — so a ceiling near the request limit would refuse ordinary
clients long before it refused a flood.

It exists because every other limit is reached too late to see this. The rate limiter partitions a request that already
has an `HttpContext`, and what a connection flood spends before that point — the accept, the TLS handshake, and on the
MCP surface the client certificate's chain building — is the most expensive per-connection work the process does.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ConnectionLimits:Enabled` | bool | `true` | Turning it off restores the framework's own default, which accepts connections until the operating system stops supplying them; it costs a startup warning | restart |
| `ConnectionLimits:MaxConcurrentConnections` | int | `1000` | 1 – 100000; process-wide, across every listener | restart |

Like every limit here it is counted in this process alone, so a deployment running several instances enforces it once
per instance rather than once in total, and none of it is protection against a distributed flood.

## `McpEndpoint`

Whether the protocol surface is served and what a client must present. The whole section is **restart** — it decides
routing and listeners — while key and certificate material is read per request or per handshake. Where it is served is
[its own `BindAddress`, `Port`, and `Transport`](#where-each-surface-is-served).
[The MCP endpoint](mcp-endpoint.md) is the page, section by section.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `McpEndpoint:Enabled` | bool | `false` | — | restart |
| `McpEndpoint:BindAddress` | string | `0.0.0.0` | An IP address; binds the clear-text socket, which `HttpsOnly` does not open | restart |
| `McpEndpoint:Port` | int | `8080` | 1–65535. The administrative and client endpoints' default as well — see [sharing a socket](#sharing-a-socket) | restart |
| `McpEndpoint:Transport` | enum | `Http` | `Http`, `HttpAndHttps`, `HttpsOnly` — see [`Transport`](#transport) | restart |
| `McpEndpoint:Authentication` | list of credentials | empty | One entry per accepted credential; empty warns at startup. A value written here rather than a list fails startup | restart |
| `McpEndpoint:PublishedToolCategories` | string list | absent = every category | `mailbox`, `flags`, `sending`, `drafts`, `answering`, `contacts`, in any case; a name nothing publishes fails startup naming the value and listing what is accepted. Only narrows — see [what the endpoint publishes](#what-the-endpoint-publishes) | restart |

### What the endpoint publishes

The coarse answer to what this instance offers, beside the per-capability switches that decide what it can do. Every
category is published unless the list names some, so a deployment written before the setting existed keeps the surface
it had, and an endpoint that should offer nothing at all is one with `Enabled` set to `false`.

**A category only ever takes away.** Naming `sending` does not enable sending — the account's own switches decide that
— and no category widens a grant or reveals a tool a caller was not offered. A tool appears when its capability is
available, its category is published, and the caller's grant reaches it.

A connecting client may narrow further for its own session, by naming categories in the `MailFathom-Tool-Categories`
header. The effective set is the intersection with the list above, so a category this list excludes is never published
because a client asked for it. [Tool categories](../features/mcp-tools.md#tool-categories) states which tools each
category carries and what the header does with a value it cannot read.

### The accepted credentials — `McpEndpoint:Authentication:<n>`

Each entry carries the block of whichever method judges it, and the block's presence is what selects that method — there
is no separate setting naming it. As many entries may state any method as a deployment needs, and one entry may carry
several blocks; an entry carrying none fails startup, named by its position. A grant written on an entry adds five more
refusals and makes one combination of blocks impossible —
[writing a grant](permissions.md#writing-a-grant) is where they are stated. Both endpoints take the
same entries; the administrative one adds a single rule, stated with it below.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:<n>:ApiKey` | secret block | — | One [named secret](secret-provisioning.md#the-secret-block) with its own `Lifetime`; a second key is a second entry | restart; material per request |
| `…:<n>:PublicKey` | secret block | — | One [named secret](secret-provisioning.md#the-secret-block) with its own `Lifetime`, resolving to one client's PEM public key. Startup refuses material that is not one, an RSA key below 2048 bits, and — explicitly — material carrying a private key | restart; material per request |
| `…:<n>:Permissions` | string list | absent = everything this surface publishes | The [permissions](permissions.md#the-published-set) every credential this entry admits may hold; an empty list grants nothing. A value writing `*` as a whole segment grants [every published name the pattern reaches on this surface](permissions.md#writing-a-grant), the wildcard standing for one or more segments at whatever position it is written, resolved against the published set on every start. A name nothing publishes, a name belonging to the other surface, a repeated name, a pattern matching nothing or matching only the other surface, a pattern overlapping what the grant already carries, and a bare `*` or `mailfathom.*` each fail startup naming the entry's index | restart |
| `…:<n>:PermissionsFromTokenScopes` | bool | `false` | Narrows the list above by each token's own scopes instead of granting all of it. Refused on an entry that also carries `ApiKey` or `PublicKey`, neither of which can carry a scope | restart |
| `…:<n>:OAuth:Resource` | string | — | Required; the canonical `https` URL clients reach this endpoint at — behind a proxy, the proxy's public URL. Every OAuth entry names the same one, because the endpoint publishes one metadata document at an address derived from it | restart |
| `…:<n>:OAuth:RequiredScopes` | string list | empty | Scopes a token from *this entry's* servers must carry; empty accepts any token they issued for this resource. A permission name is refused here, because requiring one would close the door on a caller the deployment meant to serve less, and so is a pattern covering permissions, because a scope is compared byte for byte and no authorization server can mint one | restart |
| `…:<n>:OAuth:AdvertisedScopes` | string list | empty | Scopes published in `scopes_supported` for a client to ask for and checked on no token — `offline_access` is what a client needs to be issued a refresh token. Every required scope is published regardless, so a value repeating one is refused, as is one that is not a scope token, as is a permission name — the grant that reads one advertises it already — and as is a pattern covering permissions, since the grant publishes the names one resolves to | restart |
| `…:<n>:OAuth:AuthorizationServers:<m>:Name` | string | — | Required; the identity diagnostics use, and unique across every entry because it composes the scheme its validator registers under | restart |
| `…:<n>:OAuth:AuthorizationServers:<m>:Issuer` | string | — | Required; a well-formed `https` issuer, compared against `iss` exactly, and unique across every entry | restart |
| `…:<n>:OAuth:AuthorizationServers:<m>:MetadataAddress` | string | unset | An absolute `https` URL on the issuer's own host; overrides issuer-derived discovery | restart |
| `…:<n>:OAuth:AuthorizationServers:<m>:AuthorizedSubjects` | string list | — | At least one; a token whose `sub` is not listed is refused, so every user the server can sign in does not automatically read this mailbox | restart |

MailFathom is a protected resource only; an external authorization server signs users in.
[`OAuth`](mcp-endpoint.md#oauth) records what a token must prove and
[scopes you advertise but do not require](mcp-endpoint.md#scopes-you-advertise-but-do-not-require) why the published list
is longer than the checked one,
[API keys](mcp-endpoint.md#api-keys) what a key is compared against, and
[Key pairs](mcp-endpoint.md#key-pairs) what a client signs and what the deployment verifies — including the audience,
expiry, and replay identifier an assertion carries, none of which is a setting.


### Browser origins — `McpEndpoint:Cors`

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:AllowedOrigins` | string list | absent = every origin | `*` for every origin, a list for exactly those, an empty list for none | restart |

The default is deliberately the permissive one — an `Origin` header only exists in browsers, and a native client is
unaffected — but a deployment reachable from a browser should narrow it.
[CORS and the `Origin` header](mcp-endpoint.md#cors-and-the-origin-header) explains what the check does and does not
protect.

### TLS termination — `McpEndpoint:Https:Endpoints:<n>`

Read under the two `Transport` modes that terminate TLS and refused under the one that does not. Configuring any
profile **takes over the host's listeners**: only the profiles' sockets are opened.
[HTTPS and your own domain](mcp-endpoint.md#https-and-your-own-domain) is the page.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Name` | string | — | Required; unique | restart |
| `…:Domain` | string | — | Required; the DNS name the certificate is proven to cover | restart |
| `…:BindAddress` | string | `0.0.0.0` | An IP address | restart |
| `…:Port` | int | `8443` | 1 – 65535 | restart |
| `…:MinimumTlsVersion` | enum | `Tls12` | `Tls12`, `Tls13` | restart |
| `…:HttpProtocols` | enum list | `Http1`, `Http2` | `Http1`, `Http2`, `Http3`; selecting `Http3` where the platform provides no QUIC fails startup rather than falling back | restart |
| `…:ServerCertificate` | certificate block | — | Required; see below | restart; renewal behind unchanged references — see [secret rotation](secret-rotation.md#renewing-an-mcp-server-certificate) |

A certificate block names either `Bundle` (one PKCS#12 secret block, optionally with a nested `Password`) or the pair
`CertificateChain` and `PrivateKey` (PEM, as two secret blocks). Startup proves the material loads, covers the stated
domain, and is not expired — before any listener opens.

### Clear-text redirect — `McpEndpoint:Https:Redirect`

What the surface's clear-text socket does while the profiles above are served. On, it answers every request with a `308`
to the address those profiles are at, so enabling TLS does not read as an outage to a client nobody repointed yet; it
then maps no route and runs no credential check. Off, the same socket serves the routes in clear text.

The socket is `McpEndpoint:BindAddress` and `McpEndpoint:Port` — there is no address here to state again. The section is
meaningful under `Transport: HttpAndHttps` alone, which is the one mode with both a clear-text socket and somewhere to
send what arrives on it; writing it under either other mode fails startup.
[Redirecting a client still pointed at `http://`](mcp-endpoint.md#redirecting-a-client-still-pointed-at-http) records what
a redirect does and does not protect.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Enabled` | bool | `true` | Refused unless `Transport` is `HttpAndHttps` | restart |

### Client certificates — `McpEndpoint:ClientCertificateProfiles:<n>`

Mutual TLS, judged per configured client application. A certificate exists only on a TLS connection this process
terminates — over the HTTPS profiles above, or over a listener the deployment configured with TLS otherwise — so a
plain-HTTP deployment presents none, which a `Required` profile refuses.
[Client certificates](mcp-endpoint.md#client-certificates) records how a presented certificate is judged.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Name` | string | — | Required; unique | restart |
| `…:Requirement` | enum | — | `Optional`, `Required`; required to be stated | restart |
| `…:TrustAnchors` | list of secret blocks | — | At least one; the authorities the client's chain must anchor in | restart; material per handshake |
| `…:SubjectAlternativeNames` | string list | — | At least one; a DNS name the certificate must carry | restart |

### Rate limiting

One of the two endpoint subsections where every value has a product default — [request timeout](#request-timeout)
is the other — so an enabled endpoint is bounded whether or not
anyone wrote a number. `McpEndpoint:RateLimiting`, `AdminEndpoint:RateLimiting`, and `ClientEndpoint:RateLimiting`
carry it, with the same keys, defaults, and validation, and configure it
independently: no endpoint's traffic reaches another's limits. [Rate limiting](mcp-endpoint.md#rate-limiting) records
whose capacity a request spends, and [administering a deployment](admin-endpoint.md#rate-limiting) records the one
behavioural difference on the administrative endpoint — its burst is the endpoint's rather than one caller's, because
that surface judges a credential behind the limiter.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Enabled` | bool | `true` | Turning it off costs a startup warning | restart |
| `…:MaxConcurrentRequests` | int | `20` | 1 – 1000; process-wide, per endpoint | restart |
| `…:ConcurrencyQueueLimit` | int | `0` | 0 – 1000; `0` refuses instead of queueing | restart |
| `…:TokenCapacity` | int | `60` | 1 – 1000000; the largest burst one caller may spend | restart |
| `…:TokensPerReplenishmentPeriod` | int | `60` | 1 – 1000000, and not above `TokenCapacity` | restart |
| `…:ReplenishmentPeriod` | TimeSpan | `00:01:00` | 1 s – 1 h | restart |
| `…:RequestQueueLimit` | int | `0` | 0 – 1000, and below `MaxConcurrentRequests` | restart |

### Request timeout

How long one request may run before the endpoint abandons it, answering `504` and releasing the concurrency permit it
held. Defaulted throughout like the rate limits, carried by all three request-serving endpoints with the same keys, and
configured independently of them — because how much traffic is admitted and how long an admitted request may hold what it was
admitted with are different questions, and a deployment may already have one answered in front of the process without
the other.

Without it, `MaxConcurrentRequests` bounds how many requests run at once and nothing bounds how long any of them lasts,
so twenty slow requests take a surface out of service without exceeding any rate.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Enabled` | bool | `true` | Turning it off costs a startup warning | restart |
| `…:Duration` | TimeSpan | `00:10:00` | 1 s – 1 h | restart |

**The default is a bound on a hang rather than a promise that no legitimate request is abandoned.** An `ask_mail` run is
a conversation whose length the model decides, bounded by `MailAnswering:MaxProviderCallsPerRun` at eight calls, each an
`AiProviderInvocation` whose own `TotalTimeout` defaults to five minutes — so a ceiling enclosing the maximum would sit
at forty minutes, which is not a request ceiling and would let one stalled run hold a concurrency permit
that long. Ten minutes clears an ordinary answering run by a wide margin and abandons one that walks its whole provider
budget, which is the trade taken. Raise it alongside `MailAnswering:MaxProviderCallsPerRun` if you raise that. A
deployment serving no AI-backed tool narrows it instead: every other MCP tool answers from the local mailbox copy with a
bounded query, so a minute is generous there. `AdminEndpoint` reaches no provider at all, which makes it the endpoint to
narrow without having to ask what a tool call needs.

**Both are attached to each surface's own routes rather than applied as the process's default policy.** A default
limiter or a default ceiling would count a readiness probe against the capacity a caller is spending, so a deployment
under load would start failing the probe that decides whether it is taken out of service.

The ceiling is applied ahead of the rate limiter, so time a request spends waiting for a limiter lease is inside it.
That wait is nothing under the default queue limits of `0`, and is the whole point of the ordering once a queue is
configured: a request queued for its caller's tokens already holds a concurrency permit.

## `AdminEndpoint`

Whether the administrative surface the `mfctl` command reaches is served, and what a client must present. Its own
listener, its own credentials, and its own authorization servers: a key configured under `McpEndpoint` authenticates
nothing here, and the reverse holds. The whole section is **restart**, while key and certificate material is read per
request or per handshake. [Administering a deployment](admin-endpoint.md) is the page.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `AdminEndpoint:Enabled` | bool | `false` | — | restart |
| `AdminEndpoint:BindAddress` | string | `0.0.0.0` | An IP address; binds the clear-text socket, which `HttpsOnly` does not open | restart |
| `AdminEndpoint:Port` | int | `8080` | 1–65535. The MCP and client endpoints' default as well, so enabling several without stating a port publishes one shared socket — see [sharing a socket](#sharing-a-socket) | restart |
| `AdminEndpoint:Transport` | enum | `Http` | `Http`, `HttpAndHttps`, `HttpsOnly` — the same setting the MCP endpoint carries, read the same way | restart |
| `AdminEndpoint:Authentication` | list of credentials | empty | Same shape and rules as [`McpEndpoint:Authentication:<n>`](#the-accepted-credentials--mcpendpointauthenticationn), with three additions: every `OAuth` block's `Resource` must end in `/api/admin`, because that is where these routes answer and what `mfctl` appends to find the metadata document; a client assertion presented here names the audience `urn:mailfathom:admin` rather than `urn:mailfathom:mcp`; and `Permissions` draws from the administrative half of [the published set](permissions.md#the-published-set), so a name or a pattern reaching only the mail half fails startup here | restart; material per request |
| `AdminEndpoint:Https:Endpoints:<n>` | list of profiles | empty | Same shape and rules as `McpEndpoint:Https:Endpoints:<n>`, read under the two `Transport` modes that terminate TLS | restart; material per handshake |
| `AdminEndpoint:Https:Redirect` | block | on | Same shape and rules as `McpEndpoint:Https:Redirect`; its socket is this surface's own `BindAddress` and `Port`, so terminating TLS on both surfaces opens two clear-text ports that do not collide | restart |
| `AdminEndpoint:RateLimiting` | block | bounded | Same shape, defaults, and rules as `McpEndpoint:RateLimiting` above; applied whether or not it is written | restart |
| `AdminEndpoint:RequestTimeout` | block | bounded | Same shape, defaults, and rules as [`McpEndpoint:RequestTimeout`](#request-timeout) above; applied whether or not it is written. This surface reaches no AI provider, so it is the one the default can be narrowed on freely | restart |

The routes are served beneath `/api/admin`, which is a constant rather than a setting: a client is configured with a
host and a port and appends the rest.

## `ClientEndpoint`

Whether the surface the MailFathom client reaches is served, and what a client must present. Its own listener, its own
credentials, and its own authorization servers, exactly as the administrative endpoint has: a key configured under
`McpEndpoint` or `AdminEndpoint` authenticates nothing here, and one configured here authenticates nothing there. The
whole section is **restart**, while key material is read per request. [The client endpoint](client-endpoint.md) is the
page.

What a grant here draws on is the mailbox half of [the published set](permissions.md#the-published-set) — the same half
the MCP endpoint's grants come from, because the client reads the mail an agent reads. Only the transport is separate.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ClientEndpoint:Enabled` | bool | `false` | — | restart |
| `ClientEndpoint:BindAddress` | string | `0.0.0.0` | An IP address; binds the clear-text socket, which `HttpsOnly` does not open | restart |
| `ClientEndpoint:Port` | int | `8080` | 1–65535. The other two request-serving endpoints' default as well — see [sharing a socket](#sharing-a-socket) | restart |
| `ClientEndpoint:Transport` | enum | `Http` | `Http`, `HttpAndHttps`, `HttpsOnly` — the same setting the other endpoints carry, read the same way | restart |
| `ClientEndpoint:Authentication` | list of credentials | empty | Same shape and rules as [`McpEndpoint:Authentication:<n>`](#the-accepted-credentials--mcpendpointauthenticationn), with three additions: every `OAuth` block's `Resource` must end in `/api/client`, because that is where these routes answer and what the client appends to find the metadata document; a client assertion presented here names the audience `urn:mailfathom:client`; and `Permissions` draws from the mail half of [the published set](permissions.md#the-published-set), so a name or a pattern reaching only the administrative half fails startup here | restart; material per request |
| `ClientEndpoint:Cors` | block | every origin | Same shape and rules as [`McpEndpoint:Cors`](#browser-origins--mcpendpointcors), configured separately. The setting the administrative endpoint has no use for and the one a browser-hosted client cannot start without — see [browser origins](client-endpoint.md#browser-origins) | restart |
| `ClientEndpoint:Https:Endpoints:<n>` | list of profiles | empty | Same shape and rules as `McpEndpoint:Https:Endpoints:<n>`, read under the two `Transport` modes that terminate TLS | restart; material per handshake |
| `ClientEndpoint:Https:Redirect` | block | on | Same shape and rules as `McpEndpoint:Https:Redirect`; its socket is this surface's own `BindAddress` and `Port` | restart |
| `ClientEndpoint:RateLimiting` | block | bounded | Same shape, defaults, and rules as [`McpEndpoint:RateLimiting`](#rate-limiting) above; applied whether or not it is written | restart |
| `ClientEndpoint:RequestTimeout` | block | bounded | Same shape, defaults, and rules as [`McpEndpoint:RequestTimeout`](#request-timeout) above; applied whether or not it is written | restart |
| `ClientEndpoint:Application:Enabled` | bool | `false` | Refused unless `ClientEndpoint:Enabled` is on. Serves the client's browser head from this endpoint's own listeners; the bundle travels inside the MailFathom container image, and a host started from anything else refuses at startup rather than answering 404s | restart |
| `ClientEndpoint:Application:AllowClearText` | bool | `false` | Required before the page is served over a socket this process opens in the clear, and refused where it is not — see [serving the page](#serving-the-page--clientendpointapplication) | restart |

There is no `ClientCertificateProfiles` here: the trust question a certificate answers is a second one this surface does
not yet ask.

### Serving the page — `ClientEndpoint:Application`

`ClientEndpoint:Application:Enabled` is the one setting that turns the client on, and what it adds is static files: the
browser head's bundle, answered from the root of the listeners this endpoint already serves. Nothing else changes.
The routes beneath `/api/client` are the same routes, judged by the same credentials and the same grants — the page
holds none of its own, because a browser has to load the application before it can obtain one, and what that
application then calls is authorized exactly as any other caller is. A deployment that leaves this off serves the
routes and no page, which is what every default here does.

`ClientEndpoint:Application:AllowClearText` is the transport half, and it is a declaration rather than an inference,
for the reason [a provider address in the clear](configuration-ai.md#embeddings) needs one: nothing here can tell a
socket
behind a TLS-terminating ingress from one published to a network. So the page is refused at startup when this process
would serve it over a clear-text socket it opens itself, and the refusal names both ways out — terminate TLS here with
`ClientEndpoint:Transport: HttpsOnly` and a `ClientEndpoint:Https:Endpoints` profile, or state that something in front
of this process already did by writing `ClientEndpoint:Application:AllowClearText: true`. A socket that only redirects
to HTTPS needs no permission, because it serves nothing. Permitting clear text is reported at every startup rather
than assumed to still be true.

The routes are served beneath `/api/client`, which is a constant rather than a setting, for the reason `/api/admin` is
one — a client is configured with a host and a port and appends the rest. There is no version segment in it.

## `HealthEndpoints`

The startup, readiness, and liveness probes and the dedicated listener they answer on.
[Health endpoints](health-endpoints.md) records why the surface carries no credential and how each transport behaves.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `HealthEndpoints:Enabled` | bool | `true` | Off maps no probe route and opens no listener | restart |
| `HealthEndpoints:BindAddress` | string | `0.0.0.0` | An IP address; `127.0.0.1` restricts to the machine | restart |
| `HealthEndpoints:Port` | int | `8081` | 1 – 65535. A port another surface binds is permitted and shares that socket — see [sharing a socket](#sharing-a-socket) | restart |
| `HealthEndpoints:HttpsPort` | int | unset | Required by, and only valid with, `HttpAndHttps` | restart |
| `HealthEndpoints:Transport` | enum | `Http` | `Http`, `HttpAndHttps`, `HttpsOnly` | restart |
| `HealthEndpoints:Domain` | string | — | Required by the TLS transports; the name the certificate is proven against | restart |
| `HealthEndpoints:ServerCertificate` | certificate block | unset | Required by the TLS transports; refused otherwise | restart |
