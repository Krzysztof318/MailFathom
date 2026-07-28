---
name: start-task
description: Use when beginning repository work that may edit files, change dependencies, or require roadmap, documentation, or ADR context.
---

# Start Task

## Workflow

1. Resolve the repository root and run `scripts/inspect-workspace.sh`.
2. Rename the branch when `Branch` does not match `agent/*`. The agent harness names a new worktree branch `worktree-<id>`, which this workflow rejects, so `git branch -m agent/<short-description>` is the expected first move rather than a sign that the workspace is wrong. Only a branch carrying someone else's work needs a question first.
3. Fetch the current remote base with `git fetch origin main`, then run workspace inspection again.
4. Stop before edits unless `Branch` matches `agent/*`, `Worktree` is `linked worktree`, `Contains origin/main` is `yes`, and the working tree is either clean or fully inventoried under a user-approved preservation plan. Correct the workspace according to root instructions.
   - If the working tree is dirty, run `git status --short --untracked-files=all` and inventory every existing path.
   - Return `blocked` until every path is inventoried and the user explicitly approves its preservation plan, or the changes are moved to a separate worktree. Never assume existing changes are unrelated.
5. Classify the task as a numbered roadmap specification, maintenance outside the roadmap, or documentation-only work.
6. Read the selected specification, affected implemented-behavior documentation, and relevant ADRs.
7. Identify the GitHub issue that governs the task. Create it when none exists, following the issue rules in root `AGENTS.md`; its body draws on the specification read in the previous step. A change set that adds a numbered specification also creates that specification's issue.
8. For dependency, CLI, protocol, service, or external API changes, consult current official documentation and flag licensing review.

Return:

```text
Workspace: <safe or blocked, branch, base>
Scope: <specification or maintenance classification>
Issue: <number and title, or created with reason>
Required context: <files read>
Assumptions or blockers: <none or explicit list>
Verification: <fast loop and final gate>
```

Do not begin edits while the brief says `blocked`.
