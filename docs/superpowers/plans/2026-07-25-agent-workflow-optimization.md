# Agent Workflow Optimization Implementation Plan

> **Status:** Completed on 2026-07-25. This document is a historical implementation record; its unchecked task steps are not outstanding work.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a fast, deterministic, cross-agent MailMcp development workflow with layered instructions, shared repository skills, and one complete local verification command.

**Architecture:** Keep deterministic Git and .NET command sequences in four small Bash scripts under root `scripts/`. Keep task routing and review judgment in four concise skills under `.agents/skills/`, expose the entire directory to Claude Code through one relative symlink, and move conditional guidance from the root instruction file to path-scoped instruction files.

**Tech Stack:** Bash 5, Git, .NET SDK 10, Microsoft Testing Platform v2, Agent Skills `SKILL.md`, Codex CLI, Claude Code 2.1.219.

## Global Constraints

- Base the branch directly on the fetched `origin/main`.
- Add no third-party dependency, service, container image, generated asset, or copied external code.
- Keep `.agents/skills/` as the only skill source; `.claude/skills` is a relative symlink to it.
- Keep skill frontmatter limited to `name` and `description`.
- Make `check-docs-licenses` a required step of `finish-change`.
- Do not create or modify an ADR.
- Do not add a co-author trailer.
- Publish only a draft pull request.

---

### Task 1: Specify and test the deterministic script contracts

**Files:**
- Create: `scripts/test-agent-workflow.sh`

**Interfaces:**
- Consumes: Bash, Git, a temporary directory, and `PATH`.
- Produces: executable contract tests for `inspect-workspace.sh`, `verify-fast.sh`, and `verify-full.sh`.

- [ ] **Step 1: Write failing command-sequence tests**

Create a test runner that:

- creates an isolated temporary Git repository;
- installs a fake `dotnet` in a temporary `PATH`;
- records every fake .NET invocation;
- expects fast verification to call restore, Release build with
  `--no-restore`, and tests with `--no-build`;
- expects full verification to call tool restore, solution restore, Release
  build, the coverage target, and format verification with `--no-restore`;
- asserts the full path does not also call plain `dotnet test`;
- configures the fake to fail on a selected invocation and asserts no later
  command runs;
- asserts workspace inspection does not change `HEAD`, refs, index, or working
  tree.

- [ ] **Step 2: Run the tests and verify RED**

Run:

```bash
bash scripts/test-agent-workflow.sh
```

Expected: nonzero exit because the three production scripts do not exist.

- [ ] **Step 3: Commit the failing tests**

```bash
git add scripts/test-agent-workflow.sh
git commit -m "test: define agent workflow script contracts"
```

### Task 2: Implement the workspace and verification scripts

**Files:**
- Create: `scripts/inspect-workspace.sh`
- Create: `scripts/verify-fast.sh`
- Create: `scripts/verify-full.sh`
- Modify: `scripts/test-agent-workflow.sh`

**Interfaces:**
- Consumes: a Git worktree containing `MailMcp.slnx`, `origin/main` when available, and .NET SDK 10.
- Produces: three zero-argument executable Bash entrypoints.

- [ ] **Step 1: Implement shared root resolution in each entrypoint**

Use:

```bash
repository_root="$(git rev-parse --show-toplevel)"
cd "$repository_root"
```

Fail with a concise diagnostic when outside a Git worktree.

- [ ] **Step 2: Implement read-only workspace inspection**

Report labeled values for:

```text
Repository
Branch
Worktree
Upstream
Contains origin/main
Working tree
Registered worktrees
.NET SDK
```

Treat missing upstream, missing `origin/main`, detached `HEAD`, dirty state,
and a branch that does not contain `origin/main` as visible warnings. Do not
fetch or mutate Git.

- [ ] **Step 3: Implement fast verification**

Run exactly:

```bash
dotnet restore MailMcp.slnx
dotnet build MailMcp.slnx --configuration Release --no-restore
dotnet test --solution MailMcp.slnx --configuration Release --no-build
```

- [ ] **Step 4: Implement full verification**

Run exactly:

