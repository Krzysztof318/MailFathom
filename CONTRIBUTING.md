# Contributing to MailFathom

Thank you for considering a contribution. This guide gets you from a clone to a passing verification run and states the few rules a pull request has to satisfy.

It is deliberately short. The detailed engineering rules live in [`AGENTS.md`](AGENTS.md), and this guide points at them rather than repeating them, because a second copy of a rule is a second thing to keep true.

By participating you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).

## Before you start

**Every change starts from an issue.** Open one before writing code, or comment on an existing one to say you are working on it. That is not ceremony: it is where scope is agreed, and it is what a pull request closes. For anything larger than a typo, wait for a maintainer's reply before investing time — MailFathom is pre-release and its direction changes faster than its issue list.

**Do not report a security vulnerability as an issue.** [`SECURITY.md`](SECURITY.md) has the private channel.

## What you need

MailFathom is developed and run on Linux.

- The .NET SDK version pinned in [`global.json`](global.json). `latestFeature` roll-forward applies, so a later feature band of that same major and minor version works and a different major or minor version does not.
- Docker, for the PostgreSQL container the local orchestration starts.
- Optionally the Aspire CLI and `dotnet-ef`, which only some workflows need.

[`docs/operations/local-development.md`](docs/operations/local-development.md) is the full setup: tool versions and install commands, running the app model, development secrets, and the migration workflow. Read it once before your first change.

## From a clone to a green run

```bash
git clone https://github.com/Krzysztof318/MailMcp.git
cd MailMcp
dotnet restore MailFathom.slnx
dotnet build MailFathom.slnx --no-restore
dotnet test MailFathom.slnx --no-build
```

While you work, run the fast loop instead of the individual commands:

```bash
bash scripts/verify-fast.sh
```

It restores, builds, runs the unit tests, and then formats the C# files your branch changed. **It rewrites working-tree files by design** — that is how a style diagnostic reaches you in seconds rather than after the full gate has run. Review what it changed.

Do not invoke `dotnet format` by hand. Both of its modes already run where they belong, and a hand-run pass over the whole solution costs over a minute to report what the loop just told you.

Before you commit, stage your files and run the full gate:

```bash
git add <your files>
bash scripts/verify-full.sh
```

The full gate fetches `origin main` and refuses a branch that does not contain that freshly fetched base, so rebase when it complains; verifying against a stale base proves nothing about the branch that will actually merge. It then runs the workflow contract suite, builds, runs the complete unit-test and coverage gate, verifies formatting across the solution, and checks the diff. It rejects remaining untracked files, so a newly added file cannot slip past diff validation.

Both scripts refuse to run on `main` or `master`. Check out your branch rather than working around the refusal.

The integration suite is not part of either script. It starts containers and is run deliberately, through `scripts/run-integration-tests.sh`, when a maintainer asks for it.

## Making the change

- **Branch off `main`, never commit to it.** In a fork the branch name is yours to choose; with write access, name it `agent/<short-description>`.
- **Behavior changes come with unit tests.** The suite enforces at least 85% aggregate line coverage across `Domain`, `Application`, `Infrastructure`, `AI`, and `Mcp`, and the gate fails below it. [`tests/AGENTS.md`](tests/AGENTS.md) is the unit-test policy — read it before adding tests, especially the rules on what belongs in the integration suite instead.
- **Update the documentation in the same change.** Stale guidance is a defect. [`docs/AGENTS.md`](docs/AGENTS.md) applies under `docs/`.
- **Warnings are errors.** `TreatWarningsAsErrors` is on and the analyzer set is configured in `.editorconfig`; suppress a diagnostic only at the narrowest scope, with the concrete reason stated.
- **Do not edit `CHANGELOG.md`.** It states what a release shipped and is written by the release pull request alone.
- **Do not create or modify an ADR** under `docs/decisions/` without the maintainer's explicit approval. Reading the relevant ones first is expected for any architectural change.

## Opening the pull request

- **Open it as a draft.** Mark it ready for review when it is complete and the full gate passes. A draft skips the expensive checks, so you are not burning runner minutes on work in progress.
- **Put `Closes #<issue>` in the body.** It closes the issue on merge and moves the roadmap board item.
- **No co-author trailers.** Do not put `Co-authored-by:` or any other co-author trailer on a commit. No check enforces this; a pull request carrying one is asked to remove it, which means rewriting the commits.
- **One change per pull request.** Split out anything the reviewer would have to judge separately.

Two checks gate the merge and both always report:

- **`Required CI`** — the build, the unit tests, the coverage threshold, and formatting.
- **`Protected paths`** — refuses a change from anyone but the repository owner to `.github/`, `.agents/`, `.claude/`, or `.config/`, to an `.editorconfig`, `.gitattributes`, or `.worktreeinclude` at any depth, or to the repository-root `CHANGELOG.md`, `Directory.Build.props`, `LICENSE`, `NOTICE`, `NuGet.config`, or `global.json`. Those decide how every other change is judged rather than being judged by it, so a change to one is a separate conversation rather than a line inside a feature's diff. The check names the protected paths it found either way, so a passing run tells you which ones your change moved. If your work genuinely needs one — a new local tool, a coverage setting, a package pin, a workflow step — split it out and ask.

