---
name: get-started-contributors
description: Manual only. Invoked when somebody is contributing to MailFathom for the first time — the welcome, an orientation in what the project is and how it is licensed, then the Linux-only platform check, the toolchain and its installation, the base remote, the local instruction file naming the role, the permissions an agent harness needs, and the first green verification run.
disable-model-invocation: true
license: Apache-2.0
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/Krzysztof318/MailFathom
---

# Get Started

**Manual invocation only.** Setting a machine up is something a person asks for once, so `disable-model-invocation` in
the frontmatter above keeps an agent from reaching this skill on its own — a session that stalls on a missing SDK or a
denied command has a workspace problem to report, not a setup to perform behind whoever is watching. It writes files
outside the change, which is exactly why it waits to be asked.

Everything a first change needs to be true before it starts, and nothing about the change: a welcome, an orientation in
what the project is and what its licence asks, and then the setup itself. `start-task` is where work begins and it
assumes all of this already holds; this skill is what makes it hold.

It is written for a contributor working through a fork, which is how every step below reads unless it says otherwise.
The owner's checkout differs in two places, each marked **Owner's checkout**, and nowhere else.

Nothing here edits a tracked file. One step writes a file the repository ignores, one writes a file that belongs to the
agent harness, and the rest install software or report.

## Welcome the contributor first

Open by saying hello and meaning it. Somebody has decided to spend their own evening on a mailbox tool they did not
write, and the first thing they meet should be a person's welcome rather than a checklist. Say plainly that the project
is glad they are here, that a first contribution of any size is worth having — a typo fix, a documentation sentence, a
whole feature — and that the rules below are dense because agents execute them, not because the bar for a human is high.
Then say what the next few minutes will do: a short orientation, then the setup, and a green verification run at the end
of it.

Keep it short, warm, and specific to what they are about to read. Do not perform enthusiasm, do not promise a review
time nobody controls, and never let the greeting turn into a wall of text that delays the orientation it introduces.

## Walk them through the project before the tooling

Setup makes no sense to somebody who does not yet know what they are setting up, so cover these six in order, a few
sentences each, and stop when they are covered. Read the file named beside each rather than reciting it from memory, and
offer to go deeper on any one of them instead of expanding all six.

1. **What MailFathom is.** A self-hosted mail brain: it synchronizes IMAP accounts into a PostgreSQL database the
   operator runs, indexes the whole archive rather than its newest slice, and serves it to AI agents over the Model
   Context Protocol. Two properties shape nearly everything else — reads are local, so a tool call never contacts a mail
   server, and synchronization never writes to the mailbox, so it cannot set the remote `\Seen` flag. Today's surface is
   three read-only tools, `list_emails`, `search_emails`, and `get_email_content`. `README.md` has the current state and
   the direction.

2. **How this repository is worked.** Nearly every line here is written by an autonomous agent from an issue and the
   rules in `AGENTS.md`, which is why those files read as a prescriptive contract. A contributor is encouraged to work
   the same way and equally welcome not to — a hand-written patch is judged identically. The issues are public and are
   where a contribution is discussed; what is private is the maintainer's ordering of them, on a roadmap board no
   contributor can reach, which is why step 8 below matters more than it looks. Three things hold either way:
   every change starts from an issue, the person who opens the pull request is responsible for having read the diff, and
   **everything written lands in English**, which root `AGENTS.md` states in full among its critical rules. Say that
   third one out loud rather than leaving it to be discovered in a review, and say the part that goes with it: the
   language a contributor thinks and asks questions in is their own.

3. **Where things live.** `src/` holds the clean-architecture boundaries — `Domain`, `Application`, `Infrastructure`,
   `AI`, `Mcp`, `Host`, `Cli` — and `tests/` mirrors them. `docs/` states what the code *does* and `specs/` states what a
   planned change *must* do; `docs/decisions/` holds the ADRs a change is written to be consistent with. `deploy/`,
   `scripts/`, and `.agents/skills/` are the deployment assets, the gates, and this workflow. Each directory's own
   `AGENTS.md` governs it, and the table in the root one says which to read when.

