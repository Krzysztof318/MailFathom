# Mail MCP Service — Architecture Draft

**Status:** Draft for review
**Date:** 2026-07-22
**Target:** Debian/Ubuntu, .NET 10, single owner, many mail accounts

The product and solution name is `MailMcp`. The repository uses the XML solution format in `MailMcp.slnx`, and all .NET projects use the `MailMcp.*` naming prefix.

## 1. Purpose

The service synchronizes mail from multiple IMAP accounts, keeps a durable offline copy, sends mail through SMTP, indexes messages for lexical and semantic retrieval, and exposes controlled capabilities through a public MCP endpoint.

The initial public MCP surface is read-only. Sending exists as an application capability but is not exposed as an MCP tool until its authorization and confirmation flow is reviewed separately.

## 2. Confirmed decisions

- One service owner can configure many mailboxes across unrelated domains.
- PostgreSQL is the system of record for configuration, synchronization state, message metadata, extracted searchable text, RAG chunks, and embeddings.
- Full RFC 822 messages, including their MIME attachments, are stored in a dedicated PostgreSQL table using `bytea`.
- Raw content is accessed through `IMessageContentStore`; a later release will migrate that content to MinIO without changing domain or application use cases.
- MailKit handles IMAP, SMTP, MIME, TLS modes, and standard SASL mechanisms.
- Synchronization must never mark a remote message as read.
- MCP reads from local storage and never performs a blocking IMAP fetch while serving a tool request.
- Microsoft Agent Framework is the primary agent and RAG orchestration framework.
- Semantic Kernel may be added only as an adapter for a capability unavailable or insufficient in Agent Framework.
- Chat and embedding providers are deployment choices and are not fixed in this draft.
- The public server supports ChatGPT and remains compatible with other remote MCP clients such as Claude Code.
- Unit tests are developed from the beginning with xUnit.net v3 and NSubstitute.
- Integration tests are planned for a later phase but are not created in the initial solution.
- The solution is named `MailMcp`, uses `MailMcp.slnx`, and applies the `MailMcp.*` prefix consistently to projects, assemblies, and root namespaces.

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
- Read-only MCP tools:
  - `list_emails`
  - `get_email_content`
  - `search_emails`
- A conditional `ask_mail` tool, enabled only when a chat provider and embedding profile are configured.
- SMTP sending application service and durable outbox without a public MCP tool in the first release.
- OAuth 2.1, HTTPS, and client-aware mTLS policies.
- Administration is primarily configuration-file driven in the first release; a dedicated `mcpmail` CLI is a future operational convenience, not an initial requirement.

### 3.2 Excluded from the first release

- Multiple service users or tenants.
- Editing remote flags, including `\Seen`, from MCP.
- Moving or deleting messages.
- Returning attachment bytes through MCP.
- Autonomous mail actions by an agent.
- Training or fine-tuning models on mail.
- A custom OAuth authorization server.
- A MinIO process or MinIO SDK dependency; these are introduced only during the planned object-storage migration.

## 4. Architecture

The service is a modular monolith. One deployable ASP.NET Core host contains the public Kestrel endpoint and background workers, while internal projects enforce boundaries between mail domain logic, infrastructure, RAG, and protocol adapters. Kestrel is the Internet-facing HTTPS server; no reverse proxy is required in the initial deployment.

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

### 4.1 Boundary rules

