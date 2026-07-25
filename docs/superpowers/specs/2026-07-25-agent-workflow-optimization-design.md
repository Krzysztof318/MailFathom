# Agent Workflow Optimization Design

## Goal

Reduce repeated repository discovery, verification mistakes, and always-on
instruction cost for Codex and Claude Code without restoring the removed
generic Superpowers snapshot.

## Scope

The change adds:

- three repository-owned Bash entrypoints for workspace inspection, iterative
  verification, and final verification;
- four focused MailMcp skills for starting, reviewing, checking documentation
  and licensing, and finishing a change;
- one canonical `.agents/skills/` directory exposed to Claude Code through the
  repository symlink `.claude/skills -> ../.agents/skills`;
- path-scoped `AGENTS.md` guidance with matching `CLAUDE.md` import files;
- operational documentation for the shared workflow.

The change adds no package, service, container image, copied code, or runtime
dependency.

## Design

### Deterministic scripts

`eng/agent-workflow/inspect-workspace.sh` is read-only. It reports repository
root, branch or detached state, linked-worktree state, upstream, whether the
current commit contains the locally known `origin/main`, dirty paths, registered
worktree count, and installed .NET SDK. It does not fetch, switch branches,
create worktrees, or remove stale worktrees.

`eng/agent-workflow/verify-fast.sh` provides the repeatable inner loop:

1. restore the solution;
2. build `MailMcp.slnx` in `Release`;
3. run the complete unit-test suite without rebuilding.

`eng/agent-workflow/verify-full.sh` is the final gate:

1. restore repository-local tools;
2. restore the solution;
3. build `MailMcp.slnx` in `Release`;
4. run the existing coverage target, which executes the complete unit-test
   suite and enforces 85% aggregate line coverage;
5. verify formatting without another restore;
6. check the branch range, staged diff, and unstaged diff for whitespace
   errors.

All scripts resolve the repository root through Git, use `set -euo pipefail`,
stop on the first failure, and add no hidden mutation beyond ordinary .NET
restore/build/test artifacts already ignored by Git.

### Repository skills

Each skill remains concise and delegates deterministic work to the scripts:

- `start-task` inspects the workspace and routes the agent to the
  applicable specification, implemented-behavior documentation, and ADRs before
  file changes.
- `review-change` reviews the current diff against architectural
  boundaries, naming, privacy, IMAP safety, test policy, and change scope.
- `check-docs-licenses` is a required completion check. It classifies
  documentation impact and licensing impact independently and returns
  `Docs: pass|n/a|fail` and `Licenses: pass|n/a|fail` with evidence. Dependency,
  service, protocol SDK, container image, generated asset, and externally
  sourced code changes require current official license verification and a
  matching `LICENSES.md` entry.
- `finish-change` requires `check-docs-licenses`, runs the full
  verification script, inspects the final diff, and prepares a focused commit,
  push, and draft pull request without co-author trailers.

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

Shell tests use a temporary Git repository and a fake `dotnet` executable to
prove command order, `Release` configuration, no duplicate test execution in
the full gate, fail-fast behavior, committed/staged/unstaged diff coverage, and
read-only workspace inspection across HEAD, refs, index, and working-tree
state. They also prove that a failing SDK query remains an informational
inspection result.

Every skill receives a baseline scenario without the skill, a forward test with
the skill, structural validation through the official skill validator, and
discovery checks through both `.agents/skills` and `.claude/skills`.

The final repository check runs `verify-full.sh` against the real solution and
inspects the complete diff for secrets, generated files, unrelated changes,
instruction gaps, and licensing drift.