4. **The licence, and the one mistake that cannot be undone.** MailFathom is Apache-2.0, and section 5 puts a
   contribution under the same licence by the act of submitting it: there is no CLA, no DCO, nothing to sign, and no bot
   asking for an acknowledgement comment. The contributor keeps the copyright in what they write. What the licence
   cannot check is whether the code was theirs to give — so anything copied from a GPL, AGPL, SSPL, BUSL, or
   Commons Clause project, pasted from an answer with restrictive terms, or produced by a model that reproduced such
   code, is the one defect a follow-up commit cannot repair, because it has to come out of the history. A new
   dependency, service, image, or copied sample also needs a row in `THIRD_PARTY_LICENSES.md` in the same pull request.
   `CONTRIBUTING.md` § *Licensing your contribution* is the whole of it.

5. **The file header, and no name beside it.** Every file carries the same three lines naming the project, the licence,
   and the repository. In a C# file it is never typed — `scripts/verify-fast.sh` inserts it and `IDE0073` fails the
   build without it — and everywhere else it is written by hand in that file's own comment syntax. Nothing personal
   joins it: no second copyright line, no `@author`, no handle, no "modified by". That is a consistency rule rather than
   a claim about authorship, which is recorded where it is durable — in the commit history and the pull request.

6. **What the repository is careful about.** It is public, so nothing credential-shaped, no real mailbox data, and no
   personal information belongs in a commit; every fixture uses a synthetic value, and GitHub's push protection refuses
   the push rather than raising a review comment. Mail content, metadata, and embeddings are treated as personal data by
   design. The version is still `0.x`, so a breaking change to a public surface is permitted and has to be *recorded* in
   the issue and the pull request; database migrations are append-only, and `CHANGELOG.md` is written by the release
   pull request alone.

## Workflow

1. **Refuse to continue on Windows.** MailFathom is developed on Linux and nothing here is verified against anything
   else — the orchestration starts Linux containers, the verification scripts are `bash`, and every TLS handshake goes
   through the system OpenSSL rather than through .NET:

   ```bash
   uname -s      # must print Linux
   openssl version
   ```

   `MINGW64_NT-*`, `MSYS_NT-*`, `CYGWIN_NT-*`, or a shell that is not POSIX at all means native Windows. Stop there and
   say so: the fix is WSL2 or a Linux machine, and WSL2 satisfies every step below because it reports `Linux` and runs a
   real one. Do not continue on the reasoning that the solution is ordinary .NET; `docs/operations/local-development.md`
   is explicit that development on Windows is unverified and needs a setup of its own, which is not what this skill sets
   up. macOS is the same answer for the same reason.

   OpenSSL is on the same line because it decides which mail and database servers are reachable at all: **1.1.1 is the
   hard floor**, below which .NET 10 does not start, and **3.0 or later** is what this repository is run against.

2. **Install what the build needs, and prove each one answers.** This comes before the role is resolved, because
   resolving it can need `gh`. Ask the check first: a tool already installed at a working version is left alone, since a
   second install of `dotnet` from a different source is how a machine ends up with two SDKs and one `PATH`.

   | Tool | What needs it | Check |
   |---|---|---|
   | .NET SDK, the version pinned in `global.json` | Every build, test, and format pass | `dotnet --version` |
   | Git | Everything, and the gates read its remotes | `git --version` |
   | `gh`, authenticated | The issue a change starts from and the pull request it ends in | `gh auth status` |
   | Docker, usable without `sudo` | The PostgreSQL container the app model starts, and the integration suite | `docker info` |

   **The SDK** is the one pinned decision here: `global.json` names the version and `rollForward: latestFeature` accepts
   a later feature band of that same major and minor version and nothing else, so a distribution package one minor
   ahead does not satisfy it. Read the pinned version out of the file rather than typing one, and install the matching
   channel with Microsoft's own script when the distribution has no package for it:

   ```bash
   installer="$(mktemp)"
   curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
   bash "$installer" --channel <major>.<minor>
   ```

   It installs under `~/.dotnet` and puts nothing on `PATH`, so add `~/.dotnet` and `~/.dotnet/tools` to it in the
   shell profile and reopen the shell before believing `dotnet --version`.

   **`gh`** comes from GitHub's own package repository — the distribution's copy is frequently far behind, and several
   commands the workflow uses are recent. Install it as <https://github.com/cli/cli/blob/trunk/docs/install_linux.md>
   documents for the distribution in hand, then authenticate with the scope the roadmap board needs on top of the
   defaults. Which command that is depends on whether this machine has ever authenticated, and
   `docs/operations/local-development.md` § *Command-line tooling* is the source of both halves:

   ```bash
   gh auth status                 # first: is a host already authenticated, and with which scopes?
   gh auth login -s project       # a machine that has never authenticated
   gh auth refresh -s project     # a machine that already has: expands the stored credentials
   ```

   Take the one the first line's answer picks. `gh auth refresh` fails outright when no host is authenticated, and
   `gh auth login` on a machine that already has one either re-prompts interactively or does nothing under an agent's
   non-interactive shell — so running the wrong one leaves the scope unadded while looking like it worked. Somebody
   already using `gh` for their own projects is the common case here, not the rare one. Confirm with `gh auth status`
   again, which must list `project` among the scopes.

   The scope matters to the next step and not only to the board: without `project`, `gh project` fails on permission in
   the owner's own checkout exactly as it does in a fork, and the two are then indistinguishable.

   **Docker** comes from the distribution or from Docker's own repository, and the part worth checking is not that it
   is installed but that it answers without `sudo`: the app model and the integration suite talk to the daemon socket
   as the developer's user. If `docker info` fails on permission, add the user to the `docker` group and start a new
   login session.

   Then take the repository-local tools, which are pinned in `.config/dotnet-tools.json` and needed by the coverage run
   and by migrations:

   ```bash
   dotnet tool restore
   ```

   The Aspire CLI and `csharp-ls` are installed globally, at versions
   `docs/operations/local-development.md` § *Command-line tooling* pins; take them from there rather than from here, so
   one file moves when a version does. Neither is needed by a first change.

