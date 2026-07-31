# The MCP endpoint and what protects it

The MCP endpoint is how an agent reaches MailMcp. This page records what enabling it means operationally, what a client
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
    "Authentication": "ApiKey",
    "ApiKeys": [
      {
        "Name": "workstation",
        "SecretReference": "systemd-credential:mailmcp-mcp-workstation-key"
      }
    ]
  }
}
```

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Whether the Streamable HTTP endpoint is mapped at all |
| `Authentication` | none — an enabled endpoint must state one | `ApiKey` or `None` |
| `ApiKeys` | empty | The keys a client may present, each a named secret with its own lifetime |
| `Cors.AllowedOrigins` | `["*"]` | The browser origins served: `*` for every one, a list for exactly those, an empty list for none |
| `ClientCertificateProfiles` | empty | The client applications whose certificates are accepted, each with its own authorities and expected names |
| `RateLimiting` | bounded — see [Rate limiting](#rate-limiting) | How much traffic the endpoint accepts, per process and per client |
| `Https.Endpoints` | empty | The domains MailMcp terminates TLS for; empty serves the endpoint over the host's ordinary listener |

The endpoint always answers on **`/mcp`**, which is a constant rather than a setting. An MCP client is configured with a
server URL, so a deployment could only move the path in step with every client pointed at it — the configurability would
buy nothing and add one more way for the surface to end up reachable somewhere nobody is looking. Put it behind a reverse
proxy if it has to appear elsewhere.

The transport is always **stateless**, for the same kind of reason. Every MailMcp tool answers one request from the local
mailbox copy and sends nothing back on its own, so a session would carry no state and only give a client something to lose
across a restart. Stateless is also what MCP deployments assume today. Should a tool that pushes notifications arrive, that
is a change to this surface rather than a switch an operator was expected to have found.

The section is bound strictly, so an unrecognized key fails startup instead of being ignored: a misspelled `Enabeld` would
otherwise leave the endpoint off while an operator believed they had switched it on. The whole section is read once while
the host is being composed, because whether an endpoint exists and what guards it are part of the application's routing.
Changing any of it takes effect on restart; it does not participate in configuration reload. The *material* behind a
configured key is a separate matter and is re-read on every request, which is what lets a key rotate without one.

## Authentication

**An enabled endpoint must name a mode.** There is no default, and absence is a startup failure rather than a posture:

```text
McpEndpoint:Authentication — an enabled endpoint must state 'ApiKey' or 'None'; there is no default, because an
unauthenticated endpoint serves every synchronized mailbox to anything that can reach it.
```

That is the point of having no default. A misspelled key, a section that failed to bind, and an operator who simply forgot
would otherwise all end the same way — with a mailbox served to anything that can open a connection — and none of them
would look like a decision anybody made.

### `ApiKey`

A client presents a key as an ordinary HTTP bearer credential:

```http
POST /mcp HTTP/1.1
Authorization: Bearer <the key>
```

Every MCP method and response path is covered — the JSON-RPC post, the stream it reads back, and the delete that ends a
session — and the check runs before any protocol handling. The readiness response and the health endpoints are not
covered; they carry no mailbox data and a probe has no credential to present.

Each entry is an ordinary [named secret](secret-provisioning.md#the-secret-block): the key material is provisioned like
every other credential, the `Name` is what a diagnostic and an audit record correlate on, and the `Lifetime` is enforced.
Several entries are supported, which is what makes rotation an overlap rather than an outage:

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": "ApiKey",
    "ApiKeys": [
      {
        "Name": "workstation",
        "SecretReference": "systemd-credential:mailmcp-mcp-workstation-key",
        "Lifetime": "NoLimit"
      },
      {
        "Name": "chatgpt-connector",
        "SecretReference": "file:/run/secrets/mailmcp-mcp-chatgpt-key",
        "Lifetime": "2027-01-31T00:00:00Z"
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
WWW-Authenticate: Bearer realm="MailMcp"
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

### `None`

`None` is the unauthenticated posture, and it is reachable only by writing it down. It never arises from a missing key, a
failed secret resolution, or any other fallback:

```json
{
  "McpEndpoint": { "Enabled": true, "Authentication": "None" }
}
```

Configuring keys alongside it is a startup failure rather than a silent no-op, because keys nothing checks are a deployment
believing it is protected — which is worse than one that knows it is not.

Whenever an enabled endpoint runs under `None`, startup logs one warning:

```text
warn: MailMcp.Host.Hosting.McpTransportAuthenticationWarning
      The MCP endpoint is enabled on /mcp with authentication set to None, so anything that can reach this address can
      read the synchronized mailboxes. Configure API keys instead unless the address is reachable only from this machine
      or from a network you control. Neither an origin policy nor a client certificate substitutes for this: the first
      restricts which page a browser will let call, the second names the application calling, and neither identifies the
      person whose mail is served.
