# Changelog

All notable changes to MailFathom are recorded here, in the format of
[Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/). Versions follow
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html) as
[ADR 0004](docs/decisions/0004-versioning-and-release-policy.md) interprets it over MailFathom's four public surfaces:
the MCP tool contract, the configuration schema, the database schema, and the deployment contract.

**This file is written by the release pull request and by nothing else.** Ordinary work does not touch it — not a
feature, not a fix, not a refactor — because a changelog is a statement about a *release*, and a release is what the
tagged and published pull request makes. `$prepare-release` composes each section from the work merged since the
previous tag, and that same pull request is the one whose merge commit is tagged and published to the container
registries. `CHANGELOG.md` is a protected path for the same reason: an edit to it outside that flow changes what a
release claims it shipped.

What earns an entry is what a consumer of a release would notice — anything reaching one of the four surfaces, a fixed
defect that was observable from outside, and any change with a security consequence. A refactor, a test, a
continuous-integration adjustment, a documentation edit, and an internal rename earn none.

A breaking entry opens with `**Breaking (<surface>)**` and states the operator's action rather than only the fact. A
release that touches the database schema says whether a migration must be applied, whether it can be applied while the
previous version is still running, and whether the release can be deployed over the previous release's data at all.

MailFathom is pre-release. Within `0.x` a minor bump may break any of the four surfaces, and every break is named
below against the surface it breaks; a patch is compatible on all four. Nightly builds get no section of their own,
because they are, by definition, whatever has been merged since the newest section below.

## [Unreleased]

Nothing yet. A section appears here only when a release is prepared, because this file is written by the release pull
request and by nothing else; what has merged since the newest section below is what a nightly build carries.

## [0.3.0] - 2026-08-04

The third release, and the first whose upgrade is a new image and nothing else: **the database schema does not move.**
No migration is added, so `0.3.0` serves the database `0.2.0` was serving, and `0.2.0` serves it again if you go back.
Nothing `0.1.0` or `0.2.0` promised is withdrawn either — the MCP tool contract is unchanged, and every configuration
key `0.2.0` accepted is still accepted and still means the same thing. There is no breaking entry below.

