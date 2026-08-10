# Changelog

All notable changes to MailFathom are recorded here, in the format of
[Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/). Versions follow
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html) as
[ADR 0004](docs/decisions/0004-versioning-and-release-policy.md) interprets it over MailFathom's four public surfaces:
the MCP tool contract, the configuration schema, the database schema, and the deployment contract.

**It is written for whoever runs MailFathom** — the person installing it and the administrator keeping it running — and
every section answers the same question before an upgrade: what is new for you, what was fixed, what breaks, and what
you have to do about it. So what earns an entry is what you would notice: anything reaching one of the four surfaces, a
fixed defect that was observable from outside, and any change with a security consequence. A refactor, a test, a
continuous-integration adjustment, a documentation edit, and an internal rename earn none, and nothing below is written
in the terms of the code that produced it.

A breaking entry opens with `**Breaking (<surface>)**` and states the operator's action rather than only the fact. A
release that touches the database schema says whether a migration must be applied, whether it can be applied while the
previous version is still running, and whether the release can be deployed over the previous release's data at all.

MailFathom is pre-release. Within `0.x` a minor bump may break any of the four surfaces, and every break is named
below against the surface it breaks; a patch is compatible on all four. There is no `Unreleased` heading, and neither
nightly nor prerelease builds get a section of their own: what a nightly carries is, by definition, whatever has been
merged since the newest section below.

**This file is written by the release pull request and by nothing else.** Ordinary work does not touch it — not a
feature, not a fix, not a refactor — because a changelog is a statement about a *release*, and a release is what the
tagged and published pull request makes. `$prepare-release` composes each section from the work merged since the
previous tag, and that same pull request is the one whose merge commit is tagged and published to the container
registries. `CHANGELOG.md` is a protected path for the same reason: an edit to it outside that flow changes what a
release claims it shipped.

## [0.5.0] - 2026-08-10

The fifth release, and the one that lets a client **ask about your mail rather than only look through it**. `ask_mail`
answers a question in prose and cites the messages the answer was drawn from, and `search_emails` ranks semantically as
well as lexically. Both stay dark until you declare the AI endpoints they need, so a deployment that declares none
serves exactly what it served before — and pays exactly what it paid before, which for a feature that bills per call is
the more important half.