Merging additionally requires an approving review from a code owner and a branch that is current with `main`. [`docs/operations/local-development.md`](docs/operations/local-development.md#pull-request-checks) documents both workflows and the branch ruleset in full.

A separate `Fathom review` workflow may post an automated review on a published pull request. It reports no status check and gates nothing; treat it as a second opinion. On a pull request from a fork it runs only after a maintainer applies the `fathom-review` label.

## Licensing your contribution

MailFathom is licensed under the [Apache License, Version 2.0](LICENSE), and **your contribution is offered under the same license by the act of submitting it.** Section 5 of the license says so directly:

> Unless You explicitly state otherwise, any Contribution intentionally submitted for inclusion in the Work by You to the Licensor shall be under the terms and conditions of this License, without any additional terms or conditions.

That is the whole mechanism. **There is no contributor licence agreement and no developer certificate of origin**, there is nothing to sign, and no bot will ask you to post an acceptance comment. You keep the copyright in what you write, and the patent grant in section 3 travels with the contribution because section 5 pulls the whole license in.

What the license cannot check for you is whether the code was yours to give. Before you open a pull request, satisfy yourself that:

- you wrote the contribution, or you have the right to submit it under Apache-2.0;
- any third-party code, snippet, asset, or generated output in it is compatible with Apache-2.0 and is identified in the pull request;
- your employer's agreements do not claim it, if you wrote it on their time or equipment;
- it contains no credential, token, private key, real mailbox data, or anyone's personal information.

**Bringing anything into this repository that MailFathom could not distribute under Apache-2.0 is the one mistake that cannot be fixed by a follow-up commit** — it has to be removed from the history. Copying a function from a GPL project, pasting a snippet from an answer with restrictive terms, or vendoring a file whose license nobody checked all land there. When in doubt, ask in the pull request before it is reviewed.

Adding a dependency, a service, a container image, or an externally sourced code sample also requires a row in [`THIRD_PARTY_LICENSES.md`](THIRD_PARTY_LICENSES.md) in the same pull request. Copyleft and source-available licenses — GPL, AGPL, SSPL, BUSL, Commons Clause, and similar — are refused without the owner's explicit approval, because MailFathom must stay distributable under both open-source and commercial closed-source terms. The register's acceptance policy states the allowed set.

### The file header

Every C# file in this repository carries the same two-line header, and a new file is no exception:

```csharp
// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
```

You never type it. `IDE0073` fails the build on a file that is missing it, and `scripts/verify-fast.sh` inserts it for you — which is one more reason to run the fast loop after adding a file rather than before pushing.

**Do not modify it, and do not add anything of your own beside it.** No second copyright line, no `@author` tag, no "modified by", no name, initials, handle, or contact detail in a comment, a file, or a header anywhere in the tree. A pull request that adds one is asked to remove it before review continues.

This is a consistency rule, not a claim about who wrote which line. The header states one project, one copyright holder of record, and one license, so that a reader of any file — and a tool scanning the tree — gets the same answer everywhere. It transfers nothing: **you keep the copyright in what you write**, exactly as the section above and the root [`NOTICE`](NOTICE) say, and the `NOTICE` is explicit that it claims nothing about contributions by other copyright holders. Authorship is recorded where it is actually durable and verifiable — in the commit history and in the pull request — rather than in a comment that the next refactor moves to a different file.

The files that carry the licensing decision itself are not merely off limits by convention — `LICENSE`, `NOTICE`, and `.editorconfig` are all on the `Protected paths` list above, so a pull request touching one fails that check before a reviewer reads it. Editing `LICENSE` turns GitHub's detected `Apache-2.0` into `NOASSERTION`, and editing the `file_header_template` line rewrites every source file in the repository. Propose a change to any of them in an issue instead.

## Where the detailed rules live

| Document | What it governs |
|---|---|
| [`AGENTS.md`](AGENTS.md) | Architecture boundaries, .NET and C# conventions, naming, reliability and security rules, the licensing obligations, and the issue and board conventions |
| [`tests/AGENTS.md`](tests/AGENTS.md) | Unit-test policy, coverage rules, and what belongs in the integration suite |
| [`docs/AGENTS.md`](docs/AGENTS.md) | Documentation rules |
| [`docs/decisions/`](docs/decisions/) | Architectural decision records — required context before an architectural change |
| [`specs/`](specs/) | What a planned change must do. A specification is intent; a page under `docs/` is fact |

Those files are written for the autonomous agents that do most of the work here, so they are longer and more prescriptive than a contribution guide needs to be. They remain authoritative: where this guide and one of them disagree, they win, and please report the discrepancy.

## Questions

Use [Discussions](https://github.com/Krzysztof318/MailMcp/discussions) — `Q&A` for questions, `Ideas` for proposals that are not yet scope. A question is not a unit of work and does not become one by arriving as an issue; one that turns out to be work gets converted.