3. **Resolve which role this workspace is in**, because several steps below read differently in each and none of them is
   worth guessing at:

   ```bash
   bash scripts/inspect-workspace.sh
   ```

   `Base branch: origin/main` means `origin` is MailFathom itself, which is the owner's checkout; `upstream/main` or
   `unresolved` means `origin` is a fork, and that is the whole answer to *which repository this is*.

   It is not the answer to *what this account may do with the board*, and the two are separate facts. The maintainer
   grants read or write on project `4` to a contributor whenever they decide to, so a fork is not evidence of no access
   and a clone of MailFathom made without write access is not evidence of having it. Probe for it rather than inferring
   it, always, in either role, and only when `gh auth status` from the step above lists the `project` scope — without it
   the call fails whoever is running it, and reading that as no access would write a `CLAUDE.local.md` telling every
   later session not to touch a board it may well own:

   ```bash
   gh auth status | grep -q 'project' \
     && gh api graphql -f query='{ user(login: "Krzysztof318") { projectV2(number: 4) { viewerCanUpdate } } }'
   ```

   One call, three outcomes, and `viewerCanUpdate` is what separates the two that matter: `true` is write access, so
   every board step in `start-task` and `finish-change` applies; `false` is read access, so the board may be read for
   context and every write is reported as unavailable rather than attempted; no access at all reads as
   `"projectV2": null` beside a `NOT_FOUND` error saying `Could not resolve to a ProjectV2 with the number 4`. Read
   that last one to them, because it does not look like what it is: GitHub hides a project the viewer cannot see
   instead of refusing it, so the message reads as a wrong number or a deleted board and is neither. A failure
   *without* the scope says nothing at all, so add the scope and ask again rather than concluding from it.

   **Tell them to check their own credentials before believing a negative**, because the account's access and the
   token's access are different things and only the second one is what the probe sees. An account the maintainer
   granted write on the board still probes as no access when the credential in use carries no `project` scope, and the
   answer is written into `CLAUDE.local.md` in the next step, where it goes on being wrong in every later session.
   Three things produce that, and each has its own repair:

   - the stored `gh` credential predates the grant, or was never asked for the scope — `gh auth refresh -s project`;
   - `GH_TOKEN` or `GITHUB_TOKEN` is set in the environment, which `gh` prefers over anything it stored and whose
     scopes `gh auth status` cannot list. Unset it for this shell, or reissue the token with the scope it needs;
   - the token is fine-grained. Those carry no account-level `Projects` permission at all, so no fine-grained token
     reaches a user-owned project however it was configured; a classic token with `project`, or `gh auth login`, is the
     credential that does.

   `gh auth status` is where they look first, and a `true` or `false` answer needs none of this — only a negative is
   worth a second look, and it costs one command.

