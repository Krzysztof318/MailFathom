# MailMcp Specifications

[`2026-07-22-mail-mcp-architecture-draft.md`](2026-07-22-mail-mcp-architecture-draft.md) is the architecture draft: the durable description of what MailMcp is and why. The numbered specifications below decompose the gap between that draft and the implemented code into individually reviewable units of work.

Each specification is scoped to one pull request of roughly 1000 changed lines or fewer, including tests and documentation. Implementation plans are written per specification when that work starts, not in advance.

## How to read a specification

Every specification states its roadmap group, the draft delivery stage it serves, what it depends on, and an estimated change size. It describes goal, current state, approved scope, safety and privacy consequences, testing, explicit non-scope, and a definition of done. It does not prescribe type names or method signatures; those are decided in the plan and the implementation.

## Implemented before this roadmap

The first vertical slice is already merged: periodic read-only IMAP reconciliation, domain identity for the `(account, folder, UIDVALIDITY, UID)` tuple, application-owned synchronization and persistence ports, EF Core PostgreSQL mappings for metadata, content, and checkpoints, a `bytea`-backed message content store, the MailKit adapter, and a disabled-by-default background worker.

## Roadmap

### A — Configuration, transport security, and resilience

| # | Specification | Draft stage |
|---|---|---|
| 01 | [Mail transport security policy](01-mail-transport-security-policy.md) | 2 |
| 02a | [Secret reference resolution](02a-secret-reference-resolution.md) | 2 |
| 02b | [Certificate material and secret rotation](02b-certificate-material-and-secret-rotation.md) | 2 |
| 03 | [Resilience pipeline foundation](03-resilience-pipeline-foundation.md) | cross-cutting |
| 04 | [IMAP session resilience](04-imap-session-resilience.md) | 3 |
| 05 | [Mail folder configuration and discovery](05-mail-folder-configuration-and-discovery.md) | 3 |

### B — Message data enrichment

| # | Specification | Draft stage |
|---|---|---|
| 06 | [MIME metadata extraction](06-mime-metadata-extraction.md) | 3 |
| 07 | [Message metadata persistence and indexes](07-message-metadata-persistence-and-indexes.md) | 3 |
| 08 | [Extracted text and full-text index](08-extracted-text-and-fulltext-index.md) | 5, pulled forward |

### C — Continuous synchronization

| # | Specification | Draft stage |
|---|---|---|
| 09 | [Per-account synchronization supervisor](09-per-account-synchronization-supervisor.md) | 3 |
| 10 | [Remote expunge and flag reconciliation](10-remote-expunge-and-flag-reconciliation.md) | 3 |
| 11 | [IMAP IDLE continuous synchronization](11-imap-idle-continuous-sync.md) | 3 |
| 12 | [IMAP NOTIFY and CONDSTORE](12-imap-notify-and-condstore.md) | 3 |

### D — Read side and MCP

| # | Specification | Draft stage |
|---|---|---|
| 13 | [Mailbox query read models](13-mailbox-query-read-models.md) | 4 |
| 14 | [Email content read model](14-email-content-read-model.md) | 4 |
| 15 | [Lexical email search](15-lexical-email-search.md) | 4 |
| 16 | [MCP server hosting and the `list_emails` tool](16-mcp-server-hosting-and-list-emails-tool.md) | 4 |
| 17 | [The `get_email_content` tool](17-get-email-content-tool.md) | 4 |
| 18 | [The `search_emails` tool](18-search-emails-tool.md) | 4 |

### E — Schema consolidation and infrastructure verification

| # | Specification | Draft stage |
|---|---|---|
| 19 | [EF Core migration baseline and apply policy](19-ef-core-migration-baseline-and-apply-policy.md) | 1, rescheduled |
| 20 | [Aspire integration test foundation](20-aspire-integration-test-foundation.md) | deferred integration phase |
| 21 | [IMAP behavior integration tests](21-imap-behavior-integration-tests.md) | deferred integration phase |

## Three deliberate departures from the draft's stage order

