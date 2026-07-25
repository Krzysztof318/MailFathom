# Mail MCP Service — Architecture Draft

**Status:** Draft for review
**Date:** 2026-07-22
**Target:** Debian/Ubuntu, .NET 10, single owner, many mail accounts
**Enterprise posture:** Enterprise-grade architecture, GDPR-ready privacy posture, and future AGT governance seams from the beginning

The product and solution name is `MailMcp`. The repository uses the XML solution format in `MailMcp.slnx`; project directory and file names use short boundary names, while `Directory.Build.props` applies the `MailMcp.*` prefix to assembly names and root namespaces.

## 1. Purpose

The service synchronizes mail from multiple IMAP accounts, keeps a durable offline copy, sends mail through SMTP, indexes messages for lexical and semantic retrieval, and exposes controlled capabilities through a public MCP endpoint.

MailMcp is designed as an enterprise-grade system even before the first release implements every enterprise feature. The architecture must preserve clear seams for governance, compliance evidence, privacy controls, operational hardening, auditability, and future Agent Governance Toolkit (AGT) adoption. These requirements influence boundaries and data handling from day one, but they do not justify premature dependencies or broad first-release scope.

The initial public MCP surface is read-only. Sending exists as an application capability but is not exposed as an MCP tool until its authorization and confirmation flow is reviewed separately.

## 2. Confirmed decisions

- One service owner can configure many mailboxes across unrelated domains.
- PostgreSQL is the system of record for synchronization state, message metadata, extracted searchable text, RAG chunks, and embeddings. Runtime configuration is read through the .NET configuration pipeline; local first-release deployments use JSON plus secret references, while future write-side/admin configuration storage is deferred to a separate decision.
- Full RFC 822 messages, including their MIME attachments, are stored in a dedicated PostgreSQL table using `bytea`.
- Raw content is accessed through `IEmailContentStore`; a later release will migrate that content to MinIO without changing domain or application use cases.
- MailKit handles IMAP, SMTP, MIME, TLS modes, and standard SASL mechanisms.
- Synchronization must never mark a remote message as read.
- MCP reads from local storage and never performs a blocking IMAP fetch while serving a tool request.
- Microsoft Agent Framework is the primary agent and RAG orchestration framework.
- Semantic Kernel may be added only as an adapter for a capability unavailable or insufficient in Agent Framework.
- Chat and embedding providers remain configuration choices, not constants compiled into project code. The initial deployment profile uses OpenAI `text-embedding-3-small` for embeddings and `gpt-5.6-terra` for chat when that model is available to the deployment; startup validation fails or disables `ask_mail` if configured model access is unavailable.
- The public server supports ChatGPT and remains compatible with other remote MCP clients such as Claude Code.
- GDPR readiness is an architectural requirement. First-release features must not block later implementation of data-subject access/export, erasure, restriction, retention, and processing-record workflows.
- Embeddings, chunks, snippets, audit events, and model/tool traces inherit the sensitivity and governance constraints of the source mailbox data unless a reviewed privacy design explicitly proves otherwise.
- Unit tests are developed from the beginning with xUnit.net v3, Microsoft Testing Platform v2, and NSubstitute.
- Integration tests are planned for a later phase but are not created in the initial solution.
- The solution is named `MailMcp`, uses `MailMcp.slnx`, uses short project directory and file names, and applies the `MailMcp.*` prefix consistently to assemblies and root namespaces.

## 2.1 Implementation status

The first implemented vertical slice covers periodic read-only IMAP reconciliation, message-count-bounded IMAP metadata batches, a bounded number of batches per run, raw MIME size limits, per-folder worker failure isolation, domain identities for `(account, folder, UIDVALIDITY, UID)`, application-owned synchronization and persistence ports, EF Core PostgreSQL mappings for metadata/content/checkpoints, and a disabled-by-default background worker. The following draft capabilities remain pending: deployment-specific secret binding, reviewed migrations, IMAP IDLE, IMAP NOTIFY, MCP read tools, RAG indexing, SMTP outbox processing, integration tests including ADR 001 PostgreSQL mapping/constraint verification, and production migration operations.

The gap between this draft and the implemented code is decomposed into individually reviewable specifications under `specs/`, indexed by [`specs/README.md`](README.md). Specifications 01 through 21 cover delivery stages 1 through 4 plus the deferred schema and integration-verification work; stages 5 through 10 are decomposed when that segment nears completion. This draft remains the durable description of the target architecture, and a specification that departs from it must say so explicitly, as specifications 08, 19, and 03 do for stage ordering and for the resilience mechanism this draft did not name.

## 3. Scope

### 3.1 Included

- Multiple independently configured IMAP and SMTP accounts.
- Full initial synchronization, with an optional per-account lower date boundary.
- Push-style synchronization using IMAP IDLE and NOTIFY where supported.
- Periodic reconciliation when server capabilities are limited.
- Offline message metadata, bodies, MIME structure, and attachment metadata.
- PostgreSQL full-text search and pgvector semantic search.
- Background chunking and embedding generation.
- Agent Framework RAG integration through `TextSearchProvider`.
- First-release read-only MCP tools:
  - `list_emails`
  - `get_email_content`
  - `search_emails`
  - `ask_mail`, advertised only when a chat provider and embedding profile are configured and healthy.
- SMTP sending application service and durable outbox are designed but not first-release priorities; the first release prioritizes IMAP synchronization, RAG, and the four read-only MCP tools.
- OAuth 2.1, HTTPS, and client-aware mTLS policies.
- Aspire AppHost for first-release local development orchestration of the host, PostgreSQL, and developer observability.
- Administration is primarily configuration-file driven in the first release; a dedicated `mcpmail` CLI is a future operational convenience, not an initial requirement.

### 3.2 Excluded from the first release

- Multiple service users or tenants.
- Fully automated GDPR data-subject request workflows, retention-policy engines, legal-hold workflows, DPIA tooling, or compliance-report generation. The first release must keep seams for these capabilities but does not implement them end to end.
- Runtime AGT enforcement. AGT is evaluated later for agent-mediated actions and enterprise governance, but first-release read-only tools rely on deterministic application authorization and safe local retrieval.
- Editing remote flags, including `\Seen`, from MCP.
- Moving or deleting messages.
- Returning attachment bytes through MCP.
- Autonomous mail actions by an agent.
- Training or fine-tuning models on mail.
- A custom OAuth authorization server.
- A MinIO process or MinIO SDK dependency; these are introduced only during the planned object-storage migration.

## 4. Architecture

The service is a clean-architecture modular monolith. One deployable ASP.NET Core host contains the public Kestrel endpoint and background workers, while internal projects enforce boundaries between mail domain logic, application use cases, infrastructure, RAG, and protocol adapters. Kestrel is the Internet-facing HTTPS server; no reverse proxy is required in the initial deployment.

```text
ChatGPT                 Claude Code / other MCP clients
   |                                  |
HTTPS + OpenAI mTLS + OAuth           | HTTPS + OAuth
   |                                  |
   +---------------- Kestrel ----------+
                              |
                        ASP.NET Core Host
                              |
                 +------------+-------------+
                 |                          |
             MCP adapter               Background workers
                 |                    IMAP sync / RAG / SMTP
                 +------------+-------------+
                              |
                       Application layer
                    /          |          \
               Mail domain   Retrieval   Agent Framework
                    |          |          TextSearchProvider
                    +----------+---------------+
                               |
                    +----------+----------+
                    |                     |
                 MailKit             PostgreSQL
               IMAP + SMTP       metadata + raw MIME
                                  FTS + pgvector
```

### 4.1 Enterprise-grade design posture

Enterprise grade means MailMcp is operable, auditable, secure by default, privacy-aware, and maintainable under change. The initial repository should therefore prefer explicit contracts, deterministic behavior, bounded resource use, documented trade-offs, repeatable verification, least privilege, durable state transitions, and safe failure modes. Enterprise grade does not mean adding every enterprise subsystem immediately; it means first-release shortcuts must not close the path to later governance, compliance, recovery, or scale-out work.

GDPR readiness is treated as a design invariant rather than a post-release documentation exercise. MailMcp stores email bodies, headers, recipient data, attachments, search text, chunks, embeddings, and tool traces that may all contain personal data. The architecture must therefore keep ownership, purpose, retention, access, export, deletion, and audit boundaries explicit. The first release is not a complete compliance product, but it must avoid designs that make later compliance workflows impractical, such as untracked derived data, unbounded logs, provider-specific records in core contracts, or deletion paths that cannot reach chunks and embeddings.

AGT is reserved for future governance of agent-mediated and higher-risk tool calls. The first release keeps read-only MCP tools deterministic and application-authorized, while preserving an adapter seam where a future governance engine can evaluate tool invocations, policy decisions, contextual risk, and audit outcomes without changing domain or application contracts.

### 4.2 Boundary rules

Dependencies point inward and never flow in the wrong direction. Outer adapters may know about inner contracts, but inner layers must not reference implementation details from their consumers or adapters. A type from EF Core, Npgsql, MailKit, ASP.NET Core, MCP SDKs, Agent Framework, Semantic Kernel, pgvector, container tooling, systemd integration, or a hosted AI provider must not appear in `Domain` or `Application` contracts unless this draft explicitly says otherwise. Cross-layer communication uses application-owned request/response contracts, ports, domain value objects, and explicit mappers.

