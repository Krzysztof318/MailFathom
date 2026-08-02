---
name: finish-change
description: Use when repository work is implemented and must be verified, committed, pushed, and submitted as a pull request.
---

# Finish Change

## Required Gates

1. Confirm the work is on an isolated `agent/<short-description>` branch based on current `origin/main`, never `main` or `master`.
2. Inspect status, stage only task files, and inspect the staged diff. Stop if any untracked or unrelated path remains.
3. Invoke `$check-docs-licenses`. Fix every `fail` and repeat the gate until all three verdicts pass or are `n/a`. Its changelog verdict is `n/a` for ordinary work: `CHANGELOG.md` is written by the release pull request alone, so a diff that edits it here is a defect rather than diligence.
4. Run `scripts/verify-full.sh`. Fix failures and rerun the complete script; earlier or partial results do not replace a fresh successful run. Repair a formatting failure through `scripts/verify-fast.sh`, which rewrites the changed files and reports what has no code fix, rather than through a hand-run `dotnet format` over the whole solution.
5. Inspect status and the full diff for secrets, generated artifacts, unrelated edits, architecture violations, and missing tests or documentation. `scripts/review-obligations.sh` is what answers the last of those without reading the whole tree: it names the tests and pages the change obliges and whether it touched them. It gates nothing, so a row it reports is answered — by the test, by the page, or by saying why nothing is owed — rather than treated as a blocker or as a licence to skip the reading.

Do not proceed while a gate fails.

## Publish

1. Confirm the staged diff still contains exactly the task files.
2. Commit with a focused message and no co-author trailers.
3. Fetch the remote and confirm the branch remains safely based on current `origin/main`. Integrate remote movement without discarding work, then repeat affected gates.
4. Push the `agent/*` branch.
5. Create a draft pull request whose body contains `Closes #<issue>` for the issue the change completes. Never mark it ready unless the owner explicitly asks.
6. Confirm the reference is present in the published body. `gh pr edit` fails against this repository with a Projects-classic GraphQL error and silently drops the edit, so correct a missing reference through `gh api repos/<owner>/<repo>/pulls/<number> -X PATCH -f body=...`.
7. Set `Queue: Next` on the issue the pull request closes, whether it was opened for this task or already existed, and confirm the value landed. No project automation can write that field, so a value that did not land is an incomplete gate in the same way a missing `Closes #<issue>` is. It sits outside the owner's five-slot cap and needs no clearing: the merge closes the issue out of every view that reads `Queue`.

Confirm the issue is still placed: exactly one `type:*` label, a `Track` value on the board, a milestone if the release rule assigns one, and a `Size`, which is no longer deferrable because the diff now exists and can be measured rather than estimated. A change that grew past what the issue described may have outgrown its placement too.

Leave the board's `Status` field to the project automation. Set a status by hand only for an issue created already closed, which the automation does not add.

Report:

```text
Docs: <pass or n/a with evidence>
Changelog: <pass or n/a with evidence>
Licenses: <pass or n/a with evidence>
Full verification: <command and result>
Diff review: <scope and exclusions>
Commit: <hash and subject>
Push: <remote branch>
Draft PR: <URL>
Issue link: <Closes #N confirmed in the published body>
Queue: <Next confirmed on the board after the pull request existed>
Placement: <type label, Track, Size, milestone or none>
```

Never claim completion without fresh evidence for every line.