- `Domain` has no dependency on EF Core, MailKit, MCP, Agent Framework, or storage SDKs.
- `Application` defines use cases and ports; it depends only on `Domain`.
- `Infrastructure` implements PostgreSQL persistence, IMAP/SMTP, and secret protection.
- `AI` implements chunking, embedding orchestration, hybrid retrieval, and Agent Framework composition.
- `Mcp` maps MCP schemas to application requests and contains no persistence or mail protocol logic.
- `Host` contains only configuration, dependency injection, middleware, endpoint mapping, and process lifetime.
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
│   ├── MailMcp.Domain/
│   │   ├── Accounts/
│   │   ├── Folders/
│   │   ├── Messages/
│   │   ├── Synchronization/
│   │   └── Delivery/
│   ├── MailMcp.Application/
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
│   ├── MailMcp.Infrastructure/
│   │   ├── Persistence/PostgreSql/
│   │   │   ├── Configurations/
│   │   │   └── Migrations/
│   │   ├── Mail/MailKit/
│   │   ├── Security/
│   │   └── Observability/
│   ├── MailMcp.AI/
│   │   ├── Chunking/
│   │   ├── Embeddings/
│   │   ├── Retrieval/
│   │   ├── AgentFramework/
│   │   └── SemanticKernel/
│   ├── MailMcp.Mcp/
│   │   ├── Tools/
│   │   ├── Authentication/
│   │   └── Serialization/
│   ├── MailMcp.Host/
│   │   ├── Configuration/
│   │   ├── Hosting/
│   │   └── Program.cs
│   └── MailMcp.Cli/                  # future `mcpmail` CLI, not initial scaffold
│       ├── Accounts/
│       ├── Synchronization/
│       └── Rag/
├── tests/
│   ├── MailMcp.Domain.UnitTests/
│   ├── MailMcp.Application.UnitTests/
│   ├── MailMcp.Infrastructure.UnitTests/
│   ├── MailMcp.AI.UnitTests/
│   └── MailMcp.Mcp.UnitTests/
├── deploy/
│   ├── compose.yaml
│   ├── postgres/
│   ├── certificates/
│   └── systemd/
└── specs/
    └── 2026-07-22-mail-mcp-architecture-draft.md
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
| Observability | OpenTelemetry + JSON console logging | Traces, metrics, and structured logs |
| Unit testing | xUnit.net v3 + NSubstitute | Isolated behavior tests and mocked protocol boundaries |
| Future local orchestration | Aspire AppHost | Development-time orchestration and observability for MailMcp, PostgreSQL, and future test services |
| Future CLI parser | `System.CommandLine` | Official Microsoft command-line parser for the later `mcpmail` administration CLI |

Package versions are centrally pinned in `Directory.Packages.props`. Preview Agent Framework packages are acceptable, but every update is explicit and reviewed.

### 6.1 Unit testing strategy

Unit tests are delivered with every behavior change. They follow Arrange, Act, Assert; remain deterministic and order-independent; and avoid network, filesystem, database, container, and wall-clock dependencies.

The application layer defines narrow interfaces for IMAP sessions, SMTP transports, message-content storage, local persistence, and AI providers. Unit tests use NSubstitute to model IMAP/SMTP server behavior through these interfaces, including advertised capabilities, authentication results, mailbox responses, disconnects, timeouts, and transient failures. Production code does not attempt to mock concrete MailKit clients.

The initial unit suite prioritizes:

- preserving the remote `\Seen` flag on every metadata and content retrieval path;
- UIDVALIDITY changes, duplicate events, idempotent resynchronization, and reconnect behavior;
- STARTTLS/TLS policy, authentication allow-lists, and rejection of unsafe configuration;
- SMTP outbox state transitions, retries, cancellation, and duplicate-send prevention;
- offline list/get/search behavior when IMAP is unavailable;
- MCP authorization, input validation, pagination, and bounded output;
- chunking, hybrid-result fusion, citations, and provider-independent RAG orchestration.

### 6.2 Future integration testing

A separate integration-test suite is planned after the unit-tested application and protocol boundaries stabilize. It will validate MailKit against controlled IMAP/SMTP servers, PostgreSQL with pgvector, OAuth discovery, TLS, and mTLS. It may use containers and disposable infrastructure, but no integration-test project or dependency is added during the initial phase.

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

The first release should prefer a small YAML configuration file for non-secret operational settings because mail account definitions, folder policies, TLS profiles, OAuth resource metadata, and RAG profiles are naturally hierarchical. .NET does not ship an in-box YAML configuration provider, so the implementation must either add a permissively licensed YAML provider after license review or load YAML through a narrowly owned host-boundary parser that maps into typed options. YAML is an operator-facing source format only; domain, application, infrastructure, AI, and MCP projects consume validated options and never parse YAML directly.

