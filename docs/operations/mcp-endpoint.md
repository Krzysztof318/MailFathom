# The MCP endpoint and what protects it

<!-- describes: src/Mcp/**, src/Host/Security/**, src/Infrastructure/Security/**, src/Common/OAuth/**, src/Host/Configuration/Endpoints/TransportClearTextRedirectOptions.cs, src/Host/Configuration/Endpoints/TransportListenerConfiguration.cs, src/Host/Configuration/Endpoints/ExternalListenerConfiguration.cs, src/Host/Configuration/Endpoints/ReverseProxyOptions.cs, src/Host/Hosting/Startup/ClearTextRedirectToHttps.cs, src/Host/Hosting/Warnings/TransportClearTextRedirectReport.cs, src/Host/Hosting/Warnings/ReverseProxyTrustWarning.cs, src/Host/Hosting/Warnings/McpTransportEncryptionWarning.cs -->

The MCP endpoint is how an agent reaches MailFathom. This page records what enabling it means operationally, what a client
has to present to reach it, which browser origins it answers, which client applications it accepts a certificate from,
how much traffic it accepts before it starts refusing, and how it is served over your own domain and certificate. The
tools it serves are described in
`docs/features/mcp-tools.md`.

## The endpoint is off by default

A deployment that configures nothing serves no MCP endpoint. That is a security default rather than a convenience: the
endpoint exposes synchronized mailboxes to whoever can reach it and satisfy whatever it asks for.

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": [
      {
        "ApiKey": {
          "Name": "workstation",
          "SecretReference": "systemd-credential:mailfathom-mcp-workstation-key"
        }
      }
    ]
  }
}
```

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Whether the Streamable HTTP endpoint is mapped at all |
| `Authentication` | empty | The accepted credentials, one entry per credential, each carrying its own method's block |
| `Authentication[].ApiKey` | — | One key a client may present, a named secret with its own lifetime |
| `Authentication[].OAuth.Resource` | — | The canonical public URL this deployment is known by in OAuth terms |
| `Authentication[].OAuth.RequiredScopes` | empty | The scopes an access token from *this entry's* servers must carry |
| `Authentication[].OAuth.AuthorizationServers` | — | The external authorization servers whose tokens this entry accepts |
| `Cors.AllowedOrigins` | `["*"]` | The browser origins served: `*` for every one, a list for exactly those, an empty list for none |
| `ClientCertificateProfiles` | empty | The client applications whose certificates are accepted, each with its own authorities and expected names |
| `RateLimiting` | bounded — see [Rate limiting](#rate-limiting) | How much traffic the endpoint accepts, per process and per client |
| `BindAddress`, `Port`, `Transport` | `0.0.0.0`, `8080`, `Http` | Where the endpoint is served, and under which schemes |
| `Https.Endpoints` | empty | The domains MailFathom terminates TLS for, read under the two `Transport` modes that terminate TLS |

Where the endpoint is served is stated here and nowhere else. `BindAddress` and `Port` bind its clear-text socket, and
the host's own ways of naming a listener — `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`, `Kestrel:Endpoints` — are refused
at startup rather than ignored. That socket is clear text unless something in front of it terminates TLS, which is what
[`Https.Endpoints`](#https-and-your-own-domain) is the alternative to: it moves the endpoint onto
listeners of its own rather than adding TLS to that one.

The endpoint always answers on **`/mcp`**, which is a constant rather than a setting. An MCP client is configured with a
server URL, so a deployment could only move the path in step with every client pointed at it — the configurability would
buy nothing and add one more way for the surface to end up reachable somewhere nobody is looking. Put it behind a reverse
proxy if it has to appear elsewhere.

The transport is always **stateless**, for the same kind of reason. Every MailFathom tool answers one request from the local
mailbox copy and sends nothing back on its own, so a session would carry no state and only give a client something to lose
across a restart. Stateless is also what MCP deployments assume today. Should a tool that pushes notifications arrive, that
is a change to this surface rather than a switch an operator was expected to have found.

The section is bound strictly, so an unrecognized key fails startup instead of being ignored: a misspelled `Enabeld` would
otherwise leave the endpoint off while an operator believed they had switched it on. The whole section is read once while
the host is being composed, because whether an endpoint exists and what guards it are part of the application's routing.
Changing any of it takes effect on restart; it does not participate in configuration reload. The *material* behind a
configured key is a separate matter and is re-read on every request, which is what lets a key rotate without one.

## Authentication

**`Authentication` is a list of credentials.** The two methods identify different kinds of caller — a key belongs to a client
the operator provisioned, a token belongs to a person an external authorization server signed in — so a deployment serving
both carries an entry for each:

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": [
      { "ApiKey": { "Name": "nightly-digest", "SecretReference": "systemd-credential:mailfathom-mcp-digest-key" } },
      { "OAuth": { "Resource": "https://mail.example.test/mcp", "AuthorizationServers": [ { "Name": "workforce", "Issuer": "https://sso.example.test/realms/mailfathom", "AuthorizedSubjects": [ "9f2c7c1e-8a4d-4c62-9f0b-3d2a1b5e7c04" ] } ] } }
    ]
  }
}
```

**An entry states its method by carrying that method's block**, and nothing names the method a second time. That is what
makes a method impossible to select without configuring it, or to configure without selecting it: a key is the entry that
turns keys on. There is no limit on how many entries state either method — a second key is a second entry, and a second
authorization server may be either — and an entry may carry both blocks at once, which is a matter of how you group what
you wrote rather than a distinction the endpoint draws. Exactly one shape of entry fails startup, named by its position:
one carrying neither block. So does a value written where the list belongs, because a value contributes no entries and
would otherwise leave the endpoint served with no credential at all.

A request is served when it satisfies **any one** of the entries. The credentials are told apart by shape: an access
token is a JSON Web Token naming its issuer, an API key is anything else, and each reaches only the check that understands
it. That is also why **an API key must not itself be a token of a configured authorization server**: such a key would be
judged as an access token by that server and never compared as a key, so no client could authenticate with it. Startup
refuses the combination by position — `McpEndpoint:Authentication:0:ApiKey` — rather than letting a deployment start
with a key nothing can ever use. A token-shaped key naming an issuer this deployment does not configure selects no
validator and is compared like any other opaque key, so it is accepted; issue opaque keys and the question does not arise.

**Authentication is turned on, not chosen.** An enabled endpoint whose list is empty is the unauthenticated posture and
starts, because the loopback and reverse-proxy deployment is the ordinary one and making it need extra settings would be
backwards. What keeps that from being silent is the startup warning below rather than a refusal. The near misses are still
startup failures: the section binds strictly, so a misspelled `Authentcation` fails, and so does a block naming no method —
a typo such as `ApiKye` is an unknown key rather than a method nobody configured.

### API keys

A client presents a key as an ordinary HTTP bearer credential:

```http
POST /mcp HTTP/1.1
Authorization: Bearer <the key>
```

Every MCP method and response path is covered — the JSON-RPC post, the stream it reads back, and the delete that ends a
session — and the check runs before any protocol handling. The readiness response is not covered, and neither are the
[health endpoints](health-endpoints.md), which are served on a listener of their own and deliberately carry no
credential at all.

Each key is an ordinary [named secret](secret-provisioning.md#the-secret-block): the key material is provisioned like
every other credential, the `Name` is what a diagnostic and an audit record correlate on, and the `Lifetime` is enforced.
One key per entry, and as many entries as the deployment has clients — which is what makes rotation an overlap rather
than an outage:

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": [
      {
        "ApiKey": {
          "Name": "workstation",
          "SecretReference": "systemd-credential:mailfathom-mcp-workstation-key",
          "Lifetime": "NoLimit"
        }
      },
      {
        "ApiKey": {
          "Name": "chatgpt-connector",
          "SecretReference": "file:/run/secrets/mailfathom-mcp-chatgpt-key",
          "Lifetime": "2027-01-31T00:00:00Z"
        }
      }
    ]
  }
}
```

**Rotating a key.** Add the replacement beside the one it replaces, move the client across, then remove the old entry.
Both authenticate in between, so nothing is refused while the change is in flight. An entry whose `Lifetime` has passed
authenticates nothing but does not fail startup — an expired key left beside its replacement is what a completed rotation
looks like — so it is safe to leave one in place as a record of what was retired. A lifetime is an absolute instant, so a
restart or a configuration reload never revives an expired key.

**Every refusal answers the same way.** A request with no credential, one carrying something that is not a bearer
credential, one presenting a key that matches nothing, and one presenting a key whose lifetime has ended all receive an
empty `401` with the same challenge:

```http
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer realm="MailFathom"
```

Nothing in the response says which key identifiers exist or whether a presented key was merely expired, and the comparison
is written so the time a refusal takes does not say it either: every configured key is read and compared on every request,
whatever the answer, and both sides of the comparison are reduced to a digest and compared in constant time. The server log
is where the difference is — an expired key is recorded by name at `Warning`, and a key whose material has disappeared at
`Error` — and neither line carries the presented credential, the configured material, or the reference target.

**That challenge is what a refused request receives while there is capacity to serve it.** The challenge is written by
authorization, which runs behind the rate limiter, and a request carrying no usable credential is counted against the
shared anonymous partition on the way there. Once that partition or the process-wide concurrency limit is exhausted, the
same request is answered `429 Too Many Requests` with no body and never reaches the point where a challenge is written.
That is deliberate — it is what makes a flood of bad credentials cost the sender something — and it means a client
retrying against an exhausted partition sees `429` where it expected `401`. See [Rate limiting](#rate-limiting).

### OAuth

MailFathom acts as an [OAuth 2.1 protected resource](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization).
An external authorization server the operator already runs signs users in and issues tokens; MailFathom verifies what that
server signed and nothing else. It is **never** an authorization server: it stores no password, issues no token, redeems no
authorization code, holds no refresh token, and has no login page.

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": [
      {
        "OAuth": {
          "Resource": "https://mail.example.test/mcp",
          "RequiredScopes": [ "mailfathom.read" ],
          "AuthorizationServers": [
            {
              "Name": "workforce",
              "Issuer": "https://sso.example.test/realms/mailfathom",
              "AuthorizedSubjects": [ "9f2c7c1e-8a4d-4c62-9f0b-3d2a1b5e7c04" ]
            }
          ]
        }
      }
    ]
  }
}
```

**Several OAuth entries are supported, and each states its own terms.** `RequiredScopes` and `AuthorizationServers` belong
to the entry that carries them, so a token is judged against what *its own* issuer's entry asks for — one tenant may be
required to carry a scope while another is not, without either being weakened to match the other. What every entry must
agree on is `Resource`, and startup refuses two that disagree: the endpoint publishes one protected resource metadata
document, at an address derived from that identifier, so a second resource would leave one entry's clients reading a
document that describes somebody else and asking their authorization server for the wrong audience.

An authorization server's `Name` and `Issuer` are each unique across the whole list, not just within an entry. The name
composes the scheme its token validator registers under, and the issuer is what selects that validator, so a repeat of
either would leave the key set a token is trusted against decided by configuration order.

**`Resource` is the canonical URL clients reach this deployment at**, and it is configuration rather than something derived
from the request. It is published in the metadata document, a client copies it into the `resource` parameter when asking for
a token, and every token's audience is compared against it — which is what stops a token issued for some other service on the
same authorization server from being replayed here. Behind a reverse proxy, write the proxy's public URL: deriving it from
the `Host` or `X-Forwarded-Host` header would tell each client to authenticate for whichever name it arrived under, including
one an attacker chose. That stays true with
[a trusted proxy configured](#behind-a-tls-terminating-reverse-proxy) — a forwarded header makes a request state the
name it arrived under, and leaves the resource identifier a value you wrote.

**`Issuer` is copied verbatim from the authorization server**, trailing slash included where there is one. It is compared
against a token's `iss` by exact string equality and checked against the `issuer` the discovery document reports. Several
widely deployed servers publish an issuer whose whole path is one trailing slash, so a value tidied up by hand is a
configuration that starts cleanly and then refuses every token that server issues.

**`AuthorizedSubjects` names whose tokens are served, and at least one is required.** An authorization server authenticates
whoever its tenant holds, while MailFathom serves one owner's synchronized mail to everyone it admits — so without this list
every colleague who can obtain a token for this resource reads that owner's mail. Write the `sub` the server issues, which
its administration console shows as the user's identifier: a UUID in Keycloak, `auth0|…` in Auth0, the object identifier in
Entra ID. An email address is not it, because a subject is what the server promises not to reuse and an address is
reassigned to whoever holds the mailbox next. The comparison is against the issuer and the subject together, so a subject
one server authorized is not authorized by another server that happens to name someone the same way.

**Nothing about the server's endpoints is configured or guessed.** MailFathom looks for the discovery document where the MCP
authorization specification says to look, taking the first that answers with a document reporting the configured issuer:

| Order | Address, for issuer `https://sso.example.test/realms/mailfathom` |
|---|---|
| 1 | `https://sso.example.test/.well-known/oauth-authorization-server/realms/mailfathom` |
| 2 | `https://sso.example.test/.well-known/openid-configuration/realms/mailfathom` |
| 3 | `https://sso.example.test/realms/mailfathom/.well-known/openid-configuration` |

An issuer with no path drops the third. The key set address comes out of that document, so a server that moves an endpoint
keeps working. `OAuth.AuthorizationServers[n].MetadataAddress` overrides the search for a server publishing its document
somewhere else entirely; it must sit on the issuer's own host and port, so a mistyped one cannot make the host fetch an
address the profile never named.

#### What a token must prove

Every token is checked, before any MCP protocol handling and before any tool runs, for:

- a signature by a key from **that issuer's own key set**, using an asymmetric algorithm from a fixed allow-list
  (`RS256`/`384`/`512`, `PS256`/`384`/`512`, `ES256`/`384`/`512`). `alg: none` and every symmetric algorithm are absent from
  the list, and the algorithm is never taken from the token's own header;
- an `iss` equal to the configured issuer, **exactly**;
- an `aud` equal to `Resource`. A valid signature from a trusted issuer is not by itself a reason to serve a mailbox;
- `exp` and `nbf`, with 60 seconds of clock skew tolerated;
- a `sub` among that profile's `AuthorizedSubjects`;
- every scope in `RequiredScopes`.

A subject and a scope are both required and neither stands in for the other: a scope says what a token was issued for, and
a subject says whose it is. A token from an authorized subject that is missing a scope is answered `403` naming the scopes;
a valid token from anybody else receives a plain `403` that names nothing, because asking its authorization server for more
scopes would change nothing.

**A token is refused outright when the request did not arrive over TLS.** An access token is a reusable credential, so a
request carrying one over plain HTTP has already disclosed it to anything on the path. `Resource` being HTTPS and metadata
retrieval requiring HTTPS protect what this deployment publishes and what it fetches, not the transport a request arrived
on. The refusal is silent — the caller receives the same `401` challenge an unauthenticated request receives. Nothing that
worked before is refused by this: an endpoint reached over plain HTTP cannot complete discovery anyway (see the note under
[Discovery a client uses](#discovery-a-client-uses)). What it closes is a host listening on both an encrypted and a
plaintext endpoint, where discovery succeeds over the first and the token is then presented over the second.

Several `AuthorizationServers` stay isolated from one another. Each has its own key set, reachable only from the scheme whose
issuer published it, so a token claiming one server's issuer is never validated against another's keys — and a token naming an
issuer nobody configured matches no profile at all.

`RequiredScopes` may be left empty, which accepts any token those servers issued for this resource. That is the coarser
boundary rather than a broken one, and it is the right setting where the authorization server already decides who receives a
token for MailFathom. A required scope constrains tokens only: an API key cannot carry one, and its authorization is the
operator's decision to configure it.

**Tokens are never passed on.** A token presented here is used to identify the caller and nothing else. It is not forwarded to
a mail provider, to a downstream service, or to another MCP server.

#### Discovery a client uses

The endpoint publishes [RFC 9728](https://datatracker.ietf.org/doc/html/rfc9728) protected resource metadata at the address
derived from `Resource` — for `https://mail.example.test/mcp`, that is
`https://mail.example.test/.well-known/oauth-protected-resource/mcp`:

```json
{
  "resource": "https://mail.example.test/mcp",
  "authorization_servers": [ "https://sso.example.test/realms/mailfathom" ],
  "scopes_supported": [ "mailfathom.read" ],
  "bearer_methods_supported": [ "header" ],
  "resource_name": "MailFathom"
}
```

A request with no usable credential is answered with a `401` pointing at it:

```http
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer resource_metadata="https://mail.example.test/.well-known/oauth-protected-resource/mcp"
```

A caller who **did** authenticate but whose token lacks a required scope has nothing to gain from authenticating again, so it
receives a `403` naming what would have sufficed:

```http
HTTP/1.1 403 Forbidden
WWW-Authenticate: Bearer error="insufficient_scope", scope="mailfathom.read",
                  resource_metadata="https://mail.example.test/.well-known/oauth-protected-resource/mcp"
```

Neither response says why. An expired token, a wrong audience, an unknown issuer, and an invalid signature are one answer to
the client; the server log is where they differ.

> **The metadata document is served only to a request whose own scheme and host match `Resource`.** That is the MCP SDK's
> check, and it is what stops the document being served under a name the deployment never claimed. Three deployments
> satisfy it: MailFathom terminating TLS itself, a proxy that passes HTTPS through with the `Host` header intact, and a
> TLS-terminating proxy whose forwarded scheme and host MailFathom believes — see
> [behind a TLS-terminating reverse proxy](#behind-a-tls-terminating-reverse-proxy). In the third case the proxy has to
> send both headers and has to fall inside `ReverseProxy:TrustedProxies`; a request that satisfies neither arrives as
> `http` under an internal name, nothing matches, and a `404` on the metadata address with everything else working is
> what an operator sees.

#### What the MCP client does, not MailFathom

The interactive half of OAuth belongs to the MCP client — ChatGPT, Claude Desktop, an IDE — and to the authorization server:

- **authorization-code flow with PKCE (`S256`) and the `resource` parameter** is performed by the client;
- **client registration** is an arrangement between the client and the authorization server. Client ID Metadata Documents,
  Dynamic Client Registration ([RFC 7591](https://datatracker.ietf.org/doc/html/rfc7591)), and preregistering a client by hand
  all work, and MailFathom neither advertises nor constrains the choice. Whether one is available depends on the server: a
  `registration_endpoint` in its metadata means Dynamic Client Registration, `client_id_metadata_document_supported` means the
  first;
- **client secrets, authorization codes, and refresh tokens** never reach MailFathom.

Configuring the authorization server is therefore where an operator does the work: create a client for the MCP client, allow
its redirect URIs, and make the server issue `https://mail.example.test/mcp` as the token's audience. A server that does not
yet implement Resource Indicators ([RFC 8707](https://www.rfc-editor.org/rfc/rfc8707.html)) can still do that through its own
audience mapping — Keycloak, for example, through a client scope carrying an audience mapper. **Do not answer such a server by
relaxing audience validation**; there is no setting for it, deliberately.

### Requiring no credential

An enabled endpoint whose `Authentication` list is empty requires no credential. Writing the empty list says so
explicitly and is exactly equivalent to leaving the setting out:

```json
{
  "McpEndpoint": { "Enabled": true, "Authentication": [] }
}
```

There is nothing to configure alongside it, which is the point of the shape: keys and authorization servers live inside
the entry that turns their method on, so a deployment cannot end up carrying settings nothing checks — believing it is
protected, which is worse than knowing it is not.

Whenever an enabled endpoint requires no credential, startup logs one warning:

```text
warn: MailFathom.Host.Hosting.Warnings.McpTransportAuthenticationWarning
      The MCP endpoint is enabled on /mcp with no authentication method configured, so anything that can reach this
      address can read the synchronized mailboxes. Add an entry to McpEndpoint:Authentication carrying an ApiKey block,
      an OAuth block, or one of each, unless the address is reachable only from this machine or from a network you
      control. Neither an origin policy nor a client certificate substitutes for this: the first restricts which page a
      browser will let call, the second names the application calling, and neither identifies the person whose mail is
      served.
```

Leaving `Cors.AllowedOrigins` at its default of `["*"]` with no credential required — which is what makes the endpoint
work behind a reverse proxy or on a trusted network without further configuration — adds a second warning rather than a
startup failure:

```text
warn: MailFathom.Host.Hosting.Warnings.McpTransportAuthenticationWarning
      Every browser origin is served while no credential is required, so a web page the user never visited can reach this
      endpoint through DNS rebinding and read what it returns. This is the right posture only where the address is
      unreachable from a browser that could be aimed at it, such as an intranet or a reverse proxy that authenticates.
      Replace the '*' in McpEndpoint:Cors:AllowedOrigins with the origins served wherever a browser could be pointed at
      this address.
```

Refusing that combination would make the ordinary intranet and reverse-proxy deployment the one needing extra settings,
so it stays a decision the operator makes with the risk stated. Listing the origins silences the second warning; only
requiring a credential silences the first.

The operational consequences are the ones that always applied to an unauthenticated endpoint:

- **Point it at development mailboxes only.** Do not run it against a mailbox whose contents would matter if read by
  someone else.
- **Restrict who can reach the address at the network layer.** A loopback bind, a firewall rule, a private network, or an
  authenticating reverse proxy are all outside MailFathom and all appropriate.
- **Treat the whole surface as read-only but not harmless.** The tools cannot send, delete, move, or mark mail as read, so
  the exposure is disclosure rather than modification. Disclosure of a mailbox is enough.

### What a credential decides, and what it does not

**The endpoint asks whether this is a caller the deployment serves, and of a token also which person it names.** Beyond
that, every tool call resolves the accounts the configured owner controls and refuses anything outside them, whichever
credential got the caller in. Two admitted callers therefore see the same mailboxes — which is exactly why a token has to
name an authorized subject: admitting a colleague of the same tenant would admit them to the owner's mail rather than to
their own. Per-user permissions are future work; `RequiredScopes` is the seam they will be built on, which is why it
exists before anything varies by it.

A key identifies a *client*, a token identifies a *person*, and the difference matters operationally. A shared bearer
credential has the properties every shared bearer credential has: it does not expire on its own unless you give it a
lifetime, it cannot be revoked for one user without revoking it for the client, and anything that reads it can use it. A
token expires on its own, is revoked where the authorization server says so, and carries the multi-factor and
conditional-access policy that server already enforces.

Of a validated token, MailFathom keeps three things and discards the rest: the issuer, the subject, and the scopes. A name,
an email address, a group, and a tenant claim are dropped at the boundary, so nothing downstream can begin trusting a
claim the operator never mapped. The identity is `iss` together with `sub` rather than `sub` alone, because a subject is
unique only within the server that issued it, and never an email address, which is reassignable.

## CORS and the `Origin` header

Two separate things are configured by one setting, and they are worth telling apart:

- **CORS** tells a browser what it may *read* of a response it already provoked.
- **The `Origin` check** decides whether a request a browser was talked into making is *served at all*. The MCP transport
  specification asks for it because a page the user never visited can otherwise make a browser send requests to an address
  it resolved back to the operator's own host.

Neither is authentication, and neither is why a request is trusted. A non-browser client sends no `Origin` and is served
exactly as before; any client that chooses its own headers can send whichever origin it likes. The check is worth
something against exactly one attacker — a browser, which sets the header itself and does not let a page forge it.

**One setting carries the whole policy.** `McpEndpoint:Cors:AllowedOrigins` has three postures, and each is a
consequence of what the list holds rather than a switch of its own:

| The list | What is served |
|---|---|
| `["*"]`, which is the default | Every browser origin |
| `["https://client.example.test"]` | Exactly the origins listed |
| `[]` | No browser origin at all — only clients that send no `Origin`, which is every non-browser client |

```json
{
  "McpEndpoint": {
    "Cors": {
      "AllowedOrigins": [
        "https://client.example.test",
        "https://console.example.test:8443"
      ]
    }
  }
}
```

Every origin is served by default because the endpoint is protected by the credential a caller presents rather than by
where it was loaded from. Narrowing is a deliberate step, and so is the empty list: a deployment whose only clients are
agents and command-line tools states it and serves no browser anything.

An origin is a scheme, a host, and a port where the port is not the scheme's default — nothing else. A path, a query, a
fragment, or user information means a URL was written where an origin belongs and is refused at startup, as is a value
that is not an origin at all. Entries are normalized to the form a browser sends, so `https://Client.Example.Test:443/`
and `https://client.example.test` are one entry and listing both is refused rather than quietly collapsed.

`*` beside a real origin is refused at startup. It states two policies at once, and guessing would either widen a
deployment an operator narrowed or narrow one they widened. `*` itself is answered before the entries are read as
origins, so it needs no escaping and cannot collide with anything an operator could configure.

An empty list has to be written as one — `"AllowedOrigins": []` in a JSON source, which every file-based provider
supports. Environment variables cannot express an empty list, so a deployment configured entirely through them narrows
by listing origins rather than by emptying the list.

A request whose `Origin` is outside the configured list is answered `403` before any tool runs. Preflight is handled by
the CORS middleware ahead of that check, so a browser's `OPTIONS` never reaches it as a request to refuse, and handling
preflight weakens nothing on the real request that follows.

**Credentials are never enabled**, under either policy. A browser that could attach an ambient cookie to an MCP request
would let a page act as whoever is logged in somewhere else, and the endpoint has no use for one: its credential is a
bearer token a client sets deliberately. Allowed methods and headers are the minimum the Streamable HTTP transport and
bearer authentication need, rather than everything a browser might ask to send.

**What a browser may read includes the authentication challenge.** `WWW-Authenticate` is not a header CORS exposes on its
own, and it is the one that says where to authorize and which scopes are required, so the policy names it alongside the
MCP session and protocol-version headers. The protected resource metadata document is served by the authentication
handler rather than by a mapped route, so the same policy is applied to its path directly; without both, a browser client
can provoke the response that would tell it how to proceed and then not be permitted to read it.

## HTTPS and your own domain

**MailFathom terminates no TLS by default.** With `Https.Endpoints` empty the endpoint is served over whatever listener the
host is already configured with, which is clear-text HTTP unless something in front supplies HTTPS. That default is kept
deliberately, because two ordinary deployments run it:

- **Local development**, where the endpoint is reachable only from the machine it runs on.
- **Behind a TLS-terminating reverse proxy**, where the proxy already holds your certificate and a second TLS layer
  inside the trust boundary protects nothing.

Neither of those is something MailFathom can detect, so the clear-text posture is reported rather than refused. Whenever an
enabled endpoint terminates no TLS, startup logs one warning:

```text
warn: MailFathom.Host.Hosting.Warnings.McpTransportEncryptionWarning
      The MCP endpoint is enabled on /mcp and no HTTPS profile is configured, so it is served over whichever listener
      this host was started with — clear text unless that listener or something in front of this process supplies
      HTTPS. On a clear-text hop anything on the network path can read the API key a client presents and every message
      the tools return, and a client certificate never arrives at all. This is the expected posture for local
      development and for a deployment behind a TLS-terminating reverse proxy; anywhere else, configure
      McpEndpoint:Https:Endpoints so this process presents your domain's certificate itself.
```

It fires whatever authentication mode is configured. An API key travels in a request header, so on a clear-text hop the
credential is as readable as the mail it protects.

Once [a trusted proxy is named](#behind-a-tls-terminating-reverse-proxy), the same warning stops guessing between those
two deployments and describes the one you configured:

```text
warn: MailFathom.Host.Hosting.Warnings.McpTransportEncryptionWarning
      The MCP endpoint is enabled on /mcp behind the 1 trusted reverse proxy source(s) ReverseProxy:TrustedProxies
      names, so the hop this process serves is the one between that proxy and here and TLS to your clients is the
      proxy's to terminate. Keep that hop inside a network you control: on it, anything on the path can read the API
      key a client presents and every message the tools return. A client certificate still never arrives, because the
      handshake ended at the proxy.
```

The clear-text hop is still stated, because it is still there. What changes is that the line no longer suggests
configuring `McpEndpoint:Https:Endpoints`, which is the wrong advice for a deployment whose certificate lives on the
proxy.

### One domain

A profile names the domain clients connect to, the socket to bind, and where the certificate comes from. A PKCS#12 bundle:

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": [
      { "ApiKey": { "Name": "workstation", "SecretReference": "systemd-credential:mailfathom-mcp-workstation-key" } }
    ],
    "Https": {
      "Endpoints": [
        {
          "Name": "public",
          "Domain": "mail.example.com",
          "BindAddress": "0.0.0.0",
          "Port": 8443,
          "ServerCertificate": {
            "Bundle": {
              "Name": "public-bundle",
              "SecretReference": "file:/etc/mailfathom/tls/mail.example.com.pfx",
              "Password": {
                "Name": "public-bundle-password",
                "SecretReference": "systemd-credential:mailfathom-tls-bundle-password"
              }
            }
          }
        }
      ]
    }
  }
}
```

A PEM chain beside its private key, which is what a certificate authority usually delivers:

```json
{
  "ServerCertificate": {
    "CertificateChain": {
      "Name": "public-chain",
      "SecretReference": "file:/etc/mailfathom/tls/fullchain.pem"
    },
    "PrivateKey": {
      "Name": "public-key",
      "SecretReference": "file:/etc/mailfathom/tls/privkey.pem"
    }
  }
}
```

State one or the other. Configuring both is a startup failure, because which of them supplies the identity would
otherwise be decided by nothing you wrote. The `CertificateChain` value is the whole `fullchain.pem`: its first
certificate is the identity and the rest are the intermediates MailFathom presents after it, so a client that does not
already hold the issuing authority can still build a path to a root it trusts. An encrypted private key takes its
password through a nested `Password` block, exactly as a protected bundle does.

Clients are then configured with **`https://mail.example.com:8443/mcp`** — the domain, the port, and the fixed path.

A profile is also what makes [client certificates](#client-certificates) reachable without a reverse proxy: mutual TLS
needs a handshake this process terminated, and configuring one here is how it gets one.

| Setting | Default | Meaning |
|---|---|---|
| `Name` | required | The profile's name, which every diagnostic about it reports |
| `Domain` | required | The exact DNS domain published and selected on |
| `BindAddress` | `0.0.0.0` | The IP address to bind; `::` binds IPv6 |
| `Port` | `8443` | The TCP port to bind |
| `MinimumTlsVersion` | `Tls12` | `Tls12` or `Tls13` |
| `HttpProtocols` | absent — HTTP/1.1 and HTTP/2 | Any of `Http1`, `Http2`, `Http3` |
| `ServerCertificate` | required | `Bundle`, or `CertificateChain` beside `PrivateKey` |

### Configuring a profile takes over the host's listeners

Under `Transport: HttpsOnly` **no clear-text listener stays open behind an HTTPS profile**. There is no mixed posture
arrived at by accident in which the MCP route is protected while a second listener offers the same mailbox without
protection.

`Transport: HttpAndHttps` is the deliberate exception, and the clear-text socket it keeps redirects to the profiles
unless you turn the redirect off. `ASPNETCORE_URLS`, `--urls`, and any Aspire-issued HTTP endpoint are refused at
startup whichever mode you choose, because where this endpoint is served is this section's question alone.

The [health endpoints](health-endpoints.md) are the one thing this does not take over. They are served on a listener of
their own, and that listener keeps its own transport: a deployment terminating TLS for the MCP endpoint still serves
plain HTTP probes unless it configures `HealthEndpoints:Transport` as well. Nothing is lost by that, because the probe
listener serves no mailbox — a request for `/mcp` that arrives on it is answered with `404`.

Kestrel's own `Kestrel:Endpoints` section is the one listener a profile cannot displace: those endpoints are bound
alongside the ones bound in code rather than replaced by them, so an endpoint configured there would keep its socket and
serve the same MCP route without the TLS a profile adds. Configuring both is therefore a startup failure that names
each side, because only an operator can decide which one the deployment meant:

```text
Kestrel:Endpoints:Http — a Kestrel endpoint is configured beside McpEndpoint:Https, and Kestrel binds both: this
listener would stay open alongside the HTTPS profiles and serve the same MCP endpoint without the TLS they were
configured to add. Remove the endpoint, or remove the HTTPS profiles and let this listener serve the endpoint.
```

### Redirecting a client still pointed at `http://`

A client that has not been repointed after you configured a profile meets a refused connection or an unreadable handshake
error, which is indistinguishable from an outage. So a surface that terminates TLS also binds one clear-text listener
whose only answer is a redirect, on **port 8080** unless you state another:

```console
$ curl -i http://mail.example.com:8080/mcp
HTTP/1.1 308 Permanent Redirect
Location: https://mail.example.com:8443/mcp
```

**A redirect protects the next request, never the one that arrived.** An API key sent in clear text was on the wire before
anything answered, and no redirect recovers it. Treat this as a way to find out that a client needs repointing, not as a
supported way to reach the endpoint — and repoint the client rather than leaving it following a redirect.

What that listener serves is the redirect and nothing else. It maps no route: `/mcp`, the protected-resource metadata
document, and an unmapped path are all answered the same way, and no authentication, rate-limiting, CORS, or
client-certificate handler runs for a request that arrived on it. There is nothing reachable over it to protect.

The [health endpoints](health-endpoints.md) are untouched by this. They keep their own listener and their own
`HealthEndpoints:Transport`, and no probe is ever asked on this port — which is deliberate, because a probe follows no
redirect and would read a `308` as a failure.

**If you also run a proxy, point it at a profile's port and never at this one.** The two postures rarely meet — a
deployment [behind a TLS-terminating proxy](#behind-a-tls-terminating-reverse-proxy) normally configures no profile here,
and then `8080` serves the routes rather than redirecting away from them. Where a deployment does both, a proxy
forwarding to the redirect port would have every request answered with a `308` to the profile it should have been sent to
in the first place. With [a trusted proxy named](#behind-a-tls-terminating-reverse-proxy) the redirect uses the forwarded
public host, so the `Location` carries the name your clients use rather than the internal one the proxy dialled.

Four properties are worth knowing:

- **`308`, not `301` or `302`.** The MCP transport is a `POST`; the older codes permit a client to re-send it as a `GET`,
  which would arrive over TLS as a request nobody made. The path and query are preserved.
- **The host is redirected to itself.** With [several domains on one address](#several-domains-on-one-address), each
  redirects to its own profile's port, so no client is sent to a name it did not ask for under a certificate issued for a
  different one.
- **A `Host` header naming no configured domain gets `400`.** It is refused rather than rewritten to a domain the
  deployment does publish.
- **`:443` is left out of the `Location`**, because it is the scheme's own port and a client appends nothing for it.

Turn it off with one setting, which is what a deployment behind a proxy that already answers the clear-text port wants:

```json
{
  "McpEndpoint": {
    "Https": {
      "Redirect": { "Enabled": false }
    }
  }
}
```

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Whether the clear-text listener is bound at all |
| `BindAddress` | `0.0.0.0` | The IP address to bind; `::` binds IPv6 |
| `Port` | `8080` | The TCP port to bind |

Startup reports the port beside the domains it redirects to, so every socket the process opened is readable from the log:

```text
info: MailFathom.Host.Hosting.Warnings.TransportClearTextRedirectReport
      The MCP endpoint redirects clear-text requests on port 8080 to https://mail.example.com:8443. That listener maps
      no route and answers every path with a 308, so nothing is reachable over it. A redirect protects the next request
      and not the one that arrived — a credential already sent in clear text is on the wire — so repoint your clients
      rather than relying on it. Set McpEndpoint:Https:Redirect:Enabled to false to bind no clear-text port at all.
```

Two configurations are refused before anything binds. Writing a `Redirect` section for a surface that terminates no TLS
fails startup rather than being ignored, because that surface is already served in clear text and nothing would have bound
the port:

```text
McpEndpoint:Https:Redirect — a clear-text redirect is configured while McpEndpoint:Https:Endpoints names no HTTPS
profile, so there is nothing to redirect to and this surface is already served in clear text. Configure a profile, or
remove this section.
```

And the redirect socket collides with nothing: a conflict with one of this section's own profiles, with the
[administrative endpoint](admin-endpoint.md), with the [health listener](health-endpoints.md), or with a
`Kestrel:Endpoints` entry is reported against the section that asked for it rather than as an address-in-use failure
naming a socket.

Within this section the check is on the socket rather than the port number, the same way it is
[between two profiles](#several-domains-on-one-address). A profile on `10.0.0.5:9000` and a redirect on `10.0.0.6:9000`
are two sockets the operating system grants independently, so that deployment is served; a redirect on `0.0.0.0:9000` or
`::9000` beside that profile is refused, because the wildcard already accepts the connections the profile's address would
receive and only one of the two could bind.

### Several domains on one address

Profiles that name the same `BindAddress` and `Port` share one listener and are told apart by the server name the client
sends during the TLS handshake:

```json
{
  "Https": {
    "Endpoints": [
      {
        "Name": "public",
        "Domain": "mail.example.com",
        "Port": 443,
        "ServerCertificate": { "Bundle": { "Name": "public-bundle", "SecretReference": "file:/etc/mailfathom/tls/public.pfx" } }
      },
      {
        "Name": "connector",
        "Domain": "connector.example.com",
        "Port": 443,
        "MinimumTlsVersion": "Tls13",
        "ServerCertificate": { "Bundle": { "Name": "connector-bundle", "SecretReference": "file:/etc/mailfathom/tls/connector.pfx" } }
      }
    ]
  }
}
```

**A handshake naming something else is refused.** There is no default profile and no catch-all: a client that asks for an
unconfigured name, or that sends no server name at all, gets its connection ended rather than an unrelated domain's
certificate. Wildcard names are not accepted in `Domain` either — each profile publishes one exact name. A *certificate*
whose subject alternative name is a wildcard is fine and covers one label, as clients read it.

Two rules follow from where each setting takes effect during the handshake:

- **`MinimumTlsVersion` may differ per profile**, because the floor is applied once the server name is known.
- **`HttpProtocols` may not**, because ALPN offers what the listener was bound with and HTTP/3 is a second socket the
  listener either opens or does not — both settled before any server name is known. Profiles sharing an address and port
  must name the same versions, and startup refuses them if they do not.

Sharing a port means naming the *same* address. A wildcard address beside a specific one on a single port — `0.0.0.0`
beside `127.0.0.1`, or the dual-mode `::` beside any IPv4 address — asks for two sockets the operating system grants one
of, because the wildcard already accepts the connections the second listener was bound for. Startup reports that as the
profile configuration it is rather than letting Kestrel fail on an address-in-use error that names a socket:

```text
McpEndpoint:Https:Endpoints — profiles on port 8443 bind 0.0.0.0 as well as 127.0.0.1; 0.0.0.0 already accepts the
connections those addresses would receive, so only one of the two listeners could bind. State one address for a port,
or move a profile to a port of its own.
```

### TLS versions and HTTP versions

`MinimumTlsVersion` is a floor, not a selection. `Tls12` is the default and still negotiates TLS 1.3 with a client that
offers it; `Tls13` refuses everything older. TLS 1.0, TLS 1.1, and SSL are not reachable through this setting at all —
they are deprecated by RFC 8996, and a setting able to express them would be a way to weaken the endpoint rather than to
configure it.

`HttpProtocols` defaults to HTTP/1.1 and HTTP/2, which is what every MCP client speaks and what ALPN negotiates on the
TLS connection. HTTP/3 is opt-in, runs over QUIC on UDP rather than on the TLS connection, and needs the host platform to
provide QUIC. Selecting it where the platform cannot is a startup failure rather than a quiet fall back to HTTP/2:

```text
McpEndpoint:Https:Endpoints:0:HttpProtocols — 'Http3' is configured and this host cannot provide the QUIC transport it
needs; install the platform's QUIC support or remove the version rather than have it quietly fall back.
```

Selecting HTTP/3 does not change the TLS floor for the other versions. QUIC always uses TLS 1.3 of its own accord.

### What startup proves before a listener opens

Certificates are loaded and checked **before the server starts**, so a profile that cannot serve is a host that does not
start rather than a listener that fails every handshake. Each profile's material has to satisfy all of:

- the reference resolves, and the material parses in the encoding its setting is for;
- the leaf carries a private key, and that key belongs to that leaf;
- the certificate is inside its validity period now;
- a subject alternative name covers the configured `Domain` exactly, or by a single wildcard label;
- the extended key usage permits server authentication;
- the key usage, where the certificate declares one, permits `digitalSignature`;
- the chain material states one leaf, not several;
- every certificate supplied after the leaf is a certificate authority, is inside its own validity period, and issues
  either the leaf or another supplied certificate.

The key usage is checked because a server authenticates itself by signing the handshake transcript under TLS 1.3 and
under every key exchange TLS 1.2 still negotiates. A certificate limited to `keyEncipherment` would load, open a
listener, and then fail every handshake reaching it. An absent key-usage extension leaves the key unconstrained and is
accepted, which is what a private authority commonly issues.

The certificates after the leaf are checked for what is provable without a trust anchor — the root lives in the client's
store, not in the deployment's material — so their signatures are not verified. What is proved is that each one can take
part in the path a client builds: an end-entity certificate pasted into a chain file issues nothing, an expired
authority breaks the path whatever the leaf says, and an authority that issues nothing in the chain means the wrong
material was provisioned. Order is not something the material has to state. Neither source carries a reliable one — a
PKCS#12 bundle states none at all — so the sequence presented to clients is rebuilt from the issuer each certificate
names, leading from the leaf outwards.

The subject common name is never consulted. Every current client ignores it, so a certificate accepted on the strength of
one would still be refused by everything that connects.

A failure names the profile and the reason, and nothing else:

```text
McpEndpoint:Https:Endpoints:0 — the HTTPS profile 'public' has no usable server certificate
[DomainNotCoveredBySubjectAlternativeName].
```

Every profile is checked before the host gives up, so two misconfigured endpoints are reported in one message rather than
one restart at a time. If any profile fails, none is served.

### Secrets, provisioning, and rotation

Certificate material is named through the same secret blocks everything else uses, so each part carries a required `Name`
and a `Lifetime`, and each is provisioned by reference rather than written into configuration. `docs/operations/secret-provisioning.md`
covers the schemes; a private key or a bundle password written directly into a configuration value fails startup under
the default `ReferenceOnly` interpretation.

A private key is imported into memory only. MailFathom never writes one to an operating-system key store as a side effect of
loading it. A certificate chain is public material and may be supplied inline under an inline interpretation mode; a
PKCS#12 bundle may not, because it is binary and has no faithful representation in a configuration value.

Startup records what each profile presents and when it stops working:

```text
info: MailFathom.Host.Security.Transport.TransportServerCertificateStore
      The MCP HTTPS profile public presents a server certificate valid until 2027-01-31 00:00:00Z.
```

Within thirty days of expiry the same line is a warning instead. Neither carries the certificate's subject, serial
number, or thumbprint: an operator renewing it needs to know which profile and by when, not which certificate it was.

**Replacing certificate material takes a restart.** The profiles are read once while the host is composed, like the rest
of this section, and the loaded certificates are held for the process lifetime. Renew the material, then restart; startup
validates the new certificate before anything listens, so a bad renewal is a host that refuses to start rather than an
endpoint that has stopped working. Rotating certificates without a restart is tracked separately.

### What stays yours

Provisioning the DNS record, proving ownership of the domain, and obtaining and renewing the certificate are all outside
MailFathom. It has no ACME client and issues nothing. Startup only refuses a `Domain` that could not be a DNS name at all —
an IP address, a wildcard, a name with characters a DNS name cannot carry, or a name a second profile already publishes.
An internationalized domain is configured in its punycode A-label form, because that is what a client sends and what a
certificate's names carry.

## Behind a TLS-terminating reverse proxy

When nginx, Traefik, or an ingress controller holds your certificate, the request that reaches MailFathom arrives as
`http` under whichever internal name the proxy dialled. Your deployment's public identity survives the hop only in
`X-Forwarded-Proto` and `X-Forwarded-Host`. MailFathom always reads both; the one thing you configure is which peers it
believes them from:

```json
{
  "ReverseProxy": {
    "TrustedProxies": [ "10.4.0.0/16" ]
  }
}
```

| Setting | Default | Meaning |
|---|---|---|
| `TrustedProxies` | empty, which trusts every peer | The proxy addresses or CIDR networks a forwarded scheme and host are accepted from |
| `MaximumForwardedHops` | `1` | How many proxies may have appended a value to either header |

The forwarded scheme and host are applied to the request before anything reads it, so OAuth discovery, the `401`
challenge, and every absolute address MailFathom writes carry your public name — see
[discovery a client uses](#discovery-a-client-uses).

**It is one setting for the whole process, not one per endpoint.** The MCP, administrative, and probe surfaces are
separate listeners over one request pipeline, and this runs at the front of that pipeline. A trusted proxy is therefore
trusted on every listener it can reach, which is what a network fact should be: you state where your proxy is once
rather than restating it beside each surface.

**Trust is the connection, never the header.** A forwarded value is worth exactly what the peer that sent it is worth,
so once you have named a proxy, a request from any address outside `TrustedProxies` is served under the scheme and host
it actually arrived with, and its `X-Forwarded-*` headers change nothing.

The framework's own default — trusting loopback — is cleared rather than inherited, because loopback is the wrong peer
in a container and every other process on the machine in a native installation. What you name replaces the default
trust rather than adding to it. `10.0.0.5/24` is refused, naming the `10.0.0.0/24` it would otherwise have silently
become; write the address or write the network, and the deployment gets what it asked for.

### What an unconfigured section costs

> **Name your proxies.** With `TrustedProxies` empty, MailFathom trusts `0.0.0.0/0` and `::/0` — every peer that can
> open a connection. **An OAuth access token is refused outright when the request did not arrive over TLS, and that
> check reads the scheme after a forwarded header has been applied.** With a real proxy named that is correct: the hop
> to the client genuinely was HTTPS. With every peer trusted, anything that can reach the listener sends
> `X-Forwarded-Proto: https` and the refusal stops working, so a reusable credential crosses a clear-text hop and is
> accepted. `X-Forwarded-Host` is believed on the same terms, so the name your `401` challenge and every absolute
> address carry is then a client's to choose. Name a range that covers your proxies and nothing else.

Writing `0.0.0.0/0` and `::/0` out is the same posture stated deliberately, and it is a posture an operator can mean: a
load balancer pool with no stable address, or a network already closed by something other than this setting.

Either way it is announced. Every startup that runs on it logs one line naming what the deployment gave up. A section
that named no proxy reads:

```text
warn: MailFathom.Host.Hosting.Warnings.ReverseProxyTrustWarning
      ReverseProxy:TrustedProxies names no proxy, so this process trusts 0.0.0.0/0, ::/0 — a forwarded scheme and host
      are read from any peer that can open a connection. This also turns off the refusal of an access token that
      arrived without transport encryption, because that refusal reads the scheme a forwarded header set, so a client
      can claim its own hop was encrypted and have the token accepted over clear text. Name the addresses or CIDR
      networks your proxies actually use, for example '10.0.0.5' or '10.0.0.0/24', to read a forwarded header from them
      alone; write the ranges above explicitly if trusting every peer is what this deployment means.
```

and one that wrote the ranges out reads:

```text
warn: MailFathom.Host.Hosting.Warnings.ReverseProxyTrustWarning
      ReverseProxy:TrustedProxies names 0.0.0.0/0, which covers every address, so a forwarded scheme and host are read
      from any peer that can open a connection rather than from a proxy. This also turns off the refusal of an access
      token that arrived without transport encryption, because that refusal reads the scheme a forwarded header set —
      so a client can claim its own hop was encrypted and have the token accepted over clear text. Narrow the range to
      the addresses your proxies actually use unless something other than this setting already closes the network.
```

Both are said once, while the host starts, and never per request. A merely wide range — a private `/8` — produces
nothing at all. How wide is too wide inside a network you own is a judgement only you can make, and a warning that
fired on it would be a line you learn to scroll past before it ever mattered.

**Only the two headers are read.** `X-Forwarded-For` is not, so the peer MailFathom observes stays the one that opened
the connection. Nothing here partitions, limits, or logs by client address, so adopting one from a header would replace
an observed fact with a claim and buy nothing. Each header is read right to left, and `MaximumForwardedHops` says how
far back the chain is believed — raise it only to the number of proxies a request genuinely passes through. A value
that parses as no scheme or no host is discarded and the request keeps what it arrived with for that component; nothing
is half-read out of it.

A worked nginx location, with `Host` preserved as well so the deployment works whichever of the two MailFathom ends up
reading:

```nginx
location /mcp {
    proxy_pass         http://10.4.2.11:8080;
    proxy_http_version 1.1;
    proxy_set_header   Host              $host;
    proxy_set_header   X-Forwarded-Proto $scheme;
    proxy_set_header   X-Forwarded-Host  $host;
    proxy_buffering    off;
}
```

`proxy_buffering off` is what lets the Streamable HTTP response stream rather than arrive at the end.

### What the proxy owns in this mode

- **TLS and the certificate.** `McpEndpoint:Https:Endpoints` stays empty, because a second TLS layer inside the trust
  boundary protects nothing. `Domain` on an HTTPS profile selects the certificate *MailFathom* presents, so a
  deployment in this mode configures none.
- **Redirecting a clear-text client to HTTPS**, which belongs to whichever side faces the client rather than to both.

### What MailFathom keeps owning

- **Authentication.** A proxy that authenticates its own callers is not this endpoint's authentication. `Authentication`
  still decides who reads the owner's mail, and `OAuth.AuthorizationServers[n].AuthorizedSubjects` still decides whose
  tokens are served.
- **`OAuth.Resource`.** It stays required and stays the identifier a token's audience is compared against. Nothing an
  upstream writes reaches it, which is the property that keeps a client from ever being told to authorize for a name an
  attacker chose.
- **CORS.** The response headers a browser reads are written here, so `Cors.AllowedOrigins` is still what decides them.
- **Rate limiting**, which keeps reading no address at all — neither forwarded nor remote. See
  [whose capacity a request spends](#whose-capacity-a-request-spends).

### What becomes unreachable

**Client certificates.** The TLS handshake ended at the proxy, so no certificate reaches this process, and no header is
read as a substitute. `McpEndpoint:ClientCertificateProfiles` is therefore not a posture a TLS-terminating proxy can be
combined with — the next section says the same thing from the other side.

## Client certificates

Mutual TLS is off unless `McpEndpoint:ClientCertificateProfiles` names at least one client. A profile identifies a client
*application* — the ChatGPT connector, a reporting service, a workstation fleet — and it composes with the authentication
methods rather than replacing any of them. A certificate says which program is calling; it never says on whose behalf.

**A client certificate only arrives over a TLS connection this process terminated.** That means an HTTPS profile from
the section above, under a `Transport` that terminates TLS; configuring profiles while `Transport` is `Http` is refused
at startup, because no certificate could ever be presented to judge against them. Where TLS is terminated by a
reverse proxy in front, the handshake happened somewhere else and no certificate reaches here; the headers a proxy sets
to describe what it saw are ignored, deliberately and permanently. A `Required` profile in that deployment refuses every
request, which is the honest outcome rather than a silent one.

Asking for the certificate is a decision taken while the connection is being established, so it follows whichever
listener shape the deployment has. Configuring an HTTPS profile takes over the host's listeners entirely, and those
listeners supply their own TLS settings rather than reading Kestrel's HTTPS defaults — so the request for a client
certificate is made by the profile's own handshake. Nothing about this is configured twice: one fact, that at least one
trust profile exists, reaches whichever listener is actually serving.

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": [
      { "ApiKey": { "Name": "workstation", "SecretReference": "systemd-credential:mailfathom-mcp-workstation-key" } }
    ],
    "ClientCertificateProfiles": [
      {
        "Name": "chatgpt-connector",
        "Requirement": "Optional",
        "TrustAnchors": [
          {
            "Name": "openai-connectors-ca",
            "SecretReference": "file:/etc/mailfathom/openai-connectors-ca.pem"
          }
        ],
        "SubjectAlternativeNames": ["mtls.prod.connectors.openai.com"]
      }
    ]
  }
}
```

| Setting | Meaning |
|---|---|
| `Name` | The operator-chosen name of the client, which is what a refusal in the log is read by. Required and unique |
| `Requirement` | `Required` refuses a request that presents no certificate at all; `Optional` serves it and identifies no client. Required to state |
| `TrustAnchors` | The certificate authorities a presented certificate must chain to. Ordinary [named secrets](secret-provisioning.md#the-secret-block), several of them so an authority can rotate by overlap |
| `SubjectAlternativeNames` | The DNS names of which a presented certificate must carry at least one. Required, because an authority alone accepts every certificate it has ever issued |

**A certificate has to pass all four checks**, against one profile: it carries an extended key usage naming client
authentication, it carries one of that profile's expected DNS names as a subject alternative name, it is inside its
validity period, and it chains to one of that profile's anchors. Profiles are tried in configuration order and the first
one that accepts ends the walk, so a deployment can serve several clients whose authorities have nothing to do with each
other.

**Every refusal is an empty `403`.** Which profile objected, and why, is in the server log — recorded by the
certificate's thumbprint, which is public material — and never in the response, because what a client could act on is
what to present next.

Several deliberate strictnesses are worth knowing before a profile is written:

- **The certificate comes from the TLS connection.** No header is read, however a proxy in front of MailFathom spells one.
  Terminating TLS elsewhere and forwarding what it saw is a design with its own trust boundary and is not this one.
- **A certificate carrying no extended key usage is refused**, even though X.509 reads absence as every usage. A profile
  that names client authentication asked for a certificate that says so, and the same authority commonly issues server
  certificates too.
- **The subject common name is never consulted** — only subject alternative names, which is what a certificate authority
  actually attests to.
- **Only the leaf is validated against the anchors.** The server does not see the chain a client sent, so a client whose
  certificate chains through an intermediate needs that intermediate listed as a trust anchor beside its root.
- **No revocation is checked.** The authorities behind a client profile are commonly private and publish neither a
  revocation list nor a responder, so withdrawing a client means removing its profile or its anchor — which takes effect
  on the next request, with no restart.
- **Requesting a certificate is not requiring one.** Kestrel asks every HTTPS connection for one and accepts whatever
  arrives, so the decision is made here rather than in the handshake, where a refusal would say nothing to anybody.
- **A matched profile also names a rate-limit partition**, but only where no API key identified the caller. See
  [Whose capacity a request spends](#whose-capacity-a-request-spends) for the rule between the two.

**mTLS needs an HTTPS endpoint.** A client certificate only exists on a TLS connection, so a deployment serving plain
HTTP presents none: an `Optional` profile then identifies nothing, and a `Required` profile refuses every request. Give
the endpoint a handshake of its own by configuring an [HTTPS profile](#https-and-your-own-domain), or terminate TLS in
front of MailFathom only if you are not using these profiles, because a proxy that terminates TLS is exactly what stops
the certificate from arriving.

**This is verified over a real handshake, not only in unit tests.** The integration suite serves a whole MailFathom over
an HTTPS profile with a `Required` client-certificate profile, against certificates it issues per run, and connects to
it as a client. It establishes that the handshake asks for a certificate; that a client presenting one the profile
accepts reaches the protocol surface; that a wrong authority, a wrong subject alternative name, and a certificate
limited to server authentication are each refused with `403` over a connection that was established rather than
dropped; and that a client presenting none is refused the same way rather than meeting a handshake error. The same
requests carry the accepted certificate in the four headers a reverse proxy sets, and the verdict follows the connection
in every case, which is the wire-level form of the rule above that no header is read.

### The ChatGPT connector profile

OpenAI publishes a managed client certificate for its MCP connector, and the profile above is the shape it asks for: the
leaf chains to the published OpenAI Connectors mTLS certificate authority, is valid for client authentication, and
carries the subject alternative name `mtls.prod.connectors.openai.com`. Nothing pins the leaf, because that certificate
rotates and a pinned fingerprint would turn a routine rotation into an outage.

The authority itself is **supplied by you**, as an ordinary secret reference. No third-party certificate ships in this
repository, which is what keeps OpenAI rotating their authority an operator change rather than a MailFathom release. Fetch
the current certificate from OpenAI's published location, provision it like any other trust anchor, and add the
successor beside it while a rotation is in flight.

`Requirement` is `Optional` above on purpose. A `Required` profile refuses every request that presents no certificate,
which includes the workstation reaching the same endpoint with an API key alone; state `Required` only once every client
of the deployment holds a certificate.

**Compose the profile with OAuth rather than relying on it alone.** A certificate says which application is connecting and
never whose mailbox is being read, so the connector's own OAuth 2.1 flow answers the second question — write
an `OAuth` entry beside this profile, configure the authorization server as [OAuth](#oauth) describes, and
name the owner in its `AuthorizedSubjects`. The two run independently: the certificate is judged before any credential is
read, and a request satisfying one and not the other is refused. What remains outside MailFathom is the connector-side
arrangement — registering the client with your authorization server and having it issue `Resource` as the audience — which
is the client's half of the flow and the same for every MCP client.

### Rotating an authority

Add the successor to the same `TrustAnchors` list, restart, let clients move onto certificates the new authority signed,
then remove the predecessor and restart. Both are accepted in between, so nothing is refused while the change is in
flight. Restarts are needed because the profile *list* is read once during composition; the material behind an anchor is
re-read on every request, so replacing a certificate file in place needs no restart at all.

An anchor that stops loading — a deleted file, a corrupted certificate — is recorded at `Error` and skipped, so the
other anchors of that profile keep working. A profile whose anchors all fail to load refuses every certificate rather
than accepting one, because an anchor that has become unreadable must never widen what a profile trusts.

That refusal is the *profile's*, not the endpoint's. Another profile still accepts a certificate its own anchors verify,
because the broken material took no part in that verdict — one deleted file closes the clients it belongs to, not the
ones whose trust material is intact.

## Rate limiting

An enabled endpoint is bounded. Two controls run in front of it, and they bound different things:

- **A process-wide concurrency limit** caps how many MCP requests are being served at any instant, across every client.
  It protects what the machine has one set of: database connections, threads, and open response streams.
- **A per-client token bucket** caps how often one client may ask. A client that goes into a loop spends its own capacity
  rather than everyone's.

Unlike the settings above, **every value here has a product default**, so an endpoint someone enabled is bounded by the
act of enabling it. There is no correct default for who may read a mailbox, which is why `Authentication` refuses to be
guessed; there is a sane default for how fast one client may ask, and leaving the endpoint unbounded because nobody wrote
a number is not a decision anyone made.

**The administrative endpoint carries the same section.** `AdminEndpoint:RateLimiting` takes the same keys, the same
defaults, and the same validation, and the two are configured independently: neither endpoint's traffic reaches the
other's limits, and neither endpoint's numbers say anything about the other's.
[Administering a deployment](admin-endpoint.md#rate-limiting) records the one behavioural difference between them,
which follows from that endpoint judging its credential behind the limiter rather than in front of it.

```json
{
  "McpEndpoint": {
    "RateLimiting": {
      "Enabled": true,
      "MaxConcurrentRequests": 20,
      "ConcurrencyQueueLimit": 0,
      "TokenCapacity": 60,
      "TokensPerReplenishmentPeriod": 60,
      "ReplenishmentPeriod": "00:01:00",
      "RequestQueueLimit": 0
    }
  }
}
```

| Setting | Default | Range | Meaning |
|---|---|---|---|
| `Enabled` | `true` | — | Whether the limits below are applied at all |
| `MaxConcurrentRequests` | `20` | 1–1000 | MCP requests served at once, across every client |
| `ConcurrencyQueueLimit` | `0` | 0–1000 | Requests that wait for a concurrency slot before the rest are refused |
| `TokenCapacity` | `60` | 1–1000000 | The largest burst one client may spend at once |
| `TokensPerReplenishmentPeriod` | `60` | 1–`TokenCapacity` | How much of that burst one client gets back each period |
| `ReplenishmentPeriod` | `00:01:00` | 1s–1h | How often a client's spent capacity is restored |
| `RequestQueueLimit` | `0` | 0 to `MaxConcurrentRequests` − 1 | One client's requests that wait for capacity before the rest are refused |

The defaults are sized for the work an MCP request actually does here. Every tool answers from the local mailbox copy
with a bounded query, so a request is short and database-bound rather than long and compute-bound. Twenty concurrent
requests keep the endpoint well inside the connection pool the synchronization workers share; one request per second with
a sixty-request burst covers an agent that lists a page and then reads what it found, while still costing an unattended
loop its capacity within a second.

Whatever is in force is stated once at startup, so a deployment running on defaults can read back what it is enforcing
rather than having to know these numbers:

```text
info: MailFathom.Host.Hosting.Warnings.TransportRateLimitingStartupReport
      The MCP endpoint on /mcp serves at most 20 requests at once across every caller, queueing 0 beyond that, and
      allows each caller a burst of 60 requests restored at 60 every 00:01:00, queueing 0 of its requests beyond that.
```

One line per enabled endpoint. A deployment serving both reads a second line naming the administrative endpoint and its
own numbers, so a section somebody mistyped is visible at startup rather than the first time a caller is refused.

**Queue limits default to zero throughout.** A queued request holds memory and a connection while it waits for capacity
that is already gone, which turns an overload into a slower, larger overload; refusing it immediately tells the client to
back off while the server is still healthy. A deployment that would rather absorb a short burst can configure a queue,
and a bounded queue is the only shape available.

**A client queue is bounded by the concurrency limit as well as by its own range.** The two limiters are acquired in
order — the process-wide one first, the client's bucket second — so a request waiting for its client's tokens is already
holding a concurrency permit and keeps it until the next replenishment, which can be an hour away. A queue as large as
`MaxConcurrentRequests` would therefore let one client that has run out of capacity park every permit the process has
and refuse everyone else, through the very limit that exists to keep one client's behaviour to itself. `RequestQueueLimit`
has to stay below `MaxConcurrentRequests`, and `0` — refuse immediately, and tell the client when to come back — remains
the setting to prefer.

Configuration that could never work is refused at startup rather than applied: a limit of zero or below, an unbounded
queue, a replenishment period below what the timer can resolve, a period that restores more than the bucket holds —
which is not a faster limit but a different one, because the surplus is discarded every time and the rate written down is
never the rate that applies — and a client queue that could hold every concurrency permit.

### Whose capacity a request spends

Two identities can name a caller, and they are consulted in a fixed order:

1. **The name of the API key the request authenticated with**, whenever there is one.
2. **The name of the client-certificate profile the connection matched**, when no key authenticated the request.
3. **One shared anonymous partition** otherwise.

Both are MailFathom's own configured identities — never the credential, and never anything the certificate itself carried.
The partitions a deployment keeps therefore number no more than its key list plus its profile list.

**The key wins wherever both exist, and the two are never combined.** A key names one client of this deployment, which is
exactly what an operator partitioned their clients into; a profile names a client *application*, and several keys may sit
behind one profile. Taking the profile instead would let one key starve another that happens to share its certificate,
and combining the two into a pair would hand the same credential a fresh bucket for every profile it could present
under — capacity bought by holding one more certificate.

A profile's partition is written `<profile:name>` and a key's is written bare, because the two are configured under the
same grammar and a profile named after a key would otherwise share its bucket. Under `ApiKey` both kinds exist at once,
since a request whose credential was refused still arrives carrying the profile its certificate matched.

Everything unidentified shares **one anonymous partition**: a request under `None` presenting no certificate, and a
request whose credential was refused. That is deliberately coarse. Every partition a request can name is a dictionary
entry the process keeps, so a key an attacker chooses is memory an attacker allocates. Nothing a caller writes — a
header, a path, a query string, an `Origin`, a user agent — reaches a partition key, and neither does a forwarded
address, because a proxy header is chosen by whoever is upstream. The remote address is not used either, not even as a
fallback: it is spoofable on the traffic this is aimed at, and one client behind a shared address would otherwise be
limited by another's behaviour.

Counting refused credentials against the anonymous partition is what makes a flood of bad credentials cost the sender
something. The limiter runs **behind the certificate check and behind authentication**, so both identities are settled
before it counts, and **ahead of authorization**, so a request about to be refused for its credential has still spent
capacity rather than being the one kind of traffic served without limit.

**Every partition is keyed by the surface it belongs to**, including the anonymous one. The two endpoints' key lists are
configured separately and neither consults the other's, so one name spelled under both is two independent buckets rather
than one shared between them — and the burst an agent spends reaching a mailbox is never the burst an operator needs to
administer the service.

Readiness, liveness, and the root endpoint are outside all of this and keep answering while either endpoint is refusing.

### What a refused request receives

A request over either limit is answered `429 Too Many Requests` with `Cache-Control: no-store` and **no body**. A body
would have to say either nothing useful or something about the configured limits, and the second describes the deployment
to every refused caller — including one whose credential is about to be refused, which must not learn that a named key
exists. Refusals are identical whoever provoked them.

`Retry-After` is included when the limiter can compute one, which means when the per-client bucket refused the request:
it knows when the next replenishment lands. A refusal for concurrency carries none, because a concurrency limit has no
scheduled moment at which a slot frees and a guess would be worse than silence. The advertised value is never below one
second, so a client never reads it as "immediately" and retries into the same refusal.

Rejections, active leases, queued requests, and lease durations are recorded through the built-in
`Microsoft.AspNetCore.RateLimiting` metrics, tagged with the policy name — `MailFathom:Mcp:RateLimiting` here and
`MailFathom:Admin:RateLimiting` on the administrative endpoint, which is how a rejection says which surface refused it.
Nothing MailFathom adds records a client name, an address, an origin, a credential, or anything from a request or its
response.

### What this is not

**The limits are counted in this process alone.** A deployment running several instances enforces them once per process
rather than once in total; there is no shared state and no coordination between them. Put the total behind a reverse
proxy or load balancer that bounds it if that matters.

**It is not DDoS protection.** It bounds what one client can take from the process it is talking to. A flood arriving
from many sources is a job for a WAF, a CDN, or a hosting provider's own protection, and none of that is in MailFathom.

**It bounds what the endpoint serves, not what the server spends deciding whether to serve it.** The limiter runs behind
the origin check, the certificate check, and authentication, so the work those do — reading every configured trust anchor
and comparing every configured key, on every request — happens before a permit is taken. The order is what makes the
per-client limit possible at all: run ahead of authentication and there is no client to count against, and every request
shares the anonymous bucket. What a bad credential costs the sender is therefore a partition it shares with every other
unidentified request, not the work the server already did to refuse it. Bounding connections that never authenticate is a
job for whatever fronts the process.

Turning the limits off is an explicit value and costs one startup warning:

```text
warn: MailFathom.Host.Hosting.Warnings.TransportRateLimitingStartupReport
      The MCP endpoint is enabled on /mcp with rate limiting turned off, so one caller can hold every database
      connection, response stream, and thread the process has until something runs out. This is the right setting only
      where something in front of this process already bounds the traffic reaching it. Remove
      McpEndpoint:RateLimiting:Enabled to run under the product defaults.
```

## What the endpoint records

Every tool call is logged once with the tool name, whether it ended in an error, and how long it took. Nothing else:
no filter values, no mailbox addresses, no subjects, and no part of a result, because a filter argument names a person as
surely as a result does.

An undiagnosed failure is logged in full, with its exception, at error level, correlated by the trace the request already
carries — and answered with the single generic error code `54001`, which tells the caller that the call failed and nothing
about why. When a client reports `54001`, the server log is where the reason is.

A refused call is logged with the five-digit code it was refused with, so an operator can correlate a client's complaint
against a server record without learning what was searched for.

## Verifying an enabled endpoint

With the endpoint enabled, the Streamable HTTP transport answers on `/mcp` and advertises three tools. Any MCP
client that speaks Streamable HTTP can list them; `tools/list` should report `list_emails`, `get_email_content`, and
`search_emails`, each with `readOnlyHint` true, `destructiveHint` false, `idempotentHint` true, and `openWorldHint`
false.

A call answers from the local mailbox copy, so what it returns depends on what synchronization has stored rather than on
whether a mail server is reachable. A deployment whose folders have never synchronized answers an empty page whose
`folderFreshness` entries report `wasSynchronized` as false, which is the state to check before treating an empty result as
a statement about the mailbox.

`get_email_content` reads that same local copy: it takes up to ten `storedEmailId` values a listing returned and never
fetches, so an email whose content is missing or damaged locally is reported as `55001` with a durable repair request
rather than answered with a download. That code arrives inside an otherwise successful result, on the entry for the email
it belongs to, so the emails read beside it are still returned. An operator reading `55001` in the log is reading a
local-consistency problem, not a mail-server one.

`search_emails` reads the lexical index built over that copy rather than the copy itself, so a folder that has
synchronized but whose text extraction has not run yet answers an empty window rather than a failure. `folderFreshness`
does not distinguish that case: it is computed from synchronization checkpoints alone, so such a folder reports a recent
`synchronizedAt` and `wasSynchronized` true exactly as a fully indexed one does. An empty window from a freshly
synchronized folder is therefore worth checking against extraction progress in the server log before it is read as a
statement about the mailbox. Its `retrievalMode` reports `lexical`, and a request that asks for more than 50 ranked
results is refused with `51003` rather than served a smaller window.
