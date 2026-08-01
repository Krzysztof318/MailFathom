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
then comment on the pull request itself: Codex and Claude. Both post threads
carrying a `P1`, `P2`, or `P3` severity, so one pass over the pull request's
threads answers both rather than two passes reading two vocabularies.

The Claude pass is the `Code review` workflow. It runs by itself once, when a
pull request whose branch is in this repository becomes reviewable: `opened`,
`reopened`, or `ready_for_review`. A draft is skipped, because a draft is still
being written and reviewing it spends subscription usage on a moving target. A
later push is not reviewed either — it runs the required checks and nothing
else, since those gate the merge while a re-review would mostly repeat findings
the author is already answering. Two things ask for a review anyway:

- a comment on the pull request that *begins a line* with `code-review` or
  `@code-review`, from an author with write access. Adding `opus` to that comment
  buys a second, costlier opinion; `claude-sonnet-5` is the default.

  Three things about that phrase are deliberate. It is not `@claude`, which
  collides with GitHub Copilot's own trigger. It has to lead a line rather than
  appear anywhere in the body, because `code-review` is ordinary English about
  this very workflow — writing "I'll rerun the code-review workflow" mid-sentence
  must not spend subscription usage on a run nobody asked for. And the `@` is
  optional rather than required, because every other reviewer is summoned with
  one and a trigger that silently ignores the spelling a hand reaches for first
  is a trap. `@code-review` addresses no account: the App is named
  `Fathom reviewer`, so GitHub renders it as plain text. Leading whitespace is
  fine, so a list item or a quoted line still counts, and `code-reviewer` does
  not;
- the `code-review` label, which is how a fork's pull request is reviewed at
  all. A fork's own pushes never start a review, so a maintainer decides.

The workflow reports no status check and is not in the `main` ruleset. It
advises; nothing waits on it.

### What the run is allowed to touch

The branch under review is never checked out and nothing from it is executed.
The workspace holds the base commit, which is code that already merged, and it
is there so the reviewer can read the repository's own contract: root
`AGENTS.md`, the recurring findings in the `review-change` skill, the
specifications, and the ADRs, as `main` states them rather than as the branch
would rewrite them.

The change arrives as data. A collection step reads the pull request, its
changed files with their patches, the resulting content of each changed file,
the existing review and issue comments, and the governing issue, and writes them
under `$RUNNER_TEMP/review` with an explicit ceiling on every one of them. What
a ceiling drops is recorded and ends up in the review body, because a partial
review that looks complete is worse than one that says what it did not see.

Claude then runs with `Read`, `Grep`, `Glob`, and `Write` and nothing else: no
shell, no editor, no network tool, no MCP tool, and no read access to `.git`,
where the action leaves a token for its own use. It holds no credential it could
use and posts nothing. It writes findings to one file, and the step after it
validates them and submits a single review with `event: COMMENT` — or, when the
file holds no findings, one ordinary pull-request comment instead.

The model is named exactly rather than by alias: `claude-sonnet-5` at
`--effort high`. An alias re-points at whatever ships next, and findings are only
comparable across runs when the model that produced them is the one the workflow
names.

Effort decides how much the reviewer works before answering, which is the
difference between a sweep over the whole change and a close reading of the first
few files followed by a shrug. `high` is a deliberate step down rather than the
value that would apply otherwise — Claude Code runs `xhigh` by default — because
this workflow spends a personal subscription with no per-run ceiling anywhere in
it, and the extra depth `xhigh` buys has not been measured against that cost
here. A missed finding is what would justify raising it, and the measurement
would come with the change.

That split is the point. Everything the reviewer reads about the change is
untrusted — a diff, a comment, or an issue body can carry an instruction aimed
at the model — and none of it reaches an authenticated API call. The prompt
tells Claude to report such an instruction as a P1 finding rather than obey it,
but the guarantee is structural rather than textual.

Every trigger runs the workflow file from the default branch.
`pull_request_target` and `issue_comment` both do, by definition, and there is
deliberately no `workflow_dispatch`: a dispatch takes a ref, which would let the
branch under review supply the job that receives the Claude credential.

The paths under `$RUNNER_TEMP` are declared on each step that uses them rather
than once for the job, because `runner` is not among the contexts a job-level
`env` block may read and naming it there fails the whole file's validation before
any job exists.

### What the reviewer is measured against

The prompt points the reviewer at this repository's own rules rather than at
general review practice: root `AGENTS.md`, the nested `AGENTS.md` files under
`src/`, `tests/`, and `docs/`, the recurring findings in the `review-change`
skill, and the specifications and ADRs that govern the area the change touches. A
finding is expected to name the rule it rests on, and one that applies generic
advice where this repository has stated a different rule is itself wrong.

Beyond that contract it works through five rubrics — the repository's rules,
security and privacy, reliability, performance, and clean code — each stated as
the specific things reviews have caught here rather than as a category name, and
each applied only where the change reaches it. The same prompt still rules out
what the build already enforces, anything about backward compatibility or
migration paths, and a request for tests that names no untested case, so the two
reviewers do not spend threads on findings this repository has already decided
against.

The prompt splits the work into two passes and forbids interleaving them, which
is what separates coverage from the bar. The first pass reads every entry in
`files.json` and the resulting file around every hunk, collecting candidates
without filtering or ranking any of them; it is finished when every file has been
read, not when the list feels long enough. The second pass confirms each
candidate against the file it concerns, names the rule it rests on, and drops
whatever cannot be confirmed, is already answered by the surrounding file, or was
already raised by another reviewer.

