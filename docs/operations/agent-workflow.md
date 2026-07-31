# Agent workflow

Codex and Claude Code share one repository-owned workflow. Deterministic Git and
.NET operations live in scripts, while repository-specific judgment lives in
skills.

## Entry points

Inspect the current workspace without changing Git state:

```bash
bash scripts/inspect-workspace.sh
```

Use the fast loop while implementing:

```bash
bash scripts/verify-fast.sh
```

The fast loop restores, builds Release, runs the unit tests, and then formats the
C# files the branch changed: everything committed since `origin/main`, staged,
modified, or newly added. It is the only workflow script that rewrites source
files, and it does so twice over that scope. The repairing `dotnet format` pass
applies every available code fix, and the following `--verify-no-changes` pass
reports by file and line what had none, because the repairing pass exits `0` and
identifies no file when a diagnostic such as `IDE0060` cannot be fixed
automatically. Formatting is skipped when the branch changed no C# file.

Nobody runs `dotnet format` by hand. Both of its modes already run where they
belong, so a hand-run pass either repeats about 30 seconds of workspace loading
and analysis to reproduce what the loop just reported, or, over the whole
solution, spends 70 seconds and can rewrite files the change never touched. Fix
what the loop reported and run it again.

Scoping the loop is what makes running `dotnet format` twice cheaper than
running it once over the solution. Each invocation reloads the MSBuild
workspace, which costs roughly 15 seconds regardless of scope, and the analysis
that follows scales with the files in scope: about 70 seconds for the whole
solution against about 30 for a handful of files. Splitting the run into the
`whitespace`, `style`, and `analyzers` subcommands is slower still, because it
pays the workspace load three times for work one invocation already does.

Run the complete gate before committing:

```bash
git add <task-files>
bash scripts/verify-full.sh
```

The full gate rejects remaining untracked files, fetches `origin main` and
requires the branch to contain that freshly fetched base, runs the workflow
contract suite, restores repository tools and the solution, builds Release,
executes all unit tests through the aggregate 85% coverage target, verifies
formatting, and checks committed branch changes, staged changes, and unstaged
changes for whitespace errors. It stops at the first failure. Restore, build,
test, coverage, and formatting can create ignored local artifacts, and the fetch
updates `refs/remotes/origin/main`, but the scripts do not commit, push, or
change branches.

Both scripts refuse to run on `main` or `master`, before the fetch and before
any `dotnet` invocation. The integration branch is never the subject of a
change, so a gate reporting success there describes code nobody is about to
modify, and the base check cannot notice: `origin/main` is trivially its own
ancestor, so `git merge-base --is-ancestor origin/main HEAD` passes whenever
`HEAD` *is* `origin/main`. The refusal names the branch it rejected. A detached
`HEAD` and every other branch name still verify, and running from the primary
checkout remains supported, because the hazard is the branch rather than the
kind of worktree.

The opening guard in each script reports only whether the working directory sits
inside a Git repository. It cannot establish worktree isolation: in Git
terminology the primary working tree is also a worktree, and
`git rev-parse --show-toplevel` succeeds in every checkout, so only
`git worktree add` produces the *linked* worktree that `start-task` requires.

The base check runs before any `dotnet` invocation, so a branch cut from a
`main` that has since moved fails in seconds rather than after the Release build
and coverage run. It fetches rather than trusting the local remote-tracking ref,
because a ref left behind by an earlier fetch describes the base as it was, not
as it is. The fetch names its destination explicitly as
`+refs/heads/main:refs/remotes/origin/main`: a bare `git fetch origin main` only
writes `FETCH_HEAD`, so a repository with a missing or remapped
`remote.origin.fetch` would keep a stale `refs/remotes/origin/main` and satisfy
the base check against it. An unreachable remote is a failure and never degrades
into verifying against the stale ref.

## Skills

The canonical skills are:

- `start-task` requires a clean workspace or an explicitly approved inventory
  and preservation plan, identifies or creates the GitHub issue that governs the
  task and places it on the board, then loads the applicable specification,
  documentation, and ADR context before edits;
- `review-change` performs a findings-first diff review and records verification
  status and residual risks, and reruns the fast loop only when something has
  invalidated its last green run;
- `check-docs-licenses` is the mandatory documentation and licensing gate;
- `finish-change` stages only the task files, requires the documentation and
  licensing gate, runs full verification, checks the final diff, creates a
  focused commit, pushes the branch, and opens a draft pull request that
  references its issue with `Closes #<issue>`.

Root `AGENTS.md` holds the issue rules themselves: which work needs an issue,
what an issue body contains, the `type:*` label it carries, the `Track`, `Queue`
and `Size` fields that place it on the board, the milestone that scopes it to a
release, and which board transitions belong to the project automation rather
than to an agent. Placing an issue is part of opening it, because the built-in
workflows set `Status` and nothing else.

Skills live under `.agents/skills/`. Claude Code consumes the same directory
through the relative symlink `.claude/skills -> ../.agents/skills`; do not copy
or maintain a second skill tree.

## Review on the pull request

`review-change` reviews the diff before it leaves the workspace. Two reviewers
then comment on the pull request itself: Codex, which runs on its own, and
Claude, which runs when asked. Both post threads carrying a `P1`, `P2`, or `P3`
severity, so one pass over the pull request's threads answers both rather than
two passes reading two vocabularies.