Allowed project references are intentionally narrow:

```text
Domain        -> no project references
Application   -> Domain
Infrastructure-> Application, Domain
AI            -> Application, Domain
Mcp           -> Application, Domain
Host          -> all runtime projects as the composition root
AppHost       -> Host plus development orchestration resources
Cli           -> Application plus required adapters when introduced
```

- `Domain` contains the mail business model and business invariants: entities, value objects, domain events, domain errors, domain services that require no I/O, account identity, folder identity, IMAP occurrence identity, message metadata, content integrity facts, synchronization checkpoint concepts, delivery/outbox state, embedding-profile value rules that are provider-neutral, and validation such as valid UID/UIDVALIDITY combinations or unsafe transport choices. `Domain` is not the general home for application ports. It may define an interface only when the abstraction is itself a pure domain policy or strategy with no persistence, network, clock, configuration, logging, or provider concern. It contains no persistence models, no configuration binding types, no serialization attributes for external protocols, and no infrastructure framework dependencies.
- `Application` contains the use-case implementations. Each use case coordinates domain objects, enforces authorization close to the operation, defines explicit input/output contracts, returns application result/error types, and owns ports for effects that must be supplied by outer layers: persistence, local MIME content storage, mail sessions/transports, time, cryptography, search, embedding, chat, and background job scheduling. It depends only on `Domain`; it owns abstractions such as `IEmailContentStore` and never exposes EF Core entities, MailKit objects, MCP SDK types, or AI-provider-specific types.
- `Infrastructure` contains implementations of application ports for PostgreSQL persistence, EF Core migrations, Npgsql raw-MIME storage, MailKit IMAP/SMTP sessions, Data Protection persistence, secret loading/protection adapters, and OpenTelemetry/exporter wiring that is not host-specific. It maps infrastructure records to application/domain contracts and keeps database schema, SQL, `bytea`, pgvector, SASL, TLS, and MailKit details inside adapters.
- `AI` contains implementations of application ports for chunking, embedding generation, hybrid retrieval, and Agent Framework composition. It may depend on provider SDKs behind adapters, but provider-specific request/response types never leak into `Application`, `Domain`, `Mcp`, or persistence contracts.
- `Mcp` maps MCP schemas to application requests and maps safe application results/errors back to MCP responses. It contains no persistence, mail protocol, RAG indexing, or database transaction logic.
- `Host` contains only configuration loading, options validation, dependency injection, middleware, endpoint mapping, hosted-service registration, startup migration invocation, and process lifetime. Business decisions remain in `Application` or `Domain`; adapter implementations remain in `Infrastructure`, `AI`, or `Mcp`.
- A future `Cli` project hosts the `mcpmail` administration tool and reuses application services for account setup, connection tests, synchronization status, and RAG profile management. It is not part of the first implementation slice.

## 5. Proposed project structure

```text
mail-mcp/
├── MailMcp.slnx
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
├── README.md
├── .editorconfig
├── .gitignore
├── src/
│   ├── Domain/
│   │   ├── Accounts/
│   │   ├── Folders/
│   │   ├── Messages/
│   │   ├── Synchronization/
│   │   └── Delivery/
│   ├── Application/
│   │   ├── Accounts/
│   │   ├── Messages/
│   │   │   ├── ListEmails/
│   │   │   ├── GetEmailContent/
│   │   │   └── SearchEmails/
│   │   ├── Synchronization/
│   │   ├── Delivery/
│   │   ├── Retrieval/
│   │   ├── MessageContent/
│   │   └── Abstractions/
│   ├── Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── Configurations/
│   │   │   └── Migrations/
│   │   ├── Mail/MailKit/
│   │   ├── Security/
│   │   └── Observability/
│   ├── AI/
│   │   ├── Chunking/
│   │   ├── Embeddings/
│   │   ├── Retrieval/
│   │   ├── Orchestration/
│   │   └── ProviderAdapters/
│   ├── Mcp/
│   │   ├── Tools/
│   │   ├── Authentication/
│   │   └── Serialization/
│   ├── Host/
│   │   ├── Configuration/
│   │   ├── Hosting/
│   │   └── Program.cs
│   ├── AppHost/
│   │   └── Program.cs
│   └── Cli/                          # future `mcpmail` CLI, not initial scaffold
│       ├── Accounts/
│       ├── Synchronization/
│       └── Rag/
├── tests/
│   ├── Domain.UnitTests/
│   ├── Application.UnitTests/
│   ├── Infrastructure.UnitTests/
│   ├── AI.UnitTests/
│   └── Mcp.UnitTests/
├── deploy/
│   ├── compose.yaml
│   ├── postgres/
│   ├── certificates/
│   └── systemd/
└── specs/
    ├── README.md                     # roadmap index over the numbered specifications
    ├── 2026-07-22-mail-mcp-architecture-draft.md
    └── NN-<topic>.md                 # one PR-sized specification per unit of work
```

Each unit-test project references only the production boundary it verifies and the minimum required upstream contracts. No integration-test project is added in the initial scaffold.

## 6. Technology baseline

| Area | Choice | Notes |
|---|---|---|
| Runtime | .NET 10 | LTS, supported through November 2028 |
| Host | ASP.NET Core 10 | Public Internet-facing Kestrel with HTTPS and client certificates |
| MCP | `ModelContextProtocol.AspNetCore` 1.4.1 | Official C# MCP SDK, Streamable HTTP transport |
| Mail | `MailKit` 4.17.0 | IMAP, SMTP, MIME, IDLE, NOTIFY, SASL |
| ORM | EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 | Relational persistence and migrations |
| Database | PostgreSQL with pgvector 0.8.2 | Metadata, raw MIME, FTS, chunks, embeddings |
| Vector mapping | `Pgvector.EntityFrameworkCore` 0.3.0 | EF Core 9/10 support |
| Agent orchestration | `Microsoft.Agents.AI` | `ChatClientAgent` and `TextSearchProvider` |
| AI abstractions | `Microsoft.Extensions.AI` | Provider-neutral `IChatClient` and embedding abstractions |
| Optional compatibility | Semantic Kernel | Added behind adapters only when justified by a missing MAF capability |
| Authentication | ASP.NET Core JWT bearer + external OAuth 2.1 IdP | Auth0 is the default deployment choice |
| Resilience | Polly v8 resilience pipelines | Named pipelines per outbound dependency class, resolved through `ResiliencePipelineProvider`; see section 17.1 |
| Observability | Aspire ServiceDefaults + OpenTelemetry + JSON console logging | Logs, metrics, traces, health checks, and OTLP export are scaffolded through shared extensions |
| Unit testing | xUnit.net v3 + Microsoft Testing Platform v2 + NSubstitute | Isolated behavior tests and mocked protocol boundaries |
| Integration testing | Aspire test mode via `Aspire.Hosting.Testing` | Deferred phase; drives the real `AppHost` app model rather than a parallel container definition |
| Local orchestration | Aspire AppHost | First-release development-time orchestration and observability for MailMcp and PostgreSQL |
| Future CLI parser | `System.CommandLine` | Official Microsoft command-line parser for the later `mcpmail` administration CLI |

Package versions are centrally pinned in `Directory.Packages.props`. The .NET SDK is pinned in `global.json`. Shared compiler, analyzer, nullable, documentation, warnings-as-errors, and test-project defaults belong in `Directory.Build.props` wherever they can be expressed once for the repository. Preview Agent Framework packages are acceptable, but every update is explicit and reviewed.

### 6.1 Unit testing strategy

Unit tests are delivered with every behavior change. They use xUnit.net v3 on Microsoft Testing Platform v2, follow Arrange, Act, Assert with explicit `// Arrange`, `// Act`, and `// Assert` comments, remain deterministic and order-independent, and avoid network, filesystem, database, container, and wall-clock dependencies.

The application layer defines narrow interfaces for IMAP sessions, SMTP transports, message-content storage, local persistence, and AI providers. Unit tests use NSubstitute to model IMAP/SMTP server behavior through these interfaces, including advertised capabilities, authentication results, mailbox responses, disconnects, timeouts, and transient failures. Production code does not attempt to mock concrete MailKit clients.

The initial unit suite prioritizes:

- preserving the remote `\Seen` flag on every metadata and content retrieval path;
- UIDVALIDITY changes, duplicate events, idempotent resynchronization, and reconnect behavior;
- STARTTLS/TLS policy, authentication allow-lists, and rejection of unsafe configuration;
- SMTP outbox state transitions, retries, cancellation, and duplicate-send prevention when SMTP work begins after the initial IMAP/RAG/MCP release slice;
- offline list/get/search behavior when IMAP is unavailable;
- MCP authorization, input validation, pagination, and bounded output;
- chunking, hybrid-result fusion, citations, and provider-independent RAG orchestration.

### 6.2 Future integration testing

A separate integration-test suite is planned after the unit-tested application and protocol boundaries stabilize. It will validate MailKit against controlled IMAP/SMTP servers, PostgreSQL with pgvector, OAuth discovery, TLS, and mTLS. No integration-test project or dependency is added during the initial phase.

