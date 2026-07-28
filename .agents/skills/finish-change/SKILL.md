---
name: finish-change
description: Use when repository work is implemented and must be verified, committed, pushed, and submitted as a pull request.
---

# Finish Change

## Required Gates

1. Confirm the work is on an isolated `agent/<short-description>` branch based on current `origin/main`, never `main` or `master`.
2. Inspect status, stage only task files, and inspect the staged diff. Stop if any untracked or unrelated path remains.
3. Invoke `$check-docs-licenses`. Fix every `fail` and repeat the gate until both verdicts pass or are `n/a`.
4. Run `scripts/verify-full.sh`. Fix failures and rerun the complete script; earlier or partial results do not replace a fresh successful run. Repair a formatting failure through `scripts/verify-fast.sh`, which rewrites the changed files and reports what has no code fix, rather than through a hand-run `dotnet format` over the whole solution.
5. Inspect status and the full diff for secrets, generated artifacts, unrelated edits, architecture violations, and missing tests or documentation.

Do not proceed while a gate fails.

## Publish

1. Confirm the staged diff still contains exactly the task files.
2. Commit with a focused message and no co-author trailers.
3. Fetch the remote and confirm the branch remains safely based on current `origin/main`. Integrate remote movement without discarding work, then repeat affected gates.
4. Push the `agent/*` branch.
5. Create a draft pull request whose body contains `Closes #<issue>` for the issue the change completes. Never mark it ready unless the owner explicitly asks.
6. Confirm the reference is present in the published body. `gh pr edit` fails against this repository with a Projects-classic GraphQL error and silently drops the edit, so correct a missing reference through `gh api repos/<owner>/<repo>/pulls/<number> -X PATCH -f body=...`.

Leave the board's `Status` field to the project automation. Set a status by hand only for an issue created already closed, which the automation does not add.

Report:

```text
Docs: <pass or n/a with evidence>
Licenses: <pass or n/a with evidence>
Full verification: <command and result>
Diff review: <scope and exclusions>
Commit: <hash and subject>
Push: <remote branch>
Draft PR: <URL>
Issue link: <Closes #N confirmed in the published body>
```

Never claim completion without fresh evidence for every line.