Both failure modes that split addresses are real and opposite. Judging a
candidate while still looking suppresses findings that were not yet understood,
which is how a reviewer stops searching once it has a few; reporting a candidate
it never went back to check is how a review fills with hedged noise. So the cap
of twenty findings is stated as a ceiling and never a target: a change with two
defects gets two findings, a change with none gets none, and an entry written to
lengthen the list is itself a defect in the review.

### When the reviewer stops

`claude-code-action` hides everything the reviewer produced, and rightly so: that output
is model text derived from an untrusted diff. It hides the run's own error string with
it, which is the one place a failure says what happened — an expired credential, an
exhausted subscription, a model the plan cannot use. The action does save every message
to `$RUNNER_TEMP/claude-execution-output.json`, so a step after it reads the last result
message's error text and prints that and nothing else, flattened to one line, truncated
to 500 characters, and withheld if it matches a credential shape or either credential the
workflow holds. The alternative the action offers is `show_full_output`, which would
print the entire review into the log to recover one sentence.

The submission step then distinguishes two silences. A reviewer that never answered has
already failed and said why, so a missing findings file is reported as a notice and the
run keeps the reviewer's own error as its only cause. A reviewer that answered and still
produced no valid file is this workflow's defect and fails there.

Both steps refuse to compare against an unset `CLAUDE_CODE_OAUTH_TOKEN` and report the
missing secret instead, because `grep -F ''` matches every file and an empty pattern
would otherwise turn every review into a refusal that reads like a credential leak.

### What the submission step guarantees

It validates each finding's anchor against the same patches the reviewer was
given, so a line that moved cannot make GitHub reject the whole review; an
unanchored finding moves into the review body instead of being dropped. It caps
the review at twenty findings, sets `start_side` alongside `start_line` for a
ranged comment, submits with an explicit `POST`, and refuses to post at all if
the findings contain any credential this workflow holds or anything shaped like
one.

A run that found nothing takes the other path: one ordinary pull-request comment
saying so, rather than a review. A review with no comments still opens a thread
in the timeline and asks the author to resolve something that says only that
there was nothing to resolve, and a reviewer that has to produce a review body
either way is a reviewer under quiet pressure to find something for it. The
comment carries the same sentence and leaves the review timeline to runs that
found a defect.

### Who publishes it

Not `github-actions`. The submission step authenticates as the owner's
`Fathom reviewer` GitHub App, whose installation token it mints from an app id and
a private key held as the `REVIEWER_APP_ID` and `REVIEWER_APP_PRIVATE_KEY`
secrets.

Two things follow, and the second is the reason for the first. The review carries
an identity that names what produced it instead of the identity every other
workflow in this repository posts under. And the workflow token drops to
read-only across the whole job — `contents: read`, `pull-requests: read`,
`issues: read` — because the only credential that can write to a pull request is
now minted per run, scoped to this repository, expiring with the job, and held by
the single step that makes no model call. The App itself needs exactly one
permission, `Pull requests: Read and write`, which covers submitting a review and
commenting on a pull request alike.

A missing or invalid App credential fails the run at its first step, before the
change is collected and before any subscription usage is spent.

### Provisioning the App

Once, by the owner. A GitHub App is an account-level object that is then
*installed* on repositories, so the two halves are created in different places
and both are required before a review can be posted.

1. At <https://github.com/settings/apps/new>, create an App named
   `Fathom reviewer`. The name is what appears as the review's author, so it is
   the one field with a user-visible consequence. It does not match the
   workflow's name because App names are unique across all of GitHub rather than
   per account, and `Code reviewer` was already taken — a replacement name is
   expected here, and only this file and the workflow's own comments have to
   agree with it. Give it any homepage URL — the field is required and unused —
   and clear **Webhook → Active**, because nothing here receives events.
2. Under **Permissions → Repository permissions**, grant
   **Pull requests: Read and write** and nothing else. That single scope covers
   both API calls the workflow makes. Leave **Where can this GitHub App be
   installed?** at **Only on this account**.
3. On the App's settings page after creation, note the **App ID**, then under
   **Private keys** choose **Generate a private key**. GitHub downloads a `.pem`
   file and keeps only its fingerprint; the file is the credential and cannot be
   recovered from GitHub if it is lost.
4. Under **Install App**, install it on this repository. An App that is created
   but never installed mints no token, and the workflow fails at its first step
   with an authentication error rather than a missing-secret one.
5. In the repository's **Settings → Secrets and variables → Actions**, add
   `REVIEWER_APP_ID` with the numeric App ID, and `REVIEWER_APP_PRIVATE_KEY`
   with the entire contents of the `.pem` file, including the
   `-----BEGIN…-----` and `-----END…-----` lines and the trailing newline. A key
   pasted without its header lines fails to parse.
6. Delete the downloaded `.pem` from the machine that generated it. It exists in
   the secret store now, and a second copy on disk is a second thing to protect.

Rotating the key is generating a new one, replacing `REVIEWER_APP_PRIVATE_KEY`,
and then deleting the old key from the App — in that order, so no run falls
between a revoked key and its replacement.

The reviewer authenticates with the `CLAUDE_CODE_OAUTH_TOKEN` repository secret,
produced by `claude setup-token` against the owner's Claude subscription. Without
it the run fails at the action step. That secret buys model time and nothing
else; publishing is the App's, and the two credentials are never held by the same
step. `THIRD_PARTY_LICENSES.md` records exactly what the run sends and under
whose terms, including the consumer-plan data-training setting that decides
whether what is submitted this way trains future models.

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
  fault. Run `dotnet restore MailFathom.slnx --force-evaluate`, review the
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