When that phase begins, the suite drives the existing `AppHost` app model through Aspire test mode using `DistributedApplicationTestingBuilder` from the `Aspire.Hosting.Testing` package, rather than defining a second, parallel container topology. Reusing the app model keeps the orchestration under test identical to the orchestration developers run, and lets a containerized dependency such as an IMAP server be added as an ordinary resource. Specification 20 establishes the harness and pays off the PostgreSQL verification that ADR 001 defers; specification 21 adds the IMAP wire-behavior coverage, including the `\Seen` invariant that a substituted port cannot prove.

## 7. Mail account configuration

Each `MailboxAccount` has independent IMAP and SMTP settings.

### 7.1 Connection security

- `Auto`
- `TlsOnConnect`
- `StartTlsRequired`
- `StartTlsWhenAvailable`
- `None`

`None` and opportunistic downgrade behavior require `AllowInsecureConnection=true`. Certificate validation is always enabled. Private or self-signed servers are supported by adding a trusted CA; disabling certificate validation is not a production option.

### 7.2 Authentication

- Automatic selection from a configured allow-list.
- No authentication.
- Username and password with server-supported SASL mechanisms.
- Explicit mechanisms supported by MailKit, including PLAIN, LOGIN, CRAM-MD5, DIGEST-MD5, SCRAM-SHA variants, NTLM, XOAUTH2, and OAUTHBEARER.

MailKit 4.17.0 does not provide a built-in GSSAPI/Kerberos SASL mechanism. Version 1 therefore covers the standard mechanisms implemented by MailKit; GSSAPI would require a separately reviewed authentication adapter if a specific server makes it necessary.

Clear-text password mechanisms are rejected over an unencrypted channel unless both insecure transport and that mechanism are explicitly allowed.

### 7.3 Configuration files and secret handling

The first release uses .NET configuration for runtime settings and local JSON files for non-secret operational settings. ASP.NET Core's default configuration model has official Microsoft support for `appsettings.json` and `appsettings.{Environment}.json` through the JSON configuration provider, so MailMcp should not add a YAML parser or YAML configuration provider for first-release application configuration. JSON remains an operator-facing local source format only; domain, application, infrastructure, AI, and MCP projects consume validated options or mapped business settings and never parse configuration files directly. Durable write-side/admin configuration storage, including whether a future store is file-backed, database-backed, cloud-backed, or service-backed, is intentionally deferred.

Configuration precedence is explicit: built-in defaults, committed example JSON, deployment JSON, environment-specific JSON overrides, environment variables for non-secret automation, and command-line overrides. The host validates all bound options at startup with fail-fast errors for missing TLS material, unsafe mail transport settings, invalid OAuth audience/resource values, unbounded result sizes, missing database settings, incompatible RAG profiles, or unresolved secret references.

Secrets are never committed to JSON. JSON may contain secret references such as systemd credential names, protected file paths, container secret names, or external secret-provider keys. Development may use .NET Secret Manager for local-only convenience, but because user secrets are not encrypted and are not a production secret store, production deployments should prefer systemd credentials for native Linux services, container secrets for containerized deployments, an approved external secret provider, or protected files provisioned outside Git.

For native systemd deployments, MailMcp should load sensitive values from systemd's service credential mechanism rather than environment variables. The systemd documentation describes credentials as a service-manager feature for passing sensitive keys, certificates, passwords, identity information, and similar data to services; it also notes that credentials avoid common environment-variable drawbacks such as inheritance through the process tree and provide per-service access checks. Unit files should use `LoadCredential=` or encrypted credentials managed with `systemd-creds` where appropriate, and the host should read the credential files via the runtime credentials directory exposed to the service. JSON configuration stores only the credential name or logical reference, not the secret value itself.

- Account secrets are not stored as ordinary PostgreSQL configuration rows in the first release. If a later write-side/admin configuration store persists encrypted account secret material, that storage model must be approved by a separate ADR.
- ASP.NET Core Data Protection protects any MailMcp-owned ciphertext with a persistent key ring.
- The key-ring protection certificate is injected through a systemd credential or container secret and is never stored in PostgreSQL or Git.
- PostgreSQL, SMTP, and IMAP secrets never appear in logs, traces, MCP results, or exception messages.

## 8. Domain model

### 8.1 Main entities

- `MailboxAccount`: one configured mail identity and its IMAP/SMTP settings.
- `MailFolder`: remote folder identity, sync policy, UID validity, and synchronization cursor.
- `StoredEmail`: local representation of one IMAP email occurrence in one folder, identified by `EmailOccurrenceId`.
- `EmailMessageContent`: locally stored raw RFC 822 content and its integrity metadata.
- `SynchronizationCheckpoint`: last successful UID and modification sequence per folder.
- `EmailChunk`: deterministic searchable fragment linked to a message.
- `EmbeddingProfile`: provider-independent model identifier, dimensions, distance metric, and indexing version.
- `EmailEmbedding`: chunk vector associated with one embedding profile.
- `OutgoingMessage`: durable SMTP outbox entry with delivery state and idempotency key.

### 8.2 Stable identity

The remote identity is the tuple:

```text
MailboxAccountId + FolderId + UIDVALIDITY + UID
```

The local public identifier is a UUIDv7. IMAP sequence numbers are never persisted as identity. A change in `UIDVALIDITY` invalidates the folder cursor and triggers controlled reconciliation.

## 9. PostgreSQL design

### 9.1 Core tables

- `mailbox_accounts`
- `mail_folders`
- `stored_emails`
- `email_message_contents`
- `synchronization_checkpoints`
- `embedding_profiles`
- `email_chunks`
- `email_embeddings`
- `outgoing_messages`
- `schema_jobs`

`stored_emails` contains normalized metadata, remote flags, sender address, recipient arrays, dates, subject, message ID, thread headers, attachment summary, extracted plain text, content state, and synchronization timestamps.

`email_message_contents` is a one-to-one table whose primary key is also a foreign key to `stored_emails`. It contains `raw_mime bytea`, MIME byte length, SHA-256 hash, and storage timestamp. Keeping the large binary value in a separate table ensures mailbox timelines and search queries never load or track raw MIME. PostgreSQL TOAST stores oversized `bytea` values out of line automatically.

### 9.2 Required indexes

- Unique: `(mail_folder_id, uid_validity, uid)`.
- Timeline: `(mailbox_account_id, received_at DESC, id DESC)`.
- Folder timeline: `(mail_folder_id, received_at DESC, id DESC)`.
- Sender and normalized recipient indexes.
- GIN full-text index over subject, addresses, and normalized body text.
- Partial indexes excluding remotely deleted messages.
- HNSW cosine-distance index per active embedding profile.

Pagination is keyset-based. Offset pagination is not used for mailbox timelines.

Tens of thousands of messages do not justify table partitioning. Partitioning is introduced only after measurements show a concrete maintenance or query problem.

### 9.3 Embedding dimensions decided later

`email_embeddings.embedding` uses pgvector's dimensionless `vector` type together with `embedding_profile_id` and an explicit dimension check. pgvector supports vectors of different dimensions in one column and profile-specific expression indexes.

When an embedding profile is activated, the administration command creates a partial HNSW index equivalent to:

```sql
CREATE INDEX email_embeddings_profile_1_hnsw
ON email_embeddings USING hnsw
((embedding::vector(1536)) vector_cosine_ops)
WHERE embedding_profile_id = 1;
```

The SQL above is an illustrative profile with ID `1` and 1536 dimensions; it does not select an embedding provider. The administration command substitutes validated values for the configured profile rather than accepting raw SQL from a user. Before an approximate index is built, exact vector search remains correct but slower.

## 10. PostgreSQL MIME storage

- One `email_message_contents` row stores the complete raw RFC 822 message for each synchronized email occurrence.
- Raw MIME is written and read through a focused Npgsql repository rather than ordinary tracked EF Core mailbox entities.
- Content insertion is idempotent and occurs in the same local transaction as the corresponding message metadata and synchronization state update.
- The repository verifies the recorded byte length and SHA-256 hash when consistency repair is required.
- `get_email_content` never downloads from IMAP. Missing local content produces a consistency error and schedules background repair.
- Remote images and linked resources in HTML mail are never fetched automatically.
- Large MIME and attachment data is streamed wherever the driver API permits and is never included in list or search projections.

Remote expunge marks the message as deleted; its raw MIME is retained for a configurable grace period before PostgreSQL garbage collection. PostgreSQL backups are the authoritative offline-mail backup in the initial deployment.


## 11. IMAP synchronization

### 11.1 Never mark mail as read

This is a system invariant, not an option.

- Synchronization opens folders with `FolderAccess.ReadOnly`, causing IMAP `EXAMINE` semantics.
- Message bodies and headers are retrieved with PEEK semantics.
- Synchronization interfaces expose no operation that writes flags.
- No code path calls `AddFlags`, `SetFlags`, or equivalent methods.
- Stored `\Seen` is a snapshot of remote state only.
- MCP reads local data and therefore cannot affect the remote flag.

### 11.2 Initial synchronization

