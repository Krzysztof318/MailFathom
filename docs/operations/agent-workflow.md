# Agent workflow

<!-- describes: scripts/**, .agents/skills/**, .github/workflows/**, .github/fathom-review/**, .github/pull-request/**, **/.editorconfig, .gitignore, .worktreeinclude -->

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
files, and the single `dotnet format` pass it runs is a repairing one.
Formatting is skipped when the branch changed no C# file.

There is no verifying pass behind it, because the build in front of it has
already made that report. `Directory.Build.props` sets `EnforceCodeStyleInBuild`
beside `TreatWarningsAsErrors`, and `.editorconfig` gives the IDE rules severity
`warning`, so a file with an unnecessary using, a missing licensing header, or
formatting the rules reject fails the Release build with `error IDE0005`,
`error IDE0073`, and `error IDE0055`, each naming its file and line. A
diagnostic with no code fix — `IDE0060` is the usual one — is therefore not
something a formatting pass has to surface: it failed the build several steps
earlier and the script never reached formatting at all.

What the repairing pass is for is the remainder, which is real and is invisible
to a build: the ordering of using directives and a missing final newline are
`dotnet format`'s own passes rather than analyzer rules, and no build reports
either. They also have code fixes, which is why repairing them is the whole
answer and verifying them again afterwards adds nothing.

Nobody runs `dotnet format` by hand. Both of its modes already run where they
belong, so a hand-run pass either repeats a full workspace load to reproduce
what the build named, or, over the whole solution, costs several times that and
can rewrite files the change never touched. Fix what the build named and run the
loop again.

Scoping is what makes the pass affordable at all. Each invocation reloads the
MSBuild workspace, which costs the same regardless of scope, and the analysis
that follows scales with the files in it: the whole solution costs several times
what a handful of files does, and the ratio is what the two gates are built
around rather than any particular number, which follows the machine. Splitting
the run into the `whitespace`, `style`, and `analyzers` subcommands is slower
still, because it pays the workspace load three times for work one invocation
already does.

Ask what the change obliges elsewhere while reviewing it:

```bash
bash scripts/review-obligations.sh
```

