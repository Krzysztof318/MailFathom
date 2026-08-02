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

- A multi-architecture container image for `linux/amd64` and `linux/arm64`, published to GHCR under its immutable
  version tag with `latest` moved onto the same digest
  ([#240](https://github.com/Krzysztof318/MailFathom/pull/240),
  [#256](https://github.com/Krzysztof318/MailFathom/pull/256)).
- Three supported installation shapes: Docker Compose, which provisions PostgreSQL for you; a Helm chart, which
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

[Unreleased]: https://github.com/Krzysztof318/MailFathom/compare/v0.1.0...main
[0.1.0]: https://github.com/Krzysztof318/MailFathom/releases/tag/v0.1.0
