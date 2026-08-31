<img src="https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/icon-900.png" alt="MailFathom logo" width="120">

# MailFathom

[![Stars](https://img.shields.io/github/stars/Krzysztof318/MailFathom?style=flat&label=%E2%98%85%20Stars&color=gold)](https://github.com/Krzysztof318/MailFathom) [![License](https://img.shields.io/badge/license-Apache--2.0-blue)](https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE) [![Release](https://github.com/Krzysztof318/MailFathom/actions/workflows/release.yml/badge.svg)](https://github.com/Krzysztof318/MailFathom/actions/workflows/release.yml) [![Version](https://img.shields.io/github/v/release/Krzysztof318/MailFathom?sort=semver&label=version)](https://github.com/Krzysztof318/MailFathom/releases/latest) [![Nightly](https://github.com/Krzysztof318/MailFathom/actions/workflows/nightly.yml/badge.svg)](https://github.com/Krzysztof318/MailFathom/actions/workflows/nightly.yml) [![CI](https://github.com/Krzysztof318/MailFathom/actions/workflows/ci.yml/badge.svg?branch=main&event=push)](https://github.com/Krzysztof318/MailFathom/actions/workflows/ci.yml?query=branch%3Amain+event%3Apush) [![Documentation](https://img.shields.io/badge/documentation-blue)](https://krzysztof318.github.io/MailFathom/)

**A brain for your mail — self-hosted, AI-native, and yours alone.**

MailFathom synchronizes your IMAP accounts into a PostgreSQL database you run, indexes that copy, and serves it to AI agents as tools over the [Model Context Protocol](https://modelcontextprotocol.io/). Reading answers from your copy rather than from a mail server, and it never marks anything read there. Writing — marking, drafting, sending — is a separate act each account and each credential has to be given, and every one of them is off until you turn it on.

![A chat client asked to show the latest mail, answered with a table of the ten most recent messages, their receipt times, and the moment the local copy was last synchronized](https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/mcp-tools/list-recent-emails.png)

*One question, answered from the local copy in an ordinary chat client. The `***` in these screenshots were blacked out by hand before the files entered a public repository — until you turn `SensitiveContent` on, MailFathom redacts nothing on its way to a client.*

## Install

**Docker Compose is the recommended first installation.** It is the only shape that provisions PostgreSQL for you, and its defaults publish both ports on loopback, so nothing is reachable from another machine until you decide it should be.

```bash
git clone https://github.com/Krzysztof318/MailFathom.git
cd MailFathom
scripts/quick-start-compose.sh
```

That third line is the quick way to *try* it, not the way to run it: [the script](https://github.com/Krzysztof318/MailFathom/blob/main/scripts/quick-start-compose.sh) asks where your mailbox lives, generates the credentials, writes the configuration, starts the stack, offers the schema step, and hands you the address a chat client connects to — and what it prepares serves that one machine over plain HTTP, keeps its credentials in files under the checkout, and backs nothing up. It prints that list when it finishes.

[Installing MailFathom](https://krzysztof318.github.io/MailFathom/users/installation.html) is the real thing: what every shape needs, and the guide for Compose, [Podman Quadlet](https://krzysztof318.github.io/MailFathom/operations/deployment-quadlet.html), Kubernetes, or a native systemd process. [Getting started](https://krzysztof318.github.io/MailFathom/users/getting-started.html) then walks from an installed instance to a first successful tool call. To evaluate from the checkout instead, the local Aspire orchestration provisions PostgreSQL and applies the schema on its own — [local development](https://krzysztof318.github.io/MailFathom/operations/local-development.html#running-locally-with-aspire) has that path.

## Start here

| You are | Start at |
| --- | --- |
| Deciding whether MailFathom is for you | [What it does well](https://github.com/Krzysztof318/MailFathom#what-it-does-well) below, then [the user guide](https://krzysztof318.github.io/MailFathom/users/README.html) |
| Installing or operating it | [Installing MailFathom](https://krzysztof318.github.io/MailFathom/users/installation.html), then [getting started](https://krzysztof318.github.io/MailFathom/users/getting-started.html) |
| Connecting an agent to a running instance | [Using the tools](https://krzysztof318.github.io/MailFathom/users/usage.html) |
| Reading any of that with an AI assistant beside you | [Hand it the documentation](https://github.com/Krzysztof318/MailFathom#hand-the-documentation-to-your-agent) below |
| Contributing | [CONTRIBUTING.md](https://github.com/Krzysztof318/MailFathom/blob/main/CONTRIBUTING.md) |

### Hand the documentation to your agent

If an AI assistant is helping you install, configure, or use MailFathom, give it the documentation rather than a search over the site. One line is the whole of it:

```text
Read https://krzysztof318.github.io/MailFathom/llms.txt and follow it to the pages that answer my question.
```

That address is the current release's map: every published page, what it answers, and a link to its source, so the agent fetches the one page that owns your question instead of fragments of several. MailFathom also resolves in [Context7](https://context7.com/krzysztof318/mailfathom) as `/krzysztof318/mailfathom`, which mirrors the default branch rather than a release.

→ [Handing this guide to your own agent](https://krzysztof318.github.io/MailFathom/users/README.html#handing-this-guide-to-your-own-agent)

## What an agent gets

Twenty-one tools, and they are the whole surface.

| | |
| --- | --- |
| **Reading your mail** | `list_accounts`, `list_emails`, `search_emails` — ranked lexically, and by embedding similarity once you configure a model — `get_email_content` for up to ten messages in full, and `ask_mail`, which answers a question from your mail and cites every message it drew on |
| **Marking it** | `set_mail_flags` marks a message read or unread, stars it, and writes the keywords your client shows as tags |
| **Sending** | `send_email` sends one message; `reply_to_email` and `forward_email` answer one you already hold, deriving the addressing, subject, threading, quotation, and attachments from your stored copy; `get_outgoing_email` and `cancel_outgoing_email` report what became of a send and stop one that has not left |
| **Drafting** | `save_draft`, `update_draft`, and `delete_draft` write a message into your own Drafts folder and send nothing at all; `send_draft` is what sends one |
| **Contacts** | Six tools over MailFathom's own book of people — not addresses — which lives in your database rather than at your mail provider |

Two properties hold everywhere, and much of the design follows from them:

- **Reading is local.** A read answers from your copy and never contacts a mail server, so it is fast, it works while the server is down, and it changes nothing remotely. Every result states how fresh the local copy is.
- **Nothing writes unless you asked for it.** Retrieval cannot set the remote `\Seen` flag at all. What can change your mailbox is what you configured — a mail rule, a spam action — or a tool a credential was explicitly granted. Sending is stricter again: it is off on every account, refused outright while the deployment is read-only, and bounded by a recipient policy and a per-period ceiling you set.

No send is performed while the caller waits. Each is written down, answered with a record identity, and offered by a delivery pass that survives a crash — so a message MailFathom says it queued is one it will actually try to deliver, and the seconds before it leaves are the only window in which it can be withdrawn. Nothing a client sends decides who a message is from.

![A search for the word confirmation, answered with three ranked matches, each carrying the fragment of the message that matched](https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/mcp-tools/search-emails.png)

![One message opened by subject, answered with its sender, recipient, timestamps, folder, attachment state, and full plain-text body](https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/mcp-tools/read-email-content.png)

Beside what a tool returns, MailFathom records what it can establish about each message: who authenticated its sender and whether you recognize that author, how much its text reads as machine written, and the conversation it belongs to. [What each tool answers, refuses, and never repeats back](https://krzysztof318.github.io/MailFathom/features/mcp-tools.html) is the contract.

## Project status

`0.7.0` is the current release, and it is the first one that **sends mail**. Before it, everything an agent could reach read your local copy; from here it can compose a message, answer one you already hold, forward one on, leave a draft for you, and mark or label mail on your server.

**Upgrading from `0.6.0`** takes a configuration edit and a database migration you apply. The important one: a credential entry that names no permissions holds everything its surface publishes, so on upgrade it gains the ability to send from your mailboxes — write the grant you mean on every entry. [The changelog](https://krzysztof318.github.io/MailFathom/CHANGELOG.html) states each break against the surface it breaks, and what to do about it; the schema step applies while `0.6.0` is still serving.

Nightly images are built from `main` and published beside the releases. A nightly is not a release: its schema can be ahead of any published migration, it has no upgrade path in either direction, and it is deleted once newer ones accumulate. [What a nightly build risks](https://krzysztof318.github.io/MailFathom/operations/container-image.html#what-a-nightly-build-risks) states the whole of it.

### Where the artifacts are published

| Artifact | Where |
| --- | --- |
| Container image | `ghcr.io/krzysztof318/mailfathom` and `docker.io/krzysztof318/mailfathom` |
| Helm chart | `oci://ghcr.io/krzysztof318/charts/mailfathom` |
| Database schema script | attached to each [release](https://github.com/Krzysztof318/MailFathom/releases) |
| `mfctl`, the administrative command | attached to each [release](https://github.com/Krzysztof318/MailFathom/releases), one self-contained binary per platform, verified by the checksum file beside them, and installed on Linux by [one command](https://krzysztof318.github.io/MailFathom/operations/admin-endpoint.html#on-linux-with-the-install-script) that does both |

Both registries carry the same manifest list under the same digest, so pull from whichever your environment already reaches. There is no binary artifact for the service itself: a native installation starts from a checkout.

## What it does well

MailFathom is built as an enterprise-grade system from the first line, even while its feature scope is still small. Every claim below is a property of the code and the deployment assets today.

- **Every act is behind a grant, and no grant implies another.** Reading your mail, changing a flag, writing a draft, and sending are four separate permissions, checked when a tool is listed and again when it is called; an administrative route is checked the same way. An endpoint can also publish only some kinds of tool at all, so a deployment can offer reading and drafting while withholding sending from every credential. → [Permissions](https://krzysztof318.github.io/MailFathom/operations/permissions.html)
- **Secure by default, and explicit about every weakening.** The MCP endpoint is off until you enable it, and enabling it means saying whether it requires a credential; the unauthenticated posture is legal, warned about at startup, and never the default. Client certificates and per-client rate limits belong to the endpoint rather than to a proxy. IMAP is TLS-on-connect, and a configuration that weakens the transport fails startup unless it says so explicitly. → [The MCP endpoint](https://krzysztof318.github.io/MailFathom/operations/mcp-endpoint.html)
- **Credentials never live in configuration.** A secret-bearing setting holds a *reference* — a file path, a systemd credential, an environment variable — so a configuration file is safe to review, diff, and back up: leaking it leaks paths, not passwords. Configuration is read-only to the process, permanently; nothing reachable over the network can change a setting. → [Secret provisioning](https://krzysztof318.github.io/MailFathom/operations/secret-provisioning.html), [configuration sources](https://krzysztof318.github.io/MailFathom/operations/configuration-sources.html)
- **Mail is handled as personal data by the code that touches it.** Content, metadata, extracted text, and search extracts are never logged and never carried in an error message; every call is bounded, so a deployment decides how much mail one call may draw out. Whether a deployment satisfies GDPR still depends on how you run it — what MailFathom offers is an architecture that keeps those choices open, with explicit seams for the data-subject workflows a later release implements. → [Using the tools](https://krzysztof318.github.io/MailFathom/users/usage.html)
- **It fails fast and says why.** Startup resolves every secret reference and verifies the database schema before serving anything, and a refusal names the key or the pending migration that caused it. Migrations are never applied while starting, in any environment. → [Health endpoints](https://krzysztof318.github.io/MailFathom/operations/health-endpoints.html), [telemetry](https://krzysztof318.github.io/MailFathom/operations/telemetry.html)
- **The deployment assets are hardened, not illustrative.** The image is chiseled — no shell, no package manager, no HTTP client — runs unprivileged on a read-only root filesystem with every capability dropped, and carries no tool that could apply a migration. Compose, the Quadlet units, and the Helm chart all ship that posture by default, and the chart meets the Restricted Pod Security Standard. → [The container image](https://krzysztof318.github.io/MailFathom/operations/container-image.html), [Kubernetes](https://krzysztof318.github.io/MailFathom/operations/deployment-kubernetes.html)
- **The supply chain is verifiable.** Multi-architecture images from base images pinned to an exact patch, scanned before publication, with signed build provenance tying a digest to the commit and workflow that produced it. Package versions are pinned centrally with committed lock files, and every third-party component is reviewed against a licensing policy that keeps the project commercially redistributable. → [Verification](https://krzysztof318.github.io/MailFathom/operations/container-image.html#verification), [THIRD_PARTY_LICENSES.md](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md)
- **It is built to be maintained.** A .NET 10 clean-architecture modular monolith with enforced boundaries, compiler and analyzer diagnostics as errors, tests with every behavior change, and the decisions recorded as ADRs rather than remembered. → [Solution structure](https://krzysztof318.github.io/MailFathom/architecture/solution-structure.html), [decisions](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/README.md)

## Documentation

Every documentation link here goes to **[the documentation site](https://krzysztof318.github.io/MailFathom/)**, which is the readable form: the same pages with search, an API reference generated from the source, and a version selector. It opens on the current release, and an address here names no version, so a link keeps working across releases. [`docs/`](https://github.com/Krzysztof318/MailFathom/blob/main/docs/README.md) is the index for reading the same pages in the repository instead.

| | |
| --- | --- |
| [User guide](https://krzysztof318.github.io/MailFathom/users/README.html) | Install, configure, run, and use MailFathom |
| [Configuration reference](https://krzysztof318.github.io/MailFathom/operations/configuration-reference.html) | Every user-settable option, its default, and whether changing it needs a restart |
| [Permissions](https://krzysztof318.github.io/MailFathom/operations/permissions.html) | What a credential may do, and what a refusal says |
| [MCP tools](https://krzysztof318.github.io/MailFathom/features/mcp-tools.html) | The tool contracts, their arguments and results, and the stable error codes |
| [Mail delivery](https://krzysztof318.github.io/MailFathom/features/mail-delivery.html) | What a send passes through before it leaves, and what bounds it |
| [Architecture](https://krzysztof318.github.io/MailFathom/architecture/solution-structure.html) | The boundaries, the projects, and why they are drawn there |

Documentation under `docs/` describes behavior that exists. Where something is planned, it is tracked as an issue rather than written up as though it worked.

## Why it exists

A mailbox is the largest archive most people own and the least usable one. Contracts, decisions, invoices, threads that ended without a conclusion, attachments nobody will ever find again: all of it is in there, and none of it is reachable except by scrolling. Mail clients are built to show you the newest of it, one message at a time. After twenty years of accumulation, that is the wrong shape entirely.

MailFathom is being built to change what mail *is* to software: keep the local copy current, index it so the whole of it is reachable rather than only its most recent slice, and serve it to agents as tools. MCP is how agents reach it; it is not what MailFathom is. None of it depends on somebody else's service — the copy is yours, the database is yours, the deployment is yours, and the AI capabilities arrive as providers you choose and point at rather than as ones compiled into the product.

## Where it is going

The tools are the foundation, not the product. Sending was the capability whose effect first reached somebody who is not this mailbox's owner; what remains of writing to a mailbox is moving, filing, and deleting. Every later capability takes the route these took: a named permission, a recorded change, and a pass of its own that owns the mail server.

**MailFathom's own client travels inside the image**, built from the React and TypeScript workspace under [`frontend/`](https://github.com/Krzysztof318/MailFathom/tree/main/frontend) and served from a deployment setting — no second container, no second process, about 230 kB whether or not you serve it. The same client is published as a desktop application too — a `.deb`, an `.rpm`, and a Windows installer attached to every release, for a machine that would rather have an application than a tab. It is early: the page draws its own sample data rather than your mail, and what a deployment serves beside it is the client *surface*, an HTTP API under `/api/client` that a client of your own can call too. → [The client endpoint](https://krzysztof318.github.io/MailFathom/operations/client-endpoint.html), [The desktop client](https://krzysztof318.github.io/MailFathom/operations/desktop-client.html)

**Ideas, not yet scope** — recorded as open questions, each waiting on a decision rather than on effort. [Discussions](https://github.com/Krzysztof318/MailFathom/discussions) is where they are argued, and the `Ideas` category is open to yours: [encrypted and signed mail](https://github.com/Krzysztof318/MailFathom/issues/75), [antivirus scanning of stored attachments](https://github.com/Krzysztof318/MailFathom/issues/77), [OAuth for outbound IMAP and SMTP](https://github.com/Krzysztof318/MailFathom/issues/78), and skill-based jobs, whose body is an instruction an agent carries out against a slice of your mail rather than a deterministic rule.

## Contributing

Contributions are welcome, and the entry point is [CONTRIBUTING.md](https://github.com/Krzysztof318/MailFathom/blob/main/CONTRIBUTING.md): it gets you from a clone to a passing verification run and states the few rules a pull request has to satisfy. Every change starts from an issue, so open one — or comment on an existing one — before writing code, and wait for a reply on anything larger than a typo.

**MailFathom is developed AI-first, and close to zero-touch.** Nearly every line here was written by an autonomous coding agent working from an issue and the rules in [`AGENTS.md`](https://github.com/Krzysztof318/MailFathom/blob/main/AGENTS.md), and reviewed before merge. Working the same way is encouraged rather than merely tolerated; a hand-written patch is judged identically. What does not change either way is that you read your diff before submitting it, and that the same gates and licensing obligations apply.

Fork the repository, clone your fork, and point it at this one — every verification gate measures your branch against the base it will actually merge into:

```bash
git clone https://github.com/<you>/MailFathom.git
cd MailFathom
git remote add upstream https://github.com/Krzysztof318/MailFathom.git
git fetch upstream main
```

If you work with a coding agent, hand the rest to [`get-started-contributors`](https://github.com/Krzysztof318/MailFathom/blob/main/.agents/skills/get-started-contributors/SKILL.md), the workflow skill written for somebody arriving for the first time; in Claude Code it is `/get-started-contributors`, and any other agent can be pointed at the file. Setting up by hand takes the same steps, written out in [From a clone to a green run](https://github.com/Krzysztof318/MailFathom/blob/main/CONTRIBUTING.md#from-a-clone-to-a-green-run). Development is on Linux; nothing here is verified against anything else.

[Discussions](https://github.com/Krzysztof318/MailFathom/discussions) takes questions in `Q&A` and proposals in `Ideas`. A defect or a piece of scope belongs in [issues](https://github.com/Krzysztof318/MailFathom/issues) — except a vulnerability, which has a private channel below. [CODE_OF_CONDUCT.md](https://github.com/Krzysztof318/MailFathom/blob/main/CODE_OF_CONDUCT.md) applies to everyone taking part.

## Security

MailFathom holds mailbox credentials, OAuth tokens, certificate material, and a local copy of someone's mail. Report a vulnerability privately through [SECURITY.md](https://github.com/Krzysztof318/MailFathom/blob/main/SECURITY.md) rather than in a public issue.

**Privacy policy.** MailFathom transfers no information to other networked systems unless specifically requested by the user or the person installing or operating it. It reaches the mail servers, the database, and the model provider a deployment configures, and nothing else: it collects no telemetry, phones no home, and exports OpenTelemetry data only to an endpoint an operator sets. Where mail is stored, who can reach it, and which model receives a result are deployment decisions.

**Verifying what you downloaded.** No `mfctl` binary carries a code signature, on any platform, so Windows warns about an unknown publisher. The checksum file attached beside them is what tells a genuine download from a tampered one:

```bash
sha256sum --check --ignore-missing 'mfctl-<version>.sha256'
```

The container image and the Helm chart each carry a signed build provenance statement instead, so `gh attestation verify` answers where one came from without your having to trust the registry. [The container image](https://krzysztof318.github.io/MailFathom/operations/container-image.html#published-images) records how to check one.

## License

MailFathom is licensed under the [Apache License, Version 2.0](https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE), SPDX identifier `Apache-2.0`. Source files repeat that grant in a header the build enforces, and a published artifact carries `LICENSE` and `NOTICE` beside the binaries.

MailFathom was originally created by **Krzysztof Kasprowicz**. The root [NOTICE](https://github.com/Krzysztof318/MailFathom/blob/main/NOTICE) records that attribution, which section 4(d) of the license asks a derivative distribution to preserve while it remains relevant to the derived work. The notice adds no use restriction and claims nothing about contributions written by other copyright holders. Contributions to this repository are offered under Apache-2.0 by section 5 of the license, and a contributor also accepts the [contributor licence agreement](https://github.com/Krzysztof318/MailFathom/blob/main/CLA.md) once. It is a licence rather than a transfer — **contributors keep the copyright in what they write** — and what it adds over section 5 is the freedom to publish MailFathom under other terms in future without tracing every past contributor, plus the contributor's own statement that the code was theirs to give. Nothing about that changes what this release grants you: MailFathom is Apache-2.0.

Third-party components MailFathom consumes are reviewed separately in [THIRD_PARTY_LICENSES.md](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md). That register records what MailFathom depends on and under which terms; it grants nothing in MailFathom itself, which `LICENSE` alone does. The application icon in [`assets/`](https://github.com/Krzysztof318/MailFathom/tree/main/assets) is MailFathom's own asset and the same grant covers it.

What the license grants, it grants without promising that the software works. Sections 7 and 8 give MailFathom **as is**, without warranties or conditions of any kind, and state that no contributor is liable for damages arising out of its use or out of an inability to use it — a synchronization that falls behind, a search that misses what was there, or mail disclosed by a deployment that was reachable when it should not have been. The license text is what governs rather than this summary of it: read [sections 7 and 8](https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE) before pointing MailFathom at a mailbox that matters.