```bash
dotnet tool restore
dotnet restore MailMcp.slnx
dotnet build MailMcp.slnx --configuration Release --no-restore
dotnet msbuild .config/CodeCoverage.proj -t:Collect -p:Configuration=Release
dotnet format MailMcp.slnx --no-restore --verify-no-changes --verbosity diagnostic
git diff --check origin/main...HEAD
git diff --cached --check
git diff --check
```

The coverage target is the test execution for the full path; do not add a
second plain `dotnet test`.

- [ ] **Step 5: Run the script tests and verify GREEN**

Run:

```bash
bash scripts/test-agent-workflow.sh
```

Expected: all script-contract tests pass.

- [ ] **Step 6: Run shell syntax checks**

Run:

```bash
bash -n scripts/*.sh
```

Expected: zero exit.

- [ ] **Step 7: Commit the scripts**

```bash
git add scripts
git commit -m "build: add agent workflow verification scripts"
```

### Task 3: Create and validate `start-task`

**Files:**
- Create: `.agents/skills/start-task/SKILL.md`
- Create: `.agents/skills/start-task/agents/openai.yaml`
- Test: `/home/krzysiek/.codex/skills/.system/skill-creator/scripts/quick_validate.py`

**Interfaces:**
- Consumes: `scripts/inspect-workspace.sh`, `specs/README.md`, `docs/README.md`, and `docs/decisions/README.md`.
- Produces: a task-start brief containing workspace state, selected specification, applicable ADRs, implemented-behavior documentation, assumptions, and next verification command.

- [ ] **Step 1: Run baseline scenarios without the skill**

Use fresh subagent contexts for:

1. starting a persistence change from a stale detached worktree;
2. starting a documentation-only change;
3. starting a package upgrade without checking license policy.

Record whether the baseline misses workspace state, routing, or licensing.

- [ ] **Step 2: Initialize the skill**

Run `init_skill.py` with the name `start-task`, output path
`.agents/skills`, and generated interface metadata. Do not create unused
resource directories.

- [ ] **Step 3: Write the minimal task-start workflow**

Require:

1. run workspace inspection;
2. fetch `origin/main` and rerun workspace inspection;
3. stop before edits unless the branch is `agent/*`, the worktree is linked,
   and the branch contains the freshly fetched base;
4. select the applicable numbered specification or state that the task is
   maintenance outside the roadmap;
5. read relevant ADRs before architectural changes;
6. identify implemented-behavior documentation that may need updates;
7. produce the fixed brief shape defined in the interface block.

- [ ] **Step 4: Validate and forward-test**

Run the official validator and repeat the three scenarios with the skill.
Expected: the skill blocks unsafe starts and produces the complete brief.

- [ ] **Step 5: Commit the skill**

```bash
git add .agents/skills/start-task
git commit -m "feat: add task-start skill"
```

### Task 4: Create and validate `review-change`

**Files:**
- Create: `.agents/skills/review-change/SKILL.md`
- Create: `.agents/skills/review-change/agents/openai.yaml`

**Interfaces:**
- Consumes: the current Git diff, path-scoped instructions, applicable ADRs, and `scripts/verify-fast.sh`.
- Produces: findings ordered by severity with file evidence, followed by verification status and residual risks.

- [ ] **Step 1: Run baseline review scenarios without the skill**

Use fresh contexts with diffs that contain:

1. an Infrastructure type leaking into Application;
2. an IMAP content fetch without an explicit read-only safety assertion;
3. an unrelated refactor mixed into a focused change.

Record omissions and wrong output shape.

- [ ] **Step 2: Initialize and write the minimal review skill**

Require diff scoping, applicable nested instruction loading, ADR consistency,
architecture boundaries, domain naming, privacy/logging, IMAP `\Seen` safety,
test policy, and `verify-fast.sh`. Require findings-first output and prohibit
claiming success when verification did not run.

- [ ] **Step 3: Validate and forward-test**

Run the official validator and repeat all baseline scenarios. Expected: every
seeded issue is reported with severity and file evidence.

- [ ] **Step 4: Commit the skill**

```bash
git add .agents/skills/review-change
git commit -m "feat: add change-review skill"
```

### Task 5: Create and validate `check-docs-licenses`

