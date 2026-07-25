---
name: start-task
description: Use when beginning repository work that may edit files, change dependencies, or require roadmap, documentation, or ADR context.
---

# Start Task

## Workflow

1. Resolve the repository root and run `eng/agent-workflow/inspect-workspace.sh`.
2. Stop before edits when `Branch` is detached or `Contains origin/main` is not `yes`. Correct the workspace according to root instructions.
3. Classify the task as a numbered roadmap specification, maintenance outside the roadmap, or documentation-only work.
4. Read the selected specification, affected implemented-behavior documentation, and relevant ADRs.
5. For dependency, CLI, protocol, service, or external API changes, consult current official documentation and flag licensing review.

Return:

```text
Workspace: <safe or blocked, branch, base>
Scope: <specification or maintenance classification>
Required context: <files read>
Assumptions or blockers: <none or explicit list>
Verification: <fast loop and final gate>
```

Do not begin edits while the brief says `blocked`.
