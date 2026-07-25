# Workflow Script Relocation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move manually usable workflow commands into a flat root `scripts/` directory and eliminate the unnecessary test-helper file.

**Architecture:** Keep four executable Bash files directly under `scripts/`. The three operational entrypoints retain their behavior, while `test-agent-workflow.sh` serves both as the contract-test runner and, through a temporary `dotnet` symlink, as the fake CLI used by those tests.

**Tech Stack:** Bash 5, Git, .NET SDK 10, Agent Skills `SKILL.md`.

## Global Constraints

- A script used manually belongs directly in root `scripts/`.
- Do not create deeper script or test subdirectories.
- Keep exactly four workflow files: `inspect-workspace.sh`, `verify-fast.sh`, `verify-full.sh`, and `test-agent-workflow.sh`.
- Preserve all current command sequences, fail-fast behavior, and read-only inspection guarantees.
- Update every skill, instruction, plan, design, and operations reference in the same change.
- Add no dependency or external component.
- Add no co-author trailer and keep pull request #27 as a draft.

---

### Task 1: Define the flat-layout contract

**Files:**
- Modify: `eng/agent-workflow/tests/run.sh`

**Interfaces:**
- Consumes: the current repository root and existing workflow contract runner.
- Produces: `workflow_scripts_use_flat_manual_layout`, which requires the four executable root scripts and rejects the legacy directory.

- [ ] **Step 1: Add the failing layout test**

Add:

```bash
workflow_scripts_use_flat_manual_layout() {
  [[ -x "$source_repository_root/scripts/inspect-workspace.sh" ]]
  [[ -x "$source_repository_root/scripts/verify-fast.sh" ]]
  [[ -x "$source_repository_root/scripts/verify-full.sh" ]]
  [[ -x "$source_repository_root/scripts/test-agent-workflow.sh" ]]
  [[ ! -e "$source_repository_root/eng/agent-workflow" ]]
}
```

Resolve `source_repository_root` independently from the temporary fixture and invoke the test through `run_test`.

- [ ] **Step 2: Run the contract suite and verify RED**

Run:

```bash
bash eng/agent-workflow/tests/run.sh
```

Expected: six existing tests pass and `workflow_scripts_use_flat_manual_layout` fails because root `scripts/` does not exist.

- [ ] **Step 3: Commit the failing contract**

```bash
git add eng/agent-workflow/tests/run.sh
git commit -m "test: require flat workflow script layout"
```

### Task 2: Move and consolidate the scripts

**Files:**
- Create from move: `scripts/inspect-workspace.sh`
- Create from move: `scripts/verify-fast.sh`
- Create from move: `scripts/verify-full.sh`
- Create from move and modification: `scripts/test-agent-workflow.sh`
- Delete: `eng/agent-workflow/tests/fake-dotnet.sh`
- Delete after moves: `eng/agent-workflow/`

**Interfaces:**
- Consumes: zero-argument operational entrypoints and environment variables `FAKE_DOTNET_LOG` and `FAKE_DOTNET_FAIL_MATCH` in test mode.
- Produces: four executable root scripts with unchanged operational behavior.

- [ ] **Step 1: Move the three operational entrypoints**

Move the files without changing their executable modes:

```bash
mkdir scripts
git mv eng/agent-workflow/inspect-workspace.sh scripts/inspect-workspace.sh
git mv eng/agent-workflow/verify-fast.sh scripts/verify-fast.sh
git mv eng/agent-workflow/verify-full.sh scripts/verify-full.sh
```

- [ ] **Step 2: Move the runner and embed fake-dotnet mode**

Move the runner to `scripts/test-agent-workflow.sh`. At the beginning, before test setup, detect invocation through a temporary `dotnet` symlink:

```bash
if [[ "$(basename "$0")" == 'dotnet' ]]; then
  : "${FAKE_DOTNET_LOG:?FAKE_DOTNET_LOG must identify the invocation log}"
  printf '%s\n' "$*" >> "$FAKE_DOTNET_LOG"

  if [[ -n "${FAKE_DOTNET_FAIL_MATCH:-}" && "$*" == *"$FAKE_DOTNET_FAIL_MATCH"* ]]; then
    exit 19
  fi

  if [[ "$*" == '--version' ]]; then
    printf '10.0.110\n'
  fi

  exit 0
fi
```

Resolve `scripts_directory` and `source_repository_root`, and replace the fake-file copy with:

```bash
ln -s "$scripts_directory/test-agent-workflow.sh" "$fake_bin_directory/dotnet"
```

Delete `eng/agent-workflow/tests/fake-dotnet.sh`.

- [ ] **Step 3: Run the suite and verify GREEN**

Run:

```bash
bash scripts/test-agent-workflow.sh
bash -n scripts/*.sh
```

Expected: seven tests pass, zero fail, and all four scripts pass syntax validation.

- [ ] **Step 4: Commit the move**

```bash
git add -A scripts eng/agent-workflow
git commit -m "refactor: flatten workflow scripts"
```

### Task 3: Update consumers, documentation, and the draft PR

**Files:**
- Modify: `.agents/skills/start-task/SKILL.md`
- Modify: `.agents/skills/review-change/SKILL.md`
- Modify: `.agents/skills/finish-change/SKILL.md`
- Modify: `AGENTS.md`
- Modify: `docs/operations/agent-workflow.md`
- Modify: `docs/operations/local-development.md`
- Modify: `docs/superpowers/specs/2026-07-25-agent-workflow-optimization-design.md`
- Modify: `docs/superpowers/plans/2026-07-25-agent-workflow-optimization.md`
- Modify: `docs/superpowers/plans/2026-07-25-workflow-script-relocation.md`

**Interfaces:**
- Consumes: the four paths under `scripts/`.
- Produces: no remaining `eng/agent-workflow` references and current manual/agent instructions.

- [ ] **Step 1: Baseline-test skill path retrieval**

Use fresh read-only contexts without the edited skills for task start, change review, and change finish. Record whether they consistently discover the new root paths.

- [ ] **Step 2: Replace all operational references**

Use these exact mappings:

```text
eng/agent-workflow/inspect-workspace.sh -> scripts/inspect-workspace.sh
eng/agent-workflow/verify-fast.sh -> scripts/verify-fast.sh
eng/agent-workflow/verify-full.sh -> scripts/verify-full.sh
eng/agent-workflow/tests/run.sh -> scripts/test-agent-workflow.sh
```

Update static syntax commands to `bash -n scripts/*.sh`. Remove references to the deleted fake helper.

- [ ] **Step 3: Validate skills and forward-test retrieval**

Run the official validator against all four skills and repeat the three path-retrieval scenarios. Expected: every applicable skill selects the matching `scripts/*.sh` path.

- [ ] **Step 4: Run required gates**

Run:

```bash
bash scripts/test-agent-workflow.sh
bash -n scripts/*.sh
bash scripts/verify-full.sh
```

Expected: seven script contracts pass; tool and solution restore, Release build, all unit tests, at least 85% aggregate coverage, formatting, and all diff checks pass.

- [ ] **Step 5: Run documentation and license gate**

Expected:

```text
Docs: pass
Licenses: n/a
```

No dependency, service, container image, model, asset, copied code, or tool version changes.

- [ ] **Step 6: Commit final references**

```bash
git add .agents AGENTS.md docs scripts
git commit -m "docs: point workflow to root scripts"
```

- [ ] **Step 7: Review, push, and update draft PR**

Inspect `origin/main...HEAD`, verify no secrets, generated files, unrelated edits, or co-author trailers, push `agent/optimize-agent-workflow`, and update draft pull request #27 with the flat script layout and seven-test result.
