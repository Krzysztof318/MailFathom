# Agent Workflow Optimization Design

> **Partly superseded on 2026-07-28.** `scripts/verify-full.sh` now fetches
> `origin main` into `refs/remotes/origin/main` and rejects a branch that does
> not contain that base, before the workflow contract suite. The final-gate step
> list and the "no hidden mutation" statement below therefore no longer describe
> the script; see [Agent workflow](../../operations/agent-workflow.md) for the
> current contract.
>
> **Further superseded on 2026-07-28.** `scripts/verify-fast.sh` now ends with
> two `dotnet format` passes restricted to the C# files the branch changed: a
> repairing pass and a `--verify-no-changes` pass. The inner-loop step list below
> omits formatting entirely, and the fast loop does rewrite working-tree source
> files, which the "no hidden mutation" statement did not anticipate.

## Goal

Reduce repeated repository discovery, verification mistakes, and always-on
instruction cost for Codex and Claude Code without restoring the removed
generic Superpowers snapshot.

## Scope

The change adds:

- three repository-owned Bash entrypoints for workspace inspection, iterative
  verification, and final verification, plus one contract-test runner;
- four focused repository skills for starting, reviewing, checking documentation
  and licensing, and finishing a change;
- one canonical `.agents/skills/` directory exposed to Claude Code through the
  repository symlink `.claude/skills -> ../.agents/skills`;
- path-scoped `AGENTS.md` guidance with matching `CLAUDE.md` import files;
- operational documentation for the shared workflow.

The change adds no package, service, container image, copied code, or runtime
dependency.

## Design

### Deterministic scripts

Scripts follow a placement rule based on their consumers:

- a script used by one skill only belongs in that skill's `scripts/`;
- a script shared only by skills belongs in `.agents/scripts/`;
- a script intended for manual use belongs directly in root `scripts/`.

The current entrypoints are documented for manual use, so the repository keeps
the flat files `scripts/inspect-workspace.sh`, `scripts/verify-fast.sh`,
`scripts/verify-full.sh`, and `scripts/test-agent-workflow.sh`. No deeper
workflow or test subdirectories are introduced. The test runner exposes its
fake `dotnet` behavior internally when invoked through the temporary test
fixture, avoiding a separate helper file.

`scripts/inspect-workspace.sh` is read-only. It reports repository
root, branch or detached state, linked-worktree state, upstream, whether the
current commit contains the locally known `origin/main`, dirty paths, registered
worktree count, and installed .NET SDK. It does not fetch, switch branches,
create worktrees, or remove stale worktrees.

`scripts/verify-fast.sh` provides the repeatable inner loop:

1. restore the solution;
2. build `MailMcp.slnx` in `Release`;
3. run the complete unit-test suite without rebuilding.

`scripts/verify-full.sh` is the final gate:

1. reject untracked files that focused staging has not placed in the index;
2. run `scripts/test-agent-workflow.sh`;
3. restore repository-local tools;
4. restore the solution;
5. build `MailMcp.slnx` in `Release`;
6. run the existing coverage target, which executes the complete unit-test
   suite and enforces 85% aggregate line coverage;
7. verify formatting without another restore;
8. check the branch range, staged diff, and unstaged diff for whitespace
   errors.

All scripts resolve the repository root through Git, use `set -euo pipefail`,
stop on the first failure, and add no hidden mutation beyond ordinary .NET
restore/build/test artifacts already ignored by Git.

### Repository skills

Each skill remains concise and delegates deterministic work to the scripts:

- `start-task` requires a clean workspace or a complete
  `git status --short --untracked-files=all` inventory with a user-approved
  preservation plan, then routes the agent to the applicable specification,
  implemented-behavior documentation, and ADRs before file changes.
- `review-change` reviews the current diff against architectural
  boundaries, naming, privacy, IMAP safety, test policy, and change scope.
- `check-docs-licenses` is a required completion check. It classifies
  documentation impact and licensing impact independently and returns
  `Docs: pass|n/a|fail` and `Licenses: pass|n/a|fail` with evidence. Dependency,
  service, protocol SDK, container image, generated asset, and externally
  sourced code changes require current official license verification and a
  matching `LICENSES.md` entry.
- `finish-change` stages and inspects only the task files, requires
  `check-docs-licenses`, runs the full verification script, inspects the final
  diff, and prepares a focused commit, push, and draft pull request without
  co-author trailers.

Skills contain judgment and routing. Scripts contain deterministic command
sequences. `AGENTS.md` contains only rules that apply whenever its directory is
in scope.

### Layered instructions

The root `AGENTS.md` keeps repository identity, critical Git and pull-request
rules, global architecture boundaries, enterprise privacy invariants,
third-party licensing rules, and pointers to the workflow commands.

Detailed rules move to the closest applicable directories:

- root `AGENTS.md` for .NET and C# conventions shared by production and test
  code;
- `src/AGENTS.md` for application design and dependency injection;
- `src/Infrastructure/AGENTS.md` for EF Core, persistence, MailKit, and email
  protocol safety;
- `tests/AGENTS.md` for unit tests, coverage, and the deferred integration-test
  boundary;
- `docs/AGENTS.md` for documentation and ADR workflow.

Each directory also contains a one-line `CLAUDE.md` importing its sibling
`AGENTS.md`. Critical security and Git rules remain at the root even when a
more specific instruction file exists.

## Failure handling

- A workspace inspection warning is informational and never mutates Git.
- Verification scripts return the failing command's nonzero status.
- A detached or stale workspace blocks `start-task` from authorizing
  file changes until the environment is corrected.
- Documentation or licensing uncertainty is `fail`, not `n/a`.
- ADR creation or modification still requires explicit owner approval.
- If Claude Code does not discover the directory-level symlink, implementation
  stops and reports the compatibility failure instead of silently duplicating
  skills.

## Testing

`scripts/test-agent-workflow.sh` uses a temporary Git repository and its
embedded fake `dotnet` mode to prove command order, `Release` configuration,
no duplicate test execution in
the full gate, required workflow-contract execution and failure propagation,
fail-fast behavior, committed/staged/unstaged diff coverage, untracked-file
rejection, and read-only workspace inspection across HEAD, refs, index, and
working-tree state. It also proves that a failing SDK query remains an
informational inspection result.

Every skill receives a baseline scenario without the skill, a forward test with
the skill, structural validation through the official skill validator, and
discovery checks through both `.agents/skills` and `.claude/skills`.

The final repository check runs `scripts/verify-full.sh` against the real
solution and inspects the complete diff for secrets, generated files, unrelated
changes, instruction gaps, and licensing drift.
