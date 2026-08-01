<img src="assets/icon-900.png" alt="MailFathom logo" width="120">

# MailFathom

**Your mail, on your own machine, readable by an AI agent — and by nothing else.**

MailFathom is an open-source, self-hosted service that synchronizes mail from your IMAP accounts into a PostgreSQL database you run, indexes it for search, and serves it to AI agents as read-only tools over the [Model Context Protocol](https://modelcontextprotocol.io/). A connected agent can list, read, and search your mail. It cannot send, delete, move, or mark anything, because no such tool exists on the surface.

Two properties hold everywhere, and much of the rest of the design follows from them:

- **Reading is local.** A tool call answers from your copy and never contacts a mail server, so it is fast, it works while the server is down, and it cannot change anything remotely. Every result states how fresh the local copy is.
- **Synchronization is read-only.** Fetching mail never sets the remote `\Seen` flag, so mail MailFathom has copied still shows as unread in your own mail client until you read it there.

What an agent gets is three tools, and they are the whole surface:

| Tool | What it answers |
| --- | --- |
| `list_emails` | A page of the timeline, newest first, filtered by account, folder, sender, recipient, subject, date range, seen state, or attachment presence |
| `search_emails` | Ranked matches for a text query across subjects, participants, and body text, each with short extracts around what matched |
| `get_email_content` | Up to ten messages in full: normalized headers, plain-text body, optionally sanitized HTML, and attachment names, types, and sizes — never attachment bytes |

## Start here

| You are | Start at |
| --- | --- |
| Deciding whether MailFathom is for you | [What it does well](#what-it-does-well) below, then [the user guide](docs/users/README.md) |
| Installing or operating it | [Installing MailFathom](docs/users/installation.md), then [getting started](docs/users/getting-started.md) |
| Connecting an agent to a running instance | [Using the tools](docs/users/usage.md) |
| Contributing | [CONTRIBUTING.md](CONTRIBUTING.md) |

## Project status

MailFathom has not had a first release. No versioned artifact exists yet, so every installation starts from a checkout of this repository, and the documentation says so wherever it matters rather than describing a release that has not happened. The first release is milestone [`0.1.0`](https://github.com/Krzysztof318/MailFathom/milestone/1): the read side that makes the product usable — mailbox queries, email content, lexical search, and the three MCP tools — on a settled database schema.

Nightly images are built from `main` and published to the GitHub Container Registry. A nightly is not a release: its schema can be ahead of any published migration, it has no upgrade path in either direction, and it is deleted once newer ones accumulate. [What a nightly build risks](docs/operations/container-image.md#what-a-nightly-build-risks) states the whole of it before you choose one.

## What it does well

MailFathom is built as an enterprise-grade system from the first line, even while its feature scope is still small. Every claim below is a property of the code and the deployment assets today, and each links to the page that documents it.

**Read-only by design, not by configuration.** The MCP surface is three tools — `list_emails`, `get_email_content`, `search_emails` — and that is all of it. Nothing on the surface writes, and synchronization is incapable of marking remote mail as read. Attachment bytes are never returned; a content read reports names, types, and sizes. → [MCP tools](docs/features/mcp-tools.md)

**Secure by default, and explicit about every weakening.** The MCP endpoint is off until you enable it, and enabling it means stating whether it requires an API key or nothing at all; the unauthenticated posture is legal, announced with a startup warning, and never the default. Client certificates and per-client rate limits are part of the endpoint rather than something a proxy has to add. IMAP is TLS-on-connect by default, a private certificate authority is trusted rather than validation disabled, and a configuration that weakens the transport or sends a clear-text credential fails startup unless it says so explicitly. → [The MCP endpoint](docs/operations/mcp-endpoint.md), [transport security](docs/features/imap-synchronization.md#transport-security)

**Credentials never live in configuration.** A secret-bearing setting holds a *reference* — a file path, a systemd credential, an environment variable — and the material lives wherever the deployment provisions it. A configuration file is therefore safe to review, diff, and back up: leaking it leaks paths, not passwords. → [Secret provisioning](docs/operations/secret-provisioning.md), [rotation](docs/operations/secret-rotation.md)

**Privacy by design, aligned with GDPR.** Mail content, metadata, extracted text, and search extracts are classified as personal data by default: never written to a log, never carried in an error message, and bounded per call — at most 100 summaries in a page, 50 ranked matches in a search, a configured character bound on a body. Those bounds are the deployment's control over how much mail a single call can draw out, so a request for more is refused rather than stretched. The architecture keeps explicit seams for the data-subject workflows a later release implements. → [Using the tools](docs/users/usage.md)

**It fails fast and says why.** Startup resolves every secret reference and verifies the database schema before the process serves anything, and a refusal names the configuration key or the pending migration that caused it. Migrations are never applied while starting, in any environment — applying is a step you take, with a backup first. Three probes answer on a listener of their own, and telemetry is OpenTelemetry, exported only where you point it. → [Health endpoints](docs/operations/health-endpoints.md), [configuration reference](docs/operations/configuration-reference.md), [telemetry](docs/operations/telemetry.md)

**The deployment assets are hardened, not illustrative.** The image is chiseled — no shell, no package manager, no HTTP client — runs as an unprivileged user on a read-only root filesystem with every Linux capability dropped, creates no diagnostic socket, and carries no tool that could apply a migration. Docker Compose and the Helm chart both ship that posture by default, and the chart meets the Restricted Pod Security Standard. → [The container image](docs/operations/container-image.md), [Compose](docs/operations/deployment-compose.md), [Kubernetes](docs/operations/deployment-kubernetes.md)

**The supply chain is verifiable.** Images are multi-architecture, built from base images pinned to an exact patch version, scanned before publication, and accompanied by signed build provenance that ties a digest to the commit and workflow that produced it. Package versions are pinned centrally with committed lock files, and every third-party component is reviewed against a licensing policy that keeps the project commercially redistributable. → [Verification](docs/operations/container-image.md#verification), [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md)

**It is built to be maintained.** A .NET 10 clean-architecture modular monolith with enforced boundaries between domain, application, infrastructure, protocol, and host; compiler and analyzer diagnostics are errors; every behavior change ships with tests; and the decisions that shape the system are recorded as ADRs rather than remembered. → [Solution structure](docs/architecture/solution-structure.md), [decisions](docs/decisions/README.md)

## Getting started

The recommended first installation is **Docker Compose**. It is the only shape that provisions PostgreSQL for you, and its defaults publish both ports on loopback, so nothing is reachable from another machine until you decide it should be.

```bash
git clone https://github.com/Krzysztof318/MailFathom.git
cd MailFathom
```

From there, [installing MailFathom](docs/users/installation.md) covers what every shape needs — Linux, PostgreSQL with the `vector` extension, an IMAP account, an explicit schema step — and routes you to the guide for Compose, Kubernetes, or a native systemd process. [Getting started](docs/users/getting-started.md) then walks from an installed instance to a first successful tool call: provisioning the secrets, configuring a mailbox, applying the schema, verifying health, enabling the MCP endpoint, and connecting a client.

To evaluate MailFathom from the checkout instead of deploying it, the local Aspire orchestration provisions PostgreSQL and applies the schema on its own. [Local development](docs/operations/local-development.md#running-locally-with-aspire) has that path.

## Documentation

[`docs/`](docs/README.md) is the index. The pages you are most likely to want first:

| | |
| --- | --- |
| [User guide](docs/users/README.md) | Install, configure, run, and use MailFathom |
| [Configuration reference](docs/operations/configuration-reference.md) | Every user-settable option, its default, and whether changing it needs a restart |
| [MCP endpoint](docs/operations/mcp-endpoint.md) | Authentication, TLS, browser origins, client certificates, rate limits |
| [MCP tools](docs/features/mcp-tools.md) | The tool contracts, their arguments and results, and the stable error codes |
| [IMAP synchronization](docs/features/imap-synchronization.md) | What a run stores, how it reconciles, and what it never touches |
| [Architecture](docs/architecture/solution-structure.md) | The boundaries, the projects, and why they are drawn there |
| [Decisions](docs/decisions/README.md) | The ADRs, and the workflow that produces them |

Documentation under `docs/` describes behavior that exists. Where something is planned, it is tracked as an issue rather than written up as though it worked.

## Where it is going

After the first release, the direction is set and the order is not fixed:

- **Continuous synchronization.** Today a run is periodic reconciliation. Long-lived IMAP `IDLE` and `NOTIFY` connections, with `CONDSTORE` for cheap flag reconciliation, make new mail arrive in seconds instead of on an interval.
- **Semantic retrieval.** Embeddings in pgvector beside the lexical index, and an `ask_mail` tool that answers a question from retrieved passages rather than making an agent page through a mailbox.
- **Sending.** A durable SMTP outbox exists as an application capability before it is ever an MCP tool; exposing it waits on a reviewed authorization and confirmation flow, because a tool that sends mail is a different security question from one that reads it.
- **OAuth 2.1 on the endpoint**, alongside the API keys and client certificates that guard it today.
- **Attachment handling**, from classification and file-name normalization through to retrieval.

### Ideas, not yet scope

These are recorded as open questions, each waiting on a decision rather than on effort. [Discussions](https://github.com/Krzysztof318/MailFathom/discussions) is where they are argued, and the `Ideas` category is open to yours.

- **Encrypted and signed mail**, S/MIME and OpenPGP — the hard part is not the parsing but whether local decryption should be permitted at all, since it turns end-to-end protected mail into searchable plaintext. [#75](https://github.com/Krzysztof318/MailFathom/issues/75)
- **Spam and junk classification** as an asynchronous job. [#76](https://github.com/Krzysztof318/MailFathom/issues/76)
- **Antivirus scanning of stored attachments**, constrained by which engines can be used under a permissive licensing policy. [#77](https://github.com/Krzysztof318/MailFathom/issues/77)
- **OAuth for outbound IMAP and SMTP**, so a provider that has retired password authentication stays reachable. [#78](https://github.com/Krzysztof318/MailFathom/issues/78)
- **Local secret detection before anything leaves for a model**, which only becomes concrete once retrieval-augmented answering exists. [#79](https://github.com/Krzysztof318/MailFathom/issues/79)
- **Jobs you define yourself**, in two shapes that stay separate rather than becoming one: *programmatic* jobs, where a deterministic rule matches stored mail and takes a bounded action, and *skill-based* jobs, whose body is an instruction an agent carries out against a slice of your mail. The second asks questions the first does not — what content leaves for a model, what a job may act on, and what an attacker who can write you an email can make one do. [#251](https://github.com/Krzysztof318/MailFathom/issues/251)

## Contributing

Contributions are welcome, and the entry point is [CONTRIBUTING.md](CONTRIBUTING.md): it gets you from a clone to a passing verification run and states the few rules a pull request has to satisfy. Every change starts from an issue, so open one — or comment on an existing one — before writing code, and wait for a reply on anything larger than a typo, because MailFathom is pre-release and its direction still moves faster than its issue list.

**MailFathom is developed AI-first, and close to zero-touch.** Nearly every line here was written by an autonomous coding agent working from an issue and the rules in [`AGENTS.md`](AGENTS.md), and reviewed before merge; the maintainer sets direction and decides, but rarely edits code by hand. Working the same way is encouraged rather than merely tolerated — point an agent at your checkout, let it read the instruction files, and let it produce the change, its tests, and its documentation in one pass. A hand-written patch is judged identically. What does not change either way is that you read your diff before submitting it, and that the same gates and the same licensing obligations apply. [How this project is built](CONTRIBUTING.md#how-this-project-is-built) has the whole of it.

[`docs/operations/local-development.md`](docs/operations/local-development.md) is the full development setup, [`AGENTS.md`](AGENTS.md) holds the engineering rules the build and the review enforce, and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) applies to everyone taking part.

### Questions, bugs, and proposals

[Discussions](https://github.com/Krzysztof318/MailFathom/discussions) takes questions in `Q&A` and proposals in `Ideas`; a question is not a unit of work, and one that turns out to be work gets converted into an issue. A defect or a piece of scope belongs in [issues](https://github.com/Krzysztof318/MailFathom/issues) — except a vulnerability, which has a private channel below.

## Security

MailFathom holds mailbox credentials, OAuth tokens, certificate material, and a local copy of someone's mail. Report a vulnerability privately through [SECURITY.md](SECURITY.md) rather than in a public issue.

## License

MailFathom is licensed under the [Apache License, Version 2.0](LICENSE), SPDX identifier `Apache-2.0`. Source files repeat that grant in a header the build enforces, and a published artifact carries `LICENSE` and `NOTICE` beside the binaries. The container image is that same publish output, so it carries both files and declares `org.opencontainers.image.licenses`; the Helm chart states the identifier as `artifacthub.io/license`.

MailFathom was originally created by **Krzysztof Kasprowicz**. The root [NOTICE](NOTICE) records that attribution, which section 4(d) of the license asks a derivative distribution to preserve while it remains relevant to the derived work. A fork may add its own attribution notices beside it. The notice adds no use restriction, changes nothing about the license, and claims nothing about contributions written by other copyright holders.

Contributions to this repository are offered under Apache-2.0, by section 5 of the license. There is no contributor licence agreement and no developer certificate of origin, and contributors keep the copyright in what they write.

Third-party components that MailFathom consumes are reviewed separately in [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md). That register records what MailFathom depends on and under which terms; it grants nothing in MailFathom itself, which `LICENSE` alone does.

The application icon in [`assets/`](assets) is MailFathom's own asset rather than a third-party component, and the same grant covers it. The register records how it was produced and why no one else holds rights in it.