4. **Point the gates at the base the work will actually merge into.** In a fork `origin/main` is whatever was last
   synced, so verifying against it proves nothing about the branch that will merge:

   ```bash
   git remote add upstream https://github.com/Krzysztof318/MailFathom.git
   git fetch upstream main
   ```

   The scripts identify the remote by the repository it points at rather than by its name, so `upstream` is the
   convention and not a requirement. Run `scripts/inspect-workspace.sh` again and require `Base branch` to name a
   remote and `Contains base branch` to say `yes`.

   **Owner's checkout:** `origin` is already that remote and nothing is added. Work happens in a linked worktree on an
   `agent/<short-description>` branch, and `start-task` refuses anything else.

5. **Write the role into a local instruction file**, so it is true from the first message of every session rather than
   from the step where a skill resolves it. Root `AGENTS.md` states both roles and cannot say which one is running, and
   a session that never invokes `start-task` — a question, a one-line fix, `review-change` on its own — has nothing else
   to go on.

   Claude Code loads `CLAUDE.local.md` from the repository root immediately after `CLAUDE.md`, appended to it rather
   than replacing it, which is the shape wanted: the contract holds and one fact joins it. Codex has no per-directory
   equivalent — it includes at most one file per directory and prefers `AGENTS.override.md` to `AGENTS.md`, so a root
   override would displace this repository's contract instead of adding to it — and its global `~/.codex/AGENTS.md` is
   read before the repository's files, which is where the same sentences go. `.gitignore` carries `*.local.md`, so the
   file cannot reach a commit by accident.

   Write what an agent would otherwise get wrong, and stop there:

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

   The board paragraph is the one the probe in step 3 decides, so write the outcome it returned rather than the common
   case. On `viewerCanUpdate: false` it becomes *the maintainer has granted me read access to project `4`: read it for
   context, and report every write as unavailable rather than attempting it*, and on `true` it becomes *the maintainer
   has granted me write access to project `4`, so the board steps in `$start-task` and `$finish-change` apply to me as
   written*. Both keep the rest of the block unchanged: the branch is still the contributor's, the push still goes to
   the fork, and access to a board is not authority over a repository.

   `CONTRIBUTING.md` § *Tell your agent it is working in a fork* carries the same block, and the two are one text:
   change either and change both.

   **Owner's checkout:** the file is unnecessary, because `origin` answers the question and every rule applies.

6. **Permit the commands the loop actually runs.** A verification loop that stops for consent on each `dotnet` and each
   `scripts/…` invocation is a conversation rather than a loop, and the usual repair — approving everything once — gives
   up the boundary that matters. What is portable is the list below rather than any one file: Claude Code writes it to
   `.claude/settings.local.json`, which this repository ignores, Codex to `~/.codex/config.toml`, and another harness to
   whatever it reads.

   | Allow | Why |
   |---|---|
   | `dotnet`, and `scripts/` apart from `run-integration-tests.sh` | The fast loop and the full gate, typed more often than everything else combined. The one exception starts containers and belongs in the list further down |
   | Read-only Git: `status`, `diff`, `log`, `show`, `ls-files`, `rev-parse`, `merge-base`, `branch`, `fetch`, `add` | Inspecting the workspace and staging the task files; none of them publishes anything |
   | Read-only `gh`: `pr list`, `pr view`, `pr diff`, `issue list`, `issue view`, `auth status` | Reading the issue a change starts from and the checks on its pull request |
   | Ordinary reading: `ls`, `cat`, `head`, `tail`, `wc`, `grep`, `rg`, `find` | What every search costs if it is not allowed |

   Two more lists matter only where the harness sandboxes more than commands:

   - **Writes outside the checkout**, which a .NET build makes whether or not anybody asked: `~/.nuget`,
     `~/.local/share/NuGet`, `~/.dotnet`, `~/.templateengine`, `~/.aspnet`, `~/.microsoft`, `~/.aspire`, and the
     temporary directory. A sandbox that denies these fails the restore, which reads as a broken repository rather than
     as a permission.
   - **Network hosts**: `nuget.org` and `pkgs.dev.azure.com` for packages, `dot.net`, `aka.ms`, `*.microsoft.com`, and
     `dotnetcli.blob.core.windows.net` for the SDK, `github.com` and `*.githubusercontent.com` for `gh` and the base
     fetch, and `mcr.microsoft.com` with the Docker registries for the PostgreSQL image the app model pulls. Local
     binding as well, because the app model listens on the loopback.

   Leave the other direction unpermitted, whichever harness this is: pushing, force-pushing, merging, deleting a branch
   or a worktree, writing to a remote, and running the integration suite are decisions rather than steps, and each one
   is either irreversible or occupies a resource somebody else is using.