```

Leaving `Cors.AllowedOrigins` at its default of `["*"]` under `None` — which is what makes the endpoint work behind a
reverse proxy or on a trusted network without further configuration — adds a second warning rather than a startup
failure:

```text
warn: MailMcp.Host.Hosting.McpTransportAuthenticationWarning
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
  authenticating reverse proxy are all outside MailMcp and all appropriate.
- **Treat the whole surface as read-only but not harmless.** The tools cannot send, delete, move, or mark mail as read, so
  the exposure is disclosure rather than modification. Disclosure of a mailbox is enough.

### What API keys are not

A key identifies a *client*, not a person. Every tool call still resolves the accounts the configured owner controls and
refuses anything outside them, so authorization is unchanged by this and always was in place. What the later OAuth 2.1
work adds is where the owner's identity comes from — configuration today, an authenticated token then — rather than a
first authorization check. A shared bearer credential also has the properties every shared bearer credential has: it does
not expire on its own unless you give it a lifetime, it cannot be revoked for one user without revoking it for the client,
and anything that reads it can use it.

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

## HTTPS and your own domain

**MailMcp terminates no TLS by default.** With `Https.Endpoints` empty the endpoint is served over whatever listener the
host is already configured with, which is clear-text HTTP unless something in front supplies HTTPS. That default is kept
deliberately, because two ordinary deployments run it:

- **Local development**, where the endpoint is reachable only from the machine it runs on.
- **Behind a TLS-terminating reverse proxy**, where the proxy already holds your certificate and a second TLS layer
  inside the trust boundary protects nothing.

Neither of those is something MailMcp can detect, so the clear-text posture is reported rather than refused. Whenever an
enabled endpoint terminates no TLS, startup logs one warning:

```text
warn: MailMcp.Host.Hosting.McpTransportEncryptionWarning
      The MCP endpoint is enabled on /mcp and no HTTPS profile is configured, so it is served over whichever listener
      this host was started with — clear text unless that listener or something in front of this process supplies
      HTTPS. On a clear-text hop anything on the network path can read the API key a client presents and every message
      the tools return, and a client certificate never arrives at all. This is the expected posture for local
      development and for a deployment behind a TLS-terminating reverse proxy; anywhere else, configure
      McpEndpoint:Https:Endpoints so this process presents your domain's certificate itself.
```

It fires whatever authentication mode is configured. An API key travels in a request header, so on a clear-text hop the
credential is as readable as the mail it protects.

### One domain