**Files:**
- Create: `.agents/skills/check-docs-licenses/SKILL.md`
- Create: `.agents/skills/check-docs-licenses/agents/openai.yaml`

**Interfaces:**
- Consumes: the current Git diff, `docs/`, `specs/`, `LICENSES.md`, dependency declarations, tool manifests, container declarations, and current official upstream license sources when licensing impact exists.
- Produces: exactly two verdict blocks, `Docs: pass|n/a|fail` and `Licenses: pass|n/a|fail`, each with evidence and required actions.

- [ ] **Step 1: Run baseline completion scenarios without the skill**

Use fresh contexts with:

1. behavior changed without updating implemented-behavior docs;
2. a package version changed without updating `LICENSES.md`;
3. a docs-only typo with no licensing impact;
4. a container image introduced with an SDK license cited instead of the image
   terms.

Record incorrect `n/a` decisions, missing official-source checks, and ADR
scope violations.

- [ ] **Step 2: Initialize and write the mandatory check**

Define the documentation classification:

- behavior, configuration, security, operations, failure mode, or command
  changes require a matching `docs/` review;
- future intent belongs in `specs/`, implemented behavior belongs in `docs/`;
- ADR edits require explicit owner approval.

Define the licensing classification:

- packages, tools, services, provider APIs, protocols with bundled SDKs,
  container images, models, generated assets, and copied samples require
  review;
- verify the current official upstream license and commercial-use constraints;
- require exact version, license expression, upstream URL, and NOTICE handling
  in `LICENSES.md` where applicable;
- uncertainty is `fail`.

- [ ] **Step 3: Validate and forward-test**

Run the official validator and repeat all four scenarios. Expected: both
verdict blocks are always present and each seeded licensing or documentation
gap fails.

- [ ] **Step 4: Commit the skill**

```bash
git add .agents/skills/check-docs-licenses
git commit -m "feat: add documentation and license check skill"
```

### Task 6: Create and validate `finish-change`

**Files:**
- Create: `.agents/skills/finish-change/SKILL.md`
- Create: `.agents/skills/finish-change/agents/openai.yaml`

**Interfaces:**
- Consumes: `check-docs-licenses`, `scripts/verify-full.sh`, the final diff, and GitHub draft pull-request capability.
- Produces: a completion report containing docs verdict, licenses verdict, full verification evidence, diff review, commit, push, and draft PR URL.

- [ ] **Step 1: Run baseline pressure scenarios without the skill**

Use fresh contexts where:

1. tests passed but coverage and format were not run;
2. the user asks to hurry and skip documentation;
3. a dependency changed and `LICENSES.md` appears plausible but lacks current
   upstream verification;
4. the branch is ready but the PR would default to ready-for-review.

Record skipped gates and rationalizations.

- [ ] **Step 2: Initialize and write the completion skill**

Make `check-docs-licenses` an explicit required sub-skill. Require the
full verification script after docs and license gaps are fixed, final diff
inspection, focused staging, no co-author trailers, push of an `agent/*`
branch, and creation of a draft PR only.

- [ ] **Step 3: Validate and forward-test**

Run the official validator and repeat all pressure scenarios. Expected: the
skill refuses completion until every required verdict and command succeeds.

- [ ] **Step 4: Commit the skill**

```bash
git add .agents/skills/finish-change
git commit -m "feat: add completion skill"
```

### Task 7: Share skills with Claude Code and layer repository instructions

**Files:**
- Delete: `.agents/.gitkeep`
- Create: `.claude/skills` as a symlink to `../.agents/skills`
- Modify: `AGENTS.md`
- Create: `src/AGENTS.md`
- Create: `src/CLAUDE.md`
- Create: `src/Infrastructure/AGENTS.md`
- Create: `src/Infrastructure/CLAUDE.md`
- Create: `tests/AGENTS.md`
- Create: `tests/CLAUDE.md`
- Create: `docs/AGENTS.md`
- Create: `docs/CLAUDE.md`

**Interfaces:**
- Consumes: Codex `.agents/skills` discovery, Claude Code `.claude/skills`
  discovery, root `CLAUDE.md -> @AGENTS.md`.
- Produces: one canonical skill tree and path-scoped guidance for both agents.

