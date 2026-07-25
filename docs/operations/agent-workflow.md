# Agent workflow

Codex and Claude Code share one repository-owned workflow. Deterministic Git and
.NET operations live in scripts, while repository-specific judgment lives in
skills.

## Entry points

Inspect the current workspace without changing Git state:

```bash
bash eng/agent-workflow/inspect-workspace.sh
```

Use the fast loop while implementing:

```bash
bash eng/agent-workflow/verify-fast.sh
```

Run the complete gate before committing:

```bash
bash eng/agent-workflow/verify-full.sh
```

The full gate restores repository tools and the solution, builds Release,
executes all unit tests through the aggregate 85% coverage target, verifies
formatting, and runs `git diff --check`. It stops at the first failure. Restore,
build, test, coverage, and formatting can create ignored local artifacts but
the scripts do not commit, push, or change branches.

## Skills

The canonical skills are:

- `start-task` checks the workspace and loads the applicable specification,
  documentation, and ADR context before edits;
- `review-change` performs a findings-first diff review and records verification
  status and residual risks;
- `check-docs-licenses` is the mandatory documentation and licensing gate;
- `finish-change` requires the documentation and licensing gate, runs full
  verification, checks the final diff, creates a focused commit, pushes the
  branch, and opens a draft pull request.

Skills live under `.agents/skills/`. Claude Code consumes the same directory
through the relative symlink `.claude/skills -> ../.agents/skills`; do not copy
or maintain a second skill tree.

## Instruction scope

Root `AGENTS.md` and `CLAUDE.md` carry repository-wide rules. More specific
instructions are next to the affected content:

- `src/` contains application and dependency-injection rules;
- `src/Infrastructure/` contains persistence and email-protocol rules;
- `tests/` contains test and coverage rules;
- `docs/` contains documentation and ADR rules.

The shared .NET and C# conventions remain at the root because they also apply
to test code. Each nested `CLAUDE.md` imports its sibling `AGENTS.md`.

## Completion evidence

A change is not complete until `check-docs-licenses` returns `pass` or `n/a` for
both categories, `verify-full.sh` succeeds from a fresh run, and the complete
diff has been inspected for secrets, generated files, unrelated edits, and
boundary violations. Pull requests are created as drafts and contain no
co-author trailers.