1. Connect, negotiate TLS, and authenticate.
2. Discover configured folders and capabilities.
3. Record `UIDVALIDITY`, `UIDNEXT`, and `HIGHESTMODSEQ` when available.
4. Support a future custom folder mapping layer that lets users work with stable friendly names such as `Inbox` or `Skrzynka odbiorcza` while the adapter maps those names to provider/server folder identifiers such as `server_inbox334`. This mapping belongs at the configuration/application boundary and must preserve auditability: logs and UI may show the friendly name, while synchronization stores the resolved remote folder identity needed for IMAP safety. This is a specification requirement only in the current slice; do not implement runtime mapping until the folder-configuration design is reviewed.
4. Enumerate UIDs in bounded batches.
5. Fetch metadata and raw MIME using read-only PEEK operations.
6. Store raw MIME, metadata, and checkpoint transactionally in PostgreSQL.
7. Verify the persisted content length and hash.
8. Queue extraction, chunking, and embedding jobs.

The default is a complete initial synchronization. An optional `InitialSyncSinceUtc` limits history for a specific account.

### 11.3 Continuous synchronization

- INBOX uses IDLE by default.
- NOTIFY is used when the server supports notifications for multiple folders.
- IDLE is periodically renewed before typical server timeouts.
- Time-based synchronization is the configured fallback and may also be selected explicitly for accounts where push-style behavior is not desired.
- When a notification arrives, IDLE is exited, changes are synchronized, and IDLE is re-entered.
- CONDSTORE/QRESYNC and modification sequences are used when available.
- Servers without IDLE fall back to bounded polling.
- Non-INBOX folders use NOTIFY or scheduled reconciliation according to account policy.
- Reconnects use exponential backoff with jitter and do not block other accounts.

Each account has an independent supervisor. A failing account cannot stop synchronization of another account.

## 12. RAG pipeline

### 12.1 Ingestion

1. Parse the locally stored MIME message with MimeKit.
2. Select human-readable plain text; derive safe text from HTML only when necessary.
3. Remove quoted history and signatures conservatively while retaining the original text.
4. Create deterministic chunks with overlap and stable content hashes.
5. Store chunks and PostgreSQL `tsvector` data.
6. When embeddings are enabled for the instance and the active profile is healthy, automatically generate embeddings through the configured `IEmbeddingGenerator` for new or changed messages.
7. Upsert vectors under the active `EmbeddingProfile`.

Chunk records include account, folder, message, sender, recipients, date, subject, and source coordinates. The agent can therefore cite a stable local message ID and the exact chunk used.

### 12.2 Provider-neutral operation

- RAG is part of the first release. The four first-release MCP tools are `list_emails`, `get_email_content`, `search_emails`, and `ask_mail`; `ask_mail` is exposed only when the configured chat and embedding profile are enabled and healthy.
- Without an embedding provider, synchronization and PostgreSQL full-text search continue to work, but semantic search and `ask_mail` are disabled with a safe capability status.
- Without a chat provider, `list_emails`, `get_email_content`, and lexical `search_emails` remain available, while `ask_mail` is not advertised as available.
- When an embedding provider is configured, `search_emails` becomes hybrid.
- The default first-release configuration profile uses OpenAI `text-embedding-3-small` for embeddings and `gpt-5.6-terra` for chat, but these names are configuration values validated at startup and are never hard-coded into domain, application, MCP, or AI contracts.
- Changing an embedding profile creates a new vector generation and reindexes in the background; old vectors remain active until the new generation is complete.

### 12.3 Hybrid retrieval

Hybrid search combines:

- PostgreSQL full-text rank.
- pgvector cosine similarity.
- Structured filters such as account, folder, sender, recipients, date range, and attachment presence.
- Reciprocal Rank Fusion to combine lexical and semantic ranks without model-specific score calibration.

Retrieval returns bounded snippets and source identifiers. It never places complete mailboxes or unrelated messages into model context.

### 12.4 Microsoft Agent Framework

`ChatClientAgent` is composed over a provider-neutral `IChatClient`. `TextSearchProvider` delegates retrieval to the application `IEmailKnowledgeSearch` port and operates in `OnDemandFunctionCalling` mode so the model retrieves context only when needed.

Retrieved email is untrusted input. The context formatter clearly separates mail content from system instructions, preserves source attribution, and instructs the agent never to treat message text as commands. Account and scope filters are applied before content reaches Agent Framework.

### 12.5 Semantic Kernel fallback

Semantic Kernel is not referenced by domain, application, MCP, or persistence projects. If a required connector, embedding implementation, or orchestration capability is absent from Agent Framework, it is added inside `AI/ProviderAdapters` behind an existing application interface. MAF remains the public orchestration boundary.

## 13. MCP tools

### 13.1 `list_emails`

Returns local message summaries using structured filters:

- account IDs
- folders
- sender and recipients
- subject fragment
- received date range
- remote seen/unseen state
- attachment presence
- free-text query
- sort direction
- page size and continuation cursor

Maximum page size is 100. Results include `synchronizedAt`, account identity, stable message ID, remote flags, and whether full content is locally available.

### 13.2 `get_email_content`

Accepts one stable local message ID and returns:

- normalized headers
- plain-text body
- optional sanitized HTML representation
- attachment metadata without bytes
- source account and folder
- remote flag snapshot
- truncation metadata

Plain text is the default. Large bodies are bounded and report truncation rather than overflowing the MCP context.

### 13.3 `search_emails`

Runs local lexical or hybrid retrieval. It returns ranked snippets with stable message IDs and structured source metadata. It does not call a chat model.

### 13.4 `ask_mail`

Available only when chat and embedding profiles are healthy. It uses Agent Framework and RAG to answer questions with citations to stable local message IDs. It cannot send, delete, move, or mark mail as read.

### 13.5 Tool annotations and authorization

Every MCP tool descriptor uses the current protocol metadata deliberately, including human-readable `title`, input and output schemas, safe descriptions, OAuth security scheme metadata, and behavior annotations. The read-only tools explicitly set `readOnlyHint=true`, `destructiveHint=false`, `idempotentHint=true`, and `openWorldHint=false` unless a future tool genuinely reaches outside MailMcp-controlled local state. These annotations are contract metadata, not comments; tests should verify the advertised `tools/list` metadata so clients can reason about read-only, destructive, idempotent, and external-world behavior before invoking a tool.

- Read tools declare `readOnlyHint=true` and non-destructive semantics.
- Each tool declares an OAuth security scheme and the required `mail.read` scope.
- Future sending requires `mail.send` and explicit ChatGPT confirmation semantics.
- Tool handlers recheck authentication, owner identity, audience, scopes, and message/account access.

## 14. SMTP delivery

- Application command creates a MIME message and durable `OutgoingMessage` record.
- A background worker claims outbox rows and sends through the selected account's SMTP configuration.
- An idempotency key prevents accidental duplicate delivery.
- Retry is limited to transient failures; permanent SMTP failures are terminal and visible through CLI status.
- A successfully sent message may be appended to a configured Sent folder when the server does not do this automatically.
- SMTP capability is not exposed through MCP in the first public tool set and is not a first-release priority compared with IMAP synchronization, RAG, and read-only MCP tools.

## 15. Public MCP security

### 15.1 OAuth 2.1

- External established identity provider; Auth0 is the default deployment profile.
- Authorization Code flow with PKCE `S256`.
- Protected resource metadata at `/.well-known/oauth-protected-resource`.
- OAuth or OIDC discovery metadata from the authorization server.
- The MCP resource identifier is echoed through authorization and token exchange and validated as token audience.
- Access tokens are validated for signature, issuer, audience/resource, lifetime, client identity, owner subject, and scopes on every request.
- Only the configured owner subject can access the service even if the IdP tenant contains other users.
- Unauthorized responses include the required `WWW-Authenticate` resource metadata challenge.

### 15.2 Two client profiles

#### ChatGPT profile

- Dedicated hostname, for example `chatgpt.mail-mcp.example.com`.
- HTTPS and OAuth are mandatory.
- mTLS client certificate is mandatory.
- Trust store contains the OpenAI Root CA and OpenAI Connectors intermediate CA.
- Validation requires a valid client-authentication chain and SAN `mtls.prod.connectors.openai.com`.
- Leaf fingerprints are not pinned because OpenAI rotates leaf certificates.

#### General MCP profile

- Dedicated hostname, for example `mcp.mail-mcp.example.com`.
- HTTPS and OAuth are mandatory.
- mTLS is optional and can become required per registered client policy.
- Trusted client CA bundles, expected SANs, and certificate policies are configuration entries, not code changes.
- This profile supports Claude Code, whose official remote MCP flow documents HTTP and OAuth but does not currently document an OpenAI-style managed client certificate.

Both hostnames route to the same MCP application and enforce the same owner and tool scopes. Client profile differences affect transport authentication only.

### 15.3 Kestrel TLS and client certificates

Kestrel terminates public TLS directly on port 443 and is configured with the server certificate and exact allowed host names. Its HTTPS endpoint requests client certificates with `ClientCertificateMode.AllowCertificate`, allowing generic OAuth clients to connect without one. The ChatGPT authorization policy requires the certificate and validates the OpenAI chain, client-authentication usage, validity period, and expected SAN before any MCP tool executes. The general MCP policy validates a certificate only when its registered client profile requires one.

Certificate material and private keys are supplied through protected deployment credentials rather than committed configuration. Because no proxy forwards certificates, the application trusts only the certificate obtained from the Kestrel TLS connection and ignores certificate-like request headers.

