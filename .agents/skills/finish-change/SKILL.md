---
name: finish-change
description: Use when repository work is implemented and must be verified, committed, pushed, and submitted as a pull request.
---

# Finish Change

## Required Gates

1. Confirm the work is on an isolated `agent/<short-description>` branch based on current `origin/main`, never `main` or `master`.
2. Inspect status, stage only task files, and inspect the staged diff. Stop if any untracked or unrelated path remains.
3. Invoke `$check-docs-licenses`. Fix every `fail` and repeat the gate until both verdicts pass or are `n/a`.
4. Run `scripts/verify-full.sh`. Fix failures and rerun the complete script; earlier or partial results do not replace a fresh successful run.
5. Inspect status and the full diff for secrets, generated artifacts, unrelated edits, architecture violations, and missing tests or documentation.

Do not proceed while a gate fails.

## Publish

1. Confirm the staged diff still contains exactly the task files.
2. Commit with a focused message and no co-author trailers.
3. Fetch the remote and confirm the branch remains safely based on current `origin/main`. Integrate remote movement without discarding work, then repeat affected gates.
4. Push the `agent/*` branch.
5. Create a draft pull request. Never mark it ready unless the owner explicitly asks.

Report:

```text
Docs: <pass or n/a with evidence>
Licenses: <pass or n/a with evidence>
Full verification: <command and result>
Diff review: <scope and exclusions>
Commit: <hash and subject>
Push: <remote branch>
Draft PR: <URL>
```

Never claim completion without fresh evidence for every line.