The Claude pass is the `Claude review` workflow, started from the Actions tab or
from the command line:

```bash
gh workflow run claude-review.yml -f pull_request_number=<number> -f model=opus
```

It is a `workflow_dispatch` and reports no status check. A review costs
subscription usage and advises rather than gates, so it runs on a pull request
the owner wants a second opinion on instead of on every push. `model` selects
`opus` or `sonnet`; nothing else about the run is configurable.

The run resolves the pull request's head commit and asks GitHub for its merge
base with the target branch, checks that commit out with the whole history, and
confirms both ends of the range are diffable before the review starts. Resolving
once and from the API rather than from the workspace means the review, the
commits it names, and the comments it anchors describe the same two commits even
if the branch moves while the run is in flight. Claude gets that range. The
prompt in `.github/workflows/claude-review.yml` is the whole instruction: it
points at root `AGENTS.md`, the recurring findings in the `review-change` skill,
and the specification and ADRs the change names, and it rules out the findings
this repository does not want — anything the analyzers already enforce, praise,
restatement of the diff, and compatibility or migration machinery for a release
that does not exist. A dispatch takes the workflow file from the ref it was
started on, so a branch cannot rewrite the instructions that judge it.

Claude posts exactly one review with `event: COMMENT`, each finding anchored to
the line it concerns, and says so in one comment when nothing clears the bar. It
never approves, never requests changes, never writes to the branch, and repeats
no finding an existing thread already raised. Because the dispatch carries no
pull-request context, the action installs no inline-comment tool and the review
goes through the pull-request reviews endpoint instead; the API rejects a whole
review over one line that is not in the diff, so the prompt checks its anchors
first and falls back to standalone comments.

Authentication is the `CLAUDE_CODE_OAUTH_TOKEN` repository secret, produced by
`claude setup-token` against the owner's Claude subscription. Without it the run
fails at the action step. `THIRD_PARTY_LICENSES.md` records what the run sends
and under whose terms, including the consumer-plan data-training setting that
decides whether repository source submitted this way trains future models.

## Instruction scope

Root `AGENTS.md` and `CLAUDE.md` carry repository-wide rules. More specific
instructions are next to the affected content:

- `src/` contains application and dependency-injection rules;
- `src/Infrastructure/` contains persistence and email-protocol rules;
- `tests/` contains test and coverage rules;
- `docs/` contains documentation and ADR rules.

The shared .NET and C# conventions remain at the root because they also apply
to test code. Each nested `CLAUDE.md` imports its sibling `AGENTS.md`.

## Failure recovery

- Detached HEAD, a primary checkout, a non-`agent/*` branch, or a branch that
  does not contain the freshly fetched `origin/main` blocks file changes.
  Create the required linked worktree and branch from current `origin/main`,
  then rerun `start-task`.
- A dirty workspace blocks new edits until
  `git status --short --untracked-files=all` has identified every existing path
  and the user has approved a preservation plan. Never assume pre-existing
  changes are unrelated.
- `verify-fast.sh must not run on main` (or `master`, or the same message from
  `verify-full.sh`) means verification was started on the integration branch.
  Check out the branch that carries the change and rerun; the scripts never
  change branches themselves.
- `HEAD does not contain the current origin/main` means `main` moved after the
  branch was cut. Rebase onto the fetched base, resolve any conflicts, and rerun
  the complete gate; earlier passing results describe a base that no longer
  exists.
- `verify-full.sh cannot fetch origin main` means the remote is unreachable or
  the credentials failed. Restore access and rerun. Do not work around it by
  verifying against the local remote-tracking ref.
- `Untracked files must be staged or removed before full verification` means
  the focused task files have not all entered the index. Stage only those task
  files, inspect the staged diff, and rerun the complete gate.
- `NU1004: The packages lock file is inconsistent with the project dependencies`
  means a pin moved without the lock files being regenerated. Both scripts
  restore in locked mode, so this is the intended result rather than a tooling
  fault. Run `dotnet restore MailMcp.slnx --force-evaluate`, review the
  transitive changes it writes, stage them with the pin, and rerun.
- `.NET SDK: unavailable` means the `global.json` SDK selection failed. Install
  the pinned SDK and confirm `dotnet --version` before verification.
- A coverage failure leaves detailed reports under
  `artifacts/coverage/report/`. Add meaningful tests, rerun the complete gate,
  and do not weaken the 85% scope or exclusions.
- If Claude Code cannot discover the skills, confirm that `.claude/skills` is
  the relative symlink `../.agents/skills`, that its target contains all four
  `SKILL.md` files, and that the installed Claude Code version supports
  directory symlinks. Stop instead of creating a duplicate skill tree.

## Completion evidence

A change is not complete until `check-docs-licenses` returns `pass` or `n/a` for
both categories, `verify-full.sh` succeeds from a fresh run, the complete diff
has been inspected for secrets, generated files, unrelated edits, and boundary
violations, and the published pull request body references its issue. Pull
requests are created as drafts and contain no co-author trailers.

`gh pr edit` fails against this repository with a Projects-classic GraphQL error
and silently drops the edit, so correct a missing issue reference through
`gh api repos/<owner>/<repo>/pulls/<number> -X PATCH -f body=...`.
