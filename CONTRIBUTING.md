# Contributing to MailFathom

Thank you for considering a contribution. This guide gets you from a clone to a passing verification run and states the few rules a pull request has to satisfy.

It is deliberately short. The detailed engineering rules live in [`AGENTS.md`](AGENTS.md), and this guide points at them rather than repeating them, because a second copy of a rule is a second thing to keep true.

By participating you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).

## How this project is built

**MailFathom is developed AI-first, and close to zero-touch.** Nearly every line here was written by an autonomous coding agent working from an issue and the rules in [`AGENTS.md`](AGENTS.md); the maintainer sets direction, reviews, and decides, but rarely edits code by hand. That is the normal path rather than an experiment, and it is why the instruction files read as a prescriptive contract instead of as advice: they are what the agents execute.

**You are encouraged to work the same way.** Point an agent at your checkout — Claude Code, Codex, and anything else that reads `AGENTS.md` pick the rules up on their own — give it the issue, and let it produce the change, the tests, and the documentation in one pass. A hand-written patch is equally welcome and is judged identically; the point is that the conventions here are dense enough that an agent which has actually read them will satisfy them faster than a person skimming them.

**The skills run in your fork too.** `.agents/skills/` holds the eight workflow skills the maintainer's agents use, and Claude Code finds them through the `.claude/skills` symlink in any clone. [`get-started-contributors`](.agents/skills/get-started-contributors/SKILL.md) is the one written for you rather than for them, and the only one you invoke by hand rather than letting an agent reach for it: it welcomes you, walks you through what MailFathom is, how this repository is worked, where things live, and what the licence asks of you, and then takes the clone to its first green run — the platform check, the tools and how each is installed, the remote the gates measure against, the local file that says which role you are in, and the commands your agent has to be allowed to run. **Invoke it again whenever this repository has moved under your clone.** It records what it set up, in a file inside your clone's git directory that no commit can reach, so a second invocation refreshes rather than repeats: it works out what changed since — the SDK pin, the tools, the permission list, the wording of that local file, whether the maintainer has since granted you the board — and touches only that. Deleting the record is how you ask for the walkthrough again. Everything it says is in this guide too, and the setup sections below are its short form. `start-task` works out from your remotes that it is running in a fork and adjusts three things: your branch keeps the name you gave it, the base it verifies against is `upstream/main` rather than `origin/main`, and it opens an issue without trying to label it or place it on a board. `review-change`, `check-docs-licenses`, `add-migration`, and `closed-enumeration` need nothing from this repository and run unchanged. `finish-change` runs the same gates and opens the same pull request against `main` here, and reports the one step it does not take — the maintainer's planning board is private, so there is nothing for it to write and nothing missing when it does not.

**What your agent should not spend a session on.** The protected paths below are refused from anyone but the maintainer, so a change to one cannot merge however good it is. `start-task` checks the task against them before the work rather than leaving the check to find out; if your idea needs one, open an issue and say so.

Two things are unchanged by whoever or whatever typed the code:

- **You are responsible for what you submit.** Read the diff before opening the pull request. Output nobody has read is not a contribution, it is a request that someone else read it for you.
- **The gates are identical.** The same full verification run, the same coverage threshold, and above all the same [licensing obligations](#licensing-your-contribution) — a model that reproduced code from somewhere restrictive is yours to catch, and it is the one mistake a follow-up commit cannot fix.

## Before you start

**Every change starts from an issue.** Open one before writing code, or comment on an existing one to say you are working on it. That is not ceremony: it is where scope is agreed, and it is what a pull request closes. For anything larger than a typo, wait for a maintainer's reply before investing time — MailFathom is on its `0.x` line and its direction changes faster than its issue list.

**Everything you write here is in English.** [`AGENTS.md`](AGENTS.md#critical-repository-rules) states that rule once, for every artifact and every directory, and it reaches a contribution exactly as it reaches the maintainer's agents.

**Do not report a security vulnerability as an issue.** [`SECURITY.md`](SECURITY.md) has the private channel.

## What you need

MailFathom is developed and run on Linux.

- The .NET SDK version pinned in [`global.json`](global.json). `latestFeature` roll-forward applies, so a later feature band of that same major and minor version works and a different major or minor version does not.
- Docker, for the PostgreSQL container the local orchestration starts.
- Optionally the Aspire CLI and `dotnet-ef`, which only some workflows need.

[`docs/operations/local-development.md`](docs/operations/local-development.md) is the full setup: tool versions and install commands, running the app model, development secrets, and the migration workflow. Read it once before your first change.

## From a clone to a green run

```bash
git clone https://github.com/<you>/MailFathom.git
cd MailFathom
git remote add upstream https://github.com/Krzysztof318/MailFathom.git
git fetch upstream main
dotnet restore MailFathom.slnx
dotnet build MailFathom.slnx --no-restore
dotnet test MailFathom.slnx --no-build
```

**The `upstream` remote is not optional.** Every gate here verifies your branch against the base it will actually merge into, and in your fork `origin/main` is whatever you last synced. The scripts find that base by looking for the remote that points at `Krzysztof318/MailFathom` — under any name, `upstream` is just the convention — and the full gate refuses to run rather than measure your work against the wrong base. Its refusal prints the two commands above.

## Tell your agent it is working in a fork

`AGENTS.md` is written for two roles — the maintainer's checkout and yours — and it cannot tell which one is reading it. `start-task` works that out from your remotes, but only once it runs, and plenty of sessions never reach it: a question about the code, a one-line fix, a review of a diff you already have. In those, an agent guesses, and the guess costs you a turn every time it goes the maintainer's way — a write to a private board that answers with a permission error, a label on an issue nothing can label, a push this repository refuses.

Say it once instead, in a file your agent loads before you type anything. **Claude Code** reads `CLAUDE.local.md` from the root of your clone straight after `CLAUDE.md`, adding to it rather than replacing it. **Codex** has no per-directory equivalent — it takes at most one file per directory and prefers `AGENTS.override.md`, so a file of that name at the root would *replace* this repository's instructions — and its global `~/.codex/AGENTS.md` is read first, which is where the same lines go. Another agent will have its own; the wording is what matters:

```markdown
I am an external contributor to MailFathom. `origin` is my fork and `upstream` is
`Krzysztof318/MailFathom`, so the fork role in `AGENTS.md` governs every session in this
checkout. My branch keeps the name I gave it, and nothing is ever pushed to
`Krzysztof318/MailFathom`.

The roadmap board, project `4`, is private to the maintainer and I have no access to it.
Do not read it, do not write it, and do not treat that as a step that failed: an issue I
open carries no `type:*` label, no milestone, and no `Area`, `Queue`, or `Size` value by
design, and the maintainer's triage supplies them. `$start-task` opens the issue and stops
there; `$finish-change` reports the board write as `not applicable (no board write)`.

Workflow runs on my pull request wait for a maintainer to approve them, so a check that has
not started is a queue rather than a failure to chase, and every push waits again.
`Fathom review` runs only when a maintainer applies the `fathom-review` label; my own
pushes never start one.
```

The board paragraph is the only one worth checking rather than copying, because the maintainer grants read or write on project `4` whenever they decide to, and a fork is no evidence either way. Ask, once, with the `project` scope on your `gh` credentials:

```bash
gh api graphql -f query='{ user(login: "Krzysztof318") { projectV2(number: 4) { viewerCanUpdate } } }'
```

No access prints the response body with `"projectV2": null` and a `NOT_FOUND` error, adds its own line on standard error — `gh: Could not resolve to a ProjectV2 with the number 4` — and exits `1`. That is not a wrong number and not a deleted board: GitHub hides a project you cannot see rather than telling you that you cannot see it, so the answer to *no permission* is worded as *does not exist*, and the one line `gh` puts in front of you is the half that says so least clearly.

If that is what you got, check your credentials before believing it: your account's access and your token's access are different things, and only the second is what the call sees. `gh auth status` has to list the `project` scope — `gh auth refresh -s project` adds it — and a `GH_TOKEN` or `GITHUB_TOKEN` in your environment displaces the credential `gh` stored, without `gh auth status` being able to show you its scopes. A fine-grained token never reaches a user-owned project at all, whatever it was configured with; a classic token with `project`, or a plain `gh auth login`, is what does.

A `NOT_FOUND` from a credential that does carry the scope is the paragraph as written above. `viewerCanUpdate: false` makes it *the maintainer has granted me read access to project `4`: read it for context, and report every write as unavailable rather than attempting it*, and `true` makes it *the maintainer has granted me write access to project `4`, so the board steps in `start-task` and `finish-change` apply to me as written*. Nothing else in the block changes: your branch is still yours, your push still goes to your fork, and access to a board is not authority over a repository.

`.gitignore` covers `*.local.md`, so a file written that way cannot reach a commit by accident. Do not put any of this in `AGENTS.md` or `CLAUDE.md` themselves: both are protected paths, so the change would fail a check before anyone read it, and it would be telling every other contributor's agent something true only of your machine.

## Let your agent run the loop

A verification loop that stops for your consent on every `dotnet` and every `scripts/…` invocation is a conversation rather than a loop, and the usual repair — allowing everything once — gives away the boundary worth keeping. Configure the permissions where your agent keeps them, in Claude Code's `.claude/settings.local.json`, in Codex's `~/.codex/config.toml`, or wherever yours reads. What is portable is the list, not the file:

- **allow** `dotnet` and everything under `scripts/` except `run-integration-tests.sh`, which starts containers and belongs in the last bullet, read-only Git (`status`, `diff`, `log`, `show`, `ls-files`, `rev-parse`, `merge-base`, `branch`, `fetch`, `add`), read-only `gh` (`pr list`, `pr view`, `pr diff`, `issue list`, `issue view`, `auth status`), and ordinary reading (`ls`, `cat`, `head`, `tail`, `wc`, `grep`, `rg`, `find`);
- **if your agent sandboxes the filesystem**, allow the writes a .NET build makes outside the checkout whether or not anybody asked for them: `~/.nuget`, `~/.local/share/NuGet`, `~/.dotnet`, `~/.templateengine`, `~/.aspnet`, `~/.microsoft`, `~/.aspire`, and your temporary directory. Denied, they fail the restore, which reads as a broken repository rather than as a permission;
- **if it sandboxes the network**, allow `nuget.org` and `pkgs.dev.azure.com` for packages, `dot.net`, `aka.ms`, `*.microsoft.com`, and `dotnetcli.blob.core.windows.net` for the SDK, `github.com` and `*.githubusercontent.com` for `gh`, and `mcr.microsoft.com` with the Docker registries for the PostgreSQL image, plus binding on the loopback for the local app model;
- **leave everything in the other direction to you**: pushing, force-pushing, merging, deleting a branch, and running the integration suite are decisions rather than steps.

`.claude/settings.local.json` is gitignored here, and everything else on that list lives outside the clone, so none of it can arrive in a pull request.

## The verification loop

While you work, run the fast loop instead of the three `dotnet` commands above:

```bash
bash scripts/verify-fast.sh
```

It restores, builds, runs the unit tests, and then formats the C# files your branch changed. **It rewrites working-tree files by design** — that is how a style diagnostic reaches you in seconds rather than after the full gate has run. Review what it changed.

Do not invoke `dotnet format` by hand. Both of its modes already run where they belong — the loop repairs the files you changed, the full gate verifies them — and a hand-run pass over the whole solution costs minutes to report what the build has already told you: a style rule with no automatic fix fails the Release build above, naming its file and line.

Before you commit, stage your files and run the full gate:

```bash
git add <your files>
bash scripts/verify-full.sh
```

The full gate fetches `origin main` and refuses a branch that does not contain that freshly fetched base, so rebase when it complains; verifying against a stale base proves nothing about the branch that will actually merge. It builds, runs the complete unit-test and coverage gate, verifies formatting over the C# files you changed — over the whole solution when you touched an `.editorconfig` or one of the shared build files — runs the workflow contract suite beside all of that where your change can have moved something it asserts, and checks the diff. It rejects remaining untracked files, so a newly added file cannot slip past diff validation.

Neither script proves the same tree twice. Each records a digest of what it verified under `artifacts/verify/`, which is ignored and never staged, and a run handed a tree it has already passed over prints the earlier run and stops in under a second. So run the gate rather than working out whether the last run still counts: a failing run records nothing, a fast loop whose formatting pass rewrote a file records nothing, and `VERIFY_FORCE=1` runs everything regardless.

Both scripts refuse to run on `main` or `master`. Check out your branch rather than working around the refusal.

The integration suite is not part of either script. It starts containers and is run deliberately, through `scripts/run-integration-tests.sh`, when a maintainer asks for it.

Neither script answers the third question a change raises, so there is one more command, and it is thirteen seconds whatever your diff holds:

```bash
bash scripts/review-obligations.sh
```

It prints what your change obliges the rest of the repository to do — the tests naming each type you changed, the pages that document each path you touched, and the registers whose trigger you moved — each saying whether your change touched it. That is the part no diff shows, because there the defect is the *absence* of a second file. It gates nothing and asserts nothing: a row is a place to look, and a rename owes no test. The same index runs on your pull request, so what it says here is what the reviewer will see there.

## Making the change

- **Branch off `main`, never commit to it.** In a fork the branch name is yours to choose; with write access, name it `agent/<short-description>`.
- **Behavior changes come with unit tests.** The suite enforces at least 85% aggregate line coverage across every project under `src/` except the `Host` and `AppHost` composition roots, and the gate fails below it. [`tests/AGENTS.md`](tests/AGENTS.md) is the unit-test policy — read it before adding tests, especially the rules on what belongs in the integration suite instead.
- **Update the documentation in the same change.** Stale guidance is a defect. [`docs/AGENTS.md`](docs/AGENTS.md) applies under `docs/`, and a new page there opens with a `describes:` marker naming the part of the repository it is written about — the contract suite fails a page without one, and a marker naming a path that no longer exists.
- **Warnings are errors.** `TreatWarningsAsErrors` is on and the analyzer set is configured in `.editorconfig`; suppress a diagnostic only at the narrowest scope, with the concrete reason stated.
- **Do not edit `CHANGELOG.md`.** It states what a release shipped and is written by the release pull request alone.
- **Do not create or modify an ADR** under `docs/decisions/` without the maintainer's explicit approval. Reading the relevant ones first is expected for any architectural change.

**A push carrying something credential-shaped is refused, by GitHub, before the objects reach it.** Push protection is on for this repository. The refusal names the file, the line, and what it thinks it found, and it is not a review comment you can answer — the push simply does not happen.

Fix it in the commit rather than on top of it. A secret that reached a commit is in the history even after a later commit removes it, so amend or rebase the commit that introduced it, and rotate the credential if it was ever real. Every fixture in this repository uses a synthetic value for exactly this reason; if you need a realistic-looking one, invent it. If the block is a false positive — a value that looks like a token and is not — say so in the pull request and ask the maintainer rather than looking for a bypass; bypassing is a repository permission you do not have, and that is deliberate.

## Opening the pull request

- **Put `Closes #<issue>` in the body.** It closes the issue on merge, which is also how the maintainer's own planning view learns the work is done. That view is a private project board you neither see nor need; the issue is where the conversation lives.
- **One change per pull request.** Split out anything the reviewer would have to judge separately.

**Nothing runs until a maintainer approves the run.** A workflow triggered by a pull request from a fork waits for someone with write access to release it, so on your first push the checks sit unstarted rather than red, and every later push waits the same way. That is GitHub's protection against a fork running this repository's workflows unreviewed, not a fault in your branch and not something you can retry your way past. Two consequences worth knowing: a check that has not started tells you nothing about your change, so read it as a queue rather than as a signal; and if you are working with an agent, say so in its instructions, because an agent watching for a verdict will otherwise keep waiting for one that cannot arrive yet. Run the gates locally in the meantime — they are the same ones.

Two checks gate the merge and both always report once the run is approved:

- **`Required CI`** — the build, the unit tests, the coverage threshold, and formatting.
- **`Protected paths`** — refuses a change from anyone but the repository owner to `.github/`, `.agents/`, `.claude/`, `.config/`, or `docs/decisions/`, to an `.editorconfig`, `.gitattributes`, `.worktreeinclude`, `AGENTS.md`, or `CLAUDE.md` at any depth, or to the repository-root `CHANGELOG.md`, `Directory.Build.props`, `LICENSE`, `NOTICE`, `NuGet.config`, or `global.json`. Those decide how every other change is judged rather than being judged by it, so a change to one is a separate conversation rather than a line inside a feature's diff. `docs/decisions/` is there for that reason and not because it is documentation: an architectural decision record is what the next change is written to be consistent with, so proposing a new one or a change to an existing one belongs in an issue. The check names the protected paths it found either way, so a passing run tells you which ones your change moved. If your work genuinely needs one — a new local tool, a coverage setting, a package pin, a workflow step — split it out and ask. A word for the typo checker's vocabulary is the exception, and the paragraph below says how to add one.

A third check, **`Typo check`**, runs on every pull request that is not a draft and spell-checks the files you changed, annotating each finding in the Files changed view. It gates nothing, so a red one does not block the merge — fix what it names, or, if it flagged a word this project uses on purpose or a misspelling that is deliberate, say so in the pull request. The vocabulary it reads is `.config/typos.toml`; add the word there with a line explaining which of the two it is, and expect `Protected paths` above to turn red for it, because that file sits under a prefix only the owner may change. That is the check reporting what your change moved rather than a refusal to take the word — the owner is the one who merges it either way.

A fourth, **`CodeQL`**, also runs on every pull request that is not a draft and takes the longest of the four: it builds the solution under extraction and runs GitHub's C# security queries over the result, so a finding is a value followed from an untrusted source to somewhere it should not reach rather than a style objection. Findings appear as annotations on the pull request. It gates nothing either, and for a reason worth knowing: the query pack updates upstream, so a change that was clean when it merged can become a finding later without anybody touching the code. Treat one as a question to answer in the pull request — fix it, or explain why the path is not reachable — rather than as something to route around.

Merging additionally requires an approving review from a code owner and a branch that is current with `main`. [`docs/operations/local-development.md`](docs/operations/local-development.md#pull-request-checks) documents all four workflows and the branch ruleset in full.

A separate `Fathom review` workflow may post an automated review on a published pull request. It reports no status check and gates nothing; treat it as a second opinion.

**From a fork it runs only when a maintainer asks for it**, by applying the `fathom-review` label or by commenting. Your own pushes never start one, and that is the security boundary rather than an oversight — the workflow holds a credential, so nothing a contribution can do may start it. Two practical consequences: pushing a fix does not queue a re-review the way it does on a maintainer's own branch, so say in the pull request that you have pushed and let them relabel; and a review that has not appeared is waiting on a person rather than on a queue. Nothing is lost by not having one — the required checks and the maintainer's own review are what decide the merge.

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

Every file in this repository carries the same three lines, and a new file is no exception:

```csharp
// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom
```

**In a C# file you never type it.** `IDE0073` fails the build on a file that is missing it, and `scripts/verify-fast.sh` inserts it for you — which is one more reason to run the fast loop after adding a file rather than before pushing.

**Everywhere else you do type it**, because that analyzer reads C# and nothing else. The wording never changes; only the comment syntax does, and each form is the one that file's own readers already parse:

| Where | Form |
|---|---|
| `.yml` and `.yaml` | Three `#` comment lines, first in the file, then a blank line |
| `.sh` | The same three `#` lines, directly under the shebang, which has to stay first |
| `deploy/helm/mailfathom/templates/` | A `{{- /* ... */ -}}` comment, so the header stays in the template instead of being rendered into every Kubernetes object the chart applies |
| A skill's `SKILL.md` | `license: Apache-2.0` and a `metadata` block naming the author and the repository, which is where the [Agent Skills](https://agentskills.io/specification) format puts them |

`scripts/test-agent-workflow.sh` fails when one of those is missing, so a forgotten header is a red check rather than a review comment. It reads the expected text out of `.editorconfig`, which means the header is one decision written in one place no matter how many forms it takes.

**Do not modify it, and do not add anything of your own beside it.** No second copyright line, no `@author` tag, no "modified by", no name, initials, handle, or contact detail in a comment, a file, or a header anywhere in the tree. A pull request that adds one is asked to remove it before review continues. The one `author` a skill's `metadata` block names is the copyright holder the header already states, spelled the way that format expects it; it is the same single record, not a second one, and it stays that name whoever edits the skill.

This is a consistency rule, not a claim about who wrote which line. The header states one project, one copyright holder of record, one license, and one place the project lives, so that a reader of any file — and a tool scanning the tree — gets the same answer everywhere, including from a file that has travelled far from this repository. It transfers nothing: **you keep the copyright in what you write**, exactly as the section above and the root [`NOTICE`](NOTICE) say, and the `NOTICE` is explicit that it claims nothing about contributions by other copyright holders. Authorship is recorded where it is actually durable and verifiable — in the commit history and in the pull request — rather than in a comment that the next refactor moves to a different file.

The files that carry the licensing decision itself are not merely off limits by convention — `LICENSE`, `NOTICE`, and `.editorconfig` are all on the `Protected paths` list above, so a pull request touching one fails that check before a reviewer reads it. Editing `LICENSE` turns GitHub's detected `Apache-2.0` into `NOASSERTION`, and editing the `file_header_template` line rewrites every source file in the repository. Propose a change to any of them in an issue instead.

## Where the detailed rules live

| Document | What it governs |
|---|---|
| [`AGENTS.md`](AGENTS.md) | The non-negotiables, the architecture boundaries, the privacy and licensing obligations, the reliability and security rules — and a table naming every file below and when each one is read |
| [`src/AGENTS.md`](src/AGENTS.md) | The .NET and C# conventions and naming, API and failure design, asynchronous return types, dependency injection and configuration. The conventions govern test code too |
| [`tests/AGENTS.md`](tests/AGENTS.md) | Unit-test policy, coverage rules, and what belongs in the integration suite |
| [`docs/AGENTS.md`](docs/AGENTS.md) | Documentation rules and the `describes:` marker every page carries |
| [`docs/operations/issue-tracking.md`](docs/operations/issue-tracking.md) | Which work needs an issue, what its body carries, and how a maintainer triages one that arrives from outside the project |
| [`docs/operations/agent-workflow.md`](docs/operations/agent-workflow.md) | The verification scripts and the skills at length, and how the automated review behaves |
| [`docs/decisions/`](docs/decisions/) | Architectural decision records — required context before an architectural change |
| [`specs/`](specs/) | The architecture draft: what MailFathom is being built into. The draft is intent; a page under `docs/` is fact |

Those files are written for the autonomous agents that do most of the work here, so they are longer and more prescriptive than a contribution guide needs to be. They remain authoritative: where this guide and one of them disagree, they win, and please report the discrepancy.

## Questions

Use [Discussions](https://github.com/Krzysztof318/MailFathom/discussions) — `Q&A` for questions, `Ideas` for proposals that are not yet scope. A question is not a unit of work and does not become one by arriving as an issue; one that turns out to be work gets converted.