7. **Take the first green run before writing anything**, so a later failure belongs to the change:

   ```bash
   dotnet restore MailFathom.slnx
   bash scripts/verify-fast.sh
   ```

   The fast loop restores, builds, runs the unit tests, and then formats the C# files the branch changed. **It rewrites
   working-tree files by design.** Never run `dotnet format` by hand; both of its modes already run where they belong.

   Before a commit, stage the task files and run the full gate — it rejects remaining untracked files, so a new file
   cannot slip past diff validation:

   ```bash
   git add <task-files>
   bash scripts/verify-full.sh
   ```

   Both scripts refuse to run on `main` or `master`. `scripts/review-obligations.sh` answers the third question, which
   neither gate asks: what the change obliges elsewhere.

8. **Read what is refused before a session is spent on it.** From a fork:

   - the protected paths — `.github/`, `.config/`, `.agents/`, `.claude/`, `docs/decisions/`, an `.editorconfig`,
     `.gitattributes`, `.worktreeinclude`, `AGENTS.md`, or `CLAUDE.md` at any depth, and the repository-root
     `CHANGELOG.md`, `Directory.Build.props`, `LICENSE`, `NOTICE`, `NuGet.config`, and `global.json` — are refused from
     any author but the owner, whatever the change says. Raise one as an issue;
   - **the roadmap board is private, and access to it is the maintainer's to grant.** Project `4` belongs to their
     account, the repository is public and the board is not, and by default a contributor reaches neither: `gh project
     item-list` and `gh project item-edit` fail on permission and there is nothing to fall back to. What follows is not
     a degraded workflow but a shorter one — an issue opened from outside carries no `type:*` label, no milestone, and
     no `Area`, `Queue`, or `Size` value by design, triage supplies them, `start-task` opens the issue and stops there,
     and `finish-change` reports the board write as `not applicable (no board write)` rather than leaving a report that
     reads as incomplete. The maintainer does grant read or write on the board when they decide to, which is why step 3
     probes for it instead of reading it off the remote, and why what the local instruction file says about the board is
     the probe's answer rather than this default. Either way, say it out loud and make sure that file carries it,
     because an agent that does not know it will spend a turn on a permission error and then report a gate it never
     needed to pass. Board access is never repository access: the label, the milestone, and the push stay the
     maintainer's under every grant;
   - **no workflow starts by itself.** A run triggered by a fork's pull request waits for somebody with write access to
     approve it, on the first push and on every one after, so the checks sit unstarted rather than red and nothing about
     the branch can be read from them. That is why the local gates are the ones to trust, and why an agent left watching
     for a verdict waits for one that cannot arrive yet — which is what the local instruction file above says out loud;
   - `Fathom review` runs on a fork's pull request only when a maintainer applies the `fathom-review` label. Your own
     pushes never start one, and nothing you can write in a comment does either;
   - the integration suite starts containers and runs when a maintainer asks for it.

9. **Read the contract in the order it was written to be read**: `CONTRIBUTING.md` for what a pull request has to
   satisfy, root `AGENTS.md` for the non-negotiables and the table naming every other rule file,
   `docs/operations/local-development.md` for the environment in full, and `docs/operations/agent-workflow.md` for the
   scripts and the skills. Then `start-task`, which is where a change begins.

Return:

```text
Orientation: <which of the six were covered, and what was asked about>
Platform: <uname -s and the OpenSSL version, or refused with the reason>
Toolchain: <SDK, Git, gh with its scopes, Docker — the version each answered with, or what was installed>
Role: <fork or owner's checkout, and what resolved it — the base branch alone, or the board probe>
Base remote: <name and URL, or the command that added it>
Local role file: <path written, or not applicable in the owner's checkout>
Harness permissions: <what was allowed, and where>
First run: <verify-fast result>
Blockers: <none or explicit list>
```