### 15.4 Additional controls

- TLS 1.2 or newer; TLS 1.3 preferred.
- Exact allowed host names; no wildcard host acceptance.
- Restrictive CORS or no CORS when browser access is unnecessary.
- Request size, concurrency, and rate limits.
- OpenAI egress IP allowlisting may be an additional signal but never replaces mTLS or OAuth.
- Health, metrics, PostgreSQL, and administration endpoints are not public.

## 16. GDPR and privacy architecture

MailMcp is not launched as a complete GDPR automation product, but it must be GDPR-ready by design. Regulation (EU) 2016/679 establishes principles such as lawful, fair, transparent processing; purpose limitation; data minimization; accuracy; storage limitation; integrity and confidentiality; accountability; data protection by design and by default; records of processing activities; security of processing; and data-subject rights including access, erasure, restriction, and portability. The first-release architecture records these concerns explicitly so later compliance work extends known seams instead of retrofitting unknown data flows.

### 16.1 Personal-data inventory

The following data classes are treated as personal data or potentially personal data by default:

- mailbox account identifiers and connection configuration;
- sender, recipient, subject, message IDs, thread headers, dates, folder names, and remote flags;
- raw RFC 822 content, MIME parts, attachment metadata, extracted text, sanitized HTML, snippets, chunks, and embeddings;
- OAuth subject identifiers, client identifiers, authorization decisions, and operational audit events;
- model prompts, retrieved context, generated answers, token usage, and tool-call traces whenever they include or derive from mail content.

Derived search artifacts are not considered anonymous merely because they are transformed. Chunks, embeddings, full-text indexes, snippets, cached context, and agent traces inherit the retention, access-control, export, and deletion requirements of their source messages unless a future privacy review documents a stronger guarantee.

### 16.2 First-release privacy requirements

- Data minimization: MCP tools return bounded projections and snippets; list/search operations never include raw MIME or attachment bytes.
- Purpose limitation: synchronized content is used for owner-controlled mailbox retrieval, search, RAG, local backup, and explicitly configured SMTP workflows only.
- Storage limitation: retention settings must exist for remotely expunged messages, raw MIME grace periods, logs, traces, and future derived artifacts. Conservative defaults retain data only while needed for the configured mailbox copy and recovery model.
- Confidentiality: secrets, tokens, mail bodies, attachment bytes, raw MIME, embeddings, and provider payloads are excluded from default logs and MCP errors.
- Integrity: content length and SHA-256 checks bind metadata to stored MIME and support consistency repair.
- Accountability: important access, synchronization, configuration, deletion, export, and governance decisions should emit redacted audit events with actor, purpose, target, outcome, and time.
- Provider boundaries: external AI providers receive only bounded retrieved context needed for the requested answer, never complete mailbox dumps. Provider configuration must make data-processing implications reviewable before enabling `ask_mail`.

### 16.3 Deferred GDPR workflows

The first release does not implement full data-subject request automation. It must nevertheless preserve application-level seams for later workflows:

- access/export of mailbox metadata, selected message content, derived chunks, and audit records in structured machine-readable formats;
- erasure and restriction across raw MIME, metadata, search text, chunks, embeddings, jobs, caches, and provider traces controlled by MailMcp;
- retention-policy execution with explicit treatment of remotely expunged mail, backups, and legal holds;
- records of processing activities and evidence showing which systems, providers, scopes, and retention policies apply;
- operator review steps for requests that may affect third-party correspondence or conflict with backup, legal, or security requirements.

Backups are not rewritten synchronously for ordinary erasure requests in the first design. A later compliance design must define backup retention windows, restore-time deletion replay, and evidence of eventual removal without weakening recovery guarantees.

### 16.4 Enterprise audit and governance seam

Authorization checks, MCP tool invocations, RAG retrieval decisions, SMTP outbox state transitions, configuration changes, and future data-subject workflows should pass through explicit application services that can emit redacted audit events. Audit events must avoid message bodies, attachment contents, raw prompts, access tokens, credentials, and provider payloads unless a future protected audit store is deliberately designed for that sensitivity.

A future AGT adapter can subscribe to the same governance seam before higher-risk tool execution. AGT decisions must fail closed, be testable independently from model prompts, and produce safe application errors at the MCP boundary. AGT records are compliance evidence, not a replacement for application authorization, OAuth scopes, mTLS policies, or domain invariants.

## 17. Failure handling

- Tool requests return local data during IMAP outages.
- Responses include synchronization freshness so stale data is explicit.
- Each account, embedding batch, and SMTP delivery has isolated retry state.
- PostgreSQL unavailability fails readiness and pauses workers.
- Missing or corrupt MIME content is reported explicitly and queued for repair without a synchronous IMAP fetch in the tool request.
- Poison messages are quarantined after bounded parsing attempts without blocking the folder checkpoint.
- Background jobs are persisted in PostgreSQL; no separate queue is required initially.
- Expected application failures are represented with explicit result/error types and stable safe error codes. Domain invariant violations use domain-specific exceptions only for exceptional states; adapters may wrap lower-level failures as inner exceptions for diagnostics, but MCP serialization never includes exception types, stack traces, internal identifiers, provider payloads, or `InnerException` details.
- Migrations are applied explicitly, not by a starting application instance. In Development the host may apply pending EF Core migrations at startup for local convenience. In every other environment the host verifies that the schema matches the expected migration set and fails startup when migrations are pending, so an instance either serves traffic against a known schema or does not serve traffic at all; applying them is a deliberate deployment step. An earlier revision of this draft accepted automatic startup migration as an arbitrary simplification for a single-owner release. That is superseded: an instance that mutates schema while starting can race a second instance, can apply an unreviewed destructive change, and leaves the operator no point at which to take a backup. Migrations must still be reviewed before release and must be idempotent from the application perspective. Specification 19 implements this policy and the `aspire exec` workflow that supports it; a later operational hardening phase can move long-running migrations to `mcpmail`.

### 17.1 Resilience pipelines

Retry, timeout, and circuit-breaking are one deliberate mechanism rather than a habit repeated per adapter. MailMcp uses Polly v8 resilience pipelines registered by key and resolved through `ResiliencePipelineProvider`, with one named pipeline per outbound dependency class: mailbox session establishment, mailbox data retrieval, message delivery, database command execution, and AI provider invocation. Each class has typed, startup-validated options for attempt count, backoff bounds, per-attempt and total timeout, circuit-breaker thresholds, and concurrency limits.

Polly types stay inside `Infrastructure`. `Application` expresses the question it actually has — whether a failure is worth retrying — through its own transient-failure classification port, so use cases and adapters never reference a resilience framework type.

Two rules keep the mechanism from becoming its own failure mode. A pipeline is applied at exactly one layer per logical operation, so an adapter-level retry is never wrapped by a supervisor-level retry of the same call; the supervisor decides only when to attempt the next whole run. And EF Core's `EnableRetryOnFailure` execution strategy is either used or replaced by the database pipeline, never both, because combining them breaks explicit transaction boundaries.

Retry is restricted to operations that are safe to repeat. Authentication, permission, and malformed-request failures are terminal, because repeating them can lock a mailbox account. Where a retried IMAP operation re-establishes its session, the folder is always reopened read-only, so the invariant in section 11.1 survives retry. Resilience telemetry records dependency class, outcome, attempt, and duration, never credentials, addresses, or provider payloads.

## 18. Observability

- Aspire ServiceDefaults provide the initial shared `Extensions.cs` scaffold for OpenTelemetry logs, metrics, traces, health checks, service discovery hooks where useful, and OTLP export configuration. MailMcp extends those defaults rather than duplicating per-project telemetry setup.
- Structured JSON logs with account IDs and message IDs, never addresses, subjects, bodies, tokens, or credentials by default.
- OpenTelemetry traces for MCP calls, database operations, IMAP push or time-based synchronization cycles, retrieval, embedding generation, agent runs, and SMTP delivery when SMTP is implemented.
- Metrics include sync lag, reconnect count, cached messages, missing content, embedding backlog, retrieval latency, token usage when available, outbox depth when SMTP is implemented, and tool errors.
- Health checks expose separate private endpoints for startup, healthy/readiness, and alive/liveness. Startup reports completion of configuration validation, EF Core migration, Data Protection key loading, and required local service initialization. Healthy/readiness includes PostgreSQL connectivity, migration state, background worker readiness, and configured AI provider readiness when RAG is enabled. Alive/liveness remains lightweight and process-local so external transient dependencies do not cause restart loops.
- Protocol logging is disabled by default and, when temporarily enabled, is redacted and written to a protected location.

## 19. Deployment

Default deployment uses Docker Compose or rootless Podman Compose managed by systemd:

- `mail-mcp`
- `postgres` with pgvector

Kestrel publishes only HTTPS on port 443 and does not listen on a public plain-HTTP endpoint. PostgreSQL, metrics, health, and administration endpoints remain private. The operating-system firewall permits the public MCP port and restricts database and management traffic.

The `mail-mcp` process runs as a dedicated unprivileged identity. A container maps public port 443 to a non-privileged Kestrel container port; a native systemd deployment grants only `CAP_NET_BIND_SERVICE` when Kestrel binds directly to 443. The application never runs as root.

Persistent volumes:

- PostgreSQL data
- Data Protection key ring
- TLS and trusted client CA material

One PostgreSQL backup contains metadata, raw MIME, search data, chunks, embeddings, jobs, and outbox state at a consistent logical point. Backup and restore procedures must be tested, and database volumes and backups must use encrypted storage. TLS certificates and the Data Protection key ring are backed up separately through the deployment secret-management process.

## 20. Development orchestration with Aspire

Aspire is included from the start as a development-time orchestration and observability layer, not as the production runtime or application framework. `AppHost` models the host process, PostgreSQL with pgvector, local secret/configuration bindings, and developer observability so contributors can run the local distributed environment consistently from one entry point.

The AppHost stays minimal: explicit resource names, explicit dependencies, separate development/test/production configuration, and no business logic. Production deployment continues to use Docker Compose or rootless Podman Compose managed by systemd unless a later deployment decision replaces that path. Aspire-generated service discovery or orchestration concerns must not leak into `Domain`, `Application`, `Mcp`, or mail protocol adapters.

The first AppHost covers local development only. Future integration testing can reuse Aspire orchestration when it improves repeatability, but integration-test infrastructure remains separate from the initial unit-test-only solution.

## 21. Future ideas

These ideas are deliberately outside the first release. They are recorded here so the initial architecture keeps stable seams for later work without adding premature packages, services, test projects, or operational dependencies.

### 21.1 Agent Governance Toolkit (AGT)

Microsoft Agent Governance Toolkit (AGT) may become useful when MailMcp exposes agent-mediated actions beyond read-only retrieval, especially if future MCP tools can send mail, mutate local state, delegate work, or call external systems. AGT is a governance layer for agents and MCP tool calls: it can help make policy checks, input/output inspection, and allow/deny decisions explicit instead of burying those decisions inside prompt text or ad-hoc tool handlers.

AGT is not part of the first release because the public MCP surface is read-only, deterministic tool authorization is enforced at the transport and application boundaries, and MailMcp already treats retrieved mail as untrusted input. Before adopting AGT, the team must verify package maturity, .NET 10 compatibility, license and service-term implications, policy authoring model, telemetry data exposure, and whether AGT decisions can be expressed without leaking provider-specific concepts into `Domain`, `Application`, or `Mcp`.

Potential AGT evaluation scenarios include governing a future `send_email` MCP tool, blocking prompt-injected tool escalation from message content, enforcing per-client tool policies, recording auditable governance decisions, mapping policy evidence to enterprise compliance controls, and validating that denied actions fail closed with safe MCP error codes. AGT must complement, not replace, OAuth scopes, mTLS, application authorization, and GDPR-aligned data minimization.

### 21.2 MinIO object storage migration

All raw-content operations use the application-owned `IEmailContentStore` port with streaming put, open-read, existence, and delete operations. The first implementation stores content in PostgreSQL; neither the application nor domain layer receives a PostgreSQL-specific locator or `bytea` type. This seam keeps a future MinIO or S3-compatible object-storage migration possible without changing mail use cases.

A later MinIO migration would be performed online in controlled stages:

1. Add the MinIO adapter and explicit content-backend/locator metadata while PostgreSQL remains authoritative.
2. Stream existing MIME rows to MinIO in bounded background batches.
3. Verify every copied object against its stored byte length and SHA-256 hash.
4. Enable dual-read with PostgreSQL fallback and repair while new content is written to the selected backend.
5. Switch MinIO to authoritative reads only after coverage and consistency metrics reach the required threshold.
6. Retain PostgreSQL MIME for a safety interval, then remove migrated `bytea` values in bounded maintenance batches.

No MinIO package, credentials, process, bucket, deployment volume, or object-storage test fixture is included in the first release. Any future MinIO SDK, container image, or hosted object-storage dependency must be pinned, license-reviewed, and recorded in `LICENSES.md` before adoption.

### 21.3 Future integration testing with smtp4dev

A future integration-test suite should include smtp4dev as the controlled SMTP target for delivery scenarios. smtp4dev is a fake SMTP server intended for development and testing, is available as Docker/OCI images and a .NET tool, and its NuGet package currently declares the BSD-3-Clause license. Before adding it to the repository, the exact package, container image, or tool version must be pinned and recorded in `LICENSES.md`.

The smtp4dev-based tests should validate SMTP connection policy, STARTTLS behavior where supported by the selected test setup, authentication settings, MIME envelope/content emitted by MailMcp, outbox retry classification, idempotency behavior, and failure handling. smtp4dev does not replace unit tests and does not validate IMAP semantics; any IMAP integration fixture remains a separate future decision.

The first release still does not add integration-test projects, Testcontainers, Docker fixtures, smtp4dev packages, or smtp4dev container references.

### 21.4 Future administration CLI

The dedicated administration CLI is named `mcpmail` and is a future operational interface rather than an initial implementation requirement. The first release can be administered through validated JSON configuration plus deployment secret references, with account-test and migration workflows added only when their application services exist.

When the CLI is introduced, it should use Microsoft's `System.CommandLine` package rather than a custom parser or a non-official command-line framework. `System.CommandLine` provides command parsing, help output, validation, and shell-completion support for .NET command-line applications, and the package must be centrally pinned and entered in `LICENSES.md` before use.

Candidate future commands:

- `mcpmail account add`
- `mcpmail account list`
- `mcpmail account test`
- `mcpmail account disable`
- `mcpmail sync status`
- `mcpmail sync run`
- `mcpmail rag profile add`
- `mcpmail rag profile activate`
- `mcpmail rag status`
- `mcpmail rag reindex`
- `mcpmail client-cert add-ca`
- `mcpmail client-cert list`
- `mcpmail client-cert remove`

The CLI requires local operating-system access and is not exposed through MCP.

### 21.5 Policy-driven mail processing and automation jobs

MailMcp could evolve from passive synchronization and retrieval into an asynchronous mail-processing platform. A future automation subsystem would evaluate configured rules after a message and its required local content have been committed, then enqueue durable actions without extending the IMAP synchronization transaction or delaying MCP reads. It should support useful non-AI automation first and make AI an optional condition or transformation rather than the workflow authority.

Potential triggers include:

- a message occurrence being stored or materially updated by synchronization;
- a scheduled scan over a bounded local query, such as messages received since the last successful run;
- an explicit operator request to re-evaluate selected messages after a rule changes;
- a repaired or newly extracted message becoming eligible for a rule that previously lacked required content.

Provider notifications such as IMAP IDLE or future Microsoft Graph webhooks would only wake the appropriate synchronization path. Automation would consume committed local state, not an incomplete provider payload, so retries and provider outages cannot produce a different processing boundary.

Deterministic conditions could match account, folder, sender or recipient domains, selected headers, received time, size, MIME type, attachment metadata, lexical search terms, prior local labels, or the presence of extracted content. Candidate AI-assisted conditions and transformations include intent or topic classification, priority estimation, structured field extraction, summarization, entity detection, and routing recommendations. AI output must use a bounded schema and carry the model profile, prompt or policy version, confidence or validation result, and provenance needed to explain why a later action was proposed.

Candidate actions should be introduced in increasing order of risk:

1. Local-only actions: add a MailMcp label, record a classification, produce a private summary, enqueue embedding or extraction work, or route the message into a local review queue.
2. Controlled integrations: emit a minimized webhook or create an application-owned work item after destination-specific authorization, payload filtering, timeout, and idempotency review.
3. Remote mailbox mutations: apply a provider category, move or copy a message, or change a remote flag. These actions conflict with the initial read-only posture and require a separate design for authorization, synchronization feedback loops, and provider-specific semantics.
4. External side effects: forward, reply, send, delete, or invoke another business system. These require explicit opt-in, approval and governance policy, durable idempotency, audit evidence, and safe compensation or operator recovery.

`Application` would own provider-neutral rule, trigger, action, approval, and execution-result contracts. `Infrastructure` would lease and persist jobs in PostgreSQL, implement deterministic provider actions, and enforce bounded concurrency, timeout, retry, and dead-letter behavior. `AI` would implement only AI evaluations and transformations behind application-owned ports. The host would remain responsible only for worker registration and validated settings. A generic workflow engine, message broker, or separate scheduler is not justified until PostgreSQL-backed jobs demonstrate a concrete limitation.

Each execution needs an idempotency identity derived from the message occurrence, rule version, trigger generation, and action. Rule definitions should be versioned so an in-flight job continues against the policy that created it; reprocessing under a newer rule must be an explicit operation. Rule ordering, stop-or-continue behavior, mutually exclusive actions, and conflict resolution must be deterministic and reviewable without invoking a model.

The main problems to resolve before implementation are:

- **Duplicate and out-of-order work:** synchronization retries, provider redelivery, worker crashes, and manual reprocessing can schedule the same action more than once.
- **Rule conflicts and loops:** one automation can make a change that triggers another rule or is synchronized back as a new provider update.
- **Stale decisions:** message state, configuration, authorization, or a model profile can change between evaluation, approval, and execution.
- **Prompt injection and nondeterminism:** message content is untrusted input. A model may recommend an action but must never grant itself capabilities, bypass deterministic policy, or construct unrestricted tool calls.
- **Privacy and purpose limitation:** classification, summaries, extracted fields, prompts, model traces, and webhook payloads create additional derived personal data with their own access, retention, export, and erasure obligations.
- **Cost and capacity:** attachment extraction, model calls, and bulk rule changes can create an unbounded backlog. Per-rule concurrency, content-size, token, cost, retry, and execution-time limits are required.
- **Failure recovery:** poison content and permanently failing destinations must move to a visible quarantine or dead-letter state without blocking unrelated mail.
- **Explainability and audit:** operators need to know which rule version matched, which deterministic facts were used, whether AI contributed, which action was attempted, and why it succeeded, failed, or awaited approval without storing unnecessary message content in ordinary logs.