Configuration precedence is explicit: built-in defaults, committed example YAML, deployment YAML, environment-specific overrides, environment variables for non-secret automation, and command-line overrides. The host validates all bound options at startup with fail-fast errors for missing TLS material, unsafe mail transport settings, invalid OAuth audience/resource values, unbounded result sizes, missing database settings, or incompatible RAG profiles.

Secrets are never committed to YAML. YAML may contain secret references such as credential names, file paths under a protected secrets directory, systemd credential names, or container secret names. Development may use .NET Secret Manager for local-only convenience, but because user secrets are not encrypted and are not a production secret store, production deployments must use systemd credentials, container secrets, an approved external secret provider, or protected files provisioned outside Git.

- Account secrets are encrypted before storage in PostgreSQL.
- ASP.NET Core Data Protection protects ciphertext with a persistent key ring.
- The key-ring protection certificate is injected through a systemd credential or container secret and is never stored in PostgreSQL or Git.
- PostgreSQL, SMTP, and IMAP secrets never appear in logs, traces, MCP results, or exception messages.

## 8. Domain model

### 8.1 Main entities

- `MailboxAccount`: one configured mail identity and its IMAP/SMTP settings.
- `MailFolder`: remote folder identity, sync policy, UID validity, and synchronization cursor.
- `StoredEmail`: local representation of one IMAP message occurrence in one folder.
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

- One `email_message_contents` row stores the complete raw RFC 822 message for each synchronized message occurrence.
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
6. Generate embeddings through the configured `IEmbeddingGenerator`.
7. Upsert vectors under the active `EmbeddingProfile`.

Chunk records include account, folder, message, sender, recipients, date, subject, and source coordinates. The agent can therefore cite a stable local message ID and the exact chunk used.

### 12.2 Provider-neutral operation

- Without an embedding provider, synchronization and PostgreSQL full-text search continue to work.
- Without a chat provider, `list_emails`, `get_email_content`, and lexical `search_emails` remain available.
- When an embedding provider is configured, `search_emails` becomes hybrid.
- When both embedding and chat providers are configured, the MAF-backed `ask_mail` tool is enabled.
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

Semantic Kernel is not referenced by domain, application, MCP, or persistence projects. If a required connector, embedding implementation, or orchestration capability is absent from Agent Framework, it is added inside `MailMcp.AI/SemanticKernel` behind an existing application interface. MAF remains the public orchestration boundary.

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
- SMTP capability is not exposed through MCP in the first public tool set.

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

## 16. Failure handling

- Tool requests return local data during IMAP outages.
- Responses include synchronization freshness so stale data is explicit.
- Each account, embedding batch, and SMTP delivery has isolated retry state.
- PostgreSQL unavailability fails readiness and pauses workers.
- Missing or corrupt MIME content is reported explicitly and queued for repair without a synchronous IMAP fetch in the tool request.
- Poison messages are quarantined after bounded parsing attempts without blocking the folder checkpoint.
- Background jobs are persisted in PostgreSQL; no separate queue is required initially.
- Startup applies only backward-compatible migrations automatically. Destructive or long-running migrations use the CLI.

## 17. Observability

- Structured JSON logs with account IDs and message IDs, never addresses, subjects, bodies, tokens, or credentials by default.
- OpenTelemetry traces for MCP calls, database operations, synchronization cycles, retrieval, agent runs, and SMTP delivery.
- Metrics include sync lag, reconnect count, cached messages, missing content, embedding backlog, retrieval latency, token usage when available, outbox depth, and tool errors.
- Protocol logging is disabled by default and, when temporarily enabled, is redacted and written to a protected location.

## 18. Deployment

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


## 19. Future ideas

These ideas are deliberately outside the first release. They are recorded here so the initial architecture keeps stable seams for later work without adding premature packages, services, test projects, or operational dependencies.

### 19.1 Agent Governance Toolkit (AGT)

Microsoft Agent Governance Toolkit (AGT) may become useful when MailMcp exposes agent-mediated actions beyond read-only retrieval, especially if future MCP tools can send mail, mutate local state, delegate work, or call external systems. AGT is a governance layer for agents and MCP tool calls: it can help make policy checks, input/output inspection, and allow/deny decisions explicit instead of burying those decisions inside prompt text or ad-hoc tool handlers.

