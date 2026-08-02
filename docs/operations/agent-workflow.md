# Agent workflow

<!-- describes: scripts/**, .agents/skills/**, .github/workflows/**, .github/fathom-review/** -->

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

Ask what the change obliges elsewhere while reviewing it:

```bash
bash scripts/review-obligations.sh
```

That is the third kind of question a change raises and the only one no diff
answers: not whether a changed line is correct, and not whether the build
accepts it, but whether the change left the rest of the repository consistent
with itself. [What the change obliges elsewhere](#what-the-change-obliges-elsewhere)
describes what it indexes and why it asserts nothing. It gates nothing and takes
about a second, so it belongs in the loop rather than in a checklist somebody
reaches for when a change looks like it needs one.

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
`remote.<remote>.fetch` would keep a stale remote-tracking ref and satisfy the
base check against it. An unreachable remote is a failure and never degrades
into verifying against the stale ref.

## Which remote is the base

Every script above needs one answer: which remote is MailFathom. In the owner's
checkout that is `origin` and nothing else was ever needed. In a fork `origin` is
the fork, whose `main` is whatever the contributor last synced, and the
convention every Git host documents is a second remote named `upstream`.

`scripts/resolve-base-remote.sh` is that one answer, sourced by
`inspect-workspace.sh`, `verify-fast.sh`, and `verify-full.sh` rather than
reimplemented in each. It identifies the remote by the repository it points at,
not by its name: a URL whose trailing `owner/name` is `Krzysztof318/MailFathom`,
with an optional `.git` suffix, in any of the forms Git accepts. `upstream` and
`origin` are preferred in that order so a fork configuring both gets a stable
answer, and a remote under any other name still resolves.

Assuming `origin` in a fork is the worse of the two failures available. The
branch either fails the base check with a message naming no fix a fork owner can
apply, or — the quiet one — passes against a base that is not the one it will
merge into, which is a green run that proves nothing. So when no remote names
MailFathom, the full gate refuses before any `dotnet` invocation and prints the
two commands that fix it:

```bash
git remote add upstream https://github.com/Krzysztof318/MailFathom.git
git fetch upstream main
```

`inspect-workspace.sh` reports the same state without failing, as `Base branch:
unresolved`, because it changes nothing and is read before the work rather than
at the gate. The fast loop degrades instead of refusing: the base only decides
which files it formats, so a missing one narrows the scope rather than producing
a wrong verdict, and a contributor repairing their remotes is not also blocked
from building and testing. `scripts/test-agent-workflow.sh` carries a contract
for each of those four behaviours against a real second repository.

## The two roles

The repository is public and the roadmap board is not, so the contract runs in
two places. Root `AGENTS.md` § *The two roles this contract is written for*
states which rules belong to only one; this is how the skills tell them apart.

`start-task` resolves the role from the workspace rather than from a question.
`Base branch: origin/main` means `origin` is MailFathom, which is the owner's
checkout; anything else means `origin` is a fork. A clone of MailFathom made
without write access is the one case that reads the same as the owner's checkout,
so the resolution then asks `gh project item-list 4 --owner Krzysztof318` and
takes a failure as the fork role. Two checks, the second only when the first is
inconclusive, and neither needs the network in the ordinary case.

What differs is narrow and is listed in both skills at the step it applies to:
the branch name and the linked worktree, the issue's label, milestone, and board
fields, the `Queue: Next` write when the pull request opens, and which repository
is pushed to. What does not differ is everything else, including both
verification scripts, every gate, the draft pull request, and the
`Closes #<issue>` reference.

The board write is the one that has to be reported carefully. In the fork role it
is not a gate that was skipped — project `4` is private to the maintainer, so the
step does not exist there, and `finish-change` reports `not applicable (fork)`
rather than leaving a report that looks incomplete. `review-change`,
`check-docs-licenses`, `closed-enumeration`, and `add-migration` need no
authority over this repository at all and run unchanged in both roles.
`prepare-release` is the owner's alone for a different reason: its frontmatter
sets `disable-model-invocation`, so no agent reaches it in either role.

## Skills

The canonical skills are:

- `start-task` requires a clean workspace or an explicitly approved inventory
  and preservation plan, identifies or creates the GitHub issue that governs the
  task and places it on the board, then loads the applicable specification,
  documentation, and ADR context before edits;
- `review-change` performs a findings-first diff review and records verification
  status and residual risks, and reruns the fast loop only when something has
  invalidated its last green run;
- `check-docs-licenses` is the mandatory documentation, changelog, and licensing
  gate;
- `finish-change` stages only the task files, requires that gate, runs full
  verification, checks the final diff, creates a focused commit, pushes the
  branch, opens a draft pull request that references its issue with
  `Closes #<issue>`, and then moves that issue to `Queue: Next` so the board
  shows the work as in flight;
- `prepare-release` opens the two pull requests a release consists of and prints
  the order they and the tag between them have to land in. It is the one skill
  an agent cannot invoke — its frontmatter sets `disable-model-invocation`, so
  only the owner reaches it, because when a version becomes real is their
  decision. It pushes no tag and merges nothing;
  `docs/operations/release-procedure.md` records the same sequence for a reader
  without the skill.

[Issue tracking and the roadmap board](issue-tracking.md) holds the issue rules
themselves: which work needs an issue, what an issue body contains, the `type:*`
label it carries, the `Track`, `Queue` and `Size` fields that place it on the
board, the milestone that scopes it to a release, and which board transitions
belong to the project automation rather than to an agent. It sits there rather
than in root `AGENTS.md` because it is acted on twice per task and read by
nothing else, so an always-loaded copy would cost every session that touches no
issue. `start-task` and `finish-change` each name it at the step that writes the
board. Placing an issue is part of opening it, because the built-in workflows set
`Status` and nothing else.

That same limit is why `Queue: Next` is written by a skill rather than by an
automation. No project workflow can set a custom single-select field, and the
board is user-owned, which leaves only a classic account-wide token as a
credential a GitHub Actions run could use — so the write stays where a token
that already exists is already in use, and a pull request opened outside
`finish-change` moves nothing.

Those rules describe an issue an agent opened. A public repository also receives
issues nobody here opened, and one arrives with no `type:*` label and no board
fields because none of the rules reached its author. The same page holds that
path too: the missing `type:*` label is what marks an issue untriaged, triage
either places it by the ordinary rules or closes it as `not planned` with a
reason, a question moves to Discussions instead of being given a label so the
board has somewhere to put it, and a contribution is read cheapest-check-first —
required checks, then `Protected paths`, then the code-owner review. The `Triage`
board view is where an arrival waits, and an item the project opened itself never
reaches it.

The three Discussions categories that routing rule names — `Q&A`, `Ideas`, and
`Announcements` — are the ones this project answers. The remaining defaults
GitHub creates are unused and are removed in the repository's Discussions
settings, which is a manual step rather than a scripted one: the GraphQL API
exposes no mutation for a discussion category, so nothing in this repository can
assert their absence and a periodic look is what catches a re-created one.

Skills live under `.agents/skills/`. Claude Code consumes the same directory
through the relative symlink `.claude/skills -> ../.agents/skills`; do not copy
or maintain a second skill tree.

## Review on the pull request

`review-change` reviews the diff before it leaves the workspace. Two reviewers
then comment on the pull request itself: Codex and Claude. Both post threads
carrying a `P1`, `P2`, or `P3` severity, so one pass over the pull request's
threads answers both rather than two passes reading two vocabularies.

The Claude pass is the `Fathom review` workflow. It runs by itself when a pull
request whose branch is in this repository becomes reviewable — `opened`,
`reopened`, or `ready_for_review` — and again on every push to one that is
already published. The branch that will merge is the one worth a verdict, and the
`main` ruleset sets `dismiss_stale_reviews_on_push`, so a commit landing on an
approved pull request discards that approval; without a re-review it would carry
no verdict at all, which is the state a reader is most likely to mistake for one.

A draft is skipped, because a draft is still being written and reviewing it
spends subscription usage on a moving target. That is also what contains the cost
of reviewing pushes: a branch still being written is pushed to freely and spends
nothing, and marking it ready is the deliberate act that opts every later push in.
Two things ask for a review anyway:

- a comment on the pull request that *begins a line* with `fathom-review` or
  `@fathom-review`, from an author with write access. A draft is reviewed this
  way, and so is any published pull request whose current head is worth a second
  look; the comment path applies none of the checks above, because somebody with
  write access typing the phrase has already decided the run is worth its cost.
  Adding `opus` to that comment buys a second, costlier opinion;
  `claude-sonnet-5` is the default.

  Three things about that phrase are deliberate. It is not `@claude`, which
  collides with GitHub Copilot's own trigger. It has to lead a line rather than
  appear anywhere in the body, because `fathom-review` names this pipeline and is
  therefore exactly the word somebody writes when discussing it — "I'll rerun the
  fathom-review workflow" mid-sentence must not spend subscription usage on a run
  nobody asked for. And the `@` is optional rather than required, because every
  other reviewer is summoned with one and a trigger that silently ignores the
  spelling a hand reaches for first is a trap. `@fathom-review` addresses no
  account: the App is named `Fathom reviewer`, so GitHub renders it as plain
  text.

  Leading whitespace is fine, and so is a `-`, `*`, or `+` list marker, because a
  request written as one of several bullet points is still a request. A `>` is
  not: that is GitHub's quote-reply marker, so accepting it would make answering
  a thread that contains the phrase start a second run, and a citation is not an
  instruction. The marker must be followed by whitespace, so the hyphenated
  `-fathom-review` does not count, and neither does `fathom-reviewer`;
- the `fathom-review` label, which is how a fork's pull request is reviewed at
  all. A fork's own pushes never start a review, so a maintainer decides.

An automatic review is bounded per pull request: once ten reviews by
`fathom-reviewer[bot]` stand on it, the gate refuses and says so in the run log
instead of starting an eleventh. Every push to a published branch starts a
review, so a branch pushed to forty times would otherwise be reviewed forty
times, and past some number of passes over the same change another one repeats
what it already said. The count is the App's own submitted reviews rather than a
quota read from run history, because the runs endpoint cannot tell a run that
decided not to review — a draft push, a comment about the workflow — from one
that did. What it counts is every review the App has submitted on the pull
request, the explicitly requested ones included, because the question is how many
passes the change has already had rather than which of them were unprompted. Only
the automatic path is refused by the answer: a label or a comment still starts a
review afterwards, because somebody with write access asking has already made the
decision the ceiling makes when nobody made it.

A run also ends before it finishes when the pull request it is reading closes. A
merge — the owner's ruleset bypass included — and a close both arrive as
`closed`, and the gate refuses that event outright: it is a trigger only so that
it enters the pull request's concurrency group and cancels the review still
running there. Without it the reviewer reads the rest of a change that has
already landed and posts a verdict nobody can act on, which is the one shape of
wasted subscription usage that cancelling on a push does not cover. The
submission step is skipped by the same cancellation, which is why it tests
`!cancelled()` rather than `always()`: GitHub documents `always()` as running a
step even when the run was cancelled, so it would publish the verdict the
cancellation exists to prevent.

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
the inline threads and issue comments, the reviews already submitted, and the
issues its body closes, and writes them under `$RUNNER_TEMP/review` with an explicit
ceiling on every one of them. What a ceiling drops is recorded and ends up in the
review body, because a partial review that looks complete is worse than one that
says what it did not see. `truncation.txt` is where each of them appends, so it is
created empty before the first ceiling can run rather than written by whichever one
happens to be last — the closing references carry a ceiling of their own, and it is
the script that applies it that reports what it cut, which is also what lets the
contract suite exercise both without a `gh` stub for the whole collection.

A second step then writes `obligations.json` beside them, and unlike everything
above it calls no API: it reads the base checkout and the collected `files.json`,
and what it produces is what the change obliges the rest of the repository to do.
The directory is made read-only after that step rather than after the collection,
because it is the last one that writes into it — the reviewer reads those files
and the submission step trusts them, so nothing running in between may rewrite
the anchor list that validates a finding.

Every one of those collections produces a single JSON array whatever the page
count. `--paginate` runs `--jq` once per page, so a filter that built an array
per page would leave a stream of them as soon as a pull request passed a hundred
files or comments; the line list derived from the files would inherit that shape,
and the submission step would then validate every anchor against the first page
alone and push every other finding into the review body.

Claude then runs with `Read`, `Grep`, `Glob`, and `Write` and nothing else: no
shell, no editor, no network tool, no MCP tool, and no read access to `.git`,
where the action leaves a token for its own use. It holds no credential it could
use and posts nothing. It writes findings to one file, and the step after it
validates them and submits a single review: `event: COMMENT` when the file holds
findings, `event: APPROVE` when it holds none.

The model is named exactly rather than by alias: `claude-sonnet-5` at
`--effort high`. An alias re-points at whatever ships next, and findings are only
comparable across runs when the model that produced them is the one the workflow
names.

Effort decides how much the reviewer works before answering, which is the
difference between a sweep over the whole change and a close reading of the first
few files followed by a shrug. `high` is a deliberate step down rather than the
value that would apply otherwise — Claude Code runs `xhigh` by default — because
this workflow spends a personal subscription and the extra depth `xhigh` buys has
not been measured against that cost here. A missed finding is what would justify
raising it, and the measurement would come with the change.

That split is the point. Everything the reviewer reads about the change is
untrusted — a diff, a comment, or an issue body can carry an instruction aimed
at the model — and none of it reaches an authenticated API call. The prompt
tells Claude to report such an instruction as a P1 finding rather than obey it,
but the guarantee is structural rather than textual.

Every trigger runs the workflow file from the default branch.
`pull_request_target` and `issue_comment` both do, by definition, and the next
section is why that trigger is allowed here when no other workflow may use it.

The paths under `$RUNNER_TEMP` are declared on each step that uses them rather
than once for the job, because `runner` is not among the contexts a job-level
`env` block may read and naming it there fails the whole file's validation before
any job exists.

The reviewer's instructions are a file of their own,
`.github/fathom-review/reviewer-prompt.md`, and a step substitutes the run's
values into its `{{PLACEHOLDER}}` markers before the action is reached. GitHub
compiles every string holding a `${{ }}` expression into one `format(...)`
expression and refuses a workflow whose expression passes 21000 characters, so
instructions written inline eventually invalidate the file that carries them —
every trigger included, which stops the pipeline rather than degrading it. The
expressions stay here, where they are six short lines; the prose sits where no
length limit reaches it. The template is read from the workspace, which holds the
base commit, so a pull request cannot supply the prompt that reviews it.

### Waiting for the conversation before collecting it

A step before the collection waits for the pull request's conversation to stop
moving, and it exists because answering a review is one act that GitHub delivers
as two. The fix is pushed and the replies are written into the threads a moment
later, so the event that starts the run arrives *before* the answers it should be
read with. On #223 the collection closed at `18:31:40` and the two replies
disputing the previous pass were written at `18:31:52` and `18:32:07`; the
reviewer then spent five minutes on a snapshot that could not contain them and
reported both findings again, stating that neither thread had received a reply.
No wording in the prompt recovers an answer that is not in the data, which is why
this is a step rather than a paragraph.

So the snapshot is frozen only after a minimum window, extended for as long as
comments keep arriving, and bounded by a ceiling so somebody typing steadily
cannot hold a run open. A pull request nobody has commented on has nothing to
settle and waits not at all, which is every first review. The wait costs runner
time and no subscription usage, because the model has not started. The windows
are declared in the step's own `env` block, which is also what lets
`scripts/test-agent-workflow.sh` run the real loop against seconds rather than
minutes. It covers the three decisions the loop takes: collect at once, wait out
a quiet conversation, stop at the ceiling.

The collection then records the instant it began, and the prompt states it. That
makes the snapshot's edge something the reviewer can reason about rather than
mistake for the record: it may say what the code does and what a thread contains,
and never that an answer does not exist because it was not given one.

### What a re-review is given

The threads and the submitted reviews are what a re-review runs on. The job keeps
no state between runs and a push arrives as the whole change rather than as an
increment, so the previous verdicts — its own included, posted as
`fathom-reviewer[bot]` — are the only record of what was already reported.

The threads come from GraphQL rather than from the REST comment list, for the one
field REST does not carry: whether a thread is resolved. Resolving is how this
repository closes a finding out — reply, then resolve, never one without the
other — so it is the author's clearest statement that a thread is settled, and a
reviewer that cannot see it re-opens what the pull request already closed.
GraphQL also returns the comments already grouped into their threads, so a reply
sits beside the finding it answers rather than having to be reassembled from
`in_reply_to_id`, and it marks a thread outdated when the line it was written
against has moved or gone. Both are bounded, and both keep the newest: the last
hundred threads, the last twenty comments in each, and the same body ceiling
every other collected text carries. A ceiling that kept the oldest would drop the
reviewer's own most recent pass and the answers to it, which is what the
collection exists for.

The prompt spends all of that on not repeating itself. A resolved thread is taken
as closed and re-opened only where the code still plainly has the defect. A reply
that argues against a finding is answered on its merits or the finding is
dropped, because restating it beside an argument that engaged it tells the author
their answer was not read. A reply that showed the finding was wrong is a
correction to carry. What survives is raised in one line — that it stands, and
what the reply left unanswered — rather than by restating it, and the summary
says what the new commits fixed, what they did not, and what they introduced. The
change under review is still the whole branch, because a defect introduced by the
fix for an earlier finding is what a second pass is for.

### Why `pull_request_target` is a granted exception

Every other workflow in this repository is forbidden to use
`pull_request_target`, because the trigger hands repository secrets to a run
started by a pull request, and the rule exists so contributed code never executes
with them. `Fathom review` uses it anyway, as one recorded exception rather than
as a rule the repository quietly breaks.

The exception holds because the trigger is what the workflow needs and the danger
is not what it does. `issue_comment` and a label event carry no head ref to run,
and reviewing a fork at a maintainer's request is the whole reason the workflow
reaches one at all; `pull_request` would give the run neither the secrets it needs
to publish under the App nor a trigger a maintainer can aim. What makes that safe
is structural rather than procedural: the workspace holds `base.sha` and never the
branch, nothing from the contribution is executed, and the reviewer runs with
`Read`, `Grep`, `Glob`, and `Write` and no shell, network tool, or MCP tool. The
purpose of the prohibition — untrusted code never runs with a credential — is met
without avoiding the trigger.

The exception is scoped to this workflow and to that shape. It is revoked by any
change that checks out, builds, restores, or executes the branch under review,
that grants the reviewer a shell or a network tool, that adds `workflow_dispatch`
— a dispatch takes a ref, which would let the branch supply the job that receives
the Claude credential — or that lets a trigger other than a maintainer's label or
comment reach a fork. A second workflow wanting the trigger does not inherit this
reasoning; it argues its own case or uses `pull_request`.

### What bounds the cost

The run spends the repository owner's personal Claude subscription through
`CLAUDE_CODE_OAUTH_TOKEN`, so what limits how often it runs is a design concern
rather than an operational one. Eight things do, and they are listed together
because each closes a different way the bill could grow:

- a draft is never reviewed automatically, so a branch still being written is
  pushed to freely and spends nothing;
- `concurrency` with `cancel-in-progress` means a superseded head never finishes
  a review, so a rapid series of pushes costs one run rather than one per push,
  and a merge or a close ends the run reading a pull request that has stopped
  being worth a verdict;
- a fork's own pushes never start a review; a maintainer's label does;
- the comment trigger requires an `OWNER`, `MEMBER`, or `COLLABORATOR` author, so
  nobody outside the project can spend the subscription by typing;
- an automatic review is capped per pull request, as described above;
- the model is `claude-sonnet-5` rather than the costlier Opus, which a review
  request reaches only by asking for it by name;
- `--effort high` rather than the `xhigh` that would otherwise apply;
- every collected input carries an explicit ceiling, and what a ceiling dropped is
  stated in the review body.

Moving the run onto a metered API key with a spend limit would replace that set
with one number, and it is deliberately not done: the gate above already stops
anyone outside the project from spending anything, so the remaining cost is the
owner's own pushing, and a second credential to provision, rotate, and register
buys no protection against that.

### What the reviewer is measured against

The prompt points the reviewer at this repository's own rules rather than at
general review practice: root `AGENTS.md`, the nested `AGENTS.md` files under
`src/`, `tests/`, and `docs/`, the recurring findings in the `review-change`
skill, and the specifications and ADRs that govern the area the change touches. A
finding names the rule it rests on in a field of its own, and one that applies
generic advice where this repository has stated a different rule is itself wrong.

Beyond that contract it works through six rubrics — the repository's rules,
security and privacy, reliability, performance, clean code, and what the change
says about itself — each stated as the specific things reviews have caught here
rather than as a category name, and each applied only where the change reaches
it. The same prompt still rules out
what the build already enforces, anything about backward compatibility or
migration paths, and a request for tests that names no untested case, so the two
reviewers do not spend threads on findings this repository has already decided
against.

The prompt splits the work into two passes and forbids interleaving them, which
is what separates coverage from the bar. The first pass reads every entry in
`files.json` and the resulting file around every hunk, collecting candidates
without filtering or ranking any of them; it is finished when every file and
every row of `obligations.json` has been read, not when the list feels long
enough. The second pass confirms each
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

### What the change says about itself

The pull request body and the issues it closes are the change's own account of
what it does and what it was for, and both outlive the review: the body becomes
the merge commit's message and is what a release's changelog is later composed
from, and merging closes every referenced issue whether or not the change
finished it. The reviewer therefore judges a claim in either against the diff
exactly as it judges a line of documentation, and reads the body twice — once
against the file list before reading any file, once after reading them all,
because only the second reading can tell whether the claim held.

Four shapes are findings: a body claiming behavior the diff does not have; a body
claiming verification that did not happen, which is worse because it is what a
reader uses to decide how closely to look; a diff doing something substantial the
body does not mention, which is scope nobody agreed to; and a change that does not
deliver an acceptance item of an issue it closes, which leaves a closed issue
nobody will look at again. Unrecorded scope growth against an issue is worth one
line, because `AGENTS.md` asks for it to be recorded rather than for the change to
be narrowed.

None of that reaches how the body was written. A finding here names a
contradiction between what the change says and what it does, never a preference
about clarity, length, or order, and an issue the run could not fetch supports no
finding at all — the reviewer says so in the summary and judges nothing by it.

Every issue the body closes is collected, not the first one, and every keyword
GitHub acts on is matched rather than the three a body usually spells:
`.github/fathom-review/collect-closing-references.sh` owns that parsing, so which
spellings count is pinned by `scripts/test-agent-workflow.sh` instead of living in
a grep nobody rereads. Matching fewer than GitHub does is how an issue closes on
merge with nothing having read what it asked for. A bare `#123` is a mention and
closes nothing, so it is left out; a link to another project's issue is one this
reviewer cannot fetch and must not hold the change to.

A defect in what the change says about itself is usually a property of the change
rather than of a line, so those findings carry a null `path` and the submission
step renders them in the review body. That is deliberately not the summary: the
verdict is decided by whether any finding exists, so a concern left in the summary
would arrive under an `APPROVED` heading.

### What the change obliges elsewhere

A whole class of defect here is invisible in a diff, because the defect *is* the
absence of a second file from it: a `.cs` file that changed while no test
followed it, a page that still describes the behavior the change replaced, a
moved pin with no row in `THIRD_PARTY_LICENSES.md`. Each is a rule in
`AGENTS.md`, and a reviewer reading only the diff cannot see any of them.

`.github/fathom-review/index-obligations.sh` produces the list of second files.
It runs from the workspace, which holds the base commit, so a pull request cannot
supply the index that judges it — the same reason the prompt is read from there —
and it lives under `.github/` rather than in `scripts/` so that `Protected paths`
refuses a change to it from anyone but the owner. Calling no API is what lets
`scripts/test-agent-workflow.sh` run the real script against a fixture tree with
no `gh` stub at all.

The two kinds of edge it follows are recorded differently, and which one applies
turns on whether the repository's own rules already derive it.

A production type to its test is **derived and never written down**. `AGENTS.md`
requires one primary type per file with a matching file name, and
`tests/<Boundary>.UnitTests/` mirrors `src/<Boundary>/`, so the mapping is
already a rule the build enforces; a recorded copy could drift from it. The index
searches the base tree and the tests the change itself adds, because a change
that adds a class together with its test is the case where reporting a missing
test would be most obviously wrong.

A source path to the page that documents it is **declared**, because nothing
derives it: documentation is written about configuration keys and behavior rather
than about type names, so no name match finds the edge. Each page names its own
subject in a `describes:` marker under its heading, and `docs/AGENTS.md` states
the convention. A central index was refused rather than not considered — it would
go stale exactly when it matters and silently, since the pull request that adds a
class and forgets its test forgets the edge too, and it would need a freshness
gate of its own beside the check that already exists. In the page, both ways the
declaration can rot are loud instead: `scripts/test-agent-workflow.sh` fails a
page carrying no marker and a marker naming a pattern that matches nothing, so
deleting a documented class fails the build rather than waiting for a review to
notice.

The same index answers the same question before a pull request exists.
`scripts/review-obligations.sh` is the local entry point: it builds a document of
the shape GitHub returns from `git diff <base>` and hands it to the script above,
so the two callers share one implementation and a rule cannot hold in review
while lapsing in the pipeline. `$review-change` runs it and works through what it
reports, `$check-docs-licenses` starts its documentation verdict from it, and
`$finish-change` names it in the diff inspection — which is the point of having
it locally at all: an absent test costs least to add while the file that owes it
is still open.

The local report differs from the pipeline's in two ways, both because a working
tree is not a pull request. It compares against the working tree rather than
against a commit, since what is committed, what is staged, and what is neither
are one change to a reader. And it names the untracked paths no diff contains,
because a new class that owes a test is exactly the shape one takes, and a report
silently describing less than the change is worse than one that says so.

The index is bounded like every other collected input: eighty changed source files,
and twenty listed tests per type. The second bound is the one that is not obvious —
how many tests name a type is a property of how common the name is rather than of
the change — so the true count survives the cut beside the shortened list. What a
bound dropped is recorded in the index's `notes`, and the prompt requires those to
reach the reviewer's summary alongside anything `truncation.txt` says, because a
section that was cut short looks complete to everybody but the reviewer.

The patterns a marker declares are resolved the way git's own `:(glob)` pathspec
resolves them, and the two have to agree: the contract suite validates every marker
through git, so a pattern the index read more narrowly would be called valid while
the paths it covers were skipped. That is why `**` between two slashes matches zero
directories as well as many — `src/**/*Options.cs` credits `src/FooOptions.cs`, not
only a nested one — and why a leading `**/` reaches the repository root.

Nothing the index emits is a finding. It says where to look, and it is derived
from file names and declared markers, so it points at obligations a change does
not always incur — a rename owes no test, a page whose marker covers a path may
say nothing about the part that moved, a register may already carry the row. The
prompt requires each row to be confirmed in the file it points at and names what
confirmation is: the behavior no test reaches, the sentence that stopped being
true, the row that is missing. A finding whose whole content is that a file was
not touched is a defect in the review. What survives is a `P2` anchored to the
changed line that created the obligation, which is both the line the author would
edit to discharge it and the only kind of line a review comment can reach.

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

It also lays each finding out. The reviewer writes one as separate fields —
`impact`, what breaks; `correction`, the smallest change that fixes it; `rule`,
what the finding rests on — and this step renders them under fixed headings, so
every thread answers the same three questions in the same order. An author
reading a column of threads can skip to the part they need instead of parsing a
paragraph per finding, and a reviewer that skipped a field leaves a visible gap
rather than a plausible-looking sentence. The unanchored findings in the body are
rendered by the same code, so a finding does not change shape because its line
moved. The count by severity is rendered here too, and the prompt forbids the
reviewer from restating it: a tally written by hand can disagree with the threads
that were actually posted.

Either body opens with the verdict as a heading of its own — `APPROVED` when the
findings array is empty, `NEEDS CHANGES` when it is not — and the summary sits
under it. That is the one thing a reader wants before deciding whether to read
the rest, and inferring it from whether threads appeared fails exactly where it
matters, on a long pull request.

A run that found nothing takes the other branch: `event: APPROVE`, carrying the
verdict and the reviewer's summary as the review body and no inline comments.
Nothing found is a verdict, so it is delivered where GitHub renders a verdict.
The alternatives are both worse — `event: COMMENT` with an empty comment list
records that a review happened without saying what it concluded, and an ordinary
issue comment says it somewhere nothing reads as a verdict at all.

That branch is the one place the reviewer's own exit status is consulted a second
time. An empty findings array means two different things: from a run that
finished it means nothing was found, and from one that died after writing the
file — an exhausted subscription, a transport error, a model that stopped — it
means the search never completed. Findings from such a run are still posted,
because a defect it did name is real. An approval is not, because it asserts the
absence of what the run stopped looking for, so an unfinished run that found
nothing reports a notice and publishes no verdict.

`commit_id` ties the approval to the head the reviewer actually saw, and the
`main` ruleset sets `dismiss_stale_reviews_on_push`, so the next push dismisses
it and an approval can never describe code that has since changed.

**The approval cannot merge anything on its own.** The ruleset requires one
approving review *from a code owner*, `CODEOWNERS` makes the repository owner the
code owner of every path, and a GitHub App cannot be a code owner — so the
owner's approval is still required and this one sits beside it as a signal.
`REQUEST_CHANGES` is never used in either branch: a reviewer that reports no
status check and gates nothing must not be able to block a merge either, which is
why `NEEDS CHANGES` is a heading in a body and never a review state.

### Who publishes it

Not `github-actions`. The submission step authenticates as the owner's
`Fathom reviewer` GitHub App, whose installation token it mints from the
`REVIEWER_APP_CLIENT_ID` repository *variable* and the `REVIEWER_APP_PRIVATE_KEY`
repository *secret*. The split is deliberate: a client id is visible on the App's
own page and in every installation, so it is not a credential, and keeping it out
of the secret store is what lets a failed run name the App it tried to
authenticate as rather than printing `***`. The private key is the credential,
and it is the only one.

The identifier is the App's **Client ID** rather than its numeric App ID.
`create-github-app-token` deprecated the `app-id` input in v3 and warns on every
run that still passes one; both are issuers of the same JWT, so this is the
identifier GitHub now expects rather than a second credential.

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

Every run logs `Cache reservation failed: cache write denied: token has no
writable scopes`, and that warning is expected rather than a defect.
`claude-code-action` installs Bun through `setup-bun`, which tries to cache the
binary, and a cache write needs `actions: write` on the workflow token. The
warning costs one download of a runner-local binary per run; the scope that would
silence it also permits cancelling workflow runs and deleting artifacts and
caches, from a job that `pull_request_target` starts. `setup-bun` does expose a
`no-cache` input, but the action does not forward it, so the choice is between
the warning and the scope — and the warning is the cheaper of the two.

### Provisioning the App

Once, by the owner. A GitHub App is an account-level object that is then
*installed* on repositories, so the two halves are created in different places
and both are required before a review can be posted.

1. At <https://github.com/settings/apps/new>, create an App named
   `Fathom reviewer`. The name is what appears as the review's author, so it is
   the one field with a user-visible consequence. App names are unique across all
   of GitHub rather than per account, so this one may be unavailable; pick
   another and update this file and the workflow's comments, which are the only
   two places that name it. Give it any homepage URL — the field is required and
   unused — and clear **Webhook → Active**, because nothing here receives events.
2. Under **Permissions → Repository permissions**, grant
   **Pull requests: Read and write** and nothing else. That single scope covers
   both API calls the workflow makes. Leave **Where can this GitHub App be
   installed?** at **Only on this account**.
3. On the App's settings page after creation, note the **Client ID** — the
   `Iv23…` value beside the App ID, not the numeric one — then under
   **Private keys** choose **Generate a private key**. GitHub downloads a `.pem`
   file and keeps only its fingerprint; the file is the credential and cannot be
   recovered from GitHub if it is lost.
4. Under **Install App**, install it on this repository. An App that is created
   but never installed mints no token, and the workflow fails at its first step
   with an authentication error rather than a missing-secret one.
5. In the repository's **Settings → Secrets and variables → Actions**, add both,
   on their own tabs:
   - the **Variables** tab: `REVIEWER_APP_CLIENT_ID`, the App's Client ID;
   - the **Secrets** tab: `REVIEWER_APP_PRIVATE_KEY`, the entire contents of the
     `.pem` file including the `-----BEGIN…-----` and `-----END…-----` lines and
     the trailing newline. A key pasted without its header lines fails to parse.

   The tabs are not interchangeable. The workflow reads the id through `vars` and
   the key through `secrets`, so an id added as a secret resolves to an empty
   string and the token step fails with an authentication error that names
   nothing.
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

## Dependency update pull requests

Not every pull request here comes from a task. Once a week `dependabot[bot]`
opens one of its own, because `.github/dependabot.yml` configures the
`github-actions` ecosystem and nothing else in this repository updates a pin it
has made. Minor and patch updates arrive grouped into a single pull request and
a major arrives alone; `docs/operations/local-development.md` records the
schedule, the limits, and why the `nuget` ecosystem is deliberately not
included.

**No skill runs on one and none should.** `start-task` opens an issue and
`finish-change` writes a board field, and neither has anything to do here: the
change is already written, it closes no issue, and it belongs to no roadmap
item. What it needs is a reading, and the reading is the maintainer's.

Four questions answer a Dependabot pull request, and the first is the one an
automated bump makes easy to skip.

1. **Is the new revision one this repository would have chosen?** Read the
   upstream release notes the pull request links, not just the version numbers.
   A major is separated from the group for this reason: it can rename an input
   or drop a runner, and the diff shows the tag moving rather than what moved
   with it.
2. **Does the owner stay inside the reviewed set?** An update never introduces a
   new owner, and `every_external_action_names_an_approved_owner` in
   `scripts/test-agent-workflow.sh` refuses one on this pull request as on any
   other. A version that changed what an action *is* — a transfer, a rename, a
   fork under the same name — is the case that contract cannot see and a reader
   can.
3. **Does `THIRD_PARTY_LICENSES.md` still describe the truth?** Its continuous
   integration rows name each action and, where one is pinned to a commit, that
   commit and its version. A bump moves what those rows record, so the register
   is updated in the same pull request — which is `$check-docs-licenses`'s rule
   reached from the one direction where no agent is running to apply it.
4. **Do the checks pass on their own terms?** `Required CI` and
   `Protected paths` are required on this pull request exactly as on any other,
   and `Protected paths` passes only because the exception it carries recognises
   this author for `.github/workflows/` alone. A red one is a red one; nothing
   here is exempt and nothing auto-merges.

The pull request is merged the same way everything else is: a code owner
approves it, and the owner merges it. If a bump has to be declined, close the
pull request and say why in it. Closing settles that version and not the
dependency — a later release is proposed again, which is the behaviour worth
having — and the comment is where the next reader finds out the version was
considered rather than missed.

## Instruction scope

Root `AGENTS.md` is loaded into every agent session, so it carries what has to be
true before a file is read and nothing else. Its *Where the rest of the contract
lives* table names every other file and says when each one is read; this section
is the same split seen from the other end, with the reason each destination is
reached whenever its rule matters.

| File | Loaded when | Reached because |
|---|---|---|
| `AGENTS.md` | Always | `CLAUDE.md` is a single `@AGENTS.md` include |
| `src/AGENTS.md` | A change under `src/` | The directory cascade. It holds the .NET and C# conventions, which govern test code too, so `tests/AGENTS.md` points at it rather than repeating them |
| `src/Infrastructure/AGENTS.md` | A change under `src/Infrastructure/` | The directory cascade |
| `tests/AGENTS.md` | A change under `tests/` | The directory cascade, and root `AGENTS.md` names it wherever tests are owed |
| `docs/AGENTS.md` | A change under `docs/` | The directory cascade |
| `docs/operations/issue-tracking.md` | An issue is opened, placed, or linked | `start-task` step 8 and `finish-change` both name it at the step that writes the board — the only two points at which the rules are acted on |
| `docs/operations/agent-workflow.md` | A workflow script or skill is in question | Root `AGENTS.md` § *Agent workflow and verification* opens by naming it |
| `docs/operations/local-development.md` | The SDK, database, packages, or Actions policy are involved | Root `AGENTS.md` and `CONTRIBUTING.md` both point at it for setup, and it is where the settings that live outside Git are recorded |
| `.agents/skills/check-docs-licenses/SKILL.md` | Every change | It is the mandatory completion gate, so the licensing rules it holds are read on every change by construction |
| `.agents/skills/add-migration/SKILL.md` | A model change needs a migration | Root `AGENTS.md` names it as the only way to add one |

Each nested `CLAUDE.md` imports its sibling `AGENTS.md`.

### What is here for the process rather than for the product

The repository is public, so everything below is read by people who did not write
it. Each was classified deliberately rather than left in place by default:

- **`specs/` — kept in place.** The architecture draft is named as required
  context and the numbered specifications are what an issue links to instead of
  restating. Roughly half describe work that has shipped, and that is what a
  specification becomes rather than a defect in it: `specs/README.md` states which,
  and a page under `docs/` is the statement of fact beside it.
- **The five `AGENTS.md` files and their `CLAUDE.md` imports — kept in place.**
  They are the contract the agents execute, they are what makes a contribution
  produced by an agent satisfy the same rules, and `AGENTS.md` is a convention
  other projects now share rather than a private artifact.
- **`.agents/skills/` — kept in place.** The skills run in a fork, which is the
  whole of *The two roles* above; removing them would leave a contributor's agent
  with the rules and none of the procedure.
- **`.claude/skills` — kept.** It is a relative symlink to `../.agents/skills`,
  inside the repository, and it is what makes those skills reachable from Claude
  Code in any clone. Copying the tree instead would create a second copy to keep
  true.
- **`.worktreeinclude` — kept.** It names the gitignored files a new worktree
  needs and is inert everywhere else, which costs a reader one file and saves the
  next person configuring a worktree from rediscovering the list.
- **This page — kept.** How the project is worked is part of what a contributor
  needs, not residue from it.

Nothing here is retained for sentiment: a dated implementation plan or design
note is the shape that was removed, because it records how a change was arrived
at, is not allowed to be rewritten, and therefore can only drift from the code
while sitting in a tree a reader takes as fact.

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
  the relative symlink `../.agents/skills`, that its target contains every
  `SKILL.md` file, and that the installed Claude Code version supports
  directory symlinks. Stop instead of creating a duplicate skill tree.

## Completion evidence

A change is not complete until `check-docs-licenses` returns `pass` or `n/a` for
all three categories, `verify-full.sh` succeeds from a fresh run, the complete diff
has been inspected for secrets, generated files, unrelated edits, and boundary
violations, and the published pull request body references its issue. Pull
requests are created as drafts and contain no co-author trailers.

`gh pr edit` fails against this repository with a Projects-classic GraphQL error
and silently drops the edit, so correct a missing issue reference through
`gh api repos/<owner>/<repo>/pulls/<number> -X PATCH -f body=...`.