A profile names the domain clients connect to, the socket to bind, and where the certificate comes from. A PKCS#12 bundle:

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": "ApiKey",
    "ApiKeys": [{ "Name": "workstation", "SecretReference": "systemd-credential:mailmcp-mcp-workstation-key" }],
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
              "SecretReference": "file:/etc/mailmcp/tls/mail.example.com.pfx",
              "Password": {
                "Name": "public-bundle-password",
                "SecretReference": "systemd-credential:mailmcp-tls-bundle-password"
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
      "SecretReference": "file:/etc/mailmcp/tls/fullchain.pem"
    },
    "PrivateKey": {
      "Name": "public-key",
      "SecretReference": "file:/etc/mailmcp/tls/privkey.pem"
    }
  }
}
```

State one or the other. Configuring both is a startup failure, because which of them supplies the identity would
otherwise be decided by nothing you wrote. The `CertificateChain` value is the whole `fullchain.pem`: its first
certificate is the identity and the rest are the intermediates MailMcp presents after it, so a client that does not
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

Binding a listener explicitly replaces whatever URLs the host was otherwise configured with, so **no clear-text listener
stays open behind an HTTPS profile**. That applies to everything this process serves, the health endpoints included,
because one Kestrel serves them all. There is no mixed posture in which the MCP route is protected while a second
listener offers the same mailbox without protection.

`ASPNETCORE_URLS`, `--urls`, and the Aspire-issued endpoints are therefore all ignored once a profile is configured.
A deployment that needs a plain HTTP health endpoint beside HTTPS should keep the clear-text posture and terminate TLS
at a reverse proxy instead.

Kestrel's own `Kestrel:Endpoints` section is the one listener a profile cannot displace: those endpoints are bound
alongside the ones bound in code rather than replaced by them, so an endpoint configured there would keep its socket and
serve the same MCP route without the TLS a profile adds. Configuring both is therefore a startup failure that names
each side, because only an operator can decide which one the deployment meant:

```text
Kestrel:Endpoints:Http — a Kestrel endpoint is configured beside McpEndpoint:Https, and Kestrel binds both: this
listener would stay open alongside the HTTPS profiles and serve the same MCP endpoint without the TLS they were
configured to add. Remove the endpoint, or remove the HTTPS profiles and let this listener serve the endpoint.
```

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
        "ServerCertificate": { "Bundle": { "Name": "public-bundle", "SecretReference": "file:/etc/mailmcp/tls/public.pfx" } }
      },
      {
        "Name": "connector",
        "Domain": "connector.example.com",
        "Port": 443,
        "MinimumTlsVersion": "Tls13",
        "ServerCertificate": { "Bundle": { "Name": "connector-bundle", "SecretReference": "file:/etc/mailmcp/tls/connector.pfx" } }
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

A private key is imported into memory only. MailMcp never writes one to an operating-system key store as a side effect of
loading it. A certificate chain is public material and may be supplied inline under an inline interpretation mode; a
PKCS#12 bundle may not, because it is binary and has no faithful representation in a configuration value.

Startup records what each profile presents and when it stops working:

```text
info: MailMcp.Host.Security.McpServerCertificateStore
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
MailMcp. It has no ACME client and issues nothing. Startup only refuses a `Domain` that could not be a DNS name at all —
an IP address, a wildcard, a name with characters a DNS name cannot carry, or a name a second profile already publishes.
An internationalized domain is configured in its punycode A-label form, because that is what a client sends and what a
certificate's names carry.

## Client certificates

Mutual TLS is off unless `McpEndpoint:ClientCertificateProfiles` names at least one client. A profile identifies a client
*application* — the ChatGPT connector, a reporting service, a workstation fleet — and it composes with `ApiKey` or
`None` rather than replacing either. A certificate says which program is calling; it never says on whose behalf.