AGT is not part of the first release because the public MCP surface is read-only, deterministic tool authorization is enforced at the transport and application boundaries, and MailMcp already treats retrieved mail as untrusted input. Before adopting AGT, the team must verify package maturity, .NET 10 compatibility, license and service-term implications, policy authoring model, telemetry data exposure, and whether AGT decisions can be expressed without leaking provider-specific concepts into `Domain`, `Application`, or `Mcp`.

Potential AGT evaluation scenarios include governing a future `send_email` MCP tool, blocking prompt-injected tool escalation from message content, enforcing per-client tool policies, recording auditable governance decisions, and validating that denied actions fail closed with safe MCP error codes.

### 19.2 MinIO object storage migration

All raw-content operations use the application-owned `IMessageContentStore` port with streaming put, open-read, existence, and delete operations. The first implementation stores content in PostgreSQL; neither the application nor domain layer receives a PostgreSQL-specific locator or `bytea` type. This seam keeps a future MinIO or S3-compatible object-storage migration possible without changing mail use cases.

A later MinIO migration would be performed online in controlled stages:

1. Add the MinIO adapter and explicit content-backend/locator metadata while PostgreSQL remains authoritative.
2. Stream existing MIME rows to MinIO in bounded background batches.
3. Verify every copied object against its stored byte length and SHA-256 hash.
4. Enable dual-read with PostgreSQL fallback and repair while new content is written to the selected backend.
5. Switch MinIO to authoritative reads only after coverage and consistency metrics reach the required threshold.
6. Retain PostgreSQL MIME for a safety interval, then remove migrated `bytea` values in bounded maintenance batches.

No MinIO package, credentials, process, bucket, deployment volume, or object-storage test fixture is included in the first release. Any future MinIO SDK, container image, or hosted object-storage dependency must be pinned, license-reviewed, and recorded in `LICENSES.md` before adoption.

### 19.3 Development orchestration with Aspire

Aspire is planned as a development-time orchestration and observability layer, not as the production runtime or application framework. A future `MailMcp.AppHost` may model the host process, PostgreSQL with pgvector, local secret/configuration bindings, and test-only services so contributors can run the distributed development environment consistently from one entry point.

The AppHost should stay minimal: explicit resource names, explicit dependencies, separate development/test/production configuration, and no business logic. Production deployment continues to use Docker Compose or rootless Podman Compose managed by systemd unless a later deployment decision replaces that path. Aspire-generated service discovery or orchestration concerns must not leak into `Domain`, `Application`, `Mcp`, or mail protocol adapters.

Future integration testing can reuse Aspire orchestration when it improves repeatability, but integration-test infrastructure remains separate from the initial unit-test-only solution.

### 19.4 Future integration testing with smtp4dev

A future integration-test suite should include smtp4dev as the controlled SMTP target for delivery scenarios. smtp4dev is a fake SMTP server intended for development and testing, is available as Docker/OCI images and a .NET tool, and its NuGet package currently declares the BSD-3-Clause license. Before adding it to the repository, the exact package, container image, or tool version must be pinned and recorded in `LICENSES.md`.

The smtp4dev-based tests should validate SMTP connection policy, STARTTLS behavior where supported by the selected test setup, authentication settings, MIME envelope/content emitted by MailMcp, outbox retry classification, idempotency behavior, and failure handling. smtp4dev does not replace unit tests and does not validate IMAP semantics; any IMAP integration fixture remains a separate future decision.

The first release still does not add integration-test projects, Testcontainers, Docker fixtures, smtp4dev packages, or smtp4dev container references.

### 19.5 Future administration CLI

The dedicated administration CLI is named `mcpmail` and is a future operational interface rather than an initial implementation requirement. The first release can be administered through validated YAML configuration plus deployment secret references, with account-test and migration workflows added only when their application services exist.

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

## 20. Delivery stages