What is new stands in front of the service rather than inside it: what terminates TLS for it, and what bounds the
surface you administer it through. The pages describing all of it are now published as
[a documentation site](https://krzysztof318.github.io/MailFathom/), with search, an API reference generated from the
source, and a version selector; `0.3.0` is the first release it carries a version for.

**One caveat, and it is a defect this release ships with**: a deployment that sets `HealthEndpoints:Enabled` to `false`
*and* enables the administrative endpoint loses its application listener, because binding a socket in code makes
Kestrel ignore `ASPNETCORE_HTTP_PORTS` and only the probe path restates it. The process starts and serves the
administrative port alone, and every MCP client is refused.
[#395](https://github.com/Krzysztof318/MailFathom/issues/395) carries the fix. Until it lands, leave the probes
enabled — the default — or state the application listener as a `Kestrel:Endpoints` entry.

### Added

**A deployment behind a TLS-terminating reverse proxy.** When nginx, Traefik, or an ingress controller holds the
certificate, the request that reaches MailFathom arrives as `http` under an internal name, and the deployment's public
identity survives the hop only in two headers.

- `X-Forwarded-Proto` and `X-Forwarded-Host` are read and applied before anything else sees the request, so OAuth
  discovery, the `401` challenge, and every absolute address MailFathom writes carry your public name — the
  protected-resource metadata document included, which is what a proxied OAuth deployment needed
  ([#371](https://github.com/Krzysztof318/MailFathom/pull/371)).
- `ReverseProxy:TrustedProxies` names the addresses or CIDR networks those headers are believed from, and
  `ReverseProxy:MaximumForwardedHops` (default `1`) how far back through each header a value is believed
  ([#371](https://github.com/Krzysztof318/MailFathom/pull/371),
  [#397](https://github.com/Krzysztof318/MailFathom/pull/397)). It is one section for the whole process rather than one
  per surface: a proxy's address is a network fact, so it is stated once and holds on every listener. What you name
  replaces the framework's loopback default rather than adding to it, and `10.0.0.5/24` is refused naming the
  `10.0.0.0/24` it would otherwise silently have become.
- `X-Forwarded-For` is never read, so the peer MailFathom observes stays the one that opened the connection, and
  `McpEndpoint:OAuth:Resource` stays a value you wrote rather than anything derived from a header
  ([#371](https://github.com/Krzysztof318/MailFathom/pull/371)).
- Client certificates are unreachable in this posture, because the handshake ended at the proxy and no header is read
  as a substitute ([#371](https://github.com/Krzysztof318/MailFathom/pull/371)).
  [Behind a TLS-terminating reverse proxy](https://krzysztof318.github.io/MailFathom/operations/mcp-endpoint.html#behind-a-tls-terminating-reverse-proxy)
  is the page, including what the proxy owns and what MailFathom keeps owning.

**A clear-text listener that redirects to HTTPS.** A surface that terminates TLS also binds one listener whose only
answer is a `308` to the address its profiles are served at, so a client nobody repointed meets a redirect rather than a
refused connection indistinguishable from an outage ([#374](https://github.com/Krzysztof318/MailFathom/pull/374)).

- `McpEndpoint:Https:Redirect` binds port `8080` and `AdminEndpoint:Https:Redirect` port `8091` unless you state
  another, each taking `Enabled`, `BindAddress`, and `Port`. The defaults differ so terminating TLS on both surfaces
  opens two clear-text ports that do not collide.
- That listener maps no route. Every path is answered the same way, and no authentication, rate-limiting, CORS, or
  client-certificate handler runs for a request that arrived on it, so there is nothing reachable over it to protect.
- `308` rather than `301` or `302`, because the MCP transport is a `POST` the older codes permit a client to re-send as
  a `GET`. The path and query are preserved, each domain redirects to its own profile's port, a `Host` header naming no
  configured domain gets `400`, and `:443` is left out of the `Location`.
- Writing the section for a surface that terminates no TLS fails startup rather than being ignored, and a socket
  conflict with any other listener in the process is reported against the section that asked for it. The health probes
  keep their own listener and are never asked on this port, because a probe follows no redirect.

**Rate limiting on the administrative endpoint.** `AdminEndpoint:RateLimiting` is the section
`McpEndpoint:RateLimiting` is, with the same keys, the same product defaults, and the same validation
([#373](https://github.com/Krzysztof318/MailFathom/pull/373)).

- The two are configured independently and partitioned per surface, so neither endpoint's traffic reaches the other's
  limits: an agent that exhausted `/mcp` has taken nothing from the surface you would use to stop it, and the
  concurrency limits are separate for the same reason.
- The burst is the endpoint's rather than one caller's. These routes carry no authentication middleware of their own —
  the credential is judged behind the limiter, so a request about to be refused for a wrong key has still spent
  capacity — and there is therefore no identity to partition on. Size `TokenCapacity` as what the whole endpoint may
  burst to rather than what one operator may.

### Changed

- **An enabled administrative endpoint is bounded whether or not you configure it**: 20 concurrent requests and a burst
  of 60 restored every minute, which are the MCP endpoint's defaults. `0.2.0` served it unbounded, so a deployment
  whose automation asks faster than that raises the numbers or sets `AdminEndpoint:RateLimiting:Enabled` to `false`,
  which costs one startup warning ([#373](https://github.com/Krzysztof318/MailFathom/pull/373)).
- **Configuring an HTTPS profile now also binds a clear-text port** — `8080` beside the MCP profiles, `8091` beside the
  administrative ones. Where a proxy in front of the process already answers that port, or something else on the host
  holds it, set `…:Https:Redirect:Enabled` to `false`. A conflict with another listener of this process is refused at
  startup naming the section that asked for it rather than failing later as an address-in-use error
  ([#374](https://github.com/Krzysztof318/MailFathom/pull/374)).
- The startup line stating the rate limits in force comes from
  `MailFathom.Host.Hosting.Warnings.TransportRateLimitingStartupReport` rather than `…McpRateLimitingStartupReport`,
  and one line is written per enabled endpoint. A deployment that matches on the logger category updates it
  ([#373](https://github.com/Krzysztof318/MailFathom/pull/373)).
- The clear-text transport warning describes the deployment you configured once a trusted proxy is named, rather than
  suggesting `McpEndpoint:Https:Endpoints` to a deployment whose certificate lives on the proxy
  ([#378](https://github.com/Krzysztof318/MailFathom/pull/378)).

### Security

- **A deployment that names no trusted proxy trusts every peer.** An OAuth access token is refused when the request did
  not arrive over transport encryption, and that check reads the scheme a forwarded header set — so with
  `ReverseProxy:TrustedProxies` left empty, anything that can open a connection sends `X-Forwarded-Proto: https` and
  has a reusable credential accepted over a clear-text hop, and `X-Forwarded-Host` is believed on the same terms. Name
  the addresses or CIDR networks your proxies actually use. Every startup running on the wide default logs one line
  naming what the deployment gave up ([#378](https://github.com/Krzysztof318/MailFathom/pull/378),
  [#397](https://github.com/Krzysztof318/MailFathom/pull/397)).
- The administrative endpoint is bounded by default, which is what stops a surface reachable from a network from
  serving unbounded API-key guessing — the attack it is most exposed to, and the one where a successful guess is worth
  the most ([#373](https://github.com/Krzysztof318/MailFathom/pull/373)).
- A redirect protects the next request and never the one that arrived: a credential sent in clear text was on the wire
  before anything answered. Treat the redirect as a way to find out that a client needs repointing rather than as a
  supported way to reach the endpoint ([#374](https://github.com/Krzysztof318/MailFathom/pull/374)).

## [0.2.0] - 2026-08-04

The second release, and the first that had a previous one to differ from. **Nothing `0.1.0` promised is withdrawn:**
the MCP tool contract is unchanged, every configuration key `0.1.0` accepted is still accepted and still means the
same thing, and both schema changes are additive. There is no breaking entry below, so an upgrade is the schema step
and a new image.

What is new is how a mailbox authenticates, how quickly a change on the mail server reaches the local copy, and a
second HTTP surface — an administrative endpoint with a command-line client of its own — that an operator reaches
without going through the MCP surface.

**The database schema.** Two migrations, both additive: one table and one nullable column
([#343](https://github.com/Krzysztof318/MailFathom/pull/343),
[#346](https://github.com/Krzysztof318/MailFathom/pull/346)). `0.2.0` refuses to serve until they are applied —
startup is gated on the migrations the binary carries and will not migrate a database out from under a running
process — but `0.1.0` neither reads nor writes what they add, so **they can be applied while `0.1.0` is still
serving**, and the release then deploys over `0.1.0`'s data unchanged. The gate reads only what is *pending*, so a
database already carrying both migrations still starts `0.1.0`: going back needs no schema step of its own.
[Applying the database schema](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/database-schema.md)
records the apply path and the ordering a deployment follows.

### Added

**Mailbox authentication.** An IMAP account can present an OAuth token instead of a password.

- `XOAUTH2` and `OAUTHBEARER` are accepted in
  `MailSynchronization:Accounts:<n>:TransportSecurity:PermittedAuthenticationMechanisms`, and naming either one turns
  on that account's `…:OAuth` block: the token endpoint, the client, the scope, and the grant — `refresh_token` or
  `client_credentials` — with the client secret and the refresh token supplied by reference like every other
  credential. Configuring the block for an account that authenticates with a password fails startup rather than
  provisioning something nothing can use ([#306](https://github.com/Krzysztof318/MailFathom/pull/306)).
  [Mailbox OAuth](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/mailbox-oauth.md) is the page,
  including where each value comes from for the providers this was verified against.
- Calls to an authorization server get a retry, timeout, circuit-breaker, and concurrency budget of their own, as the
  `Resilience:MailAuthorizationServerInvocation` class, rather than borrowing the mailbox session's
  ([#306](https://github.com/Krzysztof318/MailFathom/pull/306)).

**Continuous synchronization.** A folder change on the mail server can start a pass, instead of every change waiting
for the account's next interval.

- `MailSynchronization:Accounts:<n>:Mode` selects `Polling` — `0.1.0`'s behaviour, and still the default — or `Push`
  ([#339](https://github.com/Krzysztof318/MailFathom/pull/339)).
- Under `Push`, a server offering `NOTIFY` is watched over **one** connection per account covering every configured
  folder, and a server offering only `IDLE` over one connection per folder
  ([#339](https://github.com/Krzysztof318/MailFathom/pull/339),
  [#346](https://github.com/Krzysztof318/MailFathom/pull/346)). `MaxSubscribedFolders` (default `20`) bounds how many
  folders one subscription may name; the rest synchronize on the account's interval rather than being dropped.
- Where the server offers `CONDSTORE` and `QRESYNC`, a pass asks what changed since the modification sequence it last
  reconciled through instead of re-reading the folder, which is what the new nullable
  `synchronization_checkpoints.ReconciledThroughModSeq` column records
  ([#346](https://github.com/Krzysztof318/MailFathom/pull/346)).
- Push degrades to polling rather than stalling: `MaxConsecutivePushFailures` (default `3`) and
  `PushDegradationPeriod` (default `15 min`) decide when an account falls back and for how long, and
  `PushRenewalInterval` (default `20 min`) is the lifetime of one `IDLE` command — RFC 2177's ceiling, not a polling
  cycle ([#339](https://github.com/Krzysztof318/MailFathom/pull/339),
  [#341](https://github.com/Krzysztof318/MailFathom/pull/341)). Synchronization stays read-only throughout: a push
  pass sets the remote `\Seen` flag no more than a polled one does.

**An administrative endpoint, and the `mfctl` command that reaches it.**

- `AdminEndpoint` serves administrative routes beneath `/api/admin` on a listener, a credential set, and a set of
  authorization servers of its own. It is off by default, and a key or an issuer configured under `McpEndpoint`
  authenticates nothing here — the two surfaces are protected independently rather than sharing one policy
  ([#313](https://github.com/Krzysztof318/MailFathom/pull/313),
  [#317](https://github.com/Krzysztof318/MailFathom/pull/317)).
- **Each release now attaches `mfctl`**, a self-contained binary per platform — `linux-x64`, `linux-arm64`, `win-x64`,
  `win-arm64` — plus one checksum file covering all of them. It runs where you administer *from* rather than where the
  service runs, and needs no .NET installation
  ([#317](https://github.com/Krzysztof318/MailFathom/pull/317)).
- `mfctl login` signs in with an API key read from standard input, with a browser redirect caught locally, or with a
  device code entered elsewhere, and keeps the credential in a profile file of its own with the tokens encrypted at
  rest — the refresh token included, so a session outlives an access token's expiry rather than sending the operator
  back through the flow ([#348](https://github.com/Krzysztof318/MailFathom/pull/348)).
  [Administering your deployment](https://github.com/Krzysztof318/MailFathom/blob/main/docs/users/administering.md)
  states what that encryption protects and what it does not.
- `mfctl mailbox authorize` runs a mailbox's own OAuth flow from the operator's machine and **sends the resulting
  refresh token to the deployment**, which seals and stores it, instead of printing it for the operator to paste into
  a configuration file ([#356](https://github.com/Krzysztof318/MailFathom/pull/356)).
- [Administering a deployment](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/admin-endpoint.md)
  is the reference: every route, every `--mode`, the configuration, and what each failure means.

  **One caveat, and it is a defect this release ships with**: a deployment that sets `HealthEndpoints:Enabled` to
  `false` *and* enables this endpoint loses its application listener, because binding the administrative socket in
  code makes Kestrel ignore `ASPNETCORE_HTTP_PORTS` and only the probe path restates it. The process starts and serves
  the administrative port alone, and every MCP client is refused.
  [#325](https://github.com/Krzysztof318/MailFathom/issues/325) carries the fix. Until it lands, leave the probes
  enabled — the default — or state the application listener as a `Kestrel:Endpoints` entry.

**Encryption at rest.**

- `DataEncryption` configures a key ring: one active key, any number of retained ones, each 32 bytes of material
  supplied by reference like every other credential, under which MailFathom seals what it stores. An absent section is
  a valid deployment that seals nothing, and rotation is moving `ActiveKeyId` while leaving the previous key
  configured, so nothing already sealed becomes unopenable
  ([#338](https://github.com/Krzysztof318/MailFathom/pull/338)).
  [ADR 0005](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md)
  records the decision.
- The refresh token an authorization server rotates is followed and stored sealed under that ring, in the new
  `mailbox_refresh_tokens` table, so a provider that issues a new refresh token on every exchange no longer strands an
  account at the next restart ([#343](https://github.com/Krzysztof318/MailFathom/pull/343)).
- Docker Compose, the Helm chart, and the native systemd unit each provision the key by the same mechanism they
  provision every other secret, and the guides state where the file goes in each
  ([#354](https://github.com/Krzysztof318/MailFathom/pull/354)).
  [Secret provisioning](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/secret-provisioning.md)
  is the contract. **Back the key up with the database**: nothing in MailFathom regenerates it, and a database
  restored without its key restores nothing that was sealed under it.

### Changed

- `MailSynchronization:Accounts:<n>:Secrets:Password` is required only when the account's permitted mechanisms include
  a password mechanism. It was unconditionally required in `0.1.0`, which every configuration written for `0.1.0`
  already satisfies; what changes is that an account authenticating with OAuth alone now configures no password at all
  ([#306](https://github.com/Krzysztof318/MailFathom/pull/306)).

### Security

- A mailbox refresh token is held sealed in the database under the deployment's key ring rather than sitting in a
  configuration file or a secret file that nothing rotates, and the rotation an authorization server performs is
  followed rather than lost ([#343](https://github.com/Krzysztof318/MailFathom/pull/343)).
- The refresh token an authorization flow produces never reaches the operator's terminal: `mfctl mailbox authorize`
  sends it to the deployment over the administrative endpoint, so it is not in scrollback, in a shell history, or in a
  file somebody has to remember to delete ([#356](https://github.com/Krzysztof318/MailFathom/pull/356)).
- The administrative endpoint carries its own credentials, its own authorization servers, and its own TLS profiles, so
  granting somebody administrative access does not grant them the MCP surface and the reverse holds
  ([#313](https://github.com/Krzysztof318/MailFathom/pull/313),
  [#317](https://github.com/Krzysztof318/MailFathom/pull/317)).

## [0.1.0] - 2026-08-02

The first public release, and the point at which MailFathom's four public surfaces begin to promise anything. There is
no earlier release for this one to have changed, so every entry below is an addition rather than a difference.

**What it is.** A Model Context Protocol server for your own mail. It synchronizes IMAP mailboxes read-only into a
local PostgreSQL copy and serves that copy to an MCP client as three tools, so a client can list, read, and search
mail without a request ever reaching a mail server and without a message being marked as read.

**The database schema.** This release creates it. One baseline migration
([#241](https://github.com/Krzysztof318/MailFathom/pull/241),
[#127](https://github.com/Krzysztof318/MailFathom/pull/127)) builds the whole schema on an empty database, so there is
no previous version to apply it beside and nothing of an earlier release's to deploy over. The migration must be
applied before the host will serve: startup is gated on the schema and refuses to start against a database that is
behind it, rather than migrating one out from under a running process.
[Applying the database schema](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/database-schema.md)
records the apply path and the ordering a deployment follows.

### Added

**Mail synchronization.**

- Read-only IMAP synchronization of configured accounts and folders into a local PostgreSQL copy. Synchronization
  never sets the remote `\Seen` flag, and that invariant is proven against a real IMAP server rather than asserted
  ([#13](https://github.com/Krzysztof318/MailFathom/pull/13),
  [#132](https://github.com/Krzysztof318/MailFathom/pull/132)).
- A supervisor per configured account, each running on a schedule of its own
  ([#167](https://github.com/Krzysztof318/MailFathom/pull/167)).
- Remote deletions and flag changes are reconciled back onto the local copy
  ([#171](https://github.com/Krzysztof318/MailFathom/pull/171)).
- Synchronization is bounded by a configured earliest received date, so an established mailbox is not backfilled in
  full on first run ([#133](https://github.com/Krzysztof318/MailFathom/pull/133)).
- Normalized metadata is extracted from stored raw MIME and persisted with the indexes a timeline query reads
  ([#98](https://github.com/Krzysztof318/MailFathom/pull/98),
  [#106](https://github.com/Krzysztof318/MailFathom/pull/106)).
- Searchable text is derived from stored mail and indexed for PostgreSQL full-text search, with a backfill worker for
  mail stored before extraction existed ([#110](https://github.com/Krzysztof318/MailFathom/pull/110)).
- Folder aliases resolve to remote folders under a generation of their own, so a renamed or re-created folder is
  detected rather than silently followed ([#94](https://github.com/Krzysztof318/MailFathom/pull/94)).
- Each class of outbound dependency runs under one configurable resilience pipeline — timeout, bounded retry with
  jittered backoff, and a circuit breaker ([#83](https://github.com/Krzysztof318/MailFathom/pull/83)) — and a dropped
  IMAP session is recovered under it ([#92](https://github.com/Krzysztof318/MailFathom/pull/92)).

**The MCP tool contract.** Served over the Streamable HTTP transport
([#135](https://github.com/Krzysztof318/MailFathom/pull/135)). Every call reads the local copy only, so no tool
request can wait on IMAP or change anything remotely, and every tool bounds how much mail one call can draw out.

- `list_emails` returns a bounded keyset page of message summaries — at most 100, with no body text — filtered by
  account, folder, and date ([#136](https://github.com/Krzysztof318/MailFathom/pull/136)).
- `get_email_content` returns bounded bodies for at most 10 named emails under a shared character budget, and names
  attachments only when asked ([#137](https://github.com/Krzysztof318/MailFathom/pull/137),
  [#153](https://github.com/Krzysztof318/MailFathom/pull/153),
  [#232](https://github.com/Krzysztof318/MailFathom/pull/232)).
- `search_emails` returns a bounded ranked window of at most 50 lexical matches, each with bounded extracts
  ([#138](https://github.com/Krzysztof318/MailFathom/pull/138),
  [#163](https://github.com/Krzysztof318/MailFathom/pull/163)).
- Every descriptor declares `readOnlyHint`, `destructiveHint`, `idempotentHint`, and `openWorldHint`, so a client can
  judge a tool before calling it. No error and no log line carries a filter value, a mailbox address, a subject, body
  text, raw MIME, or an internal identifier; every published failure carries a five-digit error code instead
  ([#111](https://github.com/Krzysztof318/MailFathom/pull/111)).

**What protects that endpoint.** It is disabled by default, and enabling it requires stating what a client presents.

- Named, expiring API keys, and `Origin` validation for browser callers through configurable CORS
  ([#169](https://github.com/Krzysztof318/MailFathom/pull/169)).
- OAuth 2.1 access tokens from configured authorization servers, judged against the issuer, this resource, the
  required scopes, and an explicit list of authorized subjects — so signing in to the authorization server does not by
  itself grant a user this mailbox ([#183](https://github.com/Krzysztof318/MailFathom/pull/183)).
- HTTPS on operator-provided domains and certificates, with the material proven to load, to cover the stated domain,
  and not to have expired before any listener opens ([#175](https://github.com/Krzysztof318/MailFathom/pull/175)).
- Mutual TLS through named client-certificate profiles, proven against a real TLS handshake
  ([#177](https://github.com/Krzysztof318/MailFathom/pull/177),
  [#196](https://github.com/Krzysztof318/MailFathom/pull/196)).
- Per-client token-bucket and process-wide concurrency rate limits, enabled by default, so an endpoint is bounded
  whether or not anyone wrote a number ([#176](https://github.com/Krzysztof318/MailFathom/pull/176)).
- A per-account mail transport security policy decides what TLS an account's connections require
  ([#58](https://github.com/Krzysztof318/MailFathom/pull/58)), and a host whose platform TLS policy refuses a mail
  server can be configured to reach it anyway, and says so when it does
  ([#226](https://github.com/Krzysztof318/MailFathom/pull/226)).

**The configuration schema.** Every MailFathom section is bound strictly: a key the section does not define fails
startup naming it, so a typo cannot silently leave a default in force, and a violated constraint fails startup with
the configuration path in the message.
[The configuration reference](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/configuration-reference.md)
is the whole surface, key by key, including which keys reload and which need a restart.

- Secrets are supplied as references rather than inline values by default, so a plain-text credential where a
  reference belongs fails startup instead of authenticating
  ([#64](https://github.com/Krzysztof318/MailFathom/pull/64)).
- Certificate material and secrets are re-read behind unchanged references, so a renewal reaches the process without a
  restart ([#73](https://github.com/Krzysztof318/MailFathom/pull/73)).
- A mounted directory or file of JSON — a Kubernetes ConfigMap, a systemd drop-in — is a first-class configuration
  source ([#168](https://github.com/Krzysztof318/MailFathom/pull/168)).
- The deployment-wide privacy bounds on what a search result may quote, and on how much body text one read may return,
  are configuration rather than constants a caller could raise.

**The deployment contract.**

- A multi-architecture container image for `linux/amd64` and `linux/arm64`, published to `ghcr.io` **and** `docker.io`
  as one manifest list under one digest, under its immutable version tag with `latest` moved onto that same digest.
  The registry to pull from is whichever your environment already reaches
  ([#240](https://github.com/Krzysztof318/MailFathom/pull/240),
  [#256](https://github.com/Krzysztof318/MailFathom/pull/256),
  [#281](https://github.com/Krzysztof318/MailFathom/pull/281)).
- The Helm chart is published with the image, in the same run and at the same version, as an OCI artifact at
  `oci://ghcr.io/krzysztof318/charts/mailfathom`. Its `appVersion` is that release, so a chart states which application
  version it deploys without being unpacked, and it is listed on Artifact Hub
  ([#281](https://github.com/Krzysztof318/MailFathom/pull/281)).
- Every published artifact, image and chart alike, carries a signed provenance statement that
  `gh attestation verify` checks against this repository ([#281](https://github.com/Krzysztof318/MailFathom/pull/281)).
- Three supported installation shapes: Docker Compose, which provisions PostgreSQL for you; the Helm chart, which
  deliberately installs neither a database nor a Secret; and a native systemd process taking its secrets as systemd
  credentials ([#180](https://github.com/Krzysztof318/MailFathom/pull/180)). Linux is the only platform this project
  supports.
- Startup, readiness, and liveness probes on a listener of their own, with a configurable transport, which a
  deployment can turn off entirely ([#198](https://github.com/Krzysztof318/MailFathom/pull/198),
  [#264](https://github.com/Krzysztof318/MailFathom/pull/264)).
- Each release publishes an idempotent `mailfathom-schema-<version>.sql` artifact naming the migrations it carries and
  the checksum that identifies it ([#258](https://github.com/Krzysztof318/MailFathom/pull/258)).
- The declared version is written in one place and stamped from there into every assembly, the image's tags and
  labels, the packaged chart's `appVersion`, the host's startup record, and the server's MCP `initialize` response
  ([#208](https://github.com/Krzysztof318/MailFathom/pull/208)).
- OpenTelemetry logs, metrics, and traces export when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and host start, startup
  failure, and shutdown are reported from a bootstrap logger that exists before configuration does
  ([#89](https://github.com/Krzysztof318/MailFathom/pull/89)).
- Every published artifact carries `LICENSE` and `NOTICE`, and a publish that would omit either fails
  ([#172](https://github.com/Krzysztof318/MailFathom/pull/172)). MailFathom is licensed under Apache-2.0, and
  [`THIRD_PARTY_LICENSES.md`](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md) registers
  every dependency it ships beside ([#173](https://github.com/Krzysztof318/MailFathom/pull/173)).

[Unreleased]: https://github.com/Krzysztof318/MailFathom/compare/v0.3.0...main
[0.3.0]: https://github.com/Krzysztof318/MailFathom/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/Krzysztof318/MailFathom/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/Krzysztof318/MailFathom/releases/tag/v0.1.0