- [ ] **Step 1: Create the whole-directory relative symlink**

Create:

```text
.claude/skills -> ../.agents/skills
```

Verify with `readlink` and `test -f .claude/skills/start-task/SKILL.md`.

- [ ] **Step 2: Move conditional rules to scoped instruction files**

Keep root critical rules, licensing, architecture, privacy, and workflow
entrypoints. Move C# rules to `src/`, EF Core and MailKit rules to
`src/Infrastructure/`, tests and coverage to `tests/`, and documentation plus
ADR rules to `docs/`. Preserve every existing normative rule exactly once at
the narrowest safe scope.

- [ ] **Step 3: Add Claude imports**

Each new scoped `CLAUDE.md` contains:

```text
@AGENTS.md
```

- [ ] **Step 4: Verify discovery and instruction size**

Run:

```bash
find .agents/skills -mindepth 2 -maxdepth 2 -name SKILL.md -print | sort
find -L .claude/skills -mindepth 2 -maxdepth 2 -name SKILL.md -print | sort
wc -l -c AGENTS.md src/AGENTS.md src/Infrastructure/AGENTS.md tests/AGENTS.md docs/AGENTS.md
```

Expected: both discovery commands expose the same four skills and root
`AGENTS.md` is below 200 lines.

- [ ] **Step 5: Commit shared discovery and scoped guidance**

```bash
git add .agents .claude AGENTS.md src/AGENTS.md src/CLAUDE.md src/Infrastructure/AGENTS.md src/Infrastructure/CLAUDE.md tests/AGENTS.md tests/CLAUDE.md docs/AGENTS.md docs/CLAUDE.md
git commit -m "docs: scope agent guidance by workflow"
```

### Task 8: Document the implemented workflow and complete verification

**Files:**
- Create: `docs/operations/agent-workflow.md`
- Modify: `docs/operations/local-development.md`
- Modify: `docs/README.md`
- Modify: `AGENTS.md`
- Modify: `docs/superpowers/specs/2026-07-25-agent-workflow-optimization-design.md`
- Modify: `docs/superpowers/plans/2026-07-25-agent-workflow-optimization.md`

**Interfaces:**
- Consumes: implemented scripts, skills, symlink, and scoped instructions.
- Produces: durable operational documentation and a verified draft PR.

- [ ] **Step 1: Document actual behavior**

Document:

- when to run workspace inspection, fast verification, and full verification;
- that full verification uses the coverage target as its test run;
- skill names and trigger situations;
- canonical `.agents/skills` ownership and the Claude symlink;
- read-only and fail-fast behavior;
- docs and license verdict requirements;
- common failures including detached/stale Git state, missing .NET tools,
  failed coverage, and broken symlink discovery.

- [ ] **Step 2: Run static workflow checks**

Run:

```bash
bash scripts/test-agent-workflow.sh
bash -n scripts/*.sh
for skill in .agents/skills/*; do
  python3 /home/krzysiek/.codex/skills/.system/skill-creator/scripts/quick_validate.py "$skill"
done
```

Expected: all checks pass.

- [ ] **Step 3: Run the real full verification**

Run:

```bash
bash scripts/verify-full.sh
```

Expected: tool restore, solution restore, Release build, all unit tests,
aggregate coverage of at least 85%, formatting verification, and
branch-range, staged, and unstaged whitespace checks all pass.

- [ ] **Step 4: Inspect the final diff**

Run:

```bash
git diff --check origin/main...HEAD
git diff --cached --check
git diff --check
git status --short
git diff --stat origin/main...HEAD
git diff origin/main...HEAD
```

Check for secrets, generated files, unrelated edits, lost instructions,
dependency changes, stale documentation, and broken symlinks.

- [ ] **Step 5: Commit documentation and final corrections**

```bash
git add AGENTS.md docs
git commit -m "docs: document shared agent workflow"
```

- [ ] **Step 6: Push and create a draft pull request**

Push `agent/optimize-agent-workflow`, create a draft PR targeting `main`, and
include:

- the shared Codex/Claude skill model;
- the three workflow scripts;
- the mandatory documentation and licensing gate;
- the scoped instruction reduction;
- complete verification evidence.