A future spike should select a minimal declarative rule representation, define the first local-only action set, prove idempotent execution and explicit reprocessing, and compare deterministic-only and AI-assisted evaluation on representative mail. Remote mutations or external side effects should remain a later slice after the local job model, approval boundary, and audit evidence are verified.

### 21.6 Microsoft 365 and Outlook interoperability

There are several materially different interpretations of making externally hosted mail behave like a normal mailbox in Microsoft 365. They must not be collapsed into a promise that MailMcp can expose arbitrary endpoints and become an Exchange server. Outlook clients, Exchange Online, Microsoft Graph, Outlook add-ins, and Microsoft 365 Copilot connectors solve different problems and provide different user experiences.

| Option | User-visible result | Where mail data lives | Main limitation |
| --- | --- | --- | --- |
| Add the external account to Outlook through IMAP/POP | A separate account and folder tree in supported Outlook clients | The external provider remains authoritative; some Outlook clients or providers can also synchronize a copy to Microsoft Cloud | It is not an Exchange Online mailbox and feature, policy, add-in, and client support differ |
| Add a Microsoft Graph mailbox provider to MailMcp | MailMcp can synchronize real Exchange Online primary or shared mailboxes | Exchange Online and MailMcp local storage | It does not make an external IMAP mailbox appear in Outlook |
| Replicate external mail into an Exchange Online mailbox | A native Exchange Online mailbox in Outlook and Microsoft 365 | Both the external host and Exchange Online | Duplicate data, source-of-truth conflicts, licensing, compliance, and difficult bidirectional semantics |
| Build an Outlook web add-in backed by MailMcp | MailMcp search, AI, and workflow commands appear in Outlook UI | The mailbox remains with its provider; requested data is processed by MailMcp | An add-in is a task pane or command surface, not a mailbox provider or folder tree |
| Publish through a Microsoft 365 Copilot connector | External mail can be discoverable to supported Copilot or Microsoft Search experiences | Synced connectors copy indexed content to Microsoft Graph; federated MCP connectors retrieve at query time | It provides search and reasoning, not Outlook mailbox semantics |
| Emulate Exchange protocols and Autodiscover | In theory, Outlook could treat MailMcp as an Exchange-like service | Depends on the implementation | Unsupported, security-sensitive, operationally disproportionate, and outside MailMcp's product boundary |

Microsoft documents other IMAP and POP accounts as supported account types in new Outlook for Windows, so the first feasibility check should be whether the existing externally hosted account can simply be added alongside the Microsoft 365 work account. This route needs no MailMcp endpoint and does not require moving the domain. It also does not create an Exchange Online mailbox. Microsoft separately documents that supported non-Microsoft account modes can synchronize a copy of mail, calendar, or contact data into Microsoft data centers; exact behavior varies by Outlook client and provider. A deployment that requires mail to remain exclusively outside Microsoft must therefore validate the target Outlook client, SKU, tenant policy, authentication method, and data-flow disclosure rather than assuming that configuring IMAP means direct client-to-provider traffic.

Microsoft Graph is valuable for the opposite direction: it gives MailMcp authorized access to mail already stored in Exchange Online. A future `MicrosoftGraph` infrastructure adapter could implement the same application-level synchronization purpose as the MailKit adapter while preserving provider-specific contracts at the edge. Graph change notifications can reduce polling, while per-folder delta queries provide initial and incremental reconciliation. Notifications must remain hints rather than durable truth: subscriptions expire and require renewal, delivery can be delayed or missed, delta tokens can become invalid, and throttling requires bounded backoff and resynchronization.

The Graph adapter introduces identity and authorization problems that cannot be hidden by reusing IMAP values. Graph message IDs and folder IDs do not have IMAP UIDVALIDITY/UID semantics, and default message IDs can change when an item is moved unless immutable IDs are requested. Delta state is scoped to provider collections rather than to an IMAP checkpoint. Supporting both providers would therefore require an explicit provider-neutral account boundary plus provider-owned occurrence and checkpoint records; it should not weaken the existing stable IMAP identity. Application permissions must be scoped to the required mailboxes and operations through least-privilege Microsoft Graph permissions and Exchange Online Application RBAC, without retaining an overlapping organization-wide Entra grant that defeats mailbox scoping.

Microsoft Graph cannot act as a transparent bridge from an arbitrary external IMAP server into Outlook. Its mail APIs operate on primary and shared mailboxes stored in Exchange Online. If a native Exchange Online experience across Outlook clients and Microsoft 365 is mandatory, some mailbox data must exist in Exchange Online. The conventional Microsoft IMAP migration flow creates and licenses target mailboxes, copies supported mail folders, and ultimately changes mail routing; it is a migration path, not a permanent virtual mailbox backed by the source IMAP server.

A MailMcp-managed replication bridge could be explored, but only as a separate product slice. A one-way mirror from the external authoritative mailbox into a dedicated Exchange Online mailbox is substantially safer than bidirectional synchronization. Even a one-way design must define duplicate detection, folder mapping, deletion and retention behavior, message fidelity, backfill limits, lag, failure repair, legal holds, and whether users may act on mirrored messages. Bidirectional synchronization additionally needs conflict resolution for moves, read and flag state, drafts, sent items, deletes, concurrent edits, and loops. Outbound mail raises accepted-domain, sender authorization, SPF, DKIM, DMARC, Sent Items, and idempotency questions. Calling this arrangement "hosted elsewhere" would be misleading because Exchange Online contains a second copy.

An Outlook add-in is useful when the desired outcome is access to MailMcp features inside Outlook rather than a mailbox replacement. It could expose local search, cited answers, classifications, approval queues, and automation commands through a task pane or contextual command. Current Microsoft documentation shows that add-in support for non-Microsoft accounts is limited across Outlook clients, so the add-in cannot be assumed to operate on the external IMAP account itself. It may still operate in the user's Microsoft 365 mailbox context and call an owner-authorized MailMcp API, but that provides a sidecar experience rather than native folders.

Microsoft 365 Copilot connectors create another future path. A synced connector could publish minimized, access-controlled external items for Microsoft Search and Copilot, at the cost of copying and semantically indexing data in Microsoft Graph. A federated connector can retrieve data from an MCP server at query time without indexing it in Graph, which aligns more closely with MailMcp's existing protocol boundary and data-residency goal, but availability, licensing, identity propagation, citation behavior, tenant administration, and privacy terms require a dedicated evaluation. Neither connector type creates an Outlook mailbox.

The recommended evaluation order is:

1. Validate direct IMAP account support and actual data flow for the required Outlook clients, Microsoft 365 Business license, tenant policies, and external provider.
2. Add a Microsoft Graph provider only for mailboxes genuinely hosted in Exchange Online, using change notifications plus delta reconciliation and mailbox-scoped application authorization.
3. Evaluate an Outlook add-in or federated Microsoft 365 Copilot connector when the goal is to surface MailMcp capabilities inside the Microsoft ecosystem without copying a mailbox.
4. Prototype a one-way Exchange Online mirror only if a native mailbox is mandatory and the owner accepts a second copy in Microsoft 365.
5. Do not pursue an Exchange protocol façade or general bidirectional bridge without a separate architecture decision, threat model, data-protection review, and narrowly proven business requirement.

No Microsoft Graph SDK, Outlook add-in package, Entra application, Exchange Online mailbox, connector registration, tenant permission, or Microsoft-hosted data flow is introduced by recording this idea. Any implementation must re-check official API support, .NET 10 compatibility, licensing and service terms, tenant requirements, telemetry, and data-processing implications at that time.

## 22. Delivery stages

Stages describe the shape of the release. The PR-sized units of work that deliver them live in `specs/`, indexed by [`specs/README.md`](README.md); the specification numbers below are the current decomposition, and the referenced files are authoritative for scope.