1. Repository and solution foundation, unit-test projects, Kestrel HTTPS configuration, PostgreSQL, and migrations.
2. YAML-based configuration binding, typed option validation, secret-reference resolution, and MailKit connection validation with mocked IMAP/SMTP boundary tests.
3. Read-only initial and continuous IMAP synchronization with offline MIME storage and `\Seen` regression tests.
4. Deterministic MCP tools `list_emails` and `get_email_content` with unit-tested authorization and mapping.
5. PostgreSQL full-text indexing and `search_emails`.
6. pgvector ingestion with configurable embedding profile.
7. Agent Framework RAG and conditional `ask_mail`.
8. SMTP outbox and delivery service with unit-tested state transitions and retry behavior.
9. ChatGPT OAuth/mTLS validation and general OAuth MCP client profile.
10. Production hardening, backup, metrics, recovery exercises, and explicit evaluation plans for future ideas: AGT governance, Aspire local orchestration, MinIO object storage, `mcpmail`, and smtp4dev-backed SMTP integration tests.

## 21. Acceptance criteria

- Synchronizing and retrieving an unread message leaves its remote `\Seen` flag unchanged.
- Repeated synchronization is idempotent for the same account, folder, UIDVALIDITY, and UID.
- `list_emails`, `get_email_content`, and `search_emails` work while IMAP is unavailable.
- A tool request never triggers a synchronous IMAP body fetch.
- Tens of thousands of messages use indexed keyset queries rather than full scans or offset pagination.
- A message is searchable lexically after extraction and semantically after its active-profile embedding is stored.
- RAG answers cite stable local message IDs and never gain mail mutation capabilities.
- ChatGPT requests require a valid OpenAI client certificate and a valid owner OAuth token.
- A general OAuth MCP client can connect without weakening the ChatGPT mTLS profile.
- Invalid issuer, audience, expiry, subject, scope, certificate chain, SAN, or host name is rejected before tool execution.
- Secrets and mail content do not appear in default logs or telemetry.
- The complete xUnit unit suite passes without network, database, container, or filesystem dependencies.
- IMAP/SMTP success, failure, disconnect, cancellation, and capability scenarios are reproducible through NSubstitute-based protocol boundaries.
- First-release configuration can be expressed in YAML without placing secrets or encrypted secret values in Git.
- Future CLI work is explicitly deferred and uses `mcpmail` with `System.CommandLine` when implemented.
- Future ideas are collected separately from first-release scope, including AGT governance evaluation, MinIO migration, Aspire orchestration, `mcpmail`, and smtp4dev-backed SMTP delivery verification.

## 22. References verified for this draft

- [.NET releases and support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)
- [Microsoft Agent Framework overview](https://learn.microsoft.com/en-us/agent-framework/overview/)
- [Agent Framework RAG](https://learn.microsoft.com/agent-framework/agents/rag)
- [Agent Framework integrations and vector stores](https://learn.microsoft.com/agent-framework/integrations/)
- [Official MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [MCP C# SDK getting started](https://csharp.sdk.modelcontextprotocol.io/concepts/getting-started.html)
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
- [NSubstitute documentation](https://nsubstitute.github.io/)
- [.NET configuration providers](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-providers)
- [ASP.NET Core configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0)
- [Safe storage of app secrets in development](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0)
- [Aspire overview](https://aspire.dev/get-started/what-is-aspire/)
- [Aspire AppHost](https://aspire.dev/get-started/app-host/)
- [System.CommandLine overview](https://learn.microsoft.com/en-us/dotnet/standard/commandline/)
- [System.CommandLine NuGet package](https://www.nuget.org/packages/System.CommandLine)
- [System.CommandLine GitHub repository](https://github.com/dotnet/command-line-api)
- [smtp4dev GitHub repository](https://github.com/rnwood/smtp4dev)
- [smtp4dev NuGet package](https://www.nuget.org/packages/Rnwood.Smtp4dev)
- [smtp4dev Docker image](https://hub.docker.com/r/rnwood/smtp4dev)
- [smtp4dev installation documentation](https://raw.githubusercontent.com/rnwood/smtp4dev/master/docs/Installation.md)
- [Agent Governance Toolkit documentation](https://microsoft.github.io/agent-governance-toolkit/)
- [Agent Governance Toolkit GitHub repository](https://github.com/microsoft/agent-governance-toolkit)
