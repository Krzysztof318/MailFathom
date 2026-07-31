# The MCP endpoint and what protects it

The MCP endpoint is how an agent reaches MailMcp. This page records what enabling it means operationally, what a client
has to present to reach it, which browser origins it answers, and which client applications it accepts a certificate
from. The tools it serves are described in `docs/features/mcp-tools.md`.

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

## Client certificates

Mutual TLS is off unless `McpEndpoint:ClientCertificateProfiles` names at least one client. A profile identifies a client
*application* — the ChatGPT connector, a reporting service, a workstation fleet — and it composes with `ApiKey` or
`None` rather than replacing either. A certificate says which program is calling; it never says on whose behalf.

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