**Migrations move from stage 1 to the end.** Generating migrations while the schema is still growing produces a dozen incremental migrations to review, several of which partially revert each other. Specification 19 produces one baseline migration reviewed once against the settled schema. Specification 07 covers the interval with a Development-only schema bootstrap that specification 19 deletes.

**Full-text indexing moves from stage 5 to before stage 4.** The `search_emails` tool cannot be built before the index it queries exists, so specification 08 precedes specifications 15 and 18.

**Resilience becomes an explicit foundation.** The draft asks for bounded jittered backoff and isolated retry state but never names a mechanism, and the repository has resilience only for `HttpClient`. Specification 03 establishes named Polly pipelines for every outbound dependency class, and specification 04 is the first consumer.

## Not covered by this roadmap

Stages 6 through 10 of the draft: pgvector ingestion and embedding profiles, Agent Framework RAG and `ask_mail`, the SMTP outbox, OAuth 2.1 and the ChatGPT mTLS profile, and production hardening. Those are decomposed into specifications when this segment nears completion, so they are written against the code that actually exists by then.

Beyond the draft, five capabilities are tracked as issues on the roadmap board without a specification, because each needs a decision — in one case an ADR — before its scope can be written. They are listed here so the roadmap does not read as the complete set of known work.

| Capability | Issue | What has to be settled first |
|---|---|---|
| Encrypted and signed email, S/MIME and OpenPGP | [#75](https://github.com/Krzysztof318/MailMcp/issues/75) | Whether local decryption is permitted at all, since it converts end-to-end protected mail into searchable plaintext; needs an ADR |
| Spam and junk classification | [#76](https://github.com/Krzysztof318/MailMcp/issues/76) | The asynchronous job model from draft section 21.5, which nothing has built yet |
| Antivirus scanning for stored attachments | [#77](https://github.com/Krzysztof318/MailMcp/issues/77) | Engine choice under the licensing constraint: ClamAV's `libclamav` is GPL-2.0 and cannot be linked |
| OAuth for outbound IMAP and SMTP | [#78](https://github.com/Krzysztof318/MailMcp/issues/78) | The credential shape from #62 and #36, plus Gmail's restricted-scope assessment obligation |
| Local secret detection before AI egress | [#79](https://github.com/Krzysztof318/MailMcp/issues/79) | Which egress points exist, which only becomes concrete once the RAG stages land |

Attachment handling is not on that list. It is inside this roadmap, and [#80](https://github.com/Krzysztof318/MailMcp/issues/80) corrected specifications 06, 07, 08, 13, 14, 15, and 17 so the classification rule, file-name normalization, and structural parsing bounds are stated before any of that work starts.

Specification 16 shipped the MCP endpoint with no transport authentication at all, disabled by default and warning at startup whenever it was enabled. [#140](https://github.com/Krzysztof318/MailMcp/issues/140) replaces that posture before the first release: [#164](https://github.com/Krzysztof318/MailMcp/issues/164) makes an enabled endpoint state whether it requires an API key or nothing, and [#165](https://github.com/Krzysztof318/MailMcp/issues/165) adds the mTLS trust profiles. Neither is OAuth 2.1, which stays where the table above puts it — an API key names a client rather than a person, and the owner authorization every tool call already performs is unchanged by both.

## Dependencies these specifications add

| Specification | Package | License | Note |
|---|---|---|---|
| 03 | `Polly.Core`, `Polly.Extensions` | MIT | Named resilience pipelines; `Microsoft.Extensions.Resilience` evaluated for telemetry enrichment |
| 08 | none | — | HTML-to-text uses `MimeKit.Text.HtmlTokenizer`, already pinned via MailKit |
| 14 | `HtmlSanitizer` 9.0.967, transitively `AngleSharp` 0.17.1 and `AngleSharp.Css` 0.17.0 | MIT | Exact AngleSharp pin forecloses referencing AngleSharp 1.x directly; see specification 14 |
| 20 | `Aspire.Hosting.Testing` | MIT | Pinned to the Aspire version already in use |
| 21 | a containerized IMAP server image | to be verified | Selected and license-reviewed as part of that work |

Every entry is recorded in `THIRD_PARTY_LICENSES.md` in the same change set that adds the dependency.