1. Repository and solution foundation, Aspire AppHost, unit-test projects, Kestrel HTTPS configuration, PostgreSQL, and migrations. *Foundation and AppHost are implemented. Migrations are deliberately rescheduled to the end of the current segment; see specification 19 and its rationale.*
2. JSON-based configuration binding, typed option validation, systemd/container secret-reference resolution, and MailKit connection validation with mocked IMAP/SMTP boundary tests. *Specifications 01 and 02.*
3. Read-only initial and continuous IMAP synchronization with configurable push-style IDLE/NOTIFY or time-based sync, offline MIME storage, and `\Seen` regression tests. *Periodic reconciliation is implemented; specifications 04 through 12 complete the stage.*
4. Deterministic MCP tools `list_emails`, `get_email_content`, `search_emails`, and `ask_mail`, with RAG enabled when configured, rich MCP annotations, safe error mapping, unit-tested authorization, and mapping. *Specifications 13 through 18 deliver the three read-only tools; `ask_mail` belongs to stage 7.*
5. PostgreSQL full-text indexing plus automatic embedding generation for new mail when embeddings are enabled. *Full-text indexing is pulled forward to specification 08 because the stage 4 search tool depends on it; embedding generation stays here.*
6. pgvector ingestion with configurable embedding profile and first-release defaults for OpenAI `text-embedding-3-small` plus configurable chat model.
7. Agent Framework RAG hardening, prompt-injection isolation, citations, and provider-health gating.
8. Deferred SMTP outbox and delivery service with unit-tested state transitions and retry behavior after the IMAP/RAG/MCP slice.
9. ChatGPT OAuth/mTLS validation and general OAuth MCP client profile.
10. Production hardening, backup, metrics, recovery exercises, GDPR workflow design, enterprise audit evidence, and explicit evaluation plans for future ideas: AGT governance, MinIO object storage, `mcpmail`, smtp4dev-backed SMTP integration tests, policy-driven mail automation, and Microsoft 365 interoperability.

Two pieces of work cut across the numbered stages. Resilience pipelines (section 17.1) are established once in specification 03 and consumed by every later adapter, starting with specification 04. Infrastructure verification through Aspire test mode (section 6.2) lands in specifications 20 and 21 once the schema is settled, and pays off the PostgreSQL checks that ADR 001 defers together with the `\Seen` invariant that no substitute-based unit test can prove.

Stages 6 through 10 are decomposed into specifications when the current segment nears completion, so they are written against the code that exists by then rather than against a prediction of it.

## 23. Acceptance criteria

- Synchronizing and retrieving an unread message leaves its remote `\Seen` flag unchanged.
- Repeated synchronization is idempotent for the same account, folder, UIDVALIDITY, and UID.
- `list_emails`, `get_email_content`, `search_emails`, and configured `ask_mail` work from local storage while IMAP is unavailable; `ask_mail` may additionally require healthy configured AI providers.
- A tool request never triggers a synchronous IMAP body fetch.
- Tens of thousands of messages use indexed keyset queries rather than full scans or offset pagination.
- A message is searchable lexically after extraction and semantically after its active-profile embedding is stored; embedding generation starts automatically for new synchronized mail when enabled for the instance.
- RAG answers from `ask_mail` cite stable local message IDs and never gain mail mutation capabilities.
- ChatGPT requests require a valid OpenAI client certificate and a valid owner OAuth token.
- A general OAuth MCP client can connect without weakening the ChatGPT mTLS profile.
- Invalid issuer, audience, expiry, subject, scope, certificate chain, SAN, or host name is rejected before tool execution.
- Secrets, mail content, internal exceptions, stack traces, provider payloads, and inner-exception details do not appear in MCP responses, default logs, or telemetry.
- The complete xUnit unit suite passes without network, database, container, or filesystem dependencies.
- IMAP success, failure, disconnect, cancellation, push-sync, time-based sync, and capability scenarios are reproducible through NSubstitute-based protocol boundaries; SMTP scenarios are added when SMTP leaves deferred scope.
- First-release configuration can be expressed in JSON without placing secrets or encrypted secret values in Git.
- Future CLI work is explicitly deferred and uses `mcpmail` with `System.CommandLine` when implemented.
- Aspire AppHost can start the local development environment for MailMcp and PostgreSQL without introducing production runtime coupling.
- Future ideas are collected separately from first-release scope, including AGT governance evaluation, MinIO migration, `mcpmail`, smtp4dev-backed SMTP delivery verification, policy-driven mail automation, and Microsoft 365 interoperability.
- The draft identifies personal-data classes, derived-data sensitivity, first-release privacy controls, and deferred GDPR workflows so later compliance implementation has explicit seams.
- Enterprise-grade posture is visible in boundaries for auditability, governance, privacy, operational hardening, deterministic policy enforcement, and safe failure modes without prematurely adding first-release dependencies.
- Shared repository settings are centralized in `global.json`, `Directory.Build.props`, and `Directory.Packages.props` whenever possible instead of being repeated in individual projects.

## 24. References verified for this draft

- [.NET releases and support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)
- [Microsoft Agent Framework overview](https://learn.microsoft.com/en-us/agent-framework/overview/)
- [Agent Framework RAG](https://learn.microsoft.com/agent-framework/agents/rag)
- [Agent Framework integrations and vector stores](https://learn.microsoft.com/agent-framework/integrations/)
- [Official MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [MCP C# SDK getting started](https://csharp.sdk.modelcontextprotocol.io/concepts/getting-started.html)
- [MCP tool annotations specification](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
- [OpenAI Apps SDK authentication](https://developers.openai.com/apps-sdk/build/auth)
- [Connect an MCP app from ChatGPT](https://developers.openai.com/apps-sdk/deploy/connect-chatgpt)
- [Claude Code MCP](https://docs.anthropic.com/id/docs/claude-code/mcp)
- [MailKit](https://www.nuget.org/packages/MailKit/)
- [MailKit IMAP IDLE](https://mimekit.net/docs/html/M_MailKit_Net_Imap_ImapClient_IdleAsync.htm)
- [Npgsql EF Core provider](https://www.npgsql.org/efcore/)
- [pgvector](https://github.com/pgvector/pgvector)
- [pgvector-dotnet](https://github.com/pgvector/pgvector-dotnet)
- [PostgreSQL binary data types](https://www.postgresql.org/docs/current/datatype-binary.html)
- [PostgreSQL TOAST and large values](https://www.postgresql.org/docs/current/lo-intro.html)
- [Kestrel with or without a reverse proxy](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/when-to-use-a-reverse-proxy?view=aspnetcore-10.0)
- [Kestrel HTTPS endpoints and client certificates](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0)
- [.NET unit testing best practices](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)
- [xUnit.net v3 getting started](https://xunit.net/docs/getting-started/v3/getting-started)
- [xUnit.net v3 Microsoft Testing Platform v2](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
- [Microsoft Testing Platform overview](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-intro)
- [NSubstitute documentation](https://nsubstitute.github.io/)
- [.NET configuration providers](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-providers)
- [ASP.NET Core configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0)
- [Safe storage of app secrets in development](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0)
- [systemd credentials](https://systemd.io/CREDENTIALS/)
- [systemd-creds](https://www.freedesktop.org/software/systemd/man/systemd-creds.html)
- [Aspire overview](https://aspire.dev/get-started/what-is-aspire/)
- [Aspire AppHost](https://aspire.dev/get-started/app-host/)
- [Aspire Service Defaults](https://aspire.dev/get-started/csharp-service-defaults/)
- [Aspire health checks](https://aspire.dev/fundamentals/health-checks/)
- [ASP.NET Core health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0)
- [System.CommandLine overview](https://learn.microsoft.com/en-us/dotnet/standard/commandline/)
- [System.CommandLine NuGet package](https://www.nuget.org/packages/System.CommandLine)
- [System.CommandLine GitHub repository](https://github.com/dotnet/command-line-api)
- [Supported accounts in new Outlook for Windows](https://learn.microsoft.com/en-us/microsoft-365-apps/outlook/get-started/supported-account-types)
- [Sync a non-Microsoft account in Outlook to Microsoft Cloud](https://support.microsoft.com/en-us/outlook/getstarted/sync-your-account-in-outlook-to-the-microsoft-cloud)
- [Microsoft Graph Outlook mail API](https://learn.microsoft.com/en-us/graph/api/resources/mail-api-overview?view=graph-rest-1.0)
- [Organize and synchronize Outlook messages with Microsoft Graph](https://learn.microsoft.com/en-us/graph/outlook-organize-messages)
- [Microsoft Graph delta query](https://learn.microsoft.com/en-us/graph/delta-query-overview)
- [Exchange Online RBAC for applications](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac)
- [IMAP mailbox migration to Microsoft 365](https://learn.microsoft.com/en-us/exchange/mailbox-migration/migrating-imap-mailboxes/migrating-imap-mailboxes)
- [Outlook add-ins overview](https://learn.microsoft.com/en-us/office/dev/add-ins/outlook/outlook-add-ins-overview)
- [Microsoft 365 Copilot connectors overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/overview-copilot-connector)
- [smtp4dev GitHub repository](https://github.com/rnwood/smtp4dev)
- [smtp4dev NuGet package](https://www.nuget.org/packages/Rnwood.Smtp4dev)
- [smtp4dev Docker image](https://hub.docker.com/r/rnwood/smtp4dev)
- [smtp4dev installation documentation](https://raw.githubusercontent.com/rnwood/smtp4dev/master/docs/Installation.md)
- [Agent Governance Toolkit documentation](https://microsoft.github.io/agent-governance-toolkit/)
- [EU GDPR official legal text](https://eur-lex.europa.eu/eli/reg/2016/679/oj/eng)
- [GDPR Article 25: data protection by design and by default](https://gdpr-info.eu/art-25-gdpr/)
- [GDPR Article 17: right to erasure](https://gdpr-info.eu/art-17-gdpr/)
- [GDPR Article 20: right to data portability](https://gdpr-info.eu/art-20-gdpr/)
- [Agent Governance Toolkit GitHub repository](https://github.com/microsoft/agent-governance-toolkit)
