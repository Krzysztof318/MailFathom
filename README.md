<img src="https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/icon-900.png" alt="MailFathom logo" width="120">

# MailFathom

[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE) [![Release](https://github.com/Krzysztof318/MailFathom/actions/workflows/release.yml/badge.svg)](https://github.com/Krzysztof318/MailFathom/actions/workflows/release.yml) [![Version](https://img.shields.io/github/v/release/Krzysztof318/MailFathom?sort=semver&label=version)](https://github.com/Krzysztof318/MailFathom/releases/latest) [![Nightly](https://github.com/Krzysztof318/MailFathom/actions/workflows/nightly.yml/badge.svg)](https://github.com/Krzysztof318/MailFathom/actions/workflows/nightly.yml) [![CI](https://github.com/Krzysztof318/MailFathom/actions/workflows/ci.yml/badge.svg?branch=main&event=push)](https://github.com/Krzysztof318/MailFathom/actions/workflows/ci.yml?query=branch%3Amain+event%3Apush) [![Documentation](https://img.shields.io/badge/documentation-blue)](https://krzysztof318.github.io/MailFathom/)

**A brain for your mail — self-hosted, AI-native, and yours alone.**

MailFathom synchronizes your IMAP accounts into a PostgreSQL database you run, indexes that copy, and serves it to AI agents as tools over the [Model Context Protocol](https://modelcontextprotocol.io/). Reading answers from your copy rather than from a mail server, and reading never marks anything read on the server — only a rule or a spam action you configured yourself can change your mailbox.

![A chat client asked to show the latest mail, answered with a table of the ten most recent messages, their receipt times, and the moment the local copy was last synchronized](https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/mcp-tools/list-recent-emails.png)

*One question, answered from the local copy in an ordinary chat client. The `***` in these screenshots were blacked out by hand before the files entered a public repository — until you turn `SensitiveContent` on, MailFathom redacts nothing on its way to a client.*

## Install

**Docker Compose is the recommended first installation.** It is the only shape that provisions PostgreSQL for you, and its defaults publish both ports on loopback, so nothing is reachable from another machine until you decide it should be.

```bash
git clone https://github.com/Krzysztof318/MailFathom.git
cd MailFathom
```

From there, [installing MailFathom](https://krzysztof318.github.io/MailFathom/users/installation.html) covers what every shape needs — Linux, PostgreSQL with the `vector` extension, an IMAP account, an explicit schema step — and routes you to the guide for Compose, [Podman Quadlet](https://krzysztof318.github.io/MailFathom/operations/deployment-quadlet.html), Kubernetes, or a native systemd process. [Getting started](https://krzysztof318.github.io/MailFathom/users/getting-started.html) then walks from an installed instance to a first successful tool call: provisioning the secrets, configuring a mailbox, applying the schema, verifying health, enabling the MCP endpoint, and connecting a client.

To evaluate MailFathom from the checkout instead of deploying it, the local Aspire orchestration provisions PostgreSQL and applies the schema on its own. [Local development](https://krzysztof318.github.io/MailFathom/operations/local-development.html#running-locally-with-aspire) has that path.

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

That address is the current release's map: every published page, its title, and one line saying what it answers, linking each page's Markdown source. The agent loads the map in full and then fetches only the page that owns your question, so its answer comes from the page carrying the contract instead of from fragments of several. The map also names the two bundles beside it — the operator path, from choosing an installation to administering the deployment, and the mailbox user path, from connecting a client to what each tool returns — for when the question is a whole path rather than one page of it. Every version the site documents carries its own copy of all three, and the address above is the version the site opens on.

MailFathom also resolves in [Context7](https://context7.com/krzysztof318/mailfathom), as `/krzysztof318/mailfathom`, for an agent that already has that connector. It is a mirror and the map above is what it mirrors: it indexes the default branch rather than a release, so it can answer from documentation of work no version carries yet, and it is refreshed on its own schedule rather than by a publish here.

→ [Handing this guide to your own agent](https://krzysztof318.github.io/MailFathom/users/README.html#handing-this-guide-to-your-own-agent)

## What exists today

What is implemented is read-only mail retrieval, eleven tools, and the rules and spam actions your own configuration turns on, and this README is split on that line: this section and [What it does well](https://github.com/Krzysztof318/MailFathom#what-it-does-well) describe the code as it stands, while [Where it is going](https://github.com/Krzysztof318/MailFathom#where-it-is-going) is the roadmap.

Two properties hold everywhere, and much of the rest of the design follows from them:

- **Reading is local.** A tool call answers from your copy and never contacts a mail server, so it is fast, it works while the server is down, and it cannot change anything remotely. Every result states how fresh the local copy is.
- **Retrieval never writes to your mailbox.** Fetching mail never sets the remote `\Seen` flag, so mail MailFathom has copied still shows as unread in your own mail client until you read it there. What can write is what you configured to: a `MailRules` rule whose action moves, copies, deletes, or marks a message read, and `SpamClassification:Actions`, which files junk and marks it read. Both are off until you turn them on, and each account states which of the four actions a rule may ask of it.

What an agent gets is eleven tools, and they are the whole surface. Five of them read your mail:

| Tool | What it answers |
| --- | --- |
| `list_accounts` | Which mailboxes this deployment serves, each with the readable name you gave it and how current its local copy is — the tool an agent calls first, so it knows what to narrow the others to |
| `list_emails` | A page of the timeline, newest first, filtered by account, folder, sender, recipient, subject, date range, seen state, or attachment presence |
| `search_emails` | Ranked matches for a text query across subjects, participants, and body text, each with short extracts around what matched — ranked lexically, and by embedding similarity beside it once an embedding model is configured |
| `get_email_content` | Up to ten messages in full: normalized headers, plain-text body, optionally sanitized HTML, every attachment by name, type, and size, and — on request — a short-lived link that fetches each file |
| `ask_mail` | A question answered from the mail a chat model looks up while answering, citing the identifiers of every message it drew on so each claim can be read for yourself |

The first four are always there. `ask_mail` needs a chat model and an embedding model you configure and point at, so a deployment with neither does not advertise it at all rather than offering a tool that would fail on first use.

The other six are MailFathom's own contact book — `list_contacts`, `get_contact`, `create_contact`, `update_contact`, `delete_contact`, and `promote_contact`. It holds the people you write down and every address each of them uses, which is what lets an agent answer who a message is from for somebody who writes from three addresses. Four of the six change that book, and they are the only tools on the surface that change anything: they reach MailFathom's own database and no mail server, and two of them announce themselves as destructive: `delete_contact`, because an erasure removes a person and every address recorded with them for good, and `update_contact`, because an amendment states the whole record and drops whatever it leaves out. `promote_contact` is neither: it takes on a person MailFathom collected from arriving mail, so the record becomes one you asserted and every other tool may amend it. They are offered to a credential granted them, which every credential is until you narrow its entry.

The book can also fill itself. Switch [contact collection](https://krzysztof318.github.io/MailFathom/features/contacts.html#collecting-contacts-from-arriving-mail) on for an account and it records the people that account corresponds with as its mail is synchronized — the author of mail that arrives, the recipients of mail you sent, held to a threshold you set and never a mailing list, a role mailbox, or an address you excluded. It is off until you switch it on, per account, and one command takes back everything it collected.

The screenshot at the top of this page is `list_emails`. Two of the other tools, answering the same client over the same mailbox:

![A search for the word confirmation, answered with three ranked matches, each carrying the fragment of the message that matched](https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/mcp-tools/search-emails.png)

![One message opened by subject, answered with its sender, recipient, timestamps, folder, attachment state, and full plain-text body](https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/mcp-tools/read-email-content.png)

A connected agent can list, read, search, and ask about your mail. It cannot send, delete, move, or mark any of it, because no tool on the surface writes to a mailbox — including inside an answering run, which is composed with one capability and that capability searches. That describes this stage rather than a permanent limit: writing to a mailbox is on the roadmap, starting with sending, and each such capability will arrive as its own tool behind a reviewed authorization and confirmation flow — never as a setting that loosens a tool you already trust. The contact book is the one thing an agent already writes, and it is deliberately not that: it lives in your own database and reaches no mail server.

## Project status

`0.6.0` is the current release, and it builds on `0.5.0` — `ask_mail`, semantic ranking in `search_emails`, `list_accounts`, and the ceilings on what answering and embedding may spend.

- **What it adds** is everything that acts on your mail rather than reading it: rules you author in configuration that move, copy, delete, and mark messages as read; spam classification that can file junk on the server; and the durable queue underneath both. Beside that, mail can be redacted before anything is derived from it or leaves the deployment, secrets found in the process and personal data by an analyzer you run beside it. The rules, the classification, and both scanners are off until you turn them on; the queue beneath the first two runs on every instance. The MCP surface was still five read-only tools at that release, and it stays true of the mail half here: nothing a client can call writes to a mailbox.
- **Upgrading from `0.5.0`** is a configuration edit, a client edit, and a database migration you apply. Every folder you want read is named in configuration, and mail under an alias your file no longer names is unreachable until an entry names it again; switching a folder's `Synchronize` off now keeps its stored mail instead of erasing it; the folder argument of `list_emails`, `search_emails`, and `ask_mail` is `folders` where it was `folderAliases`, and the old spelling is ignored rather than refused, so a client still sending it reads every folder instead of the one it named; `get_email_content` hands back a signed link per attachment instead of base64, asked for with `includeAttachmentDownloadLinks` where `0.5.0` asked with `includeAttachmentContent`, and issues none unless `Deployment:PublicBaseAddress` is declared; `EmailContent:MaxAttachmentBytes` and `EmailContent:MaxAttachmentBytesPerRead` are deleted from the configuration file, or the host declines to start on a key it no longer knows; and mail in a folder mapped as junk is withheld from listing and search unless the call asks for it, and withheld from answering with no way to ask. The schema step applies while `0.5.0` is still serving. [The changelog](https://krzysztof318.github.io/MailFathom/CHANGELOG.html) states each break against the surface it breaks, and what to do about it.
- **What it ships** is a container image, a Helm chart, the SQL script that creates the schema it expects, and an `mfctl` binary per platform — [where the artifacts are published](https://github.com/Krzysztof318/MailFathom#where-the-artifacts-are-published) has the references. There is no binary artifact for the service itself, so a native installation starts from a checkout of this repository.
- **What it promises** across the MCP tool contract, the configuration schema, the database schema, and the deployment contract is stated in [the changelog](https://krzysztof318.github.io/MailFathom/CHANGELOG.html).

Nightly images are built from `main` and published to both registries alongside the releases. A nightly is not a release: its schema can be ahead of any published migration, it has no upgrade path in either direction, and it is deleted once newer ones accumulate. [What a nightly build risks](https://krzysztof318.github.io/MailFathom/operations/container-image.html#what-a-nightly-build-risks) states the whole of it before you choose one.

### Where the artifacts are published

| Artifact | Where |
| --- | --- |
| Container image | `ghcr.io/krzysztof318/mailfathom` and `docker.io/krzysztof318/mailfathom` |
| Helm chart | `oci://ghcr.io/krzysztof318/charts/mailfathom` |
| Database schema script | attached to each [release](https://github.com/Krzysztof318/MailFathom/releases) |
| `mfctl`, the administrative command | attached to each [release](https://github.com/Krzysztof318/MailFathom/releases), one self-contained binary per platform, verified by the checksum file beside them, and installed on Linux by [one command](https://krzysztof318.github.io/MailFathom/operations/admin-endpoint.html#on-linux-with-the-install-script) that does both |

Both registries carry the same manifest list under the same digest, so the one to pull from is whichever your environment already reaches. The image and the chart each carry a signed provenance statement; [the container image](https://krzysztof318.github.io/MailFathom/operations/container-image.html#published-images) records what each tag means and how to verify one.

## What it does well

MailFathom is built as an enterprise-grade system from the first line, even while its feature scope is still small. Every claim below is a property of the code and the deployment assets today, and each links to the page that documents it.

### Nothing on the surface writes to your mailbox, and no setting changes that

- No tool on the MCP surface writes to a mailbox, and there is none to enable: nothing a client sends can send, delete, move, or mark your mail. What a client can write is MailFathom's own contact book, which is a table in your database rather than anything at your mail provider.
- Retrieval is incapable of marking remote mail as read. A change to your mailbox happens only where your own configuration asks for one — a rule action, or a spam action — never because a caller asked.
- No response carries an attachment's bytes. A call that asks for them by name receives a signed link per file instead, valid for minutes, scoped to that one attachment, and resolved through the live mailbox so it dies with the message it points at.
- Configuration is read-only to the process, permanently. No request, command, or tool changes a setting, and the service never rewrites the file it was configured from. So the file you provisioned is the file in force: how an instance is configured is reviewable as a diff and restorable from a backup, and nothing reachable over the network can move it out from under you. What the service itself has to modify lives in the database instead.

→ [MCP tools](https://krzysztof318.github.io/MailFathom/features/mcp-tools.html), [configuration sources](https://krzysztof318.github.io/MailFathom/operations/configuration-sources.html)

### Secure by default, and explicit about every weakening

- The MCP endpoint is off until you enable it, and enabling it means stating whether it requires an API key or nothing at all. The unauthenticated posture is legal, announced with a startup warning, and never the default.
- Client certificates and per-client rate limits are part of the endpoint rather than something a proxy has to add.
- IMAP is TLS-on-connect by default, and a private certificate authority is trusted rather than validation disabled. A configuration that weakens the transport or sends a clear-text credential fails startup unless it says so explicitly.

→ [The MCP endpoint](https://krzysztof318.github.io/MailFathom/operations/mcp-endpoint.html), [transport security](https://krzysztof318.github.io/MailFathom/features/imap-synchronization.html#transport-security)

### Credentials never live in configuration

- A secret-bearing setting holds a *reference* — a file path, a systemd credential, an environment variable — and the material lives wherever the deployment provisions it.
- A configuration file is therefore safe to review, diff, and back up: leaking it leaks paths, not passwords.

→ [Secret provisioning](https://krzysztof318.github.io/MailFathom/operations/secret-provisioning.html), [rotation](https://krzysztof318.github.io/MailFathom/operations/secret-rotation.html)

### Mail is handled as personal data by the code that touches it

- Content, metadata, extracted text, and search extracts are never written to a log and never carried in an error message.
- Every call is bounded — at most 100 summaries in a page, 50 ranked matches in a search, a configured character bound on a body — so a deployment can decide how much mail a single call may draw out.
- That is the part software can settle. Whether a deployment satisfies GDPR depends on how you run it: where the database sits, who reaches it, how long you keep mail, and which model an agent hands a result to. What MailFathom offers is an architecture that keeps those choices open rather than one that has already made them badly, including explicit seams for the data-subject workflows a later release implements.

→ [Using the tools](https://krzysztof318.github.io/MailFathom/users/usage.html)

### It fails fast and says why

- Startup resolves every secret reference and verifies the database schema before the process serves anything, and a refusal names the configuration key or the pending migration that caused it.
- Migrations are never applied while starting, in any environment — applying is a step you take, with a backup first.
- Three probes answer on a listener of their own, and telemetry is OpenTelemetry, exported only where you point it.

→ [Health endpoints](https://krzysztof318.github.io/MailFathom/operations/health-endpoints.html), [configuration reference](https://krzysztof318.github.io/MailFathom/operations/configuration-reference.html), [telemetry](https://krzysztof318.github.io/MailFathom/operations/telemetry.html)

### The deployment assets are hardened, not illustrative

- The image is chiseled — no shell, no package manager, no HTTP client — runs as an unprivileged user on a read-only root filesystem with every Linux capability dropped, creates no diagnostic socket, and carries no tool that could apply a migration.
- Docker Compose, the Podman Quadlet units, and the Helm chart all ship that posture by default, and the chart meets the Restricted Pod Security Standard.
- The Quadlet shape goes one step further on secrets: because a `.container` file is a systemd unit source, the deployment's credentials are encrypted at rest and bound to the machine, decrypted only as the unit starts.

→ [The container image](https://krzysztof318.github.io/MailFathom/operations/container-image.html), [Compose](https://krzysztof318.github.io/MailFathom/operations/deployment-compose.html), [Podman Quadlet](https://krzysztof318.github.io/MailFathom/operations/deployment-quadlet.html), [Kubernetes](https://krzysztof318.github.io/MailFathom/operations/deployment-kubernetes.html)

### The supply chain is verifiable

- Images are multi-architecture, built from base images pinned to an exact patch version, scanned before publication, and accompanied by signed build provenance that ties a digest to the commit and workflow that produced it.
- Package versions are pinned centrally with committed lock files, and every third-party component is reviewed against a licensing policy that keeps the project commercially redistributable.

→ [Verification](https://krzysztof318.github.io/MailFathom/operations/container-image.html#verification), [THIRD_PARTY_LICENSES.md](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md)

### It is built to be maintained

- A .NET 10 clean-architecture modular monolith, with enforced boundaries between domain, application, infrastructure, protocol, and host.
- Compiler and analyzer diagnostics are errors, and every behavior change ships with tests.
- The decisions that shape the system are recorded as ADRs rather than remembered.

→ [Solution structure](https://krzysztof318.github.io/MailFathom/architecture/solution-structure.html), [decisions](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/README.md)

## Documentation

Every documentation link on this page goes to **[the documentation site](https://krzysztof318.github.io/MailFathom/)**,
which is the readable form: the same pages with search, an API reference generated from the source, and a version
selector. It opens on the current release, and an address here names no version, so a link keeps working across
releases and lands on the one you are most likely to be running.
[`docs/`](https://github.com/Krzysztof318/MailFathom/blob/main/docs/README.md) is the index for reading the same pages
in the repository instead. The ones you are most likely to want first:

| | |
| --- | --- |
| [User guide](https://krzysztof318.github.io/MailFathom/users/README.html) | Install, configure, run, and use MailFathom |
| [Configuration reference](https://krzysztof318.github.io/MailFathom/operations/configuration-reference.html) | Every user-settable option, its default, and whether changing it needs a restart |
| [Permissions](https://krzysztof318.github.io/MailFathom/operations/permissions.html) | What a credential may do: the published names, how a grant is written, and what a refusal says |
| [MCP endpoint](https://krzysztof318.github.io/MailFathom/operations/mcp-endpoint.html) | Authentication, TLS, browser origins, client certificates, rate limits |
| [MCP tools](https://krzysztof318.github.io/MailFathom/features/mcp-tools.html) | The tool contracts, their arguments and results, and the stable error codes |
| [IMAP synchronization](https://krzysztof318.github.io/MailFathom/features/imap-synchronization.html) | What a run stores, how it reconciles, and what it never touches |
| [Architecture](https://krzysztof318.github.io/MailFathom/architecture/solution-structure.html) | The boundaries, the projects, and why they are drawn there |
| [Decisions](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/README.md) | The ADRs, and the workflow that produces them |

Documentation under `docs/` describes behavior that exists. Where something is planned, it is tracked as an issue rather than written up as though it worked.

## Why it exists

A mailbox is the largest archive most people own and the least usable one. Contracts, decisions, invoices, threads that ended without a conclusion, attachments nobody will ever find again: all of it is in there, and none of it is reachable except by scrolling. Mail clients are built to show you the newest of it, one message at a time. After twenty years of accumulation, that is the wrong shape entirely.

MailFathom is being built to change what mail *is* to software. It keeps the local copy current, indexes it so the whole of it is reachable rather than only its most recent slice, and serves it to agents as tools. The destination is a mail brain: something an agent can put a question to and get an answer from, working across years of mail, on infrastructure that belongs to you.

MCP is how agents reach MailFathom; it is not what MailFathom is. The protocol surface is deliberately thin, and the project is building what sits behind it — continuous synchronization, extracted and indexed content, lexical and semantic retrieval, question answering over both, and eventually the ability to act on your mail rather than only read it.

None of it depends on somebody else's service. The copy is yours, the database is yours, the deployment is yours, and the AI capabilities on the roadmap arrive as providers you choose and point at rather than as ones compiled into the product.

## Where it is going

The tools are the foundation, not the product. What follows turns a synchronized, searchable copy of your mail into something an agent can reason over and eventually act on. The direction is set and the order is not fixed:

- **Acting on mail, not only reading it.** The contact tools already write, but they write MailFathom's own book rather than a mailbox; sending is the first capability that writes to your mail: a durable SMTP outbox exists as an application capability before it is ever an MCP tool, and exposing it waits on a reviewed authorization and confirmation flow, because a tool that sends mail is a different security question from one that reads it. Every later write capability takes the same route. MailFathom can already perform a change against a mailbox and record what it did, and since `0.6.0` your own rules and the spam classification ask it to — what this step adds is a caller on the other side of the protocol, which is the different security question.

### Ideas, not yet scope

These are recorded as open questions, each waiting on a decision rather than on effort. [Discussions](https://github.com/Krzysztof318/MailFathom/discussions) is where they are argued, and the `Ideas` category is open to yours.

- **Encrypted and signed mail**, S/MIME and OpenPGP — the hard part is not the parsing but whether local decryption should be permitted at all, since it turns end-to-end protected mail into searchable plaintext. [#75](https://github.com/Krzysztof318/MailFathom/issues/75)
- **Antivirus scanning of stored attachments**, constrained by which engines can be used under a permissive licensing policy. [#77](https://github.com/Krzysztof318/MailFathom/issues/77)
- **OAuth for outbound IMAP and SMTP**, so a provider that has retired password authentication stays reachable. [#78](https://github.com/Krzysztof318/MailFathom/issues/78)
- **Skill-based jobs**, whose body is an instruction an agent carries out against a slice of your mail, rather than the deterministic rule `0.6.0` shipped. It asks questions a rule does not — what content leaves for a model, what a job may act on, and what an attacker who can write you an email can make one do.

## Contributing

Contributions are welcome, and the entry point is [CONTRIBUTING.md](https://github.com/Krzysztof318/MailFathom/blob/main/CONTRIBUTING.md): it gets you from a clone to a passing verification run and states the few rules a pull request has to satisfy. Every change starts from an issue, so open one — or comment on an existing one — before writing code, and wait for a reply on anything larger than a typo, because MailFathom is on its `0.x` line and its direction still moves faster than its issue list.

**MailFathom is developed AI-first, and close to zero-touch.** Nearly every line here was written by an autonomous coding agent working from an issue and the rules in [`AGENTS.md`](https://github.com/Krzysztof318/MailFathom/blob/main/AGENTS.md), and reviewed before merge; the maintainer sets direction and decides, but rarely edits code by hand. Working the same way is encouraged rather than merely tolerated — point an agent at your checkout, let it read the instruction files, and let it produce the change, its tests, and its documentation in one pass. A hand-written patch is judged identically. What does not change either way is that you read your diff before submitting it, and that the same gates and the same licensing obligations apply. [How this project is built](https://github.com/Krzysztof318/MailFathom/blob/main/CONTRIBUTING.md#how-this-project-is-built) has the whole of it.

[`docs/operations/local-development.md`](https://krzysztof318.github.io/MailFathom/operations/local-development.html) is the full development setup, [`AGENTS.md`](https://github.com/Krzysztof318/MailFathom/blob/main/AGENTS.md) is the entry point to the engineering rules the build and the review enforce, and [CODE_OF_CONDUCT.md](https://github.com/Krzysztof318/MailFathom/blob/main/CODE_OF_CONDUCT.md) applies to everyone taking part.

### Your first contribution, from a fork to a green run

Fork the repository, then clone your fork and point it at this one, because every verification gate here measures your branch against the base it will actually merge into rather than against your fork's `main`:

```bash
git clone https://github.com/<you>/MailFathom.git
cd MailFathom
git remote add upstream https://github.com/Krzysztof318/MailFathom.git
git fetch upstream main
```

If you work with a coding agent, hand the rest to [`get-started-contributors`](https://github.com/Krzysztof318/MailFathom/blob/main/.agents/skills/get-started-contributors/SKILL.md), which is one of the workflow skills this repository ships and the one written for somebody arriving for the first time. In Claude Code it is `/get-started-contributors`; any other agent can be pointed at the file. It welcomes you and walks through what MailFathom is, how this repository is worked, where things live, and what Apache-2.0 asks of a contribution — then sets the machine up: the platform check, the .NET SDK the [`global.json`](https://github.com/Krzysztof318/MailFathom/blob/main/global.json) pin accepts, `gh`, Docker, the local file that tells your agent it is working in a fork rather than in the maintainer's checkout, the permissions your agent needs so the verification loop stops asking on every command, and a first green run. Invoke it again weeks later and it refreshes that machine against what has changed here since, rather than walking you through any of it twice. You invoke it yourself — it is deliberately not something an agent starts on its own.

Setting up by hand takes the same steps in the same order, and [From a clone to a green run](https://github.com/Krzysztof318/MailFathom/blob/main/CONTRIBUTING.md#from-a-clone-to-a-green-run) is where they are written out. Development is on Linux; nothing here is verified against anything else.

### Questions, bugs, and proposals

[Discussions](https://github.com/Krzysztof318/MailFathom/discussions) takes questions in `Q&A` and proposals in `Ideas`; a question is not a unit of work, and one that turns out to be work gets converted into an issue. A defect or a piece of scope belongs in [issues](https://github.com/Krzysztof318/MailFathom/issues) — except a vulnerability, which has a private channel below.

## Security

MailFathom holds mailbox credentials, OAuth tokens, certificate material, and a local copy of someone's mail. Report a vulnerability privately through [SECURITY.md](https://github.com/Krzysztof318/MailFathom/blob/main/SECURITY.md) rather than in a public issue.

**Privacy policy.** MailFathom transfers no information to other networked systems unless specifically requested by the user or the person installing or operating it. It reaches the mail servers, the database, and the model provider a deployment configures, and nothing else: it collects no telemetry, phones no home, and exports OpenTelemetry data only to an endpoint an operator sets. Where mail is stored, who can reach it, and which model receives a result are deployment decisions — [SECURITY.md](https://github.com/Krzysztof318/MailFathom/blob/main/SECURITY.md) and the [user guide](https://krzysztof318.github.io/MailFathom/users/README.html) describe them, and the terms of any model provider a deployment chooses are that provider's own.

### Verifying what you downloaded

**No `mfctl` binary carries a code signature**, on any platform, so Windows warns about an unknown publisher when you run one. The checksum file attached beside them is what tells a genuine download from a tampered one, and checking it is a deliberate step rather than something the operating system does for you:

```bash
sha256sum --check --ignore-missing 'mfctl-<version>.sha256'
```

[The administrative endpoint](https://krzysztof318.github.io/MailFathom/operations/admin-endpoint.html#getting-the-command) is where getting the command and verifying it are documented.

The container image and the Helm chart are different: each carries a signed build provenance statement naming the workflow and commit that produced it, so `gh attestation verify` answers where one came from without your having to trust the registry. [The container image](https://krzysztof318.github.io/MailFathom/operations/container-image.html#published-images) records how to check one.

## License

MailFathom is licensed under the [Apache License, Version 2.0](https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE), SPDX identifier `Apache-2.0`. Source files repeat that grant in a header the build enforces, and a published artifact carries `LICENSE` and `NOTICE` beside the binaries. The container image is that same publish output, so it carries both files and declares `org.opencontainers.image.licenses`; the Helm chart states the identifier as `artifacthub.io/license`.

MailFathom was originally created by **Krzysztof Kasprowicz**. The root [NOTICE](https://github.com/Krzysztof318/MailFathom/blob/main/NOTICE) records that attribution, which section 4(d) of the license asks a derivative distribution to preserve while it remains relevant to the derived work. A fork may add its own attribution notices beside it. The notice adds no use restriction, changes nothing about the license, and claims nothing about contributions written by other copyright holders.

Contributions to this repository are offered under Apache-2.0, by section 5 of the license. There is no contributor licence agreement and no developer certificate of origin, and contributors keep the copyright in what they write.

Third-party components that MailFathom consumes are reviewed separately in [THIRD_PARTY_LICENSES.md](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md). That register records what MailFathom depends on and under which terms; it grants nothing in MailFathom itself, which `LICENSE` alone does.

The application icon in [`assets/`](https://github.com/Krzysztof318/MailFathom/tree/main/assets) is MailFathom's own asset rather than a third-party component, and the same grant covers it. The register records how it was produced and why no one else holds rights in it.

What the license grants, it grants without promising that the software works. Sections 7 and 8 give MailFathom **as is**, without warranties or conditions of any kind, and state that no contributor is liable for damages arising out of its use or out of an inability to use it — a synchronization that falls behind, a search that misses what was there, or mail disclosed by a deployment that was reachable when it should not have been. That is the ordinary allocation for software given away rather than sold, and the license text is what governs rather than this summary of it: read [sections 7 and 8](https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE) before pointing MailFathom at a mailbox that matters. Where the database sits, who can reach it, how long mail is kept, and which model receives a result stay the deployment's decisions, and they are where most of the risk actually lives.