That is the third kind of question a change raises and the only one no diff
answers: not whether a changed line is correct, and not whether the build
accepts it, but whether the change left the rest of the repository consistent
with itself. [What the change obliges elsewhere](#what-the-change-obliges-elsewhere)
describes what it indexes and why it asserts nothing. It gates nothing and costs
about thirteen seconds — a fixed cost rather than a small one, since it is what
an empty diff measures too — which is still an order below every other step here,
so it belongs in the loop rather than in a checklist somebody reaches for when a
change looks like it needs one.

Run the complete gate before committing:

```bash
git add <task-files>
bash scripts/verify-full.sh
```

The full gate rejects remaining untracked files, fetches `origin main` and
requires the branch to contain that freshly fetched base, restores repository
tools and the solution, builds Release, executes all unit tests through the
aggregate 85% coverage target, verifies formatting, and checks committed branch
changes, staged changes, and unstaged changes for whitespace errors. Beside all
of that it runs the workflow contract suite, where the change can have moved
something it asserts. That chain stops at its own first failure; the suite is the
one step outside it, so a suite that fails does not stop a build already running
beside it, and a chain that fails stops the suite rather than waiting for it. The
paragraph below states what each of those buys. Restore, build, test, coverage,
and formatting can create ignored local artifacts, the gate records what it
verified under `artifacts/verify/`, and the fetch updates
`refs/remotes/origin/main`, but the scripts do not commit, push, or change
branches.

Two of those steps read what the branch changed rather than running over
everything, and both read it from `scripts/list-branch-changes.sh`, which the
fast loop reads too so the two gates cannot disagree about what a change is.

Formatting is verified over the C# files the branch changed. Formatting is a
property of a file, so those are the files this branch can have broken, and
everything else was verified by whatever change last touched it — the same
argument `ci.yml` makes when it asks a pull request rather than a push. The one
change that moves the answer for a file nobody opened is a change to a shared
style input: an `.editorconfig` at any depth, `Directory.Build.props`,
`Directory.Build.targets`, `Directory.Packages.props`, `global.json`, or
`MailFathom.slnx`. Removing one counts, and is the half a list of the files that
still exist cannot see: the rules a nested `.editorconfig` carried stop applying
the moment it is gone, and every file beneath it is then read against the ones
above without having been touched. That case, and only it, still verifies the
whole solution
here — the same list `ci.yml` gives its `format:` filter, for the same reason.
The gate verifies rather than repairs, which is the whole difference from the
loop: a change that never went through the loop is caught before it is committed
rather than quietly rewritten as it is.

Whether a step can be skipped and whether its verdict can be skipped are
different questions. `CI` runs `dotnet format` over the whole solution on every
pull request whose change can affect it, and runs the contract suite on every
pull request there is, so what the narrower local scope withholds is an earlier
verdict rather than the verdict.

The contract suite runs beside the dotnet chain rather than in front of it. It
builds the repositories it tests under its own temporary directory and fakes
`dotnet` with a symlink to itself, so it reads nothing the chain writes and
writes nothing the chain reads, and at 109 s against the chain's 105 s on the
owner's machine, sequencing the two doubles the gate for no verdict. What that
costs is stated rather than hidden: a failing suite does not mean an unspent
build, because both are already running when the suite fails. The gate refuses
either way, and it refuses having reported both answers rather than only the
first. The opposite order keeps the cheaper shape — a chain that fails stops the
suite instead of waiting it out, and says its verdict was not collected, because
a broken build is not a tree worth reporting contract findings about and a
compile error is worth answering in twenty seconds rather than a hundred.

### A gate does not prove the same tree twice

Both scripts record what they verified. A run that passes writes a digest under
`artifacts/verify/`, and a run handed a digest it already recorded prints the
earlier run's time and stops without repeating its expensive steps. The digest
covers everything a verdict depends on inside the checkout — the commit, which
paths are in which state, the content of every tracked change, the content of
every file Git is not tracking yet, and the verification scripts themselves — so
anything that could move a verdict retires the record that preceded it.

Two runs over identical content reach identical verdicts, so the second one buys
nothing. That was measurable rather than theoretical: across 150 session
transcripts, 25% of full-gate runs and 13% of fast-loop runs re-ran a tree that
no edit had touched since the previous green run of the same gate, 484 minutes of
repetition on a conservative count. `$review-change` already stated the rule in
prose; this is the same rule where a session cannot forget it.

Four things bound what a record claims.

- **It is written only when the tree stayed still.** Each run takes the digest
  again at the end and records nothing when the two disagree. The fast loop is
  why: its formatting pass rewrites files, so a run that repaired something
  verified a build and a test suite against content the working tree no longer
  holds. That guard is also what would let either gate be started in the
  background beside an editing session.
- **A gate that failed writes nothing**, however far it got.
- **The full gate's record answers for the fast loop, and never the reverse.**
  The full gate builds, tests, collects coverage over the same suite, and
  verifies the formatting the loop repairs; passing the loop says nothing about
  coverage or the contract suite. The one step the loop does settle for the full
  gate is the scoped formatting pass — the same tool over the same file set, and
  a record exists only where the repairing pass rewrote nothing, which leaves
  `--verify-no-changes` one possible answer. The whole-solution pass a shared
  style input triggers is never settled that way, because the loop never formatted
  that scope.
- **The base is deliberately not in the digest.** Whether the branch still
  contains the current `origin/main` is asked afresh on every full-gate run,
  before any record is consulted, so a record cannot stand in for it. Folding the
  base in would retire every record each time somebody else merged, while proving
  nothing about this branch's own content. The whitespace checks read the base
  too, and run on every invocation for the same reason.

`VERIFY_FORCE=1` runs everything regardless, and the message a skip prints says
so. Nothing else reads the directory, and removing it costs one repeated run.

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

Five cases in the contract suite that gate runs assert the licensing header
rather than a script's behaviour. IDE0073 applies `.editorconfig`'s
`file_header_template` to C# and reaches nothing else, so the workflows, the
shell scripts, the chart, the documentation site's own assets, the Quadlet unit
sources, and the skills would each carry the mark only for as long as somebody
remembered to type it, and nothing would say when one stopped. The cases read
`git ls-files` against the real repository, which is what keeps the fixture
checkouts the suite builds from either failing or satisfying them. Each surface
states the same three lines in the form its own readers parse: a `.yml` or
`.yaml` file opens with them as `#` comments and so does a `.container`,
`.network`, or `.volume` file under `deploy/quadlet/`, a `.sh` file carries them
under the shebang that has to stay first, a file under
`deploy/helm/mailfathom/templates/` carries them as a `{{- /* ... */ -}}` comment
so the rendered manifest is unchanged, a `.js` module opens with them as `//`
comments and a `.css` file as the one `/* ... */` block it has instead, and a
`SKILL.md` declares `license` and a `metadata` block, which is where the Agent
Skills format puts them. All of them are compared against the text parsed out of
`.editorconfig`, so the header stays one decision written in one place: an edit
to the template that leaves the other files behind fails as a disagreement rather
than quietly splitting the mark in two.

The suite runs in two places and the second one is not a convenience. `CI`'s
`Workflow contracts` job runs it on every pull request, including a draft, and on
every push to `main` after a merge, which is what makes these contracts a property
of the repository rather than of whoever remembered the gate: the change detection
in that workflow routes `.github/`, `docs/`, `scripts/`, and `.agents/` to no other
job, because none of them can move a build, a formatting verdict, or the EF Core
model, and the merge is the moment the tree they describe changes without any pull
request having been wrong. A fork's pull request has no local gate behind it at
all, and it is a fork's contributor who is most likely to change a workflow or a
script without knowing what asserts it. What the full gate
above keeps is the earlier verdict — a contract broken here is answered before the
push rather than after it — and the job is the same command against the same tree,
so the two cannot disagree.

That is also what decides when the full gate runs it. Every invariant the suite
asserts is carried by a file no C# change can move: a licensing header outside
`.cs`, a `describes:` marker, a table-of-contents entry, a link. So a branch that
only added or edited C# files has nothing here to break, and the gate skips the
suite for it. A path the branch removed or moved runs it whatever the path was,
because a marker and an entry name a path and the files that remain say nothing
about one that left. So does a change list the script cannot determine, which is
a repository state to fix rather than a verdict to guess at. `CI` is
unconditional either way, which is what makes this an earlier verdict withheld
rather than a verdict lost.

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
checkout; anything else means `origin` is a fork. That settles which repository
this is, and it deliberately does not settle what the session may do with the
board, because the owner grants read or write on project `4` to a contributor
whenever they decide to. So the skill asks separately, in either role, with
`{ user(login: "Krzysztof318") { projectV2(number: 4) { viewerCanUpdate } } }`:
`true` is write, `false` is read, and neither of them is a `null` project beside
a `NOT_FOUND` error, which is how GitHub reports a project the viewer cannot see
— it hides one rather than refusing it, so the answer for *no permission* is
worded as *does not exist* and is read as neither. One call, and the answer a
`CLAUDE.local.md` already carries is confirmed rather than replaced.

What differs is narrow and is listed in both skills at the step it applies to,
and it splits along those two facts. The repository decides the branch name and
the linked worktree, every label the issue carries and its milestone, and which
remote is pushed to; the board probe decides the `Area`, `Queue`, and `Size`
fields and the `Queue: Next` write when the pull request opens. A contributor the
owner granted board write therefore places an issue exactly as the owner's
checkout does while still not labelling it, which is the point of separating
them. What does not differ is everything else, including both verification
scripts, every gate, the pull request, and the `Closes #<issue>` reference.

The board write is the one that has to be reported carefully. Without write
access it is not a gate that was skipped — a grant on the owner's board is theirs
to make, so the step does not exist in that session, and `finish-change` reports
`not applicable (no board write)` rather than leaving a report that looks
incomplete. `review-change`, `check-docs-licenses`, `closed-enumeration`, and
`add-migration` need no authority over this repository at all and run unchanged
in both roles.
`prepare-release` is the owner's alone for a different reason: its frontmatter
sets `disable-model-invocation`, so no agent reaches it in either role.

### Stating the role before a skill resolves it

That resolution happens one step into a session, and a session is not obliged to
start there. A question about the code, a fix small enough that nobody reached
for a skill, `review-change` or `finish-change` invoked on its own, a harness
that loaded no skill at all — each runs on root `AGENTS.md`, which states both
roles and cannot say which one is running. The fork role is then inferred, and
the inference has a direction: this contract writes the owner's steps down in
more places than the fork's, because those are the steps that need writing down.

What a wrong inference costs is a turn each time, and every one of them is
visible only after it fails: a board write that returns a permission error, a
`type:*` label assigned where nothing can assign one, a push refused by
`Krzysztof318/MailFathom`, a branch renamed to `agent/<short-description>` for
nothing. So a contributor settles it once, in a file their harness loads before
the first message and this repository ignores.

Claude Code reads `CLAUDE.local.md` from the repository root immediately after
`CLAUDE.md`, appending it rather than replacing it, which is the shape wanted:
the contract holds and one fact joins it. Codex has no per-directory equivalent.
It includes at most one file per directory and prefers `AGENTS.override.md` to
`AGENTS.md`, so a root override would displace this repository's contract
instead of adding to it; its global `~/.codex/AGENTS.md` is read before the
repository's files, and that is where the same sentences go. `.gitignore`
carries `*.local.md` so neither can reach a commit by accident — no protected
path catches one, because `CLAUDE.local.md` is not `CLAUDE.md` and the guard
matches a whole file name. `.worktreeinclude` copies `CLAUDE.local.md` into a
new worktree for the same reason it copies `.env`: a gitignored file otherwise
exists only in the checkout it was written in, and the workspace an agent works
in is not that one.

Four sentences are enough, and what earns a place in one is what an agent would
otherwise get wrong: which remote is which, that project `4` is unreachable and
its label, milestone, and board fields belong to triage, that nothing is pushed
to `Krzysztof318/MailFathom`, and that the branch keeps the name it was given.
The wording itself is written once, in
`.agents/skills/get-started-contributors/SKILL.md`, and repeated once where a
contributor reads it, in `CONTRIBUTING.md` § *Tell your agent it is working in a
fork*; this page carries the reasoning and deliberately not a third copy.

None of it is a rule the skills fail to enforce — `start-task` reaches the same
answer from the remotes. It is the sessions that reach no skill, which is most
of the short ones.

## Skills

The canonical skills are:

- `get-started-contributors` takes somebody from arriving to a first green run: a
  welcome, then an orientation in what MailFathom is, how this repository is
  worked, where things live, what Apache-2.0 section 5 does and does not ask, the
  file header that carries no name, and what a public repository is careful about
  — and then the setup, which is the platform check that refuses anything but
  Linux, the toolchain and how each piece of it is installed, the remote the gates
  resolve their base from, the local instruction file above, the commands an agent
  harness has to permit for the loop to be a loop, and what the fork role is
  refused before a session is spent on it. It is the one skill written for
  somebody who has not read this page, and it changes no tracked file. Like `prepare-release` it sets `disable-model-invocation`, for the
  opposite reason: setting a machine up is asked for by a person, and an
  agent that hits a missing SDK mid-task has a blocker to report rather than an
  installation to perform while nobody is looking. It is asked for more than
  once all the same, because what it puts on a machine does not hold still — the
  SDK pin, the repository-local tools, the permission list, the role file's
  wording, and a board grant each move without the machine hearing about it. A
  completed run writes `mailfathom-setup.json` inside the clone's git directory,
  resolved through `--git-common-dir` so it is one file per clone however many
  worktrees read it and so no commit can reach it, and a later invocation reads
  it and refreshes instead of repeating:
  it diffs the recorded base commit against the current one over the paths a
  local configuration is written from, leaves an installed tool alone while its
  check answers, probes the role and the board again because neither is written
  in a commit, and rewrites the role file and the permissions only where they
  differ. Deleting that file is how a first run is asked for again;
- `start-task` requires a clean workspace or an explicitly approved inventory
  and preservation plan, identifies or creates the GitHub issue that governs the
  task, places it on the board and claims it with `agent:claimed`, then loads the
  applicable documentation and ADR context before edits;
- `review-change` performs a findings-first diff review and records verification
  status and residual risks, and reruns the fast loop only when something has
  invalidated its last green run;
- `check-docs-licenses` is the mandatory documentation, changelog, and licensing
  gate;
- `finish-change` stages only the task files, requires that gate, runs full
  verification, checks the final diff, creates a focused commit, pushes the
  branch, opens a pull request that references its issue with
  `Closes #<issue>`, and then moves that issue to `Queue: Next` so the board
  shows the work as in flight;
- `prepare-release` opens the two pull requests a release consists of and prints
  the order they and the tag between them have to land in. It composes the
  changelog section from what merged since the previous tag, reading each closed
  issue against its parent, so a feature the release delivers only part of is
  written as what the reader can do now rather than as the capability its parent
  names. Its changelog pull request also carries the files that name a version
  in prose and a sweep for prose describing the release state without naming
  one, because both go stale at the tag and neither is reached by
  `<VersionPrefix>`. Before either pull request it settles the milestones —
  creating the next one if it does not exist, opening the issue that tracks that
  release in it, moving what is still open into it, and closing the one being
  released — which is the reason the release being worked never stands without
  the issue that closes it, whether that milestone was created there or had
  already been opened as the target of a parent whose children span releases.
  Both of its pull requests name the tracking issue for the release being cut,
  and the version-bump one closes it, because a release is finished when `main`
  names the next version rather than when the changelog merged. It is one of the
  two skills an agent cannot invoke — its frontmatter sets
  `disable-model-invocation`, so only the owner reaches it, because when a
  version becomes real is their decision. It pushes no tag and merges nothing;
  `docs/operations/release-procedure.md` records the same sequence for a reader
  without the skill.

[Issue tracking and the roadmap board](issue-tracking.md) holds the issue rules
themselves: which work needs an issue, what an issue body contains, the `type:*`
label it carries, the `agent:claimed` marker a session applies when it takes one,
the `Area`, `Queue` and `Size` fields that place it on the board, the milestone
that scopes it to a release, and which board transitions belong to the project
automation rather than to an agent. It sits there rather than in root
`AGENTS.md` because it is acted on twice per task and read by nothing else, so
an always-loaded copy would cost every session that touches no issue.
`start-task` and `finish-change` each name it at the step that writes the board.
Placing an issue is part of opening it, because the built-in workflows set
`Status` and nothing else.

That same limit is why `Queue: Next` is written by a skill rather than by an
automation. No project workflow can set a custom single-select field, and the
board is user-owned, which leaves only a classic account-wide token as a
credential a GitHub Actions run could use — so the write stays where a token
that already exists is already in use, and a pull request opened by neither
`finish-change` nor `prepare-release` moves nothing.

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

## Rules on the pull request

`Apply pull request rules` derives every fact a pull request earns. A change is read in
places the issues it closes never reach — a list, a notification, a reviewer deciding how
closely to look, a board column the owner works from — and each of those facts follows from
the same few inputs: the pull request, the issues it closes, and whether it still merges.
Deriving them in one pipeline rather than in a workflow apiece is what keeps a new rule from
costing another trigger, another checkout, and another run for one small thing.

Every condition lives in a script under `.github/pull-request/` and none of them in the
workflow, so a new rule is one edit to a script the contract suite runs rather than a rule
spread across whichever workflows happen to care.

The two jobs are split by the event that can answer them, which is also what keeps each run
short. `Fathom review` waits for this workflow's run to conclude before it reads the labels
off a pull request, so anything slower on that path delays every review.

### Labels the change earns

Labels follow from the body, so they are decided on a `pull_request` event when one is
opened, reopened, marked ready, or **edited** — that last one because the body is an input:
a closing reference added afterwards changes which issues describe the change and therefore
which labels it earns.

One condition exists today: a pull request earns `security` when any issue it **refers to**
carries `security`, the label [Issue tracking](issue-tracking.md#labels) defines as *needs a
security review before it merges*.

Refers to, not closes, and the difference is the point. `collect-referenced-issues.sh`
collects every issue the body names — a closing reference, a bare `#123`, and a link to an
issue in this repository alike — where `collect-closing-issues.sh`, which the reviewer's
collection and every board write use, asks GitHub for the issues the merge will actually
close. The two answer different questions: what merging *completes* is a contract a review
holds the change to and a set of items the board acts on, while what the change is *about*
is what a label says, and "part of #123" against a security issue is a change somebody
wants read that way whether or not it finishes the issue. That is also why only one of them
is a reading of the body: a mention has no resolved answer to ask GitHub for. An issue in
another repository is left out by both, because a label earned from another project's
numbering and a board item on another project's roadmap are the same mistake.

The labelling only ever adds. A label a hand applied answers a question this pipeline cannot
see — `fathom-review` is the worked example, and it means *somebody asked* — so nothing
here removes one, and re-applying a label already present changes nothing and fires no
event. An issue that cannot be fetched earns nothing rather than stopping the walk: reading
a label is how a condition is decided, so an unreadable issue is a condition that was not
met rather than one to guess at.

It uses `pull_request` rather than `pull_request_target`, which stays reserved for
`Fathom review` alone. Nothing here needs that trigger's elevated token, and the cost of not
taking it is a fork's pull request: GitHub hands every workflow a read-only token there
whatever the file declares, so the write is refused, the run says so and ends green, and a
maintainer labels a fork's pull request by hand exactly as they already start its review by
hand.

What it checks out follows from that trigger rather than from `Fathom review`'s rule, and
the two look opposite for a reason. `pull_request_target` runs the workflow file from the
default branch, so pinning the checkout to the base is what stops a branch supplying any of
the code that judges it. `pull_request` runs the workflow file from the *head*, so the
branch already supplies the job: pinning the base would withhold nothing from it and would
only guarantee that a change adding a script together with the call to it runs the new call
against a tree without the script. That is not hypothetical — it is how the pull request
introducing this workflow failed, with `No such file or directory`. So the checkout is the
merge ref this trigger defaults to, which is the tree the workflow file itself came from,
and the read-only token on a fork is what bounds what a script from there can do.

### The board status the state earns

A board status follows from whether a pull request still merges, and *that* changes when
something else merges into `main` rather than through any event on the pull request itself.
GitHub raises nothing on the branch it happened to, so this job is triggered by the push to
`main` and reads every open pull request from the other side. It never runs on a
`pull_request` event, where it would answer nothing and lengthen the wait `Fathom review`
performs on this workflow's run.

One rule exists today, in `.github/pull-request/select-board-status.sh`: a pull request that
no longer merges moves the issues it closes from `Ready to merge` to `Conflicts`.
`Ready to merge` says the change is waiting on nothing but the owner pressing the button,
and a conflict is precisely the discovery that it is not. From `Ready to merge` and from
nowhere else — an item still being written, already blocked, or already done says nothing
about whether a conflict is news, and a rule that moved those would report the same conflict
on every push to `main` for as long as it went unresolved.

A rule states the statuses it may act on and the statuses it refuses to overwrite, and the
job passes both to `write-board-status.sh` unread. Those are the two directions of one
question, and which one a rule uses says what it means: a review's verdict describes any
item it finds and names the two statuses it must not erase, while a rule about an approved
change is only true of an approved item and names the one status it is entitled to move.

The waiting is the part with no shorter form. GitHub computes mergeability when it is asked
and not before, so every open pull request reads `UNKNOWN` for the first seconds after a
merge — which is exactly when this runs. The job polls within a bounded window and names, as
a notice, every pull request it never got an answer for; the next push to `main` decides
those. Reading `UNKNOWN` as a conflict would instead move an item on every merge.

Two ceilings bound the sweep and both report what they cut: how many open pull requests one
run reads, ordered by when each was last updated, and how many issues one pull request's
closing references are followed. A pull request or an issue nobody was told about is a board
item silently left behind, which is the same reason the reviewer's own ceilings report.

Nothing here is refused on a fork's pull request, unlike the labelling above: the sweep runs
on a push to this repository with this repository's own token, and it reads a fork's pull
request exactly as it reads any other. What it does need is `BOARD_PROJECT_TOKEN`, the same
classic token carrying the `project` scope that `Fathom review`'s two writes need, and
without it the job says so and ends green.

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

A pull request authored by `dependabot[bot]` is skipped as well, and that one is
about who opened it rather than about what state it is in: such a pull request
arrives published and non-draft, so the check above would let it straight
through. [Dependency update pull requests](#dependency-update-pull-requests)
carries the reasoning and when that author appears at all, next to the questions
a bump is actually read against.

Two things ask for a review anyway, whichever skip refused it:

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
  all. A fork's own pushes never start a review, so a maintainer decides. It is
  also how a dependency bump gets the pass anyway, which is worth doing for a
  major that touches a workflow's inputs and is not worth doing for a version
  number the register already answers.

An automatic review is bounded per pull request: once six automatic reviews by
`fathom-reviewer[bot]` stand on it, the gate refuses and says so in the run log
instead of starting a seventh. Every push to a published branch starts a review,
so a branch pushed to forty times would otherwise be reviewed forty times, and
past some number of passes over the same change another one repeats what it
already said. Six is where that begins: #811 took six rounds, and the last two of
them each moved two findings that were corrections to earlier findings rather
than anything the first pass had missed.

The count is the App's own published reviews rather than a quota read from run
history, because the runs endpoint cannot tell a run that decided not to review —
a draft push, a comment about the workflow — from one that did. Among those it
counts the automatic ones alone. A review somebody asked for neither consumes the
budget a later push draws on nor is refused by it, because asking is the decision
this ceiling exists to make when nobody made it, and a maintainer who has made it
must not find the next push unreviewed as a consequence. So a label or a comment
starts a review however many stand on the pull request already.

Which passes were automatic is not something the reviews endpoint records, so the
submission step writes it: every review it publishes carries an invisible Markdown
comment naming what started the run, and the gate of a later run counts the ones
carrying the automatic marker. Both spellings are declared once at the top of the
workflow, because a marker written differently in the two halves would stop
counting silently and the ceiling would stop holding with nothing red to say so.
A review published before this arrangement carries no marker and counts as
nothing, which resets the budget of any pull request open at the time and is
worth a sentence rather than a migration.

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

Those two events are the whole of what may cancel, and the rule they share is
worth stating in the other direction: **a comment never ends a review already
running.** A push replaces the head and a close removes it, so in both cases what
is running has stopped describing the code that will merge. Everything else —
a comment, a label, a pull request marked ready — asks for a review without
invalidating one, and queues on the same group instead. `cancel-in-progress` is
therefore an expression naming those two events rather than the literal `true`,
because `issue_comment` fires for *any* comment on a pull request, a bot's
included, and the gate that tells a request apart from a passing remark runs
inside the run — after the run has entered the group. Left unconditional, a
notice posted by another workflow ends a review several minutes in and then
declines to start one, which spends the entire cost of a review to publish
nothing. `a_comment_never_cancels_a_review_in_flight` in
`scripts/test-agent-workflow.sh` is what keeps it from quietly reverting.

The workflow reports no status check and is not in the `main` ruleset. It
advises; nothing waits on it.

### What the run is allowed to touch

The branch under review is never checked out and nothing from it is executed.
The workspace holds the base commit, which is code that already merged, and it
is there so the reviewer can read the repository's own contract: root
`AGENTS.md`, the recurring findings in the `review-change` skill, the
architecture draft, and the ADRs, as `main` states them rather than as the branch
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

Claude then runs with `Read`, `Grep`, `Glob`, and `Agent` and nothing else: no
shell, no editor, no writer, no network tool, no MCP tool, and no read access to
`.git`, where the action leaves a token for its own use. It holds no credential it
could use and posts nothing. Its findings are the run's own answer, and the step
after it validates them and submits a single review: `event: COMMENT` when the
answer holds findings, `event: APPROVE` when it holds none.

`Agent` is what spreads the first pass over subagents, described under **What the
reviewer is measured against** below. It widens what the session can *read* in
parallel and nothing else: a subagent inherits the permission rules of the session
that spawned it, so the deny list reaches it unchanged, and what it returns is
model text the main session confirms against the file before it can become a
finding.

The model is named exactly rather than by alias: `claude-sonnet-5` at
`--effort xhigh`. An alias re-points at whatever ships next, and findings are only
comparable across runs when the model that produced them is the one the workflow
names.

Effort decides how much the reviewer works before answering, which is the
difference between a sweep over the whole change and a close reading of the first
few files followed by a shrug. It was `high` — a step down from the `xhigh` Claude
Code applies by default — while the extra depth was unmeasured against a personal
subscription with no per-run ceiling. #811 measured it: six rounds ran on one
change, and two of them reported first-commit code that three earlier passes had
read through, including a file of 68 added lines first named in the fourth review.
A round that finds what an earlier round walked past costs a whole run of
collection, model time, and an author answering it, so the depth is the cheaper
half of that trade.

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

### What a dropped connection costs

Every read the workflow makes goes through
`.github/pull-request/call-github-api.sh`, and so does every read in the two
scripts it shells out to. The helper exists because a single request that never
arrived used to end the run that contained it: on 2026-08-13 the collection step
failed 0.35 seconds into its first call with `invalid character 'u' looking for
beginning of value`, which is `gh` decoding an `upstream connect error or
disconnect/reset before headers` from the proxy in front of the API as if it were
JSON. Nothing about that run was wrong. It published no review, left the pull
request with a red check and no verdict, and waited for somebody to notice and
re-run the job by hand — a review that silently does not happen being the failure
this pipeline is least able to report on itself.

The bound is **four attempts**, the first included, each of them killed after
**thirty seconds**, with the wait doubling from two seconds and carrying up to two
further seconds of jitter so several calls failing at once do not come back in
step. The deadline is what makes the attempt budget a bound at all: `gh` sets none
of its own, so a connection that stalls rather than drops — the same failure in
its other shape — would otherwise hang with the budget never advancing, until the
reviewing job's thirty minutes ran out. That recovers a request that was dropped
and deliberately cannot wait out an outage: a call that exhausts its budget
returns the failure rather than swallowing it, and says how many attempts it made,
because the caller's own failure would otherwise say only that the call did not
succeed.

**What the caller does with that failure is the caller's**, and several reads here
deliberately carry on: the per-pull-request ceiling counts zero and reviews anyway,
the model decision keeps the default model, the settle loop collects at once, and
an issue the run could not fetch is recorded as its number with a null body and
null labels. Each is argued at its own call site, and each is a place where the
retries narrow how often a read degrades without removing it. The head-content
fetch is the one that degrades *silently*: its standard error is discarded because
a path the head does not carry is an ordinary outcome of that loop, so a file
dropped after four failed attempts leaves the same gap as one that was never
there — which the reviewer reads as content too large to collect.

**Two collections call once per record, and a retry budget is per call**, so both
carry a wall-clock window beside their count ceiling: the head content, and the
issues the change closes. Without one, an endpoint that has started failing costs
every remaining record a whole budget — minutes of a job whose thirty are mostly
meant for the model, and a run killed before it starts is the failure the retries
exist to remove. Each window is tested before a call rather than during one, so
what it bounds is the record the loop *starts*: the real ceiling is the window plus
one call's budget. Every one of those ceilings writes its line into
`truncation.txt` and reaches the review body, because a file missing from `head/`
and an issue present as its number alone both say something specific to a reviewer,
and a gap left by a ceiling would otherwise be read as that statement.

What is retried is decided from what the API said rather than from the fact that
something failed. A reply carrying a client status is an answer — the endpoint
does not exist, the token cannot see it — and asking again produces it more
slowly, so it is returned on the first attempt; `408`, `429`, and every `5xx` are
retried, and so is a failure carrying no status at all, which is the shape the
lost run took. That distinction is what keeps the head-content loop, which reads
a path the head does not carry as an ordinary outcome, from spending a budget on
each of sixty files.

**Two calls are excluded by name**, both of them the submission of a review. Every
read may be repeated because asking twice returns the same answer; a submission
creates a record, so a reply lost after the review was already accepted would, on
a retry, publish a second review of the same pass — and since the per-pull-request
ceiling counts submitted reviews, spend a second automatic pass on one push. The
board write is not among them: it writes an option id the same run has already
read, so repeating it converges on the value rather than adding a record.

The helper buffers the whole answer and prints it only once the call has
succeeded, which is what makes `--paginate` safe to retry. Pages stream as they
arrive, so a call that dropped on the third page would otherwise have written the
first two already, and the retry would hand a filter expecting one record per line
a second copy of them.

### The verdict is the run's answer, not a file it might not write

`--json-schema` in `claude_args` is what makes it one. Under that flag the
reviewer's final message is validated against
`.github/fathom-review/findings-schema.json` and published as the step's
`structured_output`, so a run either answers in the shape the submission step
reads or fails naming that as its cause.

The contract it replaces was an instruction to write the findings to a file with
the `Write` tool, and nothing enforced it. Both reviews of #306 on 2026-08-02
ended without the call: the reviewer read the whole change, spent four minutes
and 34 turns on it, and ended cleanly with no permission denial — and the verdict
it reached went nowhere, leaving a red job whose message named a missing file
rather than a cause anybody could act on. The same failure had been measured once
in roughly twenty runs the day before and treated as a flake worth re-running,
which two failures out of two on one pull request is not.

The schema is a committed file rather than a literal in the workflow, because it
is a contract somebody reads; the step that composes the prompt compacts it onto
one line, because `--json-schema` takes the schema itself and not a path to one.
That step also refuses a schema containing an apostrophe — the flag is
single-quoted in `claude_args`, which the action parses with shell-quoting rules,
so an apostrophe would end the quoting and hand the CLI a fragment. Both files
are read from the workspace, so a pull request can supply neither the prompt that
reviews it nor the shape of the verdict.

Removing the write removed the reviewer's last reason to hold a tool that writes.
`Write` is denied in the session settings as well as absent from `--allowedTools`,
so the session stays read-only whichever of the two a later change touches, and
the collected inputs the submission step trusts cannot be rewritten by the
session that reads them.

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
`Read`, `Grep`, `Glob`, and `Agent` and no shell, writer, network tool, or MCP
tool. The purpose of the prohibition — untrusted code never runs with a credential
— is met without avoiding the trigger.

`Agent` is inside that argument rather than an exception to it. A subagent
inherits the spawning session's permission rules, so every deny rule the reviewer
runs under applies to it and it reaches nothing the reviewer cannot; what it adds
is a second reader of the same files, and what it returns is untrusted text on the
same terms as the diff that produced it.

The exception is scoped to this workflow and to that shape. It is revoked by any
change that checks out, builds, restores, or executes the branch under review,
that grants the reviewer a shell, a writer, or a network tool, that gives a
subagent a permission the main session does not hold, that adds
`workflow_dispatch` — a dispatch takes a ref, which would let the branch supply
the job that receives the Claude credential — or that lets a trigger other than a
maintainer's label or comment reach a fork. A second workflow wanting the trigger
does not inherit this reasoning; it argues its own case or uses `pull_request`.

### What bounds the cost

The run spends the repository owner's personal Claude subscription through
`CLAUDE_CODE_OAUTH_TOKEN`, so what limits how often it runs is a design concern
rather than an operational one. Seven things do, and they are listed together
because each closes a different way the bill could grow:

- a draft is never reviewed automatically, so a branch still being written is
  pushed to freely and spends nothing;
- `concurrency` with a conditional `cancel-in-progress` means a superseded head
  never finishes a review, so a rapid series of pushes costs one run rather than
  one per push, and a merge or a close ends the run reading a pull request that
  has stopped being worth a verdict — while a comment, which cannot supersede
  anything, queues rather than throwing a finished review away;
- a fork's own pushes never start a review; a maintainer's label does;
- the comment trigger requires an `OWNER`, `MEMBER`, or `COLLABORATOR` author, so
  nobody outside the project can spend the subscription by typing;
- an automatic review is capped at six per pull request, as described above, and a
  review somebody asked for is outside that count in both directions;
- the model is `claude-sonnet-5` rather than the costlier Opus, which exactly two
  things reach: a review request asking for it by name, and the `security` label
  on the pull request, described below;
- every collected input carries an explicit ceiling, and what a ceiling dropped is
  stated in the review body.

`--effort` was the eighth of these and is no longer one of them. It bought less
than the rounds it caused, which is the trade the paragraph on effort above
records; what remains true is that reading depth is where this workflow spends
deliberately rather than where it economizes.

Moving the run onto a metered API key with a spend limit would replace that set
with one number, and it is deliberately not done: the gate above already stops
anyone outside the project from spending anything, so the remaining cost is the
owner's own pushing, and a second credential to provision, rotate, and register
buys no protection against that.

### What the security label decides

`security` on the pull request — put there by `Apply pull request rules`, from an
issue the change refers to, which need not be one it closes — is the one input that
changes how the review is *conducted* rather than only what it concludes. Two things follow from it, and the
reviewer reads the label off the pull request for both rather than deriving it from
the issues again: which conditions earn which label is that pipeline's decision, and
a second implementation of it could disagree with the label a reader sees.

The prompt applies the security rubric to every file in the change rather than to
the ones whose diff invites it, confirms the weakness the issue names is closed on
every path the change reaches rather than the one the diff illustrates, and says in
the summary that the pass ran and what surface it covered — under an approval as
much as under findings, because a verdict that does not say what was examined is not
evidence a security review happened. It widens what the reviewer reads and nothing
about what it reports: a defect in code the change does not touch is still not a
finding. The absence of the label changes nothing, and an unlabelled change is not
read more loosely for it.

The second is the model. `claude-opus-5` performs that pass, which is the same shape
of exception as a maintainer writing `opus` in a request, taken by the project rather
than by a hand: the change whose defect would be a security defect is the one a
second, costlier opinion most repays. Nothing else escalates.

Because both workflows start from the same event, the reviewer waits for the
labelling run on that head to finish before reading the labels — otherwise a security
change opened or pushed a moment ago would be reviewed with the default model and no
pass, on exactly the changes where that costs most. Only a run still in flight is
waited for, so a comment or a label event, which arrives long after that head was
labelled, waits not at all. The wait is bounded at two minutes and then reads the
labels as they stand: this decides which model reviews rather than whether a review
happens, so a stuck labelling run must not hold a review open or fail one.

### What the reviewer is measured against

The prompt points the reviewer at this repository's own rules rather than at
general review practice: root `AGENTS.md`, the nested `AGENTS.md` files under
`src/`, `tests/`, and `docs/`, the recurring findings in the `review-change`
skill, and the ADRs and architecture draft that govern the area the change touches. A
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

That first pass is spread over subagents rather than read in one sitting.
`files.json` is split into groups of four to six related files — one project, one
feature, one directory, so that a group reads as a piece of work — and each group
goes to a subagent that returns candidates and reaches no verdict, because it saw
a fraction of the change and cannot know what the rest of it answers. One reader
holding thirty files reads the last of them less closely than the first, which is
what #811 cost four rounds; a reader holding six has no twentieth file to tire on.

Three things are never delegated, each being a judgment about the change as a
whole: the reading of `obligations.json`, the two readings of the pull request
body, and the second pass. A subagent's report is untrusted in exactly the way the
diff it read is, so it is a list of places to look, and the main session confirms
every candidate by opening the file itself. Where the subagents are unavailable
the reviewer reads the files directly and says so in its summary — covering the
change matters more than covering it in a particular shape.

### The review states what it read

The reviewer's answer carries a `covered` list naming the entries of `files.json`
it opened, and a step between the review and its submission compares that list
against the collection and writes the difference into the published body, beside
whatever a ceiling dropped. Until it existed, a pass that covered eleven of
twenty-seven files published a verdict shaped exactly like one that covered all of
them, and the only thing that told them apart was a later round finding what the
first walked past.

It reports and never gates. The ledger is the reviewer's own account and nothing
in the job can verify it, so failing a run on it would buy a stricter-sounding
gate that any reviewer closes by naming every file; what it is worth is that the
gap is visible where the verdict is already read. An approval is where that
matters most, since an approval asserts the absence of defects across a whole
change.

Every path it prints comes from `files.json` rather than from the answer, because
this is a path from model text into a published review body: what the step names
is the difference — paths the collection step wrote — and a name in the ledger
that matches nothing in the collection is reported as a count and never quoted.
The list of unread paths is bounded, and the remainder becomes a number.

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

Every issue merging will close is collected, not the first one, and the list comes
from GitHub rather than from a reading of the body:
`.github/pull-request/collect-closing-issues.sh` asks for the pull request's
`closingIssuesReferences`, which is the resolved answer the merge itself will act
on. Collecting fewer than that is how an issue closes on merge with nothing having
read what it asked for, and collecting more is how a review holds a change to a
contract nobody wrote. An issue in another repository is the one thing the script
drops, because it is neither a contract this reviewer can fetch nor an item on this
board.

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
exhausted subscription, a model the plan cannot use, or a reviewer that answered without
conforming to the schema, where the last result message holds what it said instead. The
action does save every message to `$RUNNER_TEMP/claude-execution-output.json`, and it
does so before it refuses a non-conforming answer, so a step after it reads the last
result message's text and prints that and nothing else, flattened to one line, truncated
to 500 characters, and withheld if it matches a credential shape or either credential the
workflow holds. The alternative the action offers is `show_full_output`, which would
print the entire review into the log to recover one sentence.

The submission step then distinguishes two silences. A reviewer that never answered has
already failed and said why, so an empty `structured_output` from a step that failed is
reported as a notice and the run keeps the reviewer's own error as its only cause. An empty
one from a step that *succeeded* is unreachable while the action keeps failing on a missing
structured answer, and it fails there anyway: that branch is what stops the workflow
depending on a promise it does not own, because a later action version that renamed the
output or stopped failing on it would otherwise leave every review posting nothing under a
green job — a pipeline that has silently stopped reviewing.

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

It also lays each finding out. The reviewer answers with separate fields —
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

That branch consults the reviewer's own exit status a second time. An empty
findings array means two different things: from a run that finished it means
nothing was found, and from one that answered and then failed anyway it means the
search never completed. Findings from such a run are still posted, because a
defect it did name is real. An approval is not, because it asserts the absence of
what the run stopped looking for, so an unfinished run that found nothing reports
a notice and publishes no verdict.

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

### What the verdict moves on the board

The workflow writes the roadmap board's `Status` field twice, on every issue the
pull request's body closes and on nothing else — because closing an issue is what
makes a review of the pull request a statement about that issue's lifecycle.

The first write happens as the review starts, beside the reviewing job rather
than before it, and it writes `In review`. A review takes minutes, and without it
the column says whatever the last event left there for that whole time — usually
`In progress`, the state the work was in before the pull request existed. Nothing
in the review depends on that write, which is why it runs in parallel: a project
API failure must not delay or skip a review.

The last write is the verdict: `Changes requested` where the review carried
findings, `Ready to merge` where it approved. A run that publishes no verdict
writes nothing and leaves `In review` standing, where a reader sees that a review
was asked for and produced nothing.

Both writes are one script, `write-board-status.sh`, and so is the conflict rule
in `Apply pull request rules`. The walk is identical — collect what the pull
request closes, resolve the field and the option by name, find the item on this
board, mutate it — and the callers differ only in the value they write and in the
statuses they may write it over. That authority is two arguments rather than one,
because it is one question asked in two directions: the statuses a write refuses
to overwrite, and the statuses it may act on and no others. Both reviews name the
same preserved pair and no required list, because a verdict describes whatever
item it finds; the conflict rule names one required status and no preserved list,
because it is only true of an item that is currently approved.

It exists because that half of the field had no writer at all. The board's
built-in `Code changes requested` and `Code review approved` workflows fire on a
review's *state*, and the two states they read are produced by nobody here:
`REQUEST_CHANGES` is refused for the reason the section above gives, and GitHub
does not let the author of a pull request approve or request changes on their own
— which is every pull request in this repository. So the column that says whether
a change is waiting on the owner's merge or on the agent answering its findings
was decided by a run whose conclusion reached the board through no mechanism.

The verdict is a job output rather than a second reading of the pull request. The
submission step states it in the one branch that posted a review, so a run that
ended for any of the other reasons it returns on moves nothing, and a verdict
that exists always names a review a reader can go and look at. The closing
issues come from `collect-closing-issues.sh`, the same script the collection step
and `Apply pull request rules` run, so which issues a merge closes is one answer
GitHub gives rather than three derivations of it that drift.

Two statuses are never written over, by either end of the review. `Done` is the
merge and the close, so a verdict arriving after one must not drag a finished
item back into review, and a review starting on one must not either. `Blocked` is
the one status a hand writes, and it says the issue waits on something outside the
project — a question neither a verdict nor a review in flight answers, so neither
gets to erase the answer.

Each write is a job of its own for the credential. Writing a field on a
user-owned project needs a classic token with the `project` scope: no GitHub App
permission covers one and no fine-grained token carries the scope, which
[Issue tracking and the roadmap board](issue-tracking.md#status-transitions)
records along with what that costs. Both jobs check out only the base commit, run
no model, and receive their input as a string, so the account-wide credential
never shares a runner with the reviewer session — the same separation that keeps
the App's token in the one step that makes no model call. Where the secret is
absent a job says so and ends green: this workflow gates nothing, and a missing
credential must not turn a review red. A pull request that closes no issue ends
the same way, with a notice, because a change opened without a contract is an
ordinary shape. An issue that is not on the board ends green too, but as a
warning: every issue this project opens is placed there by a built-in workflow,
so one that is missing has something wrong with it rather than nothing.

`scripts/test-agent-workflow.sh` runs both steps against a fake `gh` the way it
runs the gate, the settle loop, and the submission: it asserts which option each
verdict writes, that the announcement writes `In review`, that both leave `Done`
and `Blocked` alone, and that a run without the token writes nothing.

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

The board write is a third credential, `BOARD_PROJECT_TOKEN`, and it is optional:
without it the review is published exactly as before and only the `Status` write
is skipped. It is a **classic** personal access token with the `project` scope and
no other, created at <https://github.com/settings/tokens>, because that is the
only kind of credential that reaches a user-owned project — the App above cannot
be granted one and a fine-grained token has no such scope. Give it an expiry and
replace it there when it lapses: a token the board refuses fails that job and
says so, rather than leaving the board quietly behind. Nothing else reads it,
and it is held by a job that runs no model and checks out nothing from the branch
under review.

## Dependency update pull requests

Nothing here opens one on a schedule. Actions are referenced by major tag, so an
upstream patch arrives without a commit, and a major arrives when somebody looks;
`docs/operations/local-development.md` records how that looking is done and why an
updater is not what does it. A bump is therefore ordinary task-shaped work — a
branch, an issue, and the skills — rather than a pull request that appears.

One author can still open one without a task behind it. `Dependabot security
updates` is a repository setting, off today and one click from not being, and an
advisory the owner decides to act on that way arrives as a pull request from
`dependabot[bot]`. The paragraphs below are about that case, and the four
questions are what answers a version bump whoever wrote it.

**No skill runs on such a pull request and none should.** `start-task` opens an
issue and `finish-change` writes a board field, and neither has anything to do
there: the change is already written, it closes no issue, and it belongs to no
roadmap item. What it needs is a reading, and the reading is the maintainer's.

**`Fathom review` does not run on one either**, and the four questions below are
why. Three of them are answered somewhere the diff does not reach — the upstream
release notes, the action's ownership, the register — and the fourth is answered
by the checks. A reviewer given the diff sees a version number in a `uses:` line
and can confirm none of them, so a run would spend subscription usage restating
what the tag already says, again on every rebase the updater performs. The gate
refuses by author, before the draft and fork checks that would otherwise let a
published bump straight through; the `fathom-review` label still reaches one,
which is what a major touching a workflow's inputs is worth.

Four questions answer a version bump, and the first is the one a version number
on its own makes easy to skip.

1. **Is the new revision one this repository would have chosen?** Read the
   upstream release notes, not just the version numbers. A major is where this
   matters: it can rename an input, drop a runner, or refuse something the
   previous one allowed, and the diff shows the tag moving rather than what
   moved with it.
2. **Does the owner stay inside the reviewed set?** An update never introduces a
   new owner, and `every_external_action_names_an_approved_owner` in
   `scripts/test-agent-workflow.sh` refuses one on this pull request as on any
   other. A version that changed what an action *is* — a transfer, a rename, a
   fork under the same name — is the case that contract cannot see and a reader
   can.
3. **Does `THIRD_PARTY_LICENSES.md` still describe the truth?** Its continuous
   integration rows name each action, the version its reference resolves to, and
   the argument for allowing it. A bump moves what those rows record, so the
   register is updated in the same change — which is `$check-docs-licenses`'s
   rule, and the reason a bump is worth a task rather than a click.
4. **Do the checks pass on their own terms?** `Required CI` and
   `Protected paths` are required on this pull request exactly as on any other,
   and `Protected paths` passes only because the exception it carries recognises
   this author for `.github/workflows/` alone. A red one is a red one; nothing
   here is exempt and nothing auto-merges.

The pull request is merged the same way everything else is: a code owner
approves it, and the owner merges it. If a bump has to be declined, close the
pull request and say why in it. Closing settles that version and not the
dependency, and the comment is where the next reader finds out the version was
considered rather than missed — which matters more when nothing will raise it a
second time.

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

- **`specs/` — kept in place.** It holds the architecture draft, which root
  `AGENTS.md` names as required context and which states what MailFathom is being
  built into. A page under `docs/` is the statement of fact beside it, and the
  roadmap board decomposes the gap between the two into issues.
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
violations, and the published pull request body references its issue.

`gh pr edit` fails against this repository with a Projects-classic GraphQL error
and silently drops the edit, so correct a missing issue reference through
`gh api repos/<owner>/<repo>/pulls/<number> -X PATCH -f body=...`.