**Three things need an edit before this release starts, or before a client reaches it.** Every account states a
`DisplayName` now; two arguments on the MCP tools were renamed and the previous spellings are refused; and the database
is **PostgreSQL 18**, which does not read a data directory PostgreSQL 17 wrote. The last of those is the expensive one:
an existing deployment moves its data across a dump before the new image comes up, and
[upgrading a deployment that ran PostgreSQL 17](https://krzysztof318.github.io/MailFathom/operations/deployment-compose.html#upgrading-a-deployment-that-ran-postgresql-17)
is the procedure, command by command.

**The database schema moves as well**, by six migrations that add three tables, three columns, and two indexes, and
that change nothing `0.4.0` reads — so the schema step applies while `0.4.0` is still serving, `0.4.0` serves the
result unchanged if you roll the image back, and this release deploys over the previous release's data. Nothing else
`0.4.0` promised is withdrawn: every setting not named below still means what it meant, and no tool was removed.

**The defect `0.4.0` shipped with is gone, and it was the whole image.** The published `0.4.0` container could not
start: its base image sets `ASPNETCORE_HTTP_PORTS`, `0.4.0` is the release that began refusing that variable, and the
Dockerfile never cleared the inherited value — so every container built from that image failed startup on a setting
nobody wrote.

### Added

**`ask_mail` — a question about your mail, answered in prose and cited back to the messages it came from.** The
question is not a search query: its words are never matched against your mail, and the lookups behind it are written by
the model, which is what lets *did the supplier ever confirm the March delivery date* find the message that says so
without containing any of those words
([#579](https://github.com/Krzysztof318/MailFathom/pull/579)). Every answer carries `citations`, one entry per email
the run actually read, so nothing it says is un-checkable.

- **It is advertised only where it can work** — a declared `Chat` endpoint, and mail that is embedded. A server with
  neither does not publish the tool, so a client never sees an ability the deployment does not have. Called on a
  deployment that cannot answer, it fails with `56001` and the message says which half is missing.
- **Configuring it is one section.** `Chat:Alias`, `Chat:Model`, and one credential — an API key or a Microsoft Entra
  credential — declare the endpoint; `Chat:Api` states whether the deployment's server serves chat completions or the
  responses API, because the routed name is your own deployment's and nothing about it says which paths exist. A
  reasoning model states `Chat:ReasoningEffort` as the provider spells it, and unset sends no reasoning parameter at
  all ([#557](https://github.com/Krzysztof318/MailFathom/pull/557),
  [#624](https://github.com/Krzysztof318/MailFathom/pull/624)).
- **What one question may spend is bounded before it is asked, and what a period may spend is bounded above that.**
  `MailAnswering` sets how many passages a lookup draws on, how much of any one message it draws out, how much
  retrieved mail may leave the process for one question, how many provider calls and tokens one run may spend, and how
  many runs and tokens a period may. A run that reaches a ceiling stops with `57001` rather than continuing quietly,
  and a run that reaches only the retrieval ceiling answers from what it has and says the mailbox was not read in full
  ([#592](https://github.com/Krzysztof318/MailFathom/pull/592)).
- **An optional second filter judges the retrieved passages with the model** before they reach the answer, dropping
  what scores below `Chat:RelevanceFilter:MinimumRelevance`. It is off by default, because it is a second call per
  lookup ([#573](https://github.com/Krzysztof318/MailFathom/pull/573)).
- **An account can keep a record of what each question read**, off by default and enabled per account with
  `AnsweringAuditTrail:Enabled` and a retention window. It names the mail a run drew on and holds none of it, and
  `GET /api/admin/answering/audit` reads it back in bounded pages
  ([#610](https://github.com/Krzysztof318/MailFathom/pull/610)).
- The model is composed over Agent Framework and the mail it reads is fenced away from the instructions it follows, so
  an instruction written into a message is data rather than a command
  ([#564](https://github.com/Krzysztof318/MailFathom/pull/564),
  [#565](https://github.com/Krzysztof318/MailFathom/pull/565),
  [#603](https://github.com/Krzysztof318/MailFathom/pull/603)).
  [Mail answering](https://krzysztof318.github.io/MailFathom/features/mail-answering.html) is the page.

**`search_emails` ranks semantically as well as lexically.** Where an embedding profile is active, a search fuses the
two rankings with Reciprocal Rank Fusion and reports `retrievalMode: hybrid`; where none is, it answers exactly as
`0.4.0` did and says `lexical` ([#555](https://github.com/Krzysztof318/MailFathom/pull/555)). Every response also
carries `semanticSearch` — `inactive`, `available`, or `degraded` — so a client can tell a server that never embedded
anything from one whose provider is failing right now, which the two modes alone cannot distinguish
([#562](https://github.com/Krzysztof318/MailFathom/pull/562)).

- **A failing provider degrades the search rather than the deployment.** An unhealthy profile falls back to lexical
  ranking and says so, instead of failing the call.
- **Changing the model is a reindex with no search outage.** A new vector generation is built beside the one that is
  serving and takes over only when it is complete, and `POST /api/admin/embeddings/reindex/cancellation` stops one
  under way and leaves the serving generation where it is
  ([#570](https://github.com/Krzysztof318/MailFathom/pull/570)).
- **What embedding may cost is bounded before it is spent.** `Embeddings:MaxRequestsPerMinute` paces a provider whose
  quota is per minute, `Embeddings:MaxInputCharactersPerPeriod` and `Embeddings:SpendPeriod` cap what a fixed window
  may send, and `Embeddings:MaxCharactersPerEmail` bounds a single enormous message rather than refusing it
  ([#581](https://github.com/Krzysztof318/MailFathom/pull/581)).
- **`mfctl` administers it.** `mfctl embedding status` reports whether semantic search is working, how far behind it
  is, and when the next backfill pass is due; `mfctl embedding activate` forecasts what taking up the declared model
  would cost before it starts, and starting it wakes the backfill instead of leaving the deployment to look broken for
  up to fifteen minutes ([#593](https://github.com/Krzysztof318/MailFathom/pull/593),
  [#626](https://github.com/Krzysztof318/MailFathom/pull/626)).
  [Embedding profiles](https://krzysztof318.github.io/MailFathom/operations/embedding-profiles.html) states the whole
  lifecycle.

**`list_accounts`, so a client can find out what it may ask about.** It reports each account's identifier, the display
name you gave it, whether its next pass polls or listens, and one entry per folder with how fresh that folder's local
copy is — and deliberately publishes no address, no credential, and no server name
([#637](https://github.com/Krzysztof318/MailFathom/pull/637)). Every tool that takes accounts now accepts either the
identifier or the display name, so a person can say *work* where the configuration says `work-imap-01`.

**`get_email_content` returns attachment content.** Pass `includeAttachmentContent` and each attachment arrives as
base64, bounded by `EmailContent:MaxAttachmentBytes` per file and `EmailContent:MaxAttachmentBytesPerRead` across the
call; a file over the limit is described and not returned, never truncated
([#633](https://github.com/Krzysztof318/MailFathom/pull/633)). Setting `MaxAttachmentBytes` to `0` returns no
attachment content at all, which is the deployment that wants the metadata and nothing else.

**A record of every change MailFathom makes to a mailbox, off by default and enabled per account.** An audit trail of
mail movements says where a person's mail has been, when, and at whose instruction, so a deployment that never asked
for one never accumulates one. `AuditTrail:Enabled` and `AuditTrail:Retention` turn it on per account, one entry is
written per finished change, and `GET /api/admin/mailbox/mutations/audit` reads it back filterable by account, by
change, and by time ([#568](https://github.com/Krzysztof318/MailFathom/pull/568)).

- **It holds no mail content and it outlives the mail.** Folder paths, identifiers, a five-digit failure code where
  there was one, and MailFathom's own configured names are all an entry carries — no subject, no address, no body
  fragment, no filename — and erasing the email leaves the entry standing, including where the change recorded *was*
  that deletion.
- Retention rides the account's own run and erases at most five thousand entries a pass, so shortening a long window
  clears the backlog over several runs rather than in one delete that locks the trail.
- `AuthoredDeleteEmailDisposition` decides what becomes of the local copy of mail MailFathom itself deleted —
  `RetainLocalCopy`, `RetainTombstone`, or `EraseLocalCopy` — separately from the setting that governs mail somebody
  else deleted, because a deletion of ours and a deletion of theirs are different facts
  ([#554](https://github.com/Krzysztof318/MailFathom/pull/554),
  [#561](https://github.com/Krzysztof318/MailFathom/pull/561),
  [#563](https://github.com/Krzysztof318/MailFathom/pull/563)).

**A ceiling on how much mail one deployment stores, and how much one run brings in.**
`MailSynchronization:MaxStoredContentBytes` is what stops a large mailbox from filling the volume: past it, ingestion
degrades to metadata only and keeps listing and searching rather than failing, and the messages it skipped are picked
up once there is room. `MaxContentBytesPerRun` ends a folder run at its checkpoint instead of at the end of the
mailbox, and `MaxInFlightRawMimeBytes` bounds what a run holds in memory at once
([#580](https://github.com/Krzysztof318/MailFathom/pull/580)).

**`mfctl` reaches a deployment whose certificate this machine does not trust, by asking once.** `mfctl login` shows
the fingerprint, asks, and pins what you accept to that profile, so a later renewal is a question rather than a silent
acceptance; `--trust-untrusted-certificate` and `--allow-clear-text` answer the same two questions where there is no
terminal to ask on ([#560](https://github.com/Krzysztof318/MailFathom/pull/560)).

**Every exported record names the build it came from.** `service.version` carries the semantic version and
`vcs.ref.head.revision` the commit, on every log record, metric, and span the host exports, so a report from a
deployment can be tied to the code that produced it ([#620](https://github.com/Krzysztof318/MailFathom/pull/620),
[#655](https://github.com/Krzysztof318/MailFathom/pull/655)).

### Changed

- **Breaking (deployment contract)** — **the database is PostgreSQL 18.4 with pgvector 0.8.6**, where `0.4.0` ran 17.
  PostgreSQL does not read a data directory an earlier major version wrote, so **bringing the new image up over an
  existing volume does not upgrade it** — the container exits `1` naming the data it found, the server never listens,
  and nothing that depends on it comes up. The attempt writes nothing, so the old directory is intact and still
  dumpable afterwards. Move the data across a dump from a PostgreSQL 17 server before the upgrade, or delete the volume
  and let synchronization refill it from IMAP — which costs the embeddings and the audit trails, since neither is in
  the mailbox ([#658](https://github.com/Krzysztof318/MailFathom/pull/658)).
  [Upgrading a deployment that ran PostgreSQL 17](https://krzysztof318.github.io/MailFathom/operations/deployment-compose.html#upgrading-a-deployment-that-ran-postgresql-17)
  is the sequence for Compose, and the same reasoning holds for a claim the chart wrote.
- **Breaking (deployment contract)** — **the Helm chart runs PostgreSQL itself unless you tell it not to**, where
  `0.4.0` installed none and required `database.host`. A values document that names a host now fails to render, because
  `database.host` is refused while `database.deploy.enabled` is on and the address is derived from the release name
  instead: two values naming one server is how a deployment ends up connecting somewhere it did not install. **Keep
  your own server by setting `database.deploy.enabled: false` beside the `host` you already have.** A deployment that
  takes the default instead names a second Secret in `database.deploy.superuserPasswordSecret` — separate from
  `secrets.existingSecret`, and refused if it is the same one — because the application's Secret is mounted whole into
  the pod that parses untrusted mail ([#658](https://github.com/Krzysztof318/MailFathom/pull/658)).
- **Breaking (deployment contract)** — **`mfctl` refuses a deployment from another release line before it sends
  anything.** A `0.4.x` command against a `0.5.x` deployment stops with a message naming both versions, because the
  administrative contract is what a minor may break and a command that guesses at it is worse than one that declines.
  Take `mfctl` from the deployment's own release; two builds that share a `major.minor` and differ otherwise warn and
  run, and a version that cannot be read warns and runs
  ([#628](https://github.com/Krzysztof318/MailFathom/pull/628)).
- **Breaking (configuration schema)** — **every account states a `DisplayName`**, and startup fails naming the account
  that has none. It is what a client sees and what a person names an account by, it is at most 128 characters, and it
  may not collide with another account's identifier or display name compared without regard to case. Add one line per
  account under `MailSynchronization:Accounts`
  ([#637](https://github.com/Krzysztof318/MailFathom/pull/637)).
- **Breaking (MCP tool contract)** — **`list_emails` and `search_emails` take `accounts` where they took
  `accountIds`.** The argument was renamed because it now accepts a display name as readily as an identifier, and a
  client passing the old name is refused rather than having it ignored. Omitting it still reads every account the
  deployment serves ([#637](https://github.com/Krzysztof318/MailFathom/pull/637)).
- **Breaking (MCP tool contract)** — **`get_email_content` takes `includeAttachmentContent` where it took
  `includeAttachmentDetails`**, and every attachment's file name, media type, and decoded size are now returned
  whether or not the call asks for anything. The old argument bought the metadata; the new one buys the bytes, so a
  client that passed it to see what was attached needs to pass nothing at all
  ([#633](https://github.com/Krzysztof318/MailFathom/pull/633)).
- **`search_emails` can report a `retrievalMode` it never reported before.** `lexical` was the only value `0.4.0`
  produced; `hybrid` is a second one, and a client matching on the field exactly rather than on the results should
  expect it ([#555](https://github.com/Krzysztof318/MailFathom/pull/555)).
- **Every SQL statement MailFathom runs is logged at `Debug` rather than `Information`.** A deployment at the default
  level no longer writes one log record per database command, which is where the bulk of its log volume was going —
  and those records carry the text of every query the mailbox is read with. Set
  `Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command` to `Debug` to get them back
  ([#654](https://github.com/Krzysztof318/MailFathom/pull/654)).

### Fixed

- **The published `0.4.0` container could not start.** Its base image sets `ASPNETCORE_HTTP_PORTS`, `0.4.0` refuses
  that variable by design, and the Dockerfile did not clear the inherited value — so the image failed startup on a
  setting nobody had written, with a message naming a variable that is not in any configuration file
  ([#575](https://github.com/Krzysztof318/MailFathom/pull/575)).
- **An MCP endpoint configured with OAuth and nothing else threw on every request that carried no credential**,
  including the health probes, which are documented as carrying none. The endpoint now answers an uncredentialed
  request with the challenge it is supposed to, and the probes are served without one
  ([#577](https://github.com/Krzysztof318/MailFathom/pull/577)).
- **A `list_emails` date filter written at a non-zero UTC offset failed the whole listing.** `receivedOnOrAfter` and
  `receivedBefore` reached the database unconverted, and anything but `+00:00` was refused there — so a client in a
  time zone sent the value its clock produced and got a failure rather than a page. Both bounds are held as instants
  now, and every offset names the same moment ([#612](https://github.com/Krzysztof318/MailFathom/pull/612)).

### Security

- **A question and the mail that answered it leave no copy at the provider.** A chat endpoint declaring
  `Api: Responses` reached an API that retains what it is sent for thirty days by default and makes it readable in the
  provider's dashboard — so adopting that API would have placed the operator's correspondence in a third party's log
  because of a default nobody wrote. Every request states that it is stateless, and the model's reasoning is carried
  between turns as the encrypted content the provider returns rather than by leaving the conversation behind
  ([#636](https://github.com/Krzysztof318/MailFathom/pull/636)).
- **What leaves the process to answer one question is bounded and countable.** The passages a run may send, how much of
  any single message goes with them, and the total that may cross the boundary for one question are each configured and
  each enforced before the call rather than after it
  ([#592](https://github.com/Krzysztof318/MailFathom/pull/592)).
- **A database the chart deploys keeps its superuser credential out of the pod that parses mail.** The application's
  Secret is mounted whole, because MailFathom reads the keys your own configuration names, so the superuser password
  lives in a second Secret the application never mounts and the chart refuses a values document that names one Secret
  for both. The role MailFathom connects as is never a superuser in either arrangement
  ([#658](https://github.com/Krzysztof318/MailFathom/pull/658)).

## [0.4.0] - 2026-08-07

The fourth release, and the first that asks every deployment to edit its configuration before it will start. Two things
every installation states have moved: **where each surface is served, and how a credential is configured.** Neither
previous form is ignored — both fail startup naming what replaces them — so an upgrade that skips the edit stops rather
than quietly serving something you did not configure. **The database schema moves as well**, by five migrations that
add three tables and then refine one of the three, and that touch nothing `0.3.0` reads — so the schema step belongs to
this upgrade, it applies while `0.3.0` is still running, and `0.3.0` serves the result unchanged if you go back.

Nothing else `0.3.0` promised is withdrawn. The MCP tool contract is untouched — `list_emails`, `get_email_content`,
and `search_emails` answer exactly as they did — and every setting not named below still means what it meant.

**The defect `0.3.0` shipped with is gone.** A deployment that set `HealthEndpoints:Enabled` to `false` and enabled the
administrative endpoint lost its application listener and refused every MCP client. There is no application listener to
lose now, because every surface binds the socket its own section names.

### Added

**A key pair as a third way to authenticate, on both endpoints.** The client holds the private key and the deployment
holds only the public half, so nothing this host stores in order to verify a request is worth stealing from it — not
from the configuration, not from a backup of it, and not from the deployment tool that wrote it
([#527](https://github.com/Krzysztof318/MailFathom/pull/527)).

- Configure a `PublicKey` entry under `Authentication` exactly as you would a key: one named secret, reached through
  every reference scheme the deployment already has, with a `Name` diagnostics correlate on and a `Lifetime` that is
  enforced. Startup refuses material that is not a PEM public key, an RSA key below 2048 bits, a curve outside P-256,
  P-384, and P-521, and — explicitly — material carrying a private key.
- The client mints a short-lived JSON Web Token, signs it with the private half, and presents it as an ordinary bearer
  credential: the arrangement RFC 7523 describes and OpenID Connect deploys as `private_key_jwt`. It carries `typ:
  mailfathom-client-assertion+jwt`, an audience of `urn:mailfathom:mcp` or `urn:mailfathom:admin`, an expiry no more
  than five minutes ahead, and a fresh identifier the endpoint refuses to serve twice — so a captured assertion stops
  working on its own, and cannot be replayed even inside its remaining seconds.
- `mfctl login --mode keypair --private-key <file>` mints all of it and stores no credential; every command signs its
  own assertion.
- Rotating a key is an overlap with no secret to coordinate across two machines: add the new public key as a second
  entry, move the client to the new private key, remove the old entry.
  [Key pairs](https://krzysztof318.github.io/MailFathom/operations/mcp-endpoint.html#key-pairs) is the page.

**`mfctl` from the Windows Package Manager.** Each release submits its own manifest, so `winget install
MailFathom.mfctl` becomes a packaged path beside the download and `winget upgrade` carries you to the next release
([#498](https://github.com/Krzysztof318/MailFathom/pull/498)). The manifest names the same release asset the releases
page does and carries the same hash the checksum file does, so both paths install the same bytes and check them the
same way. A version is offered a little after it is attached here, because the community repository reviews the
submission; until one is accepted, the releases page is where the command comes from on every platform.

**The metrics and traces the libraries underneath MailFathom already emit.** Where `OTEL_EXPORTER_OTLP_ENDPOINT` names
a destination, four more meters now reach it: `Npgsql` for connection-pool state and command durations and counts,
`Microsoft.EntityFrameworkCore` for contexts, queries, saves, compiled-query cache hits, and concurrency failures,
`Experimental.ModelContextProtocol` for MCP session duration and per-operation duration broken down by protocol method
and tool name, and `Polly` for every outbound pipeline's attempts, outcomes, timeouts, and circuit-breaker transitions
([#521](https://github.com/Krzysztof318/MailFathom/pull/521)). Database commands and MCP protocol operations are
spanned as well and correlated with the request that caused them; the probe paths stay untraced, because a probe
arrives every few seconds and says the same thing every time.

- Every tag on them is a bounded set — a protocol method, a transport, one of the three tool names, an outcome — so
  none of them opens a time series per message or per person.
- What MailFathom publishes under a name of its own goes under exactly one: **`MailFathom`**, serving as both activity
  source and meter, which is what a dashboard filters on to see this process and nothing a library emits
  ([#510](https://github.com/Krzysztof318/MailFathom/pull/510)).
  [Telemetry](https://krzysztof318.github.io/MailFathom/operations/telemetry.html) records each of them.

### Changed

- **Breaking (configuration schema)** — **every surface states where it is served, and the host's own ways of naming a
  listener are refused.** `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`, `ASPNETCORE_HTTPS_PORTS`, `--urls`, and any entry
  under `Kestrel:Endpoints` each fail startup with a message naming the setting that replaces them. Write
  `McpEndpoint:BindAddress`, `McpEndpoint:Port`, and `McpEndpoint:Transport`; the administrative endpoint and the probes
  take the same three. **A deployment of your own that sets `ASPNETCORE_HTTP_PORTS` sets `McpEndpoint__Port` instead** —
  the published image and the packaged chart already do, so an upgrade that takes both as they ship needs no edit here
  ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)). They are refused rather than ignored because ignoring
  them is silent: Kestrel drops URL-shaped addresses as soon as a listener is bound in code, which every surface now
  does, and a configured endpoint would otherwise be bound beside them on a socket no section describes and no
  credential guards. A deployment that enables no surface at all is refused for the same reason.
- **Breaking (configuration schema)** — **the administrative endpoint's default port is `8080`, the MCP endpoint's**,
  where `0.3.0` gave it `8090`. Two surfaces may deliberately share one socket now — the posture a single-node
  deployment behind one ingress wants — so a deployment that enabled the administrative endpoint without stating a port
  publishes it wherever `8080` is published rather than on a port of its own. State `AdminEndpoint:Port`, where `8090`
  restores what you had, unless sharing is what you want; the socket serves each surface's own paths either way, and a
  path a surface does not own is still refused there with a `404`
  ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).
- **Breaking (configuration schema)** — **`Transport` decides what a surface's clear-text socket does**, where `0.3.0`
  inferred that from whether HTTPS profiles were configured. `Http` serves the routes and refuses profiles,
  `HttpAndHttps` binds the profiles and redirects the clear-text socket to them, and `HttpsOnly` does not open it at
  all. `Http` is the default, so adopting this release costs no certificate work
  ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).
- **Breaking (configuration schema)** — **`Https:Redirect` no longer binds a port of its own.** `0.3.0` gave it `8080`
  beside the MCP profiles and `8091` beside the administrative ones; the redirect now answers on the surface's own
  `BindAddress` and `Port`. A deployment that published `8091` to reach the administrative redirect publishes that
  surface's own port instead ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).
- **Breaking (configuration schema)** — **authentication is a list of the credentials an endpoint accepts**, where
  `0.3.0` named methods in a flag set and configured each in a sibling section. `McpEndpoint:Authentication` and
  `AdminEndpoint:Authentication` each take entries, and the block an entry carries is what selects the method that
  judges it — there is no setting naming the method any more. `Authentication: "ApiKey"` beside an `ApiKeys` list
  becomes one entry per key, each carrying an `ApiKey` block; `Authentication: "OAuth"` beside an `OAuth` section
  becomes an entry carrying that section. An entry carrying no block fails startup, named by its position
  ([#515](https://github.com/Krzysztof318/MailFathom/pull/515)).
  - **`RequiredScopes` is per entry** rather than per endpoint, so two authorization servers one endpoint accepts may
    demand different scopes. Every OAuth entry still names the same `Resource`, because the endpoint publishes one
    metadata document.
  - An empty list warns at startup exactly as `None` did, and a value written where the list belongs fails it rather
    than being read as a method name.
- **Breaking (configuration schema)** — **a setting only the process environment can deliver, written anywhere else,
  fails startup** naming every such variable at once, with error code `12002`. `OPENSSL_CONF`, `OTEL_SERVICE_NAME`, and
  every `OTEL_*`, `ASPNETCORE_*`, and `DOTNET_*` variable are read before MailFathom's configuration exists or by a
  library that never consults it, so a value written into an appsettings file, a provisioned configuration file, or a
  command-line argument reached nobody — while the file read it back happily and nothing said which of the two you were
  looking at. Set each on the host process, or remove it
  ([#509](https://github.com/Krzysztof318/MailFathom/pull/509)).
- **Every synchronized message is also cut into passages and stored**, in the same transaction that stores what was
  extracted from it, so a mailbox costs more storage per message than it did under `0.3.0` — roughly its extracted text
  again, in overlapping windows ([#488](https://github.com/Krzysztof318/MailFathom/pull/488)). A message that yielded no
  text is cut into nothing, mail stored before this release is not revisited, and nothing else in this release reads a
  passage.

### Removed

- **Breaking (deployment contract)** — **`GET /` no longer answers.** `0.3.0` served
  `{"service":"MailFathom","status":"ready"}` at the root of the application listener; the MCP endpoint's port serves
  `/mcp` and answers everything else with `404`. An external check pointed at `/` moves to the probes on their own
  listener — `/alive` for liveness, `/health` for readiness, `/started` for startup, on `HealthEndpoints:Port` unless
  you moved it ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).

### Fixed

- **A `file:` secret reference pointing at a FIFO or a stalled mount hung the host indefinitely.** Opening the file is
  bounded now, so an unreachable mount is reported as one line of the startup failure report rather than as a process
  that never finishes starting and never says why
  ([#511](https://github.com/Krzysztof318/MailFathom/pull/511)).
- **The device sign-in prompt raced the rest of `mfctl`'s output.** Both device-code flows handed the verification
  address and the short code to the console through a type that marshals onto a synchronization context a console
  process does not have, so nothing ordered the printing of the code against the wait for you to type it. The prompt now
  reaches the terminal before polling begins, on the thread that asked for it
  ([#418](https://github.com/Krzysztof318/MailFathom/pull/418)).
- **`HealthEndpoints:Enabled: false` beside an enabled administrative endpoint no longer costs the application
  listener** — the defect `0.3.0`'s notes named as shipped with it
  ([#419](https://github.com/Krzysztof318/MailFathom/pull/419)), and one that cannot recur now that each surface binds
  its own socket ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).

### Security

- **A key pair leaves nothing on the host worth stealing.** An API key is a shared secret, so a copy of every credential
  that reaches the mailbox sits in the configuration and in whatever produced it; a public key verifies the same client
  and is not a secret at all. It is the method for a scheduled job, which has no person to sign in as
  ([#527](https://github.com/Krzysztof318/MailFathom/pull/527)).
- **The administrative endpoint shares the MCP endpoint's port unless you say otherwise.** Administering the service is
  a different authority from reading the mailbox, and the probes answer without a credential, so putting either on the
  endpoint's port publishes it wherever that port is published. The ports exist so the decision is yours; take it rather
  than inherit it ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).
- **A listener nothing configured can no longer be bound.** Refusing the host's own address settings closes the case
  where a `Kestrel:Endpoints` entry survived beside a listener bound in code and served the routes on a socket no
  section describes, no credential guards, and no isolation middleware was composed for
  ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).

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
- **Startup now reports the rate limits once per enabled endpoint rather than once**, and under a different logger
  category: `MailFathom.Host.Hosting.Warnings.TransportRateLimitingStartupReport`, where `0.2.0` wrote
  `…McpRateLimitingStartupReport`. A log pipeline that matches on that category updates it, or it stops seeing the
  line ([#373](https://github.com/Krzysztof318/MailFathom/pull/373)).
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
- Each configured account synchronizes on a schedule of its own, and a failure is isolated to the account and the
  folder it happened in rather than stopping the rest ([#167](https://github.com/Krzysztof318/MailFathom/pull/167)).
- Remote deletions and flag changes are reconciled back onto the local copy
  ([#171](https://github.com/Krzysztof318/MailFathom/pull/171)).
- Synchronization is bounded by a configured earliest received date, so an established mailbox is not backfilled in
  full on first run ([#133](https://github.com/Krzysztof318/MailFathom/pull/133)).
- Senders, recipients, subjects, and dates are read out of each stored message and indexed, so listing a folder by date
  reads an index rather than re-parsing stored mail ([#98](https://github.com/Krzysztof318/MailFathom/pull/98),
  [#106](https://github.com/Krzysztof318/MailFathom/pull/106)).
- Message text is indexed for full-text search as mail arrives, and anything already stored before that indexing
  existed is caught up in the background rather than left unsearchable
  ([#110](https://github.com/Krzysztof318/MailFathom/pull/110)).
- A folder renamed or re-created on the mail server is detected rather than silently followed
  ([#94](https://github.com/Krzysztof318/MailFathom/pull/94)).
- Everything MailFathom calls out to runs under a configurable timeout, a bounded retry with jittered backoff, and a
  circuit breaker, set per class of dependency ([#83](https://github.com/Krzysztof318/MailFathom/pull/83)), and a
  dropped IMAP session is recovered under that same budget
  ([#92](https://github.com/Krzysztof318/MailFathom/pull/92)).

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
- One version identifies a deployment wherever you look for it: the assemblies, the image's tags and labels, the
  packaged chart's `appVersion`, the line the host writes at startup, and the server's MCP `initialize` response all
  report the same number ([#208](https://github.com/Krzysztof318/MailFathom/pull/208)).
- OpenTelemetry logs, metrics, and traces export when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and host start, startup
  failure, and shutdown are reported from a bootstrap logger that exists before configuration does
  ([#89](https://github.com/Krzysztof318/MailFathom/pull/89)).
- Every published artifact carries `LICENSE` and `NOTICE`
  ([#172](https://github.com/Krzysztof318/MailFathom/pull/172)). MailFathom is licensed under Apache-2.0, and
  [`THIRD_PARTY_LICENSES.md`](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md) registers
  every dependency it ships beside ([#173](https://github.com/Krzysztof318/MailFathom/pull/173)).

[0.5.0]: https://github.com/Krzysztof318/MailFathom/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/Krzysztof318/MailFathom/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/Krzysztof318/MailFathom/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/Krzysztof318/MailFathom/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/Krzysztof318/MailFathom/releases/tag/v0.1.0
