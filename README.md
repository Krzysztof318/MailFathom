<img src="https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/icon-900.png" alt="MailFathom logo" width="120">

# MailFathom

[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE) [![Release](https://github.com/Krzysztof318/MailFathom/actions/workflows/release.yml/badge.svg)](https://github.com/Krzysztof318/MailFathom/actions/workflows/release.yml) [![Version](https://img.shields.io/github/v/release/Krzysztof318/MailFathom?sort=semver&label=version)](https://github.com/Krzysztof318/MailFathom/releases/latest) [![Nightly](https://github.com/Krzysztof318/MailFathom/actions/workflows/nightly.yml/badge.svg)](https://github.com/Krzysztof318/MailFathom/actions/workflows/nightly.yml)

**A brain for your mail — self-hosted, AI-native, and yours alone.**

A mailbox is the largest archive most people own and the least usable one. Contracts, decisions, invoices, threads that ended without a conclusion, attachments nobody will ever find again: all of it is in there, and none of it is reachable except by scrolling. Mail clients are built to show you the newest of it, one message at a time. After twenty years of accumulation, that is the wrong shape entirely.

MailFathom is being built to change what mail *is* to software. It synchronizes your IMAP accounts into a PostgreSQL database you run, keeps that copy current, indexes it so the whole of it is reachable rather than only its most recent slice, and serves it to AI agents as tools over the [Model Context Protocol](https://modelcontextprotocol.io/). The destination is a mail brain: something an agent can put a question to and get an answer from, working across years of mail, on infrastructure that belongs to you.

MCP is how agents reach MailFathom; it is not what MailFathom is. The protocol surface is deliberately thin, and the project is building what sits behind it — continuous synchronization, extracted and indexed content, lexical search today, semantic retrieval and question answering next, and eventually the ability to act on your mail rather than only read it. [Where it is going](https://github.com/Krzysztof318/MailFathom#where-it-is-going) is the current direction in full.

None of it depends on somebody else's service. The copy is yours, the database is yours, the deployment is yours, and the AI capabilities on the roadmap arrive as providers you choose and point at rather than as ones compiled into the product.

## What exists today

What is implemented is read-only synchronization and three tools, and this README is split on that line: this section and [What it does well](https://github.com/Krzysztof318/MailFathom#what-it-does-well) describe the code as it stands, while [Where it is going](https://github.com/Krzysztof318/MailFathom#where-it-is-going) is the roadmap.

Two properties hold everywhere, and much of the rest of the design follows from them:

- **Reading is local.** A tool call answers from your copy and never contacts a mail server, so it is fast, it works while the server is down, and it cannot change anything remotely. Every result states how fresh the local copy is.
- **Synchronization never writes to your mailbox.** Fetching mail never sets the remote `\Seen` flag, so mail MailFathom has copied still shows as unread in your own mail client until you read it there.

What an agent gets is three tools, and they are the whole surface:

| Tool | What it answers |
| --- | --- |
| `list_emails` | A page of the timeline, newest first, filtered by account, folder, sender, recipient, subject, date range, seen state, or attachment presence |
| `search_emails` | Ranked matches for a text query across subjects, participants, and body text, each with short extracts around what matched |
| `get_email_content` | Up to ten messages in full: normalized headers, plain-text body, optionally sanitized HTML, and attachment names, types, and sizes — never attachment bytes |

A connected agent can list, read, and search your mail. It cannot send, delete, move, or mark anything, because no such tool exists on the surface. That describes this stage rather than a permanent limit: writing is on the roadmap, starting with sending, and each write capability will arrive as its own tool behind a reviewed authorization and confirmation flow — never as a setting that loosens a tool you already trust.

## Start here

| You are | Start at |
| --- | --- |
| Deciding whether MailFathom is for you | [What it does well](https://github.com/Krzysztof318/MailFathom#what-it-does-well) below, then [the user guide](https://github.com/Krzysztof318/MailFathom/blob/main/docs/users/README.md) |
| Installing or operating it | [Installing MailFathom](https://github.com/Krzysztof318/MailFathom/blob/main/docs/users/installation.md), then [getting started](https://github.com/Krzysztof318/MailFathom/blob/main/docs/users/getting-started.md) |
| Connecting an agent to a running instance | [Using the tools](https://github.com/Krzysztof318/MailFathom/blob/main/docs/users/usage.md) |
| Contributing | [CONTRIBUTING.md](https://github.com/Krzysztof318/MailFathom/blob/main/CONTRIBUTING.md) |

## Project status

`0.2.0` is the current release. It builds on `0.1.0`'s read side — mailbox queries, email content, lexical search, and the three MCP tools — with OAuth mailbox authentication, push synchronization that starts a pass when the mail server says a folder changed, and an administrative endpoint an operator reaches with the `mfctl` command. It ships as a container image, a Helm chart, the SQL script that creates the schema it expects, and an `mfctl` binary per platform — [where the artifacts are published](https://github.com/Krzysztof318/MailFathom#where-the-artifacts-are-published) has the references. There is no binary artifact for the service itself, so a native installation starts from a checkout of this repository. [The changelog](https://github.com/Krzysztof318/MailFathom/blob/main/CHANGELOG.md) states what the release promises across the MCP tool contract, the configuration schema, the database schema, and the deployment contract.

Nightly images are built from `main` and published to both registries alongside the releases. A nightly is not a release: its schema can be ahead of any published migration, it has no upgrade path in either direction, and it is deleted once newer ones accumulate. [What a nightly build risks](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/container-image.md#what-a-nightly-build-risks) states the whole of it before you choose one.

## What it does well

MailFathom is built as an enterprise-grade system from the first line, even while its feature scope is still small. Every claim below is a property of the code and the deployment assets today, and each links to the page that documents it.

**Nothing on the surface writes, and no setting changes that.** The MCP surface is three tools — `list_emails`, `get_email_content`, `search_emails` — and that is all of it. There is no write tool to enable, and synchronization is incapable of marking remote mail as read. Attachment bytes are never returned; a content read reports names, types, and sizes. → [MCP tools](https://github.com/Krzysztof318/MailFathom/blob/main/docs/features/mcp-tools.md)

**Secure by default, and explicit about every weakening.** The MCP endpoint is off until you enable it, and enabling it means stating whether it requires an API key or nothing at all; the unauthenticated posture is legal, announced with a startup warning, and never the default. Client certificates and per-client rate limits are part of the endpoint rather than something a proxy has to add. IMAP is TLS-on-connect by default, a private certificate authority is trusted rather than validation disabled, and a configuration that weakens the transport or sends a clear-text credential fails startup unless it says so explicitly. → [The MCP endpoint](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/mcp-endpoint.md), [transport security](https://github.com/Krzysztof318/MailFathom/blob/main/docs/features/imap-synchronization.md#transport-security)

**Credentials never live in configuration.** A secret-bearing setting holds a *reference* — a file path, a systemd credential, an environment variable — and the material lives wherever the deployment provisions it. A configuration file is therefore safe to review, diff, and back up: leaking it leaks paths, not passwords. → [Secret provisioning](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/secret-provisioning.md), [rotation](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/secret-rotation.md)

**Mail is handled as personal data by the code that touches it.** Content, metadata, extracted text, and search extracts are never written to a log, never carried in an error message, and bounded per call — at most 100 summaries in a page, 50 ranked matches in a search, a configured character bound on a body — so a deployment can decide how much mail a single call may draw out. That is the part software can settle. Whether a deployment satisfies GDPR depends on how you run it: where the database sits, who reaches it, how long you keep mail, and which model an agent hands a result to. What MailFathom offers is an architecture that keeps those choices open rather than one that has already made them badly, including explicit seams for the data-subject workflows a later release implements. → [Using the tools](https://github.com/Krzysztof318/MailFathom/blob/main/docs/users/usage.md)

**It fails fast and says why.** Startup resolves every secret reference and verifies the database schema before the process serves anything, and a refusal names the configuration key or the pending migration that caused it. Migrations are never applied while starting, in any environment — applying is a step you take, with a backup first. Three probes answer on a listener of their own, and telemetry is OpenTelemetry, exported only where you point it. → [Health endpoints](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/health-endpoints.md), [configuration reference](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/configuration-reference.md), [telemetry](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/telemetry.md)

**The deployment assets are hardened, not illustrative.** The image is chiseled — no shell, no package manager, no HTTP client — runs as an unprivileged user on a read-only root filesystem with every Linux capability dropped, creates no diagnostic socket, and carries no tool that could apply a migration. Docker Compose and the Helm chart both ship that posture by default, and the chart meets the Restricted Pod Security Standard. → [The container image](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/container-image.md), [Compose](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/deployment-compose.md), [Kubernetes](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/deployment-kubernetes.md)

**The supply chain is verifiable.** Images are multi-architecture, built from base images pinned to an exact patch version, scanned before publication, and accompanied by signed build provenance that ties a digest to the commit and workflow that produced it. Package versions are pinned centrally with committed lock files, and every third-party component is reviewed against a licensing policy that keeps the project commercially redistributable. → [Verification](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/container-image.md#verification), [THIRD_PARTY_LICENSES.md](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md)

**It is built to be maintained.** A .NET 10 clean-architecture modular monolith with enforced boundaries between domain, application, infrastructure, protocol, and host; compiler and analyzer diagnostics are errors; every behavior change ships with tests; and the decisions that shape the system are recorded as ADRs rather than remembered. → [Solution structure](https://github.com/Krzysztof318/MailFathom/blob/main/docs/architecture/solution-structure.md), [decisions](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/README.md)

## Getting started

The recommended first installation is **Docker Compose**. It is the only shape that provisions PostgreSQL for you, and its defaults publish both ports on loopback, so nothing is reachable from another machine until you decide it should be.

```bash
git clone https://github.com/Krzysztof318/MailFathom.git
cd MailFathom
```

From there, [installing MailFathom](https://github.com/Krzysztof318/MailFathom/blob/main/docs/users/installation.md) covers what every shape needs — Linux, PostgreSQL with the `vector` extension, an IMAP account, an explicit schema step — and routes you to the guide for Compose, Kubernetes, or a native systemd process. [Getting started](https://github.com/Krzysztof318/MailFathom/blob/main/docs/users/getting-started.md) then walks from an installed instance to a first successful tool call: provisioning the secrets, configuring a mailbox, applying the schema, verifying health, enabling the MCP endpoint, and connecting a client.

To evaluate MailFathom from the checkout instead of deploying it, the local Aspire orchestration provisions PostgreSQL and applies the schema on its own. [Local development](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/local-development.md#running-locally-with-aspire) has that path.

### Where the artifacts are published

| Artifact | Where |
| --- | --- |
| Container image | `ghcr.io/krzysztof318/mailfathom` and `docker.io/krzysztof318/mailfathom` |
| Helm chart | `oci://ghcr.io/krzysztof318/charts/mailfathom` |
| Database schema script | attached to each [release](https://github.com/Krzysztof318/MailFathom/releases) |
| `mfctl`, the administrative command | attached to each [release](https://github.com/Krzysztof318/MailFathom/releases), one self-contained binary per platform, the Windows ones Authenticode-signed |

Both registries carry the same manifest list under the same digest, so the one to pull from is whichever your environment already reaches. Every published artifact carries a signed provenance statement; [the container image](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/container-image.md#published-images) records what each tag means and how to verify one.

## Documentation

[`docs/`](https://github.com/Krzysztof318/MailFathom/blob/main/docs/README.md) is the index. The pages you are most likely to want first:

| | |
| --- | --- |
| [User guide](https://github.com/Krzysztof318/MailFathom/blob/main/docs/users/README.md) | Install, configure, run, and use MailFathom |
| [Configuration reference](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/configuration-reference.md) | Every user-settable option, its default, and whether changing it needs a restart |
| [MCP endpoint](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/mcp-endpoint.md) | Authentication, TLS, browser origins, client certificates, rate limits |
| [MCP tools](https://github.com/Krzysztof318/MailFathom/blob/main/docs/features/mcp-tools.md) | The tool contracts, their arguments and results, and the stable error codes |
| [IMAP synchronization](https://github.com/Krzysztof318/MailFathom/blob/main/docs/features/imap-synchronization.md) | What a run stores, how it reconciles, and what it never touches |
| [Architecture](https://github.com/Krzysztof318/MailFathom/blob/main/docs/architecture/solution-structure.md) | The boundaries, the projects, and why they are drawn there |
| [Decisions](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/README.md) | The ADRs, and the workflow that produces them |

Documentation under `docs/` describes behavior that exists. Where something is planned, it is tracked as an issue rather than written up as though it worked.

## Where it is going

The three tools are the foundation, not the product. What follows turns a synchronized, searchable copy of your mail into something an agent can reason over and eventually act on. The direction is set and the order is not fixed:

- **Continuous synchronization.** Today a run is periodic reconciliation. Long-lived IMAP `IDLE` and `NOTIFY` connections, with `CONDSTORE` for cheap flag reconciliation, make new mail arrive in seconds instead of on an interval.
- **Semantic retrieval and answering.** Embeddings in pgvector beside the lexical index, and an `ask_mail` tool that answers a question from retrieved passages rather than making an agent page through a mailbox. This is the step from a searchable archive toward a mail brain, and the chat and embedding providers stay configuration you choose rather than constants compiled in.
- **Acting on mail, not only reading it.** Sending is the first write capability: a durable SMTP outbox exists as an application capability before it is ever an MCP tool, and exposing it waits on a reviewed authorization and confirmation flow, because a tool that sends mail is a different security question from one that reads it. Every later write capability takes the same route.
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

Contributions are welcome, and the entry point is [CONTRIBUTING.md](https://github.com/Krzysztof318/MailFathom/blob/main/CONTRIBUTING.md): it gets you from a clone to a passing verification run and states the few rules a pull request has to satisfy. Every change starts from an issue, so open one — or comment on an existing one — before writing code, and wait for a reply on anything larger than a typo, because MailFathom is pre-release and its direction still moves faster than its issue list.

**MailFathom is developed AI-first, and close to zero-touch.** Nearly every line here was written by an autonomous coding agent working from an issue and the rules in [`AGENTS.md`](https://github.com/Krzysztof318/MailFathom/blob/main/AGENTS.md), and reviewed before merge; the maintainer sets direction and decides, but rarely edits code by hand. Working the same way is encouraged rather than merely tolerated — point an agent at your checkout, let it read the instruction files, and let it produce the change, its tests, and its documentation in one pass. A hand-written patch is judged identically. What does not change either way is that you read your diff before submitting it, and that the same gates and the same licensing obligations apply. [How this project is built](https://github.com/Krzysztof318/MailFathom/blob/main/CONTRIBUTING.md#how-this-project-is-built) has the whole of it.

[`docs/operations/local-development.md`](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/local-development.md) is the full development setup, [`AGENTS.md`](https://github.com/Krzysztof318/MailFathom/blob/main/AGENTS.md) is the entry point to the engineering rules the build and the review enforce, and [CODE_OF_CONDUCT.md](https://github.com/Krzysztof318/MailFathom/blob/main/CODE_OF_CONDUCT.md) applies to everyone taking part.

### Questions, bugs, and proposals

[Discussions](https://github.com/Krzysztof318/MailFathom/discussions) takes questions in `Q&A` and proposals in `Ideas`; a question is not a unit of work, and one that turns out to be work gets converted into an issue. A defect or a piece of scope belongs in [issues](https://github.com/Krzysztof318/MailFathom/issues) — except a vulnerability, which has a private channel below.

## Security

MailFathom holds mailbox credentials, OAuth tokens, certificate material, and a local copy of someone's mail. Report a vulnerability privately through [SECURITY.md](https://github.com/Krzysztof318/MailFathom/blob/main/SECURITY.md) rather than in a public issue.

## Code signing policy

The two Windows `mfctl` binaries attached to each release are Authenticode-signed, so Windows names a publisher instead of warning about an unknown one. Free code signing is provided by [SignPath.io](https://signpath.io/), with the certificate issued by the [SignPath Foundation](https://signpath.org/). The Linux binaries carry no signature, and the checksum file published beside them is what verifies those.

Every published artifact — signed or not, and including the container image and the Helm chart — also carries a signed build provenance statement naming the workflow and commit that produced it. The signature says who vouches for a file; the attestation says where it came from.

| Role | Who |
| --- | --- |
| Committers | Krzysztof Kasprowicz, and contributors whose pull requests are merged after review |
| Reviewers and approvers | Krzysztof Kasprowicz |

Every release is approved by a person before anything is signed; nothing signs on a schedule, and the nightly channel signs nothing at all. [Signing the Windows CLI binaries](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/windows-code-signing.md) records how the pipeline does it and how to verify a download.

This project collects no telemetry and no personal data through its released binaries. What a deployment holds is a different question, and [SECURITY.md](https://github.com/Krzysztof318/MailFathom/blob/main/SECURITY.md) with the [user guide](https://github.com/Krzysztof318/MailFathom/blob/main/docs/users/README.md) is where that is described.

## License

MailFathom is licensed under the [Apache License, Version 2.0](https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE), SPDX identifier `Apache-2.0`. Source files repeat that grant in a header the build enforces, and a published artifact carries `LICENSE` and `NOTICE` beside the binaries. The container image is that same publish output, so it carries both files and declares `org.opencontainers.image.licenses`; the Helm chart states the identifier as `artifacthub.io/license`.

MailFathom was originally created by **Krzysztof Kasprowicz**. The root [NOTICE](https://github.com/Krzysztof318/MailFathom/blob/main/NOTICE) records that attribution, which section 4(d) of the license asks a derivative distribution to preserve while it remains relevant to the derived work. A fork may add its own attribution notices beside it. The notice adds no use restriction, changes nothing about the license, and claims nothing about contributions written by other copyright holders.

Contributions to this repository are offered under Apache-2.0, by section 5 of the license. There is no contributor licence agreement and no developer certificate of origin, and contributors keep the copyright in what they write.

Third-party components that MailFathom consumes are reviewed separately in [THIRD_PARTY_LICENSES.md](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md). That register records what MailFathom depends on and under which terms; it grants nothing in MailFathom itself, which `LICENSE` alone does.

The application icon in [`assets/`](https://github.com/Krzysztof318/MailFathom/tree/main/assets) is MailFathom's own asset rather than a third-party component, and the same grant covers it. The register records how it was produced and why no one else holds rights in it.

What the license grants, it grants without promising that the software works. Sections 7 and 8 give MailFathom **as is**, without warranties or conditions of any kind, and state that no contributor is liable for damages arising out of its use or out of an inability to use it — a synchronization that falls behind, a search that misses what was there, or mail disclosed by a deployment that was reachable when it should not have been. That is the ordinary allocation for software given away rather than sold, and the license text is what governs rather than this summary of it: read [sections 7 and 8](https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE) before pointing MailFathom at a mailbox that matters. Where the database sits, who can reach it, how long mail is kept, and which model receives a result stay the deployment's decisions, and they are where most of the risk actually lives.
