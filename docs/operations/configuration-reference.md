# Configuration reference

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

### One account — `MailSynchronization:Accounts:<n>`

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:AccountId` | string | — | Required; unique across accounts after normalization | reload |
| `…:Host` | string | — | Required when synchronization is enabled | reload |
| `…:Port` | int | `993` | 1 – 65535 | reload |
| `…:UserName` | string | — | Required when synchronization is enabled; an identifier, not a secret | reload |
| `…:Secrets:Password` | secret block | — | Must resolve at startup | reload; material per connection |
| `…:EarliestEmailReceivedDate` | date | unset (everything) | Not in the future (compared in UTC) | reload |
| `…:RemotelyDeletedEmailDisposition` | enum | `RetainTombstone` | `RetainTombstone`, `EraseLocalCopy` | reload; governs disappearances observed from then on |
| `…:Folders` | list | inbox by role | Aliases unique; each entry below | reload |

A folder entry names `Alias` (required — your stable name for the folder) and **exactly one** of `RemotePath` (the
server's own path) or `SpecialUse` (a role discovery resolves: `Inbox`, `Archive`, `Drafts`, `Sent`, `Junk`, `Trash`,
`All`, `Flagged`, `Important`). Configuring no folder synchronizes the inbox by role.

### Transport security — `…:TransportSecurity`

[The rules](../features/imap-synchronization.md#transport-security) are the domain's; every weakening is an explicit
opt-in, and unsafe combinations fail startup.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:ConnectionSecurity` | enum | `TlsOnConnect` | `Auto`, `TlsOnConnect`, `StartTlsRequired`, `StartTlsWhenAvailable`, `None`; anything but the two guaranteed-TLS modes requires `AllowInsecureConnection` | reload |
| `…:PermittedAuthenticationMechanisms` | string list | `PLAIN`, `LOGIN` | Supported SASL names; an unordered allow-list, the client picks the strongest that survives | reload |
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

## `MailExtractionBackfill`

The worker that extracts text for messages stored before extraction existed or before a limit was raised.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailExtractionBackfill:Enabled` | bool | `true` | — | restart |
| `MailExtractionBackfill:Interval` | TimeSpan | `00:00:30` | 1 s – 1 h | restart |
| `MailExtractionBackfill:BatchSize` | int | `50` | 1 – 500 | restart |
| `MailExtractionBackfill:MaxBatchesPerRun` | int | `10` | 1 – 1000 | restart |

## `McpEndpoint`

Whether the protocol surface is served and what a client must present. The whole section is **restart** — it decides
routing and listeners — while key and certificate material is read per request or per handshake.
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

Empty by default, which serves the endpoint over the host's ordinary listener. Configuring any profile **takes over
the host's listeners**: only the profiles' sockets are opened.
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

### Rate limiting — `McpEndpoint:RateLimiting`

The one MCP subsection where every value has a product default, so an enabled endpoint is bounded whether or not
anyone wrote a number. [Rate limiting](mcp-endpoint.md#rate-limiting) records whose capacity a request spends.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Enabled` | bool | `true` | Turning it off costs a startup warning | restart |
| `…:MaxConcurrentRequests` | int | `20` | 1 – 1000; process-wide | restart |
| `…:ConcurrencyQueueLimit` | int | `0` | 0 – 1000; `0` refuses instead of queueing | restart |
| `…:TokenCapacity` | int | `60` | 1 – 1000000; the largest burst one client may spend | restart |
| `…:TokensPerReplenishmentPeriod` | int | `60` | 1 – 1000000, and not above `TokenCapacity` | restart |
| `…:ReplenishmentPeriod` | TimeSpan | `00:01:00` | 1 s – 1 h | restart |
| `…:RequestQueueLimit` | int | `0` | 0 – 1000, and below `MaxConcurrentRequests` | restart |

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
dependency class: `MailboxSessionEstablishment`, `MailboxDataRetrieval`, `EmailDelivery`, `DatabaseCommandExecution`,
`AiProviderInvocation`. A subsection naming no class fails startup. Every setting is **restart** by construction, and
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
| `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS` | The application listener's addresses, unless MCP HTTPS profiles or explicit Kestrel endpoints replace them |
| `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT` | The environment name; `Development` is what admits user secrets and `appsettings.Development.json` |
| `DOTNET_USE_POLLING_FILE_WATCHER` | Set to `1` where reload must observe a mounted volume's atomic update — Kubernetes ConfigMaps in particular |