**A client certificate only arrives over a TLS connection this process terminated.** That is either an HTTPS profile
from the section above, or a listener the host was started with that serves HTTPS of its own — a `https://` entry in
`ASPNETCORE_URLS` with a certificate configured under Kestrel's own `Certificates` section. Where TLS is terminated by a
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
    "Authentication": "ApiKey",
    "ClientCertificateProfiles": [
      {
        "Name": "chatgpt-connector",
        "Requirement": "Optional",
        "TrustAnchors": [
          {
            "Name": "openai-connectors-ca",
            "SecretReference": "file:/etc/mailmcp/openai-connectors-ca.pem"
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

- **The certificate comes from the TLS connection.** No header is read, however a proxy in front of MailMcp spells one.
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
HTTP presents none: an `Optional` profile then identifies nothing, and a `Required` profile refuses every request.
Serving MCP over HTTPS with operator-provided certificates is
[#142](https://github.com/Krzysztof318/MailMcp/issues/142); until it lands, terminate TLS in front of MailMcp only if you
are not using these profiles, because a proxy that terminates TLS is exactly what stops the certificate from arriving.

### The ChatGPT connector profile

OpenAI publishes a managed client certificate for its MCP connector, and the profile above is the shape it asks for: the
leaf chains to the published OpenAI Connectors mTLS certificate authority, is valid for client authentication, and
carries the subject alternative name `mtls.prod.connectors.openai.com`. Nothing pins the leaf, because that certificate
rotates and a pinned fingerprint would turn a routine rotation into an outage.

The authority itself is **supplied by you**, as an ordinary secret reference. No third-party certificate ships in this
repository, which is what keeps OpenAI rotating their authority an operator change rather than a MailMcp release. Fetch
the current certificate from OpenAI's published location, provision it like any other trust anchor, and add the
successor beside it while a rotation is in flight.

`Requirement` is `Optional` above on purpose. A `Required` profile refuses every request that presents no certificate,
which includes the workstation reaching the same endpoint with an API key alone; state `Required` only once every client
of the deployment holds a certificate.

**This is not complete ChatGPT production authentication.** OpenAI's connector expects an OAuth 2.1 authorization flow
alongside the client certificate, and that is later work. What mTLS provides here is confidence about which application
is connecting, which is worth having and is not the same thing as knowing whose mailbox is being read.

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

They will need MailMcp to terminate TLS itself, which is what the HTTPS profiles above make possible: a client
certificate is presented during the handshake, and a deployment that terminates TLS at a reverse proxy has no handshake
here to read one from. Certificate-like HTTP headers are ignored and will stay ignored; trusting a proxy to assert a
client's identity is its own reviewed design, not something a header enables.

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
info: MailMcp.Host.Hosting.McpRateLimitingStartupReport
      The MCP endpoint on /mcp serves at most 20 requests at once across every client, queueing 0 beyond that, and
      allows each client a burst of 60 requests restored at 60 every 00:01:00, queueing 0 of its requests beyond that.
```

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

Both are MailMcp's own configured identities — never the credential, and never anything the certificate itself carried.
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

Readiness, liveness, and the root endpoint are outside all of this and keep answering while the MCP endpoint is refusing.

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
`Microsoft.AspNetCore.RateLimiting` metrics, tagged with the policy name. Nothing MailMcp adds records a client name, an
address, an origin, a credential, or anything from a request or its response.

### What this is not

**The limits are counted in this process alone.** A deployment running several instances enforces them once per process
rather than once in total; there is no shared state and no coordination between them. Put the total behind a reverse
proxy or load balancer that bounds it if that matters.

**It is not DDoS protection.** It bounds what one client can take from the process it is talking to. A flood arriving
from many sources is a job for a WAF, a CDN, or a hosting provider's own protection, and none of that is in MailMcp.

**It bounds what the endpoint serves, not what the server spends deciding whether to serve it.** The limiter runs behind
the origin check, the certificate check, and authentication, so the work those do — reading every configured trust anchor
and comparing every configured key, on every request — happens before a permit is taken. The order is what makes the
per-client limit possible at all: run ahead of authentication and there is no client to count against, and every request
shares the anonymous bucket. What a bad credential costs the sender is therefore a partition it shares with every other
unidentified request, not the work the server already did to refuse it. Bounding connections that never authenticate is a
job for whatever fronts the process.

Turning the limits off is an explicit value and costs one startup warning:

```text
warn: MailMcp.Host.Hosting.McpRateLimitingStartupReport
      The MCP endpoint is enabled on /mcp with rate limiting turned off, so one client can hold every database
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

`get_email_content` reads that same local copy: it takes a `storedEmailId` a listing returned and never fetches, so an
email whose content is missing or damaged locally is answered with `55001` and a durable repair request rather than with
a download. An operator reading `55001` in the log is reading a local-consistency problem, not a mail-server one.

`search_emails` reads the lexical index built over that copy rather than the copy itself, so a folder that has
synchronized but whose text extraction has not run yet answers an empty window rather than a failure. `folderFreshness`
does not distinguish that case: it is computed from synchronization checkpoints alone, so such a folder reports a recent
`synchronizedAt` and `wasSynchronized` true exactly as a fully indexed one does. An empty window from a freshly
synchronized folder is therefore worth checking against extraction progress in the server log before it is read as a
statement about the mailbox. Its `retrievalMode` reports `lexical`, and a request that asks for more than 50 ranked
results is refused with `51003` rather than served a smaller window.
