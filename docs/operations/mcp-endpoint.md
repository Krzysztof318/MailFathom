# The MCP endpoint and what protects it

<!-- describes: backend/src/Mcp/**, backend/src/Host/Security/**, backend/src/Infrastructure/Security/**, backend/src/Common/OAuth/**, backend/src/Common/ClientAssertions/**, backend/src/Host/Hosting/Warnings/McpTransportAuthenticationWarning.cs, backend/src/Host/Hosting/Warnings/TransportGrantStartupReport.cs, backend/src/Domain/Access/MailFathomPermission.cs, backend/src/Host/Configuration/Endpoints/TransportClearTextRedirectOptions.cs, backend/src/Host/Configuration/Endpoints/TransportListenerConfiguration.cs, backend/src/Host/Configuration/Endpoints/ExternalListenerConfiguration.cs, backend/src/Host/Configuration/Endpoints/ReverseProxyOptions.cs, backend/src/Host/Hosting/Startup/ClearTextRedirectToHttps.cs, backend/src/Host/Hosting/Warnings/TransportClearTextRedirectReport.cs, backend/src/Host/Hosting/Warnings/ReverseProxyTrustWarning.cs, backend/src/Host/Hosting/Warnings/McpTransportEncryptionWarning.cs -->

The MCP endpoint is how an agent reaches MailFathom. This page records what enabling it means operationally, what a client
has to present to reach it, which browser origins it answers, which client applications it accepts a certificate from,
how much traffic it accepts before it starts refusing, and how it is served over your own domain and certificate. The
tools it serves are described in
`docs/features/mcp-tools.md`.

What a reader does at the other end of the connection is a separate page:
[connecting the chat client you already use](../users/mcp-clients.md) has the dialog, the address kind, and the
authentication shapes of each popular client by name — including the two that offer no field for a static header, which
decides what a deployment configures here rather than only how it is set up.

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
| `Authentication[].OAuth.AdvertisedScopes` | empty | Scopes published for a client to ask for and checked on no token — `offline_access` above all |
| `Authentication[].OAuth.AuthorizationServers` | — | The external authorization servers whose tokens this entry accepts |
| `Cors.AllowedOrigins` | `["*"]` | The browser origins served: `*` for every one, a list for exactly those, an empty list for none |
| `ClientCertificateProfiles` | empty | The client applications whose certificates are accepted, each with its own authorities and expected names |
| `RateLimiting` | bounded — see [Rate limiting](#rate-limiting) | How much traffic the endpoint accepts, per process and per client |
| `RequestTimeout` | bounded — see [Request timeouts](#request-timeouts) | How long one request may run before the endpoint abandons it |
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

## The one route on this surface that admits no credential

Beside the protocol route, an enabled MCP endpoint serves `GET /attachments/<capability>` on the same listeners. It is
what a `get_email_content` link points at, and it **requires no credential**: the signed capability in the URL is the
whole of its access control. A link exists to be handed to whatever actually fetches files — a browser, a downloader, a
client's own HTTP stack — and none of those can attach this endpoint's key, certificate, or token, so requiring one
would make the capability unusable by its only callers.

What bounds it instead:

- The capability names exactly one attachment of one email, carries an HMAC-SHA256 tag verified in constant time, and
  expires within minutes — ten by default, thirty at the most this product permits.
- The signing key is derived per operation from the deployment's
  [data-encryption key ring](secret-provisioning.md#the-data-encryption-key). A deployment configuring none issues no link,
  and neither does one that has not declared `Deployment:PublicBaseAddress`.
- The address a link points at is that declared value rather than the request's `Host` header, so nothing a caller sends
  decides where the URL it receives resolves to.
- Redemption reads the mailbox afresh, so a link dies with the message it points at. An expired capability, a forged
  one, and one whose mail is gone are all `404` with the same body.
- It rides this endpoint's listeners, its transport, both of its rate limits, and its
  [request ceiling](#request-timeouts). It spends the surface's shared anonymous per-caller bucket, because it presents
  no credential to partition on, and it takes a permit from the same process-wide concurrency limiter the protocol
  route does — each redemption loads a stored message, parses it, and holds a response stream open. The ceiling is what
  bounds how long it holds that permit: a client reading just above Kestrel's minimum response rate is otherwise the
  one place a burst of slow readers is unbounded, and it needs no credential to be that client.
- The response is `Cache-Control: no-store`. A proxy that cached it would keep serving the file for that URL after the
  capability expired, which is the one way an intermediary can outlive the window.

**What does not cover it are the two checks written for the protocol route**, both of which are scoped to `/mcp`
deliberately. A configured `ClientCertificateProfiles` list is not enforced here, because a client certificate is
exactly the kind of credential a downloader cannot present, and the origin allow-list is not consulted, because a
browser navigating to a URL sends no `Origin` header to check. A deployment that requires mutual TLS on this endpoint is
therefore still handing out links anything on the network can redeem within their window; if that is not the posture you
want, leave `Deployment:PublicBaseAddress` unset and no link is ever issued.

Two consequences for a deployment. **Serve this endpoint over HTTPS if anything but this machine reaches it**, since a
capability in a URL is a secret in transit; the address setting refuses clear text to a non-loopback host for that
reason. And **a reverse proxy in front of this endpoint must pass `/attachments/` through**, or every issued link
resolves to nothing while the rest of the tool keeps working; it must also honour `no-store` rather than override it
with a freshness policy of its own.

**Nothing records the capability.** MailFathom writes no log line about a download, and the request span this host
exports carries the route template `/attachments/{capability}` in place of the path the request actually arrived with —
otherwise a deployment exporting traces would be shipping short-lived bearer credentials over mail to whatever stores
them. The exported log records carry no request scope either, which is why: the scope ASP.NET Core opens around every
request holds the path exactly as it arrived, so a database command logged during a download would have carried the
capability with it. Two operator settings undo that, and both are ordinary things to reach for while diagnosing
something else: lowering `Logging:LogLevel:Microsoft.AspNetCore` from its shipped `Warning` turns on the framework's own
request logging, which writes the whole URL, and setting `Logging:Console:FormatterOptions:IncludeScopes` puts the
unredacted request path on every console record a download produces — which a container log collector ships exactly as
an exporter would. Turn either on and the capabilities issued during the window are in the log until they expire.

[Email content](../features/email-content.md#what-a-download-link-is-and-what-bounds-it) records what the capability
carries and how it is verified.

## Authentication

**`Authentication` is a list of credentials.** The four methods identify different kinds of caller — a key belongs to a
client the operator provisioned, a public key belongs to a client that holds the private half and signs for itself, a
token belongs to a person an external authorization server signed in, and a password belongs to one owner of mail this
deployment holds — so a deployment serving several carries an entry for each:

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": [
      { "ApiKey": { "Name": "nightly-digest", "SecretReference": "systemd-credential:mailfathom-mcp-digest-key" } },
      { "PublicKey": { "Name": "reporting-job", "SecretReference": "file:/etc/mailfathom/reporting-job.pub" } },
      { "OAuth": { "Resource": "https://mail.example.test/mcp", "AuthorizationServers": [ { "Name": "workforce", "Issuer": "https://sso.example.test/realms/mailfathom", "AuthorizedSubjects": [ "9f2c7c1e-8a4d-4c62-9f0b-3d2a1b5e7c04" ] } ] } },
      { "Basic": {} }
    ]
  }
}
```

**An entry states its method by carrying that method's block**, and nothing names the method a second time. That is what
makes a method impossible to select without configuring it, or to configure without selecting it: a key is the entry that
turns keys on. There is no limit on how many entries state any method — a second key is a second entry, and a second
authorization server may be either — and an entry may carry several blocks at once, which is a matter of how you group
what you wrote rather than a distinction the endpoint draws, until you write a grant on one:
[what a credential may do](#what-a-credential-may-do) is where grouping acquires a consequence, and it names the one
combination of blocks that is refused. Otherwise one shape of entry fails startup, named by its position: one carrying
no block. So does a value written where the list belongs, because a value contributes no entries and would otherwise
leave the endpoint served with no credential at all.

A request is served when it satisfies **any one** of the entries. A password is told from the rest by name — it is the
one credential whose header says `Basic` where every other says `Bearer` — and the bearer credentials are told apart by
shape: a client assertion is a JSON Web Token declaring MailFathom's own media type in its header, an access token is a
JSON Web Token naming its issuer, an API key is anything else, and each reaches only the check that understands it. That is also why
**an API key must not itself be a token of a configured authorization server**: such a key would be judged as an access
token by that server and never compared as a key, so no client could authenticate with it. Startup refuses the
combination by position — `McpEndpoint:Authentication:0:ApiKey` — rather than letting a deployment start with a key
nothing can ever use. A token-shaped key naming an issuer this deployment does not configure selects no validator and is
compared like any other opaque key, so it is accepted; issue opaque keys and the question does not arise.

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

### Key pairs

A key pair answers the case the other two do not. An API key is a shared secret, so a copy of every credential that
reaches the mailbox sits on the host and in whatever produced the configuration. OAuth answers for a person who signed
in, which a scheduled job does not have. Here **the client holds the private key and the deployment holds only the public
half**, so nothing this host stores in order to verify a request is worth stealing from it — not from the configuration,
not from a backup of it, and not from the deployment tool that wrote it.

Configure one public key per entry, exactly as you would a key:

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": [
      {
        "PublicKey": {
          "Name": "reporting-job",
          "SecretReference": "file:/etc/mailfathom/reporting-job.pub",
          "Lifetime": "2027-01-31T00:00:00Z"
        }
      }
    ]
  }
}
```

The block is an ordinary [named secret](secret-provisioning.md#the-secret-block), so the material is reached through
every reference scheme the deployment already has — a file, an environment variable, a systemd credential — the `Name` is
what a diagnostic and the rate-limit partition correlate on, and the `Lifetime` is enforced. The material itself is not a
secret; the shape is what gives it a name, a reference, and an expiry.

**Generating a pair.** The client generates it and never sends the private half anywhere:

```console
$ openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out reporting-job.key
$ chmod 600 reporting-job.key
$ openssl pkey -in reporting-job.key -pubout -out reporting-job.pub
```

Give the deployment `reporting-job.pub`. RSA works too — `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072` —
with a modulus of at least 2048 bits; elliptic curves P-256, P-384, and P-521 are accepted and no others, because those
are the three RFC 7518 defines a signature algorithm over. A curve is recognized by its identifier rather than by its
size, so `secp256k1` and the Brainpool curves are refused although they are the same lengths. The signature algorithm
follows from the key, so there is nothing to agree on and nothing to configure. The permitted algorithms are the same
asymmetric allow-list an access token is judged by: nothing symmetric, and no `none`.

**What the client presents.** It mints a short-lived JSON Web Token, signs it with its private key, and presents it as an
ordinary bearer credential — the arrangement RFC 7523 describes and OpenID Connect deploys as `private_key_jwt`. The
header, the refusal, and the rate-limit partition are therefore the same as for any other method; only what verifies the
credential is new. The assertion carries three claims and one header:

```json
{ "alg": "ES256", "typ": "mailfathom-client-assertion+jwt" }
{ "aud": "urn:mailfathom:mcp", "exp": 1786060860, "jti": "hLdI6NHKQ4qXCXhPrsRJfA" }
```

- **`typ`** is `mailfathom-client-assertion+jwt`, which is what separates this credential from an access token. Declaring
  it is required, not optional: nothing else may be presented here, and this may be presented nowhere else.
- **`aud`** is `urn:mailfathom:mcp` for this endpoint and `urn:mailfathom:admin` for the
  [administrative endpoint](admin-endpoint.md). It is what stops an assertion minted to read a mailbox from
  administering the service, whichever surface the key is registered on. It does not separate two deployments that
  registered the same public key; where that matters, give the client a key pair per deployment.
- **`exp`** must be present and no more than five minutes ahead — a minute is a good value. This is what a shared secret
  cannot offer: a captured assertion stops working on its own, whatever anyone does about it.
- **`jti`** is a fresh unguessable value per assertion — 128 random bits, base64url-encoded, is the right shape. The
  endpoint refuses an identifier it has already served, so a captured assertion cannot be replayed even inside its
  remaining seconds.

`mfctl` mints all of this for you; see [Signing in with a key pair](admin-endpoint.md#with-a-key-pair).

**Every refusal answers the same way** as every other method's: an empty `401` with the same challenge. That covers a
signature no configured key made, a key whose lifetime has ended, a wrong audience, an absent or too distant expiry, a
missing identifier, and one already spent. Nothing in the response distinguishes them, and nothing logged carries the
presented assertion or key material. The server log names the configured key at `Warning` for a retired key, an over-long
expiry, and a replayed identifier, which are the three an operator acts on.

**Rotating a key.** The same overlap a key rotation uses: add the new public key as a second entry, move the client to
the new private key, remove the old entry. Nothing is refused in between, and there is no secret to coordinate across two
machines — only a public file to replace.

**Startup refuses unusable material by position.** An entry whose reference resolves to something that is not a PEM
public key fails at `McpEndpoint:Authentication:0:PublicKey`, and so does an RSA key below the accepted modulus or a key
of a kind no permitted algorithm covers. One of them is worth naming separately: **material carrying a private key is
refused explicitly**, because it would otherwise import cleanly, verify every client correctly, and leave the host
holding exactly what this method exists to keep off it.

### OAuth

MailFathom acts as an [OAuth 2.1 protected resource](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization).
An external authorization server the operator already runs signs users in and issues tokens; MailFathom verifies what that
server signed and nothing else. It is **never** an authorization server: it stores no password, issues no token, redeems no
authorization code, holds no refresh token, and has no login page.

This section is the reference for every setting below and for what a token has to prove.
[MCP client OAuth](mcp-client-oauth.md) is the order those settings are arrived at in, including the half that happens in
the identity provider — read that first if this is a deployment's first OAuth connection.

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": [
      {
        "OAuth": {
          "Resource": "https://mail.example.test/mcp",
          "RequiredScopes": [ "mailfathom.read" ],
          "AdvertisedScopes": [ "offline_access" ],
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

**Several OAuth entries are supported, and each states its own terms.** `RequiredScopes`, `AdvertisedScopes`, and
`AuthorizationServers` belong to the entry that carries them, so a token is judged against what *its own* issuer's entry
asks for — one tenant may be required to carry a scope while another is not, without either being weakened to match the
other. The published document is the one place they are read together, because there is one of it: it lists every scope
any entry requires or advertises, so a client cannot tell from it which entry asked for what. What every entry must
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

It must be an `https` URL with no user information, no query, and no fragment, and startup refuses anything else.
**What is published and compared is that value brought to one canonical form**, rather than the characters you typed:
the scheme and host are lowercased, a default port is dropped, and a trailing slash is dropped where the path is empty —
`HTTPS://Mail.Example.TEST:443/mcp` and `https://mail.example.test/mcp` are one identifier. A trailing slash on a path
that names something is part of what it names and is left alone, so `/mcp` and `/mcp/` stay two identifiers. That is why
the value a client is configured with comes from the published metadata document rather than from the configuration
file: an audience is settled by an exact string comparison, and only one of the two spellings ever reaches it. An issuer
is deliberately the opposite and is never rewritten, for the reason the next paragraph gives.

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
- every scope in `RequiredScopes`. `AdvertisedScopes` is checked on nothing — it is published for clients and takes no
  part in any of this.

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

#### Scopes you advertise but do not require

**`AdvertisedScopes` publishes a scope for clients to ask for, and checks it on nothing.** The metadata document's
`scopes_supported` is what a client should request — that is what
[RFC 9728](https://datatracker.ietf.org/doc/html/rfc9728) defines the field as — and it is not the same list as what a
token is refused for lacking. Both lists reach the document; only `RequiredScopes` reaches the check. An entry that
narrows by token scopes publishes its whole ceiling there too, for the reason
[what a credential may do](#what-a-credential-may-do) gives: a client cannot ask for a permission nothing advertises.

**`offline_access` is what the setting exists for.** A client asks for a refresh token by naming that scope, and the
widely deployed authorization servers issue none without it. A client that reads your metadata document and asks for
exactly what it lists therefore holds no refresh token, and sends the person back through a sign-in page whenever the
access token expires — every hour on most servers. Advertising it fixes that for every client at once:

```json
{
  "AdvertisedScopes": [ "offline_access" ]
}
```

**Requiring it instead would be wrong, and there is deliberately no way to arrive at that by accident.** The value
describes the client's own session rather than anything MailFathom protects, and an authorization server need not put it
in the access token's `scope` claim at all — so requiring it would refuse perfectly good tokens from a server that
grants offline access and does not name it in the token. A scope listed here can never turn a caller away.

Every value is a scope token, refused at startup with the same message a malformed required scope gets, naming the
setting and the index. A value that is already in `RequiredScopes` is refused as well: every required scope is published
regardless, so writing it twice would say nothing and would leave the setting reading as the whole advertised list
rather than as what is advertised beyond what is checked.

`mfctl` asks for exactly what the document lists and adds nothing to it, so this is the setting that decides whether an
`mfctl` sign-in survives its first hour — see
[how long an OAuth sign-in lasts](admin-endpoint.md#how-long-an-oauth-sign-in-lasts).

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
  "scopes_supported": [ "mailfathom.read", "offline_access" ],
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

[MCP client OAuth](mcp-client-oauth.md) walks that work in the order it happens, with one provider's field names, the
callback URL a client generates rather than accepts, a verification recipe to run before a client is touched, and what
each failed sign-in looks like.

### Passwords

A password belongs to a person rather than to a client, and it is the one credential this deployment holds a record of
rather than reads out of your configuration. So the block turns the method on and carries nothing to steal:

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": [
      { "Basic": { "AttemptsPerMinute": 10 } }
    ]
  }
}
```

The credentials themselves are provisioned over the administrative endpoint, with
[`mfctl credential`](admin-endpoint.md#owner-credentials). Nothing provisions one on its own and there is no default, so
an endpoint carrying this block and no provisioned credential authenticates nobody.

A client presents one exactly as [RFC 7617](https://www.rfc-editor.org/rfc/rfc7617.html) describes:

```http
POST /mcp HTTP/1.1
Host: mail.example.test
Authorization: Basic b3duZXI6Y29ycmVjdGhvcnNlYmF0dGVyeXN0YXBsZQ==
```

The two halves are a username and a password joined by a colon and encoded as base64 — which is an encoding rather than
a protection, and is the whole reason for the transport rule below. A username is folded to lower case, so `Owner` and
`owner` are one credential, and it names exactly one owner across the deployment.

**A surface accepting a password must be confidential, and startup refuses it otherwise.** Every other method here is
warned about over clear text and permitted, because a deployment may knowingly be on a loopback socket. A password is
not: it is typed by a person, it is the credential that person is most likely to have typed somewhere else, and reading
it once off the network is reading it for as long as it stands. Two arrangements satisfy the rule, and they are the two
a deployment actually runs — the endpoint terminates TLS itself, through [`Transport`](#https-and-your-own-domain), or
you have named what stands in front in
[`ReverseProxy:TrustedProxies`](#behind-a-tls-terminating-reverse-proxy). A range covering every address is not naming
one: it trusts whatever can open a connection, which is what a section naming nothing already does.

What the rule reads is what the endpoint **serves**, not whether a certificate is configured. `HttpsOnly` satisfies it.
`HttpAndHttps` satisfies it too, but only while [`Https:Redirect:Enabled`](#redirecting-a-client-still-pointed-at-http)
is left on: with the redirect off, that mode's clear-text socket answers the routes rather than pointing away from them,
which is the same unencrypted hop as no certificate at all. A `Basic` block beside that arrangement is refused at
startup, with a sentence naming both ways out.

**A request that reaches this process as clear text is refused before its header is read**, even on a deployment the
rule above admits. The arrangement it admits behind a proxy leaves a clear-text socket open, and a request arriving
there from anywhere but the named proxy carries no forwarded scheme — so it is answered with the challenge below rather
than having its password compared. The startup rule decides which deployments may accept a password; this decides which
requests may carry one.

An endpoint carries **at most one** `Basic` block and startup refuses a second. Rotation is a second credential row
rather than a second block, because a presented credential names a username rather than an entry — two blocks would
leave the grant an owner holds decided by configuration order.

**A refusal says nothing about which half was wrong.** No credential at all, a header that does not decode, an unknown
username, a wrong password, a credential somebody disabled, and a caller that has spent its attempts each receive the
same answer, and the deployment spends the same work reaching it:

```http
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer realm="MailFathom"
WWW-Authenticate: Basic realm="MailFathom", charset="UTF-8"
```

Both challenges are offered, which is what lets an OAuth client's discovery keep working on an endpoint that also
accepts a password. The `charset` parameter is the only one RFC 7617 permits and is what makes a password outside
US-ASCII survive the round trip.

`AttemptsPerMinute` bounds guessing and defaults to 10, which is a person correcting a mistyped password. It is applied
**per source and per username** rather than per endpoint, because those are the two shapes an attack takes: one host
trying many passwords, and many hosts trying one account's. **Only a wrong password spends any of it.** Basic
re-presents the credential on every request and this deployment keeps no session, so a bucket a working password spent
would bound an owner's request rate rather than anybody's guessing — at the default, the eleventh call of a working
session would be refused with the answer a wrong password gets. Both buckets replenish continuously, so a client that
has spent its capacity gets some back within seconds rather than being locked out — the point is to make guessing
expensive rather than to give anybody a way to lock an owner out. It is separate from
[`RateLimiting`](#rate-limiting), which bounds requests to the surface rather than guesses at a credential. The ceiling
is 600; a number above it is refused, because a thousand verifications a minute against one username is an offline
guessing rate rather than a bound.

**The method is refused on the administrative endpoint**, at startup, naming the section. That surface answers for the
deployment rather than for a person, so a credential naming an owner has nothing there to act for —
[the administrative endpoint](admin-endpoint.md) states what it accepts instead.

### Requiring no credential

An enabled endpoint whose `Authentication` list is empty requires no credential. Writing the empty list says so
explicitly and is exactly equivalent to leaving the setting out:

```json
{
  "McpEndpoint": { "Enabled": true, "Authentication": [] }
}
```

There is nothing to configure alongside it, which is the point of the shape: keys, public keys, authorization servers,
and the bound on guessing a password all live inside the entry that turns their method on, so a deployment cannot end up
carrying settings nothing checks — believing it is protected, which is worse than knowing it is not.

Whenever an enabled endpoint requires no credential, startup logs one warning:

```text
warn: MailFathom.Host.Hosting.Warnings.McpTransportAuthenticationWarning
      The MCP endpoint is enabled on /mcp with no authentication method configured, so anything that can reach this
      address can read the synchronized mailboxes. Add an entry to McpEndpoint:Authentication carrying an ApiKey block,
      a PublicKey block, an OAuth block, a Basic block, or any combination of them, unless the address is reachable only
      from this machine or from a network you control. Neither an origin policy nor a client certificate substitutes for
      this: the first restricts which page a browser will let call, the second names the application calling, and
      neither identifies the person whose mail is served.
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
- **What can be read here can also be marked, and the contact book can be erased.** An endpoint with no
  `Authentication` entry grants every permission this surface publishes, so anyone who can reach the port holds the
  reading half — where the exposure is disclosure of a mailbox, which is enough on its own — and every writing half
  with it. `mailfathom.mail.flags.write` lets them mark, star, and relabel the owner's mail on the real mail server
  through `set_mail_flags`, and the change converges out over the account's own write connection; nothing there sends,
  deletes, or moves mail, but a message somebody else marked read is a message the owner never saw arrive.
  `mailfathom.mail.contacts.write` lets them record, amend, and irreversibly erase the deployment's records about
  identified third parties. `mailfathom.mail.send` is the third writing half and the one whose effect cannot be
  recalled: through `send_email` it lets anyone who can reach the port send mail from the owner's own address to
  anybody, and through `reply_to_email` and `forward_email` — which such an endpoint also grants the reading half those
  two need — it lets them answer the owner's correspondents and pass the owner's mail and its attachments on to
  strangers. It carries `get_outgoing_email` and `cancel_outgoing_email` with it, so the same reach reads back what the
  sends it made were answered with and stops one before it leaves; those two are confined to what the calling principal
  queued, which on an endpoint that admits everybody as one identity confines nothing. It carries `send_draft` too, so
  a message the owner wrote and did not send is one call from having been sent. Nothing takes any of it back.
  `mailfathom.mail.drafts.write` is the mildest of the four writing halves and still not nothing: through `save_draft`,
  `update_draft`, and `delete_draft` it lets anyone who can reach the port put a message into the owner's own Drafts
  folder, rewrite one, and delete one — and a message somebody else drafted is one the owner may read as their own.
  Narrow the entry, or keep the port unreachable.

### What a credential may do

Every entry in `McpEndpoint:Authentication` states what the credentials it admits may do, as `Permissions`. This
surface's half of the published set is seven names — `mailfathom.mail.read`, `mailfathom.mail.ask`,
`mailfathom.mail.contacts.read`, `mailfathom.mail.contacts.write`, `mailfathom.mail.flags.write`,
`mailfathom.mail.drafts.write`, and `mailfathom.mail.send` — and
[what a credential may do](permissions.md) holds the model behind them in full: what each name reaches, which tool each
one covers, how a grant is written, what an absent `Permissions` key and an empty list mean, what
`PermissionsFromTokenScopes` turns the list into, and what fails startup.

Two things about it are this surface's own, and both are below. The first is what a grant does to what a caller is
offered; the second is what startup writes about every entry.

**What varies by the grant is the tool surface itself.** A caller is offered exactly the tools its grant permits and no
others: `tools/list` omits the rest, so a client never plans a call that could only fail, and a call naming one of them
is answered as a call naming a tool that does not exist. The refusal says nothing about the caller, the credential, the
permission, or what a different caller would have been served: a message a client could tell apart would disclose the
capability the listing just withheld. Nothing is cached either — the listing is composed per request, so one caller's
answer never serves another.

The availability rule that already decides `ask_mail` composes with this rather than being replaced by it. A tool may be
unavailable, unauthorized, or both, and no grant makes a capability this deployment does not have appear — an endpoint
whose chat provider is unconfigured withholds `ask_mail` from a caller granted `mailfathom.mail.ask` exactly as it does
from one granted nothing.

Startup states what every entry resolved to, one line per entry, so the posture is one an operator reads on the first
run rather than infers later. An entry that wrote no grant says so rather than being reported as though somebody had
chosen what it holds:

```text
info: MailFathom.Host.Hosting.Warnings.TransportGrantStartupReport
      The MCP endpoint entry McpEndpoint:Authentication:0 writes down no grant, so every credential it admits holds
      mailfathom.mail.read, mailfathom.mail.ask, mailfathom.mail.contacts.read, mailfathom.mail.contacts.write,
      mailfathom.mail.flags.write, mailfathom.mail.drafts.write, mailfathom.mail.send — everything this surface
      publishes. Write a 'Permissions' list
      on the entry to narrow it, or an empty one to grant nothing. A caller here is served only the tools its grant
      permits, and a call naming any other is answered as a tool that does not exist.
```

Every line closes with what a grant on that surface does, so an operator reading back the one entry they edited learns
what the narrowing costs a caller without reading the rest of the report. An entry that narrowed its grant is reported
as what it grants, an entry with `PermissionsFromTokenScopes` as what it grants *at most*, and an entry granted nothing
as `nothing` rather than as a line that lost its argument. An endpoint with no entry at all gets one line naming the
section a grant would be written under.

### What a credential decides, and what it does not

**The endpoint asks whether this is a caller the deployment serves, and of a token also which person it names.** What an
admitted caller may then do is the grant its entry carries, and that decides one thing: which of this surface's tools it
is offered and may call. Which tool each of the seven names covers is
[what a credential may do](permissions.md#which-tool-each-name-covers); an entry narrowed to the contact half therefore
reaches the contact book and nothing else, and one granted none of the seven is served an empty tool list and refused
every call it makes. `mailfathom.mail.send` is the one worth checking a narrowed entry against deliberately, because
it is the only name here whose effect leaves the deployment and cannot be recalled.

**A refused caller is told nothing**, for the reason
[what a refused caller is told](permissions.md#what-a-refused-caller-is-told) gives: a message a client could tell apart
from an unknown tool would disclose the capability the listing withheld. Diagnosing a client that stopped working is
therefore done from this deployment's own record rather than from what the client received: every refusal is counted by
`mailfathom.authorization.refusals` and written as a warning naming the credential it was refused, and the permission
the grant omits wherever the tool the call named publishes one — a call naming no tool this surface publishes is refused
for no permission and its warning names none, which is how a client on a stale or misspelled name reads apart from one
asking for what it was never granted.
[Telemetry](telemetry.md#what-an-authorization-refusal-records) describes both in full.

**Which mailboxes a caller reaches is not something a credential decides.** Every tool call resolves the accounts the
configured owner controls and refuses anything outside them, whichever credential got the caller in, and no setting
narrows that — which is exactly why a token has to name an authorized subject, since admitting a colleague of the same
tenant would admit them to the owner's mail rather than to their own.

A key identifies a *client*, a public key identifies a *client that can prove it holds the other half*, a token
identifies a *person*, and the difference matters operationally. A shared bearer credential has the properties every
shared bearer credential has: it does not expire on its own unless you give it a lifetime, it cannot be revoked for one
user without revoking it for the client, and anything that reads it can use it. A key pair removes the sharing: nothing
reusable ever crosses the network or sits on the host, the credential presented expires within minutes, and revoking a
client is deleting one entry. A token expires on its own, is revoked where the authorization server says so, and carries
the multi-factor and conditional-access policy that server already enforces.

Neither an authorized subject nor a required scope is asked of a key or of a public key. Both are credentials the
operator provisioned by writing them into this deployment's configuration, so that decision *is* the authorization; a
token is issued by a server that decides for itself who receives one, which is what makes both worth checking there.

Of a validated token, MailFathom keeps three things and discards the rest: the issuer, the subject, and the scopes. A name,
an email address, a group, and a tenant claim are dropped at the boundary, so nothing downstream can begin trusting a
claim the operator never mapped. The identity is `iss` together with `sub` rather than `sub` alone, because a subject is
unique only within the server that issued it, and never an email address, which is reassignable.

## What this endpoint publishes

A grant decides what an admitted caller may do. What this endpoint offers at all is a separate decision, and
`McpEndpoint:PublishedToolCategories` is where it is written:

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "PublishedToolCategories": ["mailbox", "contacts"]
  }
}
```

The categories are `mailbox`, `flags`, `sending`, `drafts`, `answering`, and `contacts`, and
[Tool categories](../features/mcp-tools.md#tool-categories) is the table of which tools each one carries. **Naming none
publishes every one of them**, which is the surface a deployment has without the setting — so adding a release that has
it changes nothing about an endpoint already running, and narrowing is always something you did. A name no category
answers to fails startup, naming the value and listing what is accepted, rather than being ignored and leaving the
endpoint quieter than you wrote it.

**It only ever takes away.** Publishing `sending` does not make this deployment able to send — an account's own SMTP
settings decide that — and no category widens a grant or reveals a tool a caller was not offered. A tool is served when
its capability is available, its category is published, and the caller's grant reaches it; a tool outside any of the
three is absent from `tools/list` and its name is answered as a tool that does not exist.

Two reasons to reach for it. The first is exposure: an instance stood up for retrieval can publish the reading tools
alone, so an agent you do not fully trust cannot see that anything else exists, and a release adding tools does not
change what your endpoint offers without you noticing. The second is cost — a listing of thirty tools is thirty
descriptors in a model's context on every session, and an instance used only for reading pays that for nothing.

### A client may narrow its own session

A connecting client may name categories in the `MailFathom-Tool-Categories` header, and what it is served is the
intersection of that with the list above. That is what lets one endpoint serve a retrieval-only agent beside a
full-capability one without running two of them.

**The header is not an authorization mechanism.** It is written by the caller and can only take away: a category this
deployment excluded is never published because a client asked for it, and a request naming only excluded categories is
served nothing rather than being widened back to your selection. A value this endpoint cannot read — an unknown name,
a header longer than its bound — is dropped rather than failing the request, and a header naming nothing usable leaves
your own selection in force. [The `MailFathom-Tool-Categories` header](../features/mcp-tools.md#the-mailfathom-tool-categories-header)
holds the syntax and the bounds. A browser-based client reaches the header because the endpoint's CORS policy names it
among the request headers it permits, which needs no configuration of yours.

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

**Every partition is keyed by the surface it belongs to**, including the anonymous one. Each endpoint's key list is
configured separately and none consults another's, so one name spelled under two sections is two independent buckets
rather than one shared between them — and the burst an agent spends reaching a mailbox is never the burst an operator
needs to administer the service, nor the burst somebody's own mail client spends on the client endpoint.

Readiness, liveness, and the root endpoint are outside all of this and keep answering while any endpoint is refusing,
because the limits are attached to each surface's routes rather than applied as the process's default policy.

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
unidentified request, not the work the server already did to refuse it. What bounds the connections underneath all of
that — including the ones that never send a request at all — is [`ConnectionLimits`](configuration-endpoints.md#connectionlimits),
which is a ceiling on the process rather than on this endpoint.

Turning the limits off is an explicit value and costs one startup warning:

```text
warn: MailFathom.Host.Hosting.Warnings.TransportRateLimitingStartupReport
      The MCP endpoint is enabled on /mcp with rate limiting turned off, so one caller can hold every database
      connection, response stream, and thread the process has until something runs out. This is the right setting only
      where something in front of this process already bounds the traffic reaching it. Remove
      McpEndpoint:RateLimiting:Enabled to run under the product defaults.
```

## Request timeouts

Rate limiting decides how much traffic is admitted. It does not decide how long an admitted request may hold what it was
admitted with, and those are separate bounds on separate resources — which is why `RequestTimeout` is its own section
beside `RateLimiting` rather than another number inside it.

A concurrency permit is taken on the way in and released when the request ends. Without a ceiling, `MaxConcurrentRequests`
bounds how many requests run at once and nothing bounds how long any of them lasts, so twenty slow requests take the
endpoint out of service without exceeding any rate. An enabled endpoint therefore carries a ceiling by default, on the
same reasoning the limits do.

```json
{
  "McpEndpoint": {
    "RequestTimeout": {
      "Enabled": true,
      "Duration": "00:10:00"
    }
  }
}
```

| Setting | Default | Range | Meaning |
|---|---|---|---|
| `Enabled` | `true` | — | Whether a request that outlives `Duration` is abandoned |
| `Duration` | `00:10:00` | 1s–1h | How long one request may run |

A request that reaches the ceiling is abandoned: its `CancellationToken` is signalled, the response is `504`, and the
concurrency permit it held is released. `AdminEndpoint:RequestTimeout` takes the same keys and the same default and is
configured independently, exactly as the limits are.

**Ten minutes is a bound on a hang, not a promise that no legitimate request is abandoned**, and the two cannot both
hold here. An `ask_mail` run is a conversation whose length the model decides — bounded by
[`MailAnswering:MaxProviderCallsPerRun`](../features/mail-answering.md#what-one-question-may-spend) at eight calls, each one an
`AiProviderInvocation` whose own `TotalTimeout` defaults to five minutes. A ceiling that enclosed the maximum would have
to sit at forty minutes, which is not a request ceiling at all and would let one stalled run hold a
concurrency permit for that long.

So the number is chosen against what a request costs to hold rather than against what the slowest one may spend. It
clears an ordinary answering run by a wide margin, and **a run that walks its whole provider budget is abandoned with a
`504`** — deliberately, and worth knowing before you read one as a fault. Raise this alongside
`MailAnswering:MaxProviderCallsPerRun` if you raise that, or if your questions genuinely run long.

Narrowing is the other direction and the more common one. A deployment serving no AI-backed tool can drop it to a
minute: every other tool answers from the local mailbox copy with a bounded query. The administrative endpoint reaches
no provider at all, which makes it the one to narrow without having to ask what a tool call needs.

**The ceiling is applied ahead of the rate limiter**, so time spent waiting for a limiter lease is inside it rather than
outside. Under the default queue limits of `0` that wait is nothing; once a queue is configured it is the whole point,
because a request queued for its client's tokens is already holding a concurrency permit.

**Both routes on this surface carry it**, the protocol route and the
[attachment download](#the-one-route-on-this-surface-that-admits-no-credential). The download is the one that most
needs it: it holds a response stream open for as long as its reader takes, so without a ceiling a client reading just
above Kestrel's minimum response rate holds a concurrency permit indefinitely, and it presents no credential.

The probes are outside it. A named policy is attached to each route rather than a default policy applied to every one,
so a readiness answer is never abandoned because a mailbox query was slow — which would take the instance out of
traffic for the one thing that was still working.

What is in force is stated once at startup, one line per enabled endpoint:

```text
info: MailFathom.Host.Hosting.Warnings.TransportRequestTimeoutStartupReport
      The MCP endpoint on /mcp abandons a request that has run for 00:10:00, answering 504 and releasing the
      concurrency permit it held.
```

Turning it off is an explicit value and costs one startup warning, in the shape the rate limits use.

## What the endpoint records

Every tool call is logged once with the tool name, whether it ended in an error, and how long it took. Nothing else:
no filter values, no mailbox addresses, no subjects, and no part of a result, because a filter argument names a person as
surely as a result does.

An undiagnosed failure is logged in full, with its exception, at error level, correlated by the trace the request already
carries — and answered with the single generic error code `54001`, which tells the caller that the call failed and nothing
about why. When a client reports `54001`, the server log is where the reason is.

A refused call is logged with the five-digit code it was refused with, so an operator can correlate a client's complaint
against a server record without learning what was searched for.

A call refused for want of a permission is recorded once more, and separately, because the caller was told nothing it
could report: it is counted by `mailfathom.authorization.refusals` and written as a warning naming the credential and
the permission the grant omits. That record is the whole of what an operator has for this boundary, and
[telemetry](telemetry.md#what-an-authorization-refusal-records) says what it carries and what it deliberately does not.

That same measurement is published as instruments, so a rate and a distribution can be read without going through the
records one at a time. What they carry is the tool and how the call ended, and nothing else;
[telemetry](telemetry.md#what-mailfathom-publishes-under-its-own-name) names them and says why a tool a caller asked for
by a name this deployment does not publish is measured under one fixed placeholder rather than under the name it sent.

## What a client learns while connecting

The `initialize` handshake reports this deployment as `MailFathom` and names the version the protocol assembly was built
with — not the host's assembly name, which would tell a client about a composition detail rather than about the surface
it is talking to, and not the source revision, which is build provenance an operator reads from the [startup
record](host-startup-telemetry.md) instead.

Beside that, the handshake carries **instructions**: one sentence naming where the documentation for that running
version is published, at `https://krzysztof318.github.io/MailFathom/v<version>/`. A client that connected over MCP may
be the only way its user meets MailFathom at all, so the session itself is what says where to read — otherwise an agent
asked to consult the documentation reaches whichever version a search engine ranked first. The address is derived from
the version the same handshake reports and is not configurable, so the pages it names cannot come to describe a
different build from the one the client was told it is talking to. A deployment running a nightly names `latest`, which
is what a nightly carries, and a build whose version cannot be read carries no instructions rather than an address that
goes nowhere.

Nothing else travels in them. The protocol places no bound on what instructions may hold, and a client may put them in
front of a model, so what belongs there is where to read rather than what to read: this makes the documentation
findable and never serves it over the protocol.

## Verifying an enabled endpoint

With the endpoint enabled, the Streamable HTTP transport answers on `/mcp`. Any MCP
client that speaks Streamable HTTP can list what it advertises; `tools/list` should report `list_accounts`,
`list_emails`, `get_email_content`, and `search_emails`, each with `readOnlyHint` true, `destructiveHint` false,
`idempotentHint` true, and `openWorldHint` false.

The six contact tools are beside them whenever the credential holds the permission each one needs, which an entry that
writes no `Permissions` list does: `list_contacts` and `get_contact` read like the four above, while `create_contact`,
`update_contact`, `delete_contact`, and `promote_contact` report `readOnlyHint` false, and `update_contact` and
`delete_contact` report `destructiveHint` true.
A contact tool missing from the listing is the grant rather than a fault — [What a credential may
do](#what-a-credential-may-do) is what decides it, and the startup line for the entry says what it resolved to.

`set_mail_flags` is beside them under the same condition, and it is the one tool in the listing that reports
`readOnlyHint` false with `openWorldHint` true: it changes the owner's mailbox rather than MailFathom's copy of it, so
its effect leaves this process. It is `idempotentHint` true because each value it writes is stated rather than
adjusted, so a second identical call asks for what the first one asked for, and `destructiveHint` true because that
annotation asks whether a tool performs only additive updates: a keyword replacement states the whole set and so takes
off a label the caller did not list, a removal takes named labels off, and clearing either flag removes a value the
message carried. Every one of those is reversible, which is a separate fact the annotation does not answer. An entry
granting
`mailfathom.mail.flags.write` is what puts it in the listing, and an entry that writes no `Permissions` list grants it
like everything else this surface publishes.

**Verify with a credential whose entry writes no grant, or read what that entry granted first.** A listing narrows to
the caller's grant as well as to the deployment, so a credential granted less than the whole surface is served fewer
tools than the paragraph above describes and nothing in the answer says why. Confirm against
[what a credential may do](#what-a-credential-may-do) before treating a short listing as a fault.

`ask_mail` is the fifth, and it appears only while this deployment can answer a question: a chat endpoint declared and
not currently refusing, and an embedding profile whose space a query can be placed in. Its absence from the listing of a
caller granted `mailfathom.mail.ask` is therefore a statement about the deployment rather than a fault — an instance
that declared no chat endpoint never advertises it, and one whose chat provider refused within the last minute withholds
it and offers it again afterwards, so a rotated credential is picked up without a restart. Read the health record for
the chat role before treating that absence as a defect. Such a caller calling it anyway is refused with `56001`, whose
message says whether this deployment answers no questions at all or answers them and currently cannot; a caller whose
grant does not carry `mailfathom.mail.ask` receives the unknown-tool error instead, which is the grant rather than the
deployment and is read from the configured entry rather than from the health record.

A call answers from the local mailbox copy, so what it returns depends on what synchronization has stored rather than on
whether a mail server is reachable. A deployment whose folders have never synchronized answers an empty page whose
`folderFreshness` entries report `wasSynchronized` as false, which is the state to check before treating an empty result as
a statement about the mailbox.

`get_email_content` reads that same local copy: it takes up to ten `storedEmailId` values a listing returned — or one
`threadId`, which resolves to that conversation's messages under the same bound — and never
fetches, so an email whose content is missing or damaged locally is reported as `55001` with a durable repair request
rather than answered with a download. That code arrives inside an otherwise successful result, on the entry for the email
it belongs to, so the emails read beside it are still returned. An operator reading `55001` in the log is reading a
local-consistency problem, not a mail-server one.

`search_emails` reads the lexical index built over that copy rather than the copy itself, so a folder that has
synchronized but whose text extraction has not run yet answers an empty window rather than a failure. `folderFreshness`
does not distinguish that case: it is computed from synchronization checkpoints alone, so such a folder reports a recent
`synchronizedAt` and `wasSynchronized` true exactly as a fully indexed one does. An empty window from a freshly
synchronized folder is therefore worth checking against extraction progress in the server log before it is read as a
statement about the mailbox. Its `retrievalMode` reports `hybrid` on an instance with a healthy embedding profile and
`lexical` otherwise — including for the length of a provider outage on an instance that is otherwise hybrid — while
`semanticSearch` beside it says which of the two a `lexical` answer was: `inactive` on an instance that has activated no
profile, `degraded` on one whose provider or model declaration currently needs an operator, and `available` on one that
is ranking both ways. A request that asks for more than 50 ranked results is refused with `51003` rather than served a
smaller window.

`ask_mail` reads that same retrieval and then spends provider calls on top of it, which makes it the one tool whose
latency and cost an operator has to think about: a run is a conversation, so one question is several calls to the
declared chat endpoint, each under that endpoint's own deadline and resilience budget. What the run may spend across all
of them, and what every run of a period may spend between them, is
[`MailAnswering`](configuration-ai.md#mailanswering); a question the current period has no allowance left for and
a run that reached what one question may cost are both refused with `57001`, whose message says which. A failure inside the run reaches
the client as `54001` and reaches the log as the chat-provider code it actually was — `71001` for a refused credential,
`72001` for an endpoint that did not answer, `73001` for a call that produced no text. Nothing on this path logs the
question, the answer, the query the model wrote, or any retrieved passage; what a record carries is the endpoint alias,
how many passages were retrieved, and how many messages they came from.
