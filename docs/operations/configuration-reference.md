# Configuration reference

<!-- describes: src/**/*Options.cs, src/Host/Configuration/** -->

Every user-settable option, in one place, checked against the options classes that bind it. Each section's table
states the key, its type, the value a deployment gets by writing nothing, the constraint startup enforces, and what a
change needs to take effect. The prose around a setting group — what it means, why it is shaped that way, how to
choose a value — lives on the page each section links; this page is the inventory.

## How to read the tables

**Keys.** Written in configuration-section form. As an environment variable, `:` becomes `__` and a list index is a
numbered segment: `MailSynchronization:Accounts:0:Host` is `MailSynchronization__Accounts__0__Host`. Where the
configuration comes from, and which source wins, is [configuration sources](configuration-sources.md).

**Types.** A `TimeSpan` binds from `hh:mm:ss` (`"00:05:00"` is five minutes; a leading `d.` adds days). A date binds
as `yyyy-MM-dd`, an instant as ISO 8601 with an explicit offset. An enum binds by member name, and a **secret block**
is the three-field shape [secret provisioning](secret-provisioning.md#the-secret-block) defines:

```json
{ "Name": "imap-primary-password", "SecretReference": "file:/etc/mailfathom/secrets/imap-primary-password", "Lifetime": "NoLimit" }
```

`Name` is the identity diagnostics use, `SecretReference` is `<scheme>:<target>` with the schemes
`systemd-credential:`, `file:`, `env:`, and `plaintext:`, and `Lifetime` is `NoLimit` (the default) or the ISO 8601
instant the material stops being accepted. Trust-anchor and certificate blocks nest a fourth field, `Password`, itself
a secret block, for protected PKCS#12 bundles.

**Change.** What ADR 0002 classifies for the group:

- *restart* — the section is read while the host composes itself; edit it, then restart.
- *reload* — a changed value is validated and, if sound, adopted by the next operation without a restart; a rejected
  candidate leaves the running configuration in force. Reload of a file-shaped source has caveats of its own under
  Kubernetes — see [configuration sources](configuration-sources.md#reload).

Whatever the classification, the **material behind a secret reference is read per use**: rotating a password, key, or
certificate behind an unchanged reference needs no restart and no reload. [Secret rotation](secret-rotation.md) walks
each case.

**Validation.** Every MailFathom section below is bound strictly: a key the section does not define fails startup
naming it, so a typo cannot silently leave a default in force. Values are validated on start, and a violated
constraint fails startup with the configuration path in the message. The two exceptions are the framework-shaped
entries — `Logging` and `ConnectionStrings` — and the single-key `Secrets:Interpretation`, which is read with a
default rather than bound as a section.

## `ConfigurationSources`

Names JSON configuration provisioned outside the application — a mounted ConfigMap, a systemd drop-in.
[Configuration sources](configuration-sources.md) is the page.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ConfigurationSources:Directory` | string | unset | Must exist when named | restart |
| `ConfigurationSources:File` | string | unset | Must exist when named | restart |

The *content* of files that existed at startup reloads; adding or removing a file is a restart.

## `Secrets`

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Secrets:Interpretation` | enum | `ReferenceOnly` | `ReferenceOnly`, `ReferenceOrInline`, `InlineOnly` | restart |

Under the default, a plain-text value where a reference belongs fails startup instead of authenticating.
[Interpretation modes](secret-provisioning.md#interpretation-modes) records when the other two are appropriate;
development keeps `ReferenceOrInline` so `plaintext:` references stay convenient.

## `MailSynchronization`

Whether and how mailboxes are synchronized. [IMAP synchronization](../features/imap-synchronization.md#configuration)
explains the model. The section reloads **per operation** — a run takes one validated snapshot when it begins, so a
changed account list, bound, or policy is adopted at the next run rather than mid-run — except the four values that
shape the coordinator loop itself, which are read once at start and marked *restart* below.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailSynchronization:Enabled` | bool | `false` | Enabled requires at least one account | restart |
| `MailSynchronization:Interval` | TimeSpan | `00:05:00` | 10 s – 1 day; measured end-of-run to start-of-run | restart |
| `MailSynchronization:MaxFailureBackoff` | TimeSpan | `00:30:00` | 10 s – 1 day, and never below `Interval` | reload |
| `MailSynchronization:MaxConcurrentAccounts` | int | `4` | 1 – 100 | restart |
| `MailSynchronization:MaxConcurrentFoldersPerAccount` | int | `1` | 1 – 20 | reload |
| `MailSynchronization:ShutdownDrainTimeout` | TimeSpan | `00:00:10` | 0 – 2 min | restart |
| `MailSynchronization:MaxMetadataBatchSize` | int | `100` | 1 – 1000 | reload |
| `MailSynchronization:MaxRawMimeBytes` | long | `26214400` (25 MiB) | 1024 – 104857600; larger messages are stored without content | reload |
| `MailSynchronization:MaxMetadataBatchesPerRun` | int | `10` | 1 – 1000 | reload |
| `MailSynchronization:MaxReconciledEmailsPerRun` | int | `500` | 1 – 10000 | reload |
| `MailSynchronization:MaxMimePartCount` | int | `1000` | 1 – 100000 | reload |
| `MailSynchronization:MaxMimeNestingDepth` | int | `30` | 1 – 1000 | reload |
| `MailSynchronization:MaxExtractedTextCharacters` | int | `100000` | 1000 – 200000; the ceiling keeps the search vector inside PostgreSQL's limit | reload |
| `MailSynchronization:PushRenewalInterval` | TimeSpan | `00:20:00` | 1 min – 29 min; the lifetime of one `IDLE` command, **not** a polling cycle — the ceiling is what RFC 2177 mandates | reload |
| `MailSynchronization:MaxConsecutivePushFailures` | int | `3` | 1 – 100 | reload |
| `MailSynchronization:PushDegradationPeriod` | TimeSpan | `00:15:00` | 10 s – 1 day | reload |
| `MailSynchronization:MaxSubscribedFolders` | int | `20` | 1 – 100; how many folders one push subscription may name on a server supporting `NOTIFY`, the rest synchronizing on the account's interval | reload |

### One account — `MailSynchronization:Accounts:<n>`

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:AccountId` | string | — | Required; unique across accounts after normalization | reload |
| `…:Host` | string | — | Required when synchronization is enabled | reload |
| `…:Port` | int | `993` | 1 – 65535 | reload |
| `…:UserName` | string | — | Required when synchronization is enabled; an identifier, not a secret | reload |
| `…:Secrets:Password` | secret block | unset | Required when the permitted mechanisms include any password mechanism; must resolve at startup | reload; material per connection |
| `…:Mode` | enum | `Polling` | `Polling`, `Push`; push holds one connection open per account on a server supporting `NOTIFY`, and one per folder on a server offering only `IDLE` | reload; the next run adopts it |
| `…:EarliestEmailReceivedDate` | date | unset (everything) | Not in the future (compared in UTC) | reload |
| `…:RemotelyDeletedEmailDisposition` | enum | `RetainTombstone` | `RetainTombstone`, `EraseLocalCopy` | reload; governs disappearances observed from then on |
| `…:Folders` | list | inbox by role | Aliases unique; each entry below | reload |

A folder entry names `Alias` (required — your stable name for the folder) and **exactly one** of `RemotePath` (the
server's own path) or `SpecialUse` (a role discovery resolves: `Inbox`, `Archive`, `Drafts`, `Sent`, `Junk`, `Trash`,
`All`, `Flagged`, `Important`). Configuring no folder synchronizes the inbox by role.

### OAuth — `…:OAuth`

Read only when the account's permitted mechanisms include `XOAUTH2` or `OAUTHBEARER`. An account that authenticates
with a password leaves the whole block unset, and configuring it anyway fails startup rather than provisioning
credentials nothing can use. [Mailbox OAuth](mailbox-oauth.md) covers where each value comes from.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:OAuth:Grant` | string | — | `refresh_token` or `client_credentials` | reload |
| `…:OAuth:TokenEndpoint` | string | — | Absolute HTTPS address; no opt-in exists for `http` | reload |
| `…:OAuth:ClientId` | string | — | Required; an identifier, not a secret | reload |
| `…:OAuth:Scope` | string | — | Space-delimited, as RFC 6749 defines it | reload |
| `…:OAuth:PublicClient` | bool | `false` | Set when the application is registered as a public client, which holds no secret | reload |
| `…:OAuth:ClientSecret` | secret block | unset | Required unless `PublicClient` is `true`, and refused alongside it; must resolve at startup | reload; material per token request |
| `…:OAuth:RefreshToken` | secret block | unset | Required by `refresh_token`; absent for `client_credentials` | reload; material per token request |

### Transport security — `…:TransportSecurity`

[The rules](../features/imap-synchronization.md#transport-security) are the domain's; every weakening is an explicit
opt-in, and unsafe combinations fail startup.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:ConnectionSecurity` | enum | `TlsOnConnect` | `Auto`, `TlsOnConnect`, `StartTlsRequired`, `StartTlsWhenAvailable`, `None`; anything but the two guaranteed-TLS modes requires `AllowInsecureConnection` | reload |
| `…:PermittedAuthenticationMechanisms` | string list | `PLAIN`, `LOGIN` | Supported SASL names, including `XOAUTH2` and `OAUTHBEARER`; an unordered allow-list, the client picks the strongest that survives | reload |
| `…:AllowInsecureConnection` | bool | `false` | Opt-in for modes that can leave the channel unencrypted | reload |
| `…:AllowClearTextAuthenticationOverUnencryptedConnection` | bool | `false` | Opt-in on top of the above | reload |
| `…:CertificateTrust` | enum | `SystemTrustStore` | `SystemTrustStore`, `AdditionalTrustedAuthority` | reload |
| `…:TrustedCertificateAuthority` | secret block | unset | Required by, and only valid with, `AdditionalTrustedAuthority` | reload; material per connection |

Certificate validation itself cannot be disabled; a private server is supported by trusting its authority.

## `Persistence` and the connection string

Where the local copy lives. The connection settings travel through the validated snapshot, so repointing them reaches
the next physical connection without a restart; the remaining settings are read while the host composes itself.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ConnectionStrings:mailfathom` | string | `Host=localhost;Database=mailfathom;Username=mailfathom` | Carries no password | reload (new connections) |
| `Persistence:ConnectionString` | secret block | unset | Replaces `ConnectionStrings:mailfathom` entirely when set | reload (new connections) |
| `Persistence:Password` | secret block | unset | A present block must carry a reference | reload (new connections); material per connection |
| `Persistence:MaximumConcurrencyCommitAttempts` | int | `2` | 1 – 10; counts the first attempt | restart |
| `Persistence:CommandTimeoutSeconds` | int | `30` | 1 – 600; bounds one command, not one unit of work | restart |
| `Persistence:TextSearchConfiguration` | string | `simple` | A stock PostgreSQL text search configuration (`simple`, `english`, `german`, …) | restart — **and it is part of the schema**: the value is compiled into the index, startup fails with `32003` on a mismatch, and changing it means regenerating the migration and rebuilding the search documents |

Repointing a reference or editing the connection string reloads; changing *which* setting supplies the credential —
moving a password out of the connection string into `Persistence:Password`, or back — is refused on reload and needs a
restart, because the connection pool attaches its password provider once.

## `DataEncryption`

The key ring every value MailFathom seals at rest is sealed under. A configuration root of its own rather than a
section of `Persistence`, because the database is the first thing sealed under it and there is no reason it is the
last. [ADR 0005](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md) records the whole decision, and
[secret provisioning](secret-provisioning.md) states how the material is generated and referenced.

An absent section is a valid deployment that seals nothing. Configuring the section makes every rule below apply.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `DataEncryption:ActiveKeyId` | string | unset | Must name one of `Keys`; required once any key is configured, and refused when none is | reload |
| `DataEncryption:Keys:<n>:KeyId` | string | — | Up to 64 letters, digits, dots, dashes, and underscores, beginning with a letter or a digit; unique within the ring | reload |
| `DataEncryption:Keys:<n>:Material` | secret block | — | Base64 decoding to exactly 32 bytes, generated with `openssl rand -base64 32` | reload; material per operation |

`KeyId` is stored beside every value the key seals, so it is chosen once and never edited — renaming it orphans every
value already carrying the previous spelling. The operator's own label for a key is its material's `Name`, which every
secret block requires; there is no second name on the entry.

The ring holds several keys so that rotation needs no downtime: move `ActiveKeyId` to the new key, leave the previous
key configured, and every value still carrying it keeps opening under it. Removing a key the database still references
makes those values unopenable, and the failure appears at the next read rather than at the edit.

## `MailboxSearch`

The deployment-wide privacy bound on what a search result may quote. [Lexical email
search](../features/lexical-email-search.md) records how snippets are cut.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailboxSearch:SnippetsPerEmail` | int | `3` | 1 – 10 | restart |
| `MailboxSearch:WordsPerSnippet` | int | `24` | 4 – 100 | restart |

## `EmailContent`

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `EmailContent:MaxBodyCharacters` | int | `100000` | 1000 – 1000000; each body representation is truncated to it, explicitly | restart |
| `EmailContent:MaxCharactersPerRead` | int | `200000` | 2000 – 2000000, and at least twice `MaxBodyCharacters`; the body characters one call returns across every email it names | restart |

## `MailExtractionBackfill`

The worker that extracts text for messages stored before extraction existed or before a limit was raised.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailExtractionBackfill:Enabled` | bool | `true` | — | restart |
| `MailExtractionBackfill:Interval` | TimeSpan | `00:00:30` | 1 s – 1 h | restart |
| `MailExtractionBackfill:BatchSize` | int | `50` | 1 – 500 | restart |
| `MailExtractionBackfill:MaxBatchesPerRun` | int | `10` | 1 – 1000 | restart |

## The application listener

The socket that serves `/` and the MCP endpoint at `/mcp`. It is the one listener MailFathom configures nothing of its
own for: `McpEndpoint` decides whether the protocol surface is served and what guards it, never where it is served, so
a deployment that enables the endpoint and configures no address serves it here. The administrative and probe
listeners are the opposite — each binds a socket of its own from its own section, and neither answers the other's
paths.

**It is clear-text HTTP unless a deployment says otherwise.** Three things change that: a TLS-terminating reverse
proxy in front of the process, an `https://` address configured below, or
[`McpEndpoint:Https:Endpoints`](#tls-termination--mcpendpointhttpsendpointsn), which replaces this listener rather than
adding to it.

### Where its address comes from

| Source | Value | Wins over |
| --- | --- | --- |
| `Kestrel:Endpoints:<name>:Url` | An address per named endpoint | Everything below; naming any endpoint makes Kestrel ignore the two rows under it |
| `ASPNETCORE_URLS` | `;`-separated addresses | `ASPNETCORE_HTTP_PORTS` |
| `ASPNETCORE_HTTP_PORTS` / `ASPNETCORE_HTTPS_PORTS` | `;`-separated ports, each expanded to every interface | Nothing |
| Nothing configured | `http://localhost:5000` | — |

The default binds loopback alone, so a process installed natively and started without an address is reachable from its
own machine and nowhere else. The container image and the Helm chart both set `ASPNETCORE_HTTP_PORTS=8080` instead, so
this default is the native-process path's.

Kestrel's own fallback would add `https://localhost:5001` whenever an ASP.NET Core development certificate happens to
be installed. MailFathom restates only the clear-text half, so what the process listens on is decided by what was
configured rather than by what is installed on the machine — it never serves a listener out of a development
certificate. That restatement happens while the [probe listener](health-endpoints.md#the-application-listener-is-preserved)
is being opened, which is the default; a deployment that sets `HealthEndpoints:Enabled` to `false` restates nothing and
is left with Kestrel's own defaults, development certificate included.

### `Kestrel:Endpoints:<name>`

Kestrel's own section, bound by the framework rather than by MailFathom, and the way to give this listener TLS without
moving the MCP endpoint onto profiles of its own. `<name>` is any name; it appears in the "Now listening on" line and
in nothing else. The full contract is
[Kestrel endpoint configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0);
the keys are listed here because two MailFathom rules depend on this section existing.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Url` | string | — | Required; an endpoint carrying no `Url` binds nothing. This is the key MailFathom itself reads, for the port collision checks and the two rules below | restart |
| `…:Protocols` | string | `Http1AndHttp2` | `Http1`, `Http2`, `Http3`, `Http1AndHttp2`, `Http1AndHttp2AndHttp3` | restart |
| `…:SslProtocols` | string list | the platform's | The `SslProtocols` names, per the linked contract; MailFathom's own sections take `Tls12` and `Tls13` alone, and this one does not | restart |
| `…:ClientCertificateMode` | string | `NoCertificate` | `NoCertificate`, `AllowCertificate`, `RequireCertificate` | restart |
| `…:Certificate` | object | — | Kestrel's own certificate block — a file through `Path`, or a store through `Subject` — which is **not** the [secret block](secret-provisioning.md#the-secret-block) the MailFathom sections take, so material for it is provisioned as Kestrel documents rather than through a `SecretReference` | restart |
| `…:Sni` | object | — | Kestrel's server-name mapping | restart |

Two rules are MailFathom's rather than Kestrel's:

- **An endpoint here beside `McpEndpoint:Https` fails startup**, naming both sides. Kestrel binds configured endpoints
  alongside the ones bound in code rather than replacing them, so the configured listener would stay open and serve the
  same MCP route without the TLS the profiles were configured to add. Only an operator can decide which of the two the
  deployment meant. [Configuring a profile takes over the host's listeners](mcp-endpoint.md#configuring-a-profile-takes-over-the-hosts-listeners)
  has the message.
- **A port this listener binds is refused to the administrative and probe listeners**, checked against whichever source
  is the one actually binding — the MCP HTTPS profiles when they have replaced this listener, the endpoints named here
  when they exist, and the URL-shaped addresses otherwise. A collision is reported against the section that asked for
  it rather than left to fail later as an address-in-use error naming a socket.

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

`X-Forwarded-For` is never read, so the peer MailFathom observes stays the one that opened the connection, and
`McpEndpoint:OAuth:Resource` stays a configured value rather than anything derived from a header.

## `McpEndpoint`

Whether the protocol surface is served and what a client must present. The whole section is **restart** — it decides
routing and listeners — while key and certificate material is read per request or per handshake. Where it is served is
[the application listener](#the-application-listener) unless `Https:Endpoints` moves it.
[The MCP endpoint](mcp-endpoint.md) is the page, section by section.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `McpEndpoint:Enabled` | bool | `false` | — | restart |
| `McpEndpoint:Authentication` | flag set | `None` | `ApiKey`, `OAuth`, both comma-separated, or `None`; `None` warns at startup | restart |
| `McpEndpoint:ApiKeys` | list of secret blocks | empty | Required non-empty when `ApiKey` is named; refused when configured while it is not | restart; material per request |

### OAuth — `McpEndpoint:OAuth`

MailFathom is a protected resource only; an external authorization server signs users in.
[`OAuth`](mcp-endpoint.md#oauth) records what a token must prove.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Resource` | string | — | Required; the canonical `https` URL clients reach this endpoint at — behind a proxy, the proxy's public URL | restart |
| `…:RequiredScopes` | string list | empty | Scopes every access token must carry; empty accepts any token the configured servers issued for this resource | restart |
| `…:AuthorizationServers:<n>:Name` | string | — | Required; the identity diagnostics use | restart |
| `…:AuthorizationServers:<n>:Issuer` | string | — | Required; a well-formed `https` issuer, compared against `iss` exactly | restart |
| `…:AuthorizationServers:<n>:MetadataAddress` | string | unset | An absolute `https` URL on the issuer's own host; overrides issuer-derived discovery | restart |
| `…:AuthorizationServers:<n>:AuthorizedSubjects` | string list | — | At least one; a token whose `sub` is not listed is refused, so every user the server can sign in does not automatically read this mailbox | restart |

### Browser origins — `McpEndpoint:Cors`

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:AllowedOrigins` | string list | absent = every origin | `*` for every origin, a list for exactly those, an empty list for none | restart |

The default is deliberately the permissive one — an `Origin` header only exists in browsers, and a native client is
unaffected — but a deployment reachable from a browser should narrow it.
[CORS and the `Origin` header](mcp-endpoint.md#cors-and-the-origin-header) explains what the check does and does not
protect.

### TLS termination — `McpEndpoint:Https:Endpoints:<n>`

Empty by default, which serves the endpoint over [the application listener](#the-application-listener). Configuring any
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

One clear-text listener whose only answer is a `308` to the address the profiles above are served at, so enabling TLS does
not read as an outage to a client nobody repointed yet. It maps no route and runs no credential check, and it exists only
while `McpEndpoint:Https:Endpoints` names a profile. Writing this section without one fails startup.
[Redirecting a client still pointed at `http://`](mcp-endpoint.md#redirecting-a-client-still-pointed-at-http) records what
a redirect does and does not protect.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Enabled` | bool | `true` | Honored only while an HTTPS profile is configured | restart |
| `…:BindAddress` | string | `0.0.0.0` | An IP address; `::` binds IPv6 | restart |
| `…:Port` | int | `8080` | 1 – 65535; the resulting address and port bound by no HTTPS profile in this section, and the port bound by no other listener in the process | restart |

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

### Rate limiting — `McpEndpoint:RateLimiting` and `AdminEndpoint:RateLimiting`

The one endpoint subsection where every value has a product default, so an enabled endpoint is bounded whether or not
anyone wrote a number. Both endpoints carry it, with the same keys, defaults, and validation, and configure it
independently: neither one's traffic reaches the other's limits. [Rate limiting](mcp-endpoint.md#rate-limiting) records
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

## `AdminEndpoint`

Whether the administrative surface the `mfctl` command reaches is served, and what a client must present. Its own
listener, its own credentials, and its own authorization servers: a key configured under `McpEndpoint` authenticates
nothing here, and the reverse holds. The whole section is **restart**, while key and certificate material is read per
request or per handshake. [Administering a deployment](admin-endpoint.md) is the page.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `AdminEndpoint:Enabled` | bool | `false` | — | restart |
| `AdminEndpoint:BindAddress` | string | `0.0.0.0` | An IP address; used only when no HTTPS profile is configured | restart |
| `AdminEndpoint:Port` | int | `8090` | 1–65535, and bound by no other listener in the process; used only when no HTTPS profile is configured | restart |
| `AdminEndpoint:Authentication` | flag set | `None` | `ApiKey`, `OAuth`, both comma-separated, or `None`; `None` warns at startup | restart |
| `AdminEndpoint:ApiKeys` | list of secret blocks | empty | Required non-empty when `ApiKey` is named; refused when configured while it is not | restart; material per request |
| `AdminEndpoint:OAuth` | block | empty | Same shape and rules as `McpEndpoint:OAuth`, with one addition: `Resource` must end in `/api/admin`, because that is where these routes answer and what `mfctl` appends to find the metadata document. Refused when configured while `OAuth` is not named | restart |
| `AdminEndpoint:Https:Endpoints:<n>` | list of profiles | empty | Same shape and rules as `McpEndpoint:Https:Endpoints:<n>`; naming any binds those listeners and no clear-text one serving these routes | restart; material per handshake |
| `AdminEndpoint:Https:Redirect` | block | on, port `8091` | Same shape and rules as `McpEndpoint:Https:Redirect`; the default port differs so terminating TLS on both surfaces opens two clear-text ports that do not collide | restart |
| `AdminEndpoint:RateLimiting` | block | bounded | Same shape, defaults, and rules as `McpEndpoint:RateLimiting` above; applied whether or not it is written | restart |

The routes are served beneath `/api/admin`, which is a constant rather than a setting: a client is configured with a
host and a port and appends the rest.

## `HealthEndpoints`

The startup, readiness, and liveness probes and the dedicated listener they answer on.
[Health endpoints](health-endpoints.md) records why the surface carries no credential and how each transport behaves.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `HealthEndpoints:Enabled` | bool | `true` | Off maps no probe route and opens no listener | restart |
| `HealthEndpoints:BindAddress` | string | `0.0.0.0` | An IP address; `127.0.0.1` restricts to the machine | restart |
| `HealthEndpoints:Port` | int | `8081` | 1 – 65535; never a port the application listener binds | restart |
| `HealthEndpoints:HttpsPort` | int | unset | Required by, and only valid with, `HttpAndHttps` | restart |
| `HealthEndpoints:Transport` | enum | `Http` | `Http`, `HttpAndHttps`, `HttpsOnly` | restart |
| `HealthEndpoints:Domain` | string | — | Required by the TLS transports; the name the certificate is proven against | restart |
| `HealthEndpoints:ServerCertificate` | certificate block | unset | Required by the TLS transports; refused otherwise | restart |

## `Resilience`

Retry, timeout, circuit-breaker, and concurrency budgets for the non-HTTP outbound dependencies, one subsection per
dependency class: `MailboxSessionEstablishment`, `MailboxDataRetrieval`, `MailAuthorizationServerInvocation`,
`EmailDelivery`, `DatabaseCommandExecution`, `AiProviderInvocation`. A subsection naming no class fails startup. Every setting is **restart** by construction, and
[outbound resilience](../architecture/outbound-resilience.md#configuration) explains each strategy and the
per-class reasoning.

Settings, per class:

| Key | Type | Constraint |
| --- | --- | --- |
| `Resilience:<Class>:MaxAttempts` | int | 1 – 10; counts the first call, so `1` disables retry |
| `Resilience:<Class>:BaseDelay` / `MaxDelay` | TimeSpan | Jittered exponential backoff between attempts |
| `Resilience:<Class>:AttemptTimeout` / `TotalTimeout` | TimeSpan | One attempt / the whole operation |
| `Resilience:<Class>:CircuitBreakerFailureRatio` | double | 0.01 – 1.0 |
| `Resilience:<Class>:CircuitBreakerMinimumThroughput` | int | 2 – 1000 |
| `Resilience:<Class>:CircuitBreakerSamplingDuration` / `CircuitBreakerBreakDuration` | TimeSpan | — |
| `Resilience:<Class>:ConcurrencyLimit` | int | 1 – 1000 |

Defaults, per class:

| Class | Attempts | Base/max delay | Attempt/total timeout | Breaker ratio · min · sampling · break | Concurrency |
| --- | --- | --- | --- | --- | --- |
| `MailboxSessionEstablishment` | 3 | 2 s / 30 s | 30 s / 2 min | 0.5 · 5 · 60 s · 30 s | 4 |
| `MailboxDataRetrieval` | 3 | 1 s / 15 s | 60 s / 3 min | 0.5 · 10 · 30 s · 15 s | 8 |
| `MailAuthorizationServerInvocation` | 3 | 500 ms / 5 s | 10 s / 30 s | 0.5 · 10 · 60 s · 30 s | 8 |
| `EmailDelivery` | 2 | 5 s / 60 s | 60 s / 3 min | 0.5 · 5 · 60 s · 60 s | 4 |
| `DatabaseCommandExecution` | 3 | 200 ms / 2 s | 15 s / 30 s | 0.5 · 20 · 30 s · 5 s | 32 |
| `AiProviderInvocation` | 3 | 2 s / 30 s | 120 s / 5 min | 0.5 · 5 · 60 s · 30 s | 4 |

## `Logging`

The standard .NET `Logging` section applies unchanged — `Logging:LogLevel:Default` is `Information` out of the box,
with `Microsoft.AspNetCore` at `Warning`. Log lines are structured and never carry credentials, message content, or
raw MIME, whatever the level.

## Environment-only settings

A few settings are read from the environment alone, because they configure the process before configuration exists or
belong to the platform rather than to MailFathom:

| Variable | What it does |
| --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Attaches the OTLP exporter for logs, metrics, and traces — startup records included. Unset exports nothing. [Telemetry](telemetry.md) is the page, including the sibling `OTEL_*` variables the exporter reads itself |
| `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS` | [The application listener's](#the-application-listener) addresses, unless MCP HTTPS profiles or explicit Kestrel endpoints replace them |
| `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT` | The environment name; `Development` is what admits user secrets and `appsettings.Development.json` |
| `DOTNET_USE_POLLING_FILE_WATCHER` | Set to `1` where reload must observe a mounted volume's atomic update — Kubernetes ConfigMaps in particular |
| `OPENSSL_CONF` | The OpenSSL configuration file every TLS connection in the process is handshaked under. Unset is the platform's own policy; setting it is how a mail server the platform refuses is reached at all, and the host warns at startup that it is in force. [The platform TLS policy](platform-tls-policy.md) is the page |

`OPENSSL_CONF` is the one entry here that could not be a MailFathom setting even in principle: OpenSSL reads it while
initializing, before configuration binding exists, so the same name written into `appsettings.json` or a mounted file is
silently ineffective.
