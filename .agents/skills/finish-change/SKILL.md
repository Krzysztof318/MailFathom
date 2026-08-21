---
name: finish-change
description: Use when repository work is implemented and must be verified, committed, pushed, and submitted as a pull request.
license: Apache-2.0
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/Krzysztof318/MailFathom
---

# Finish Change

`$start-task` resolved whether this is the **owner's checkout** or the **fork role**, and the steps
below split on it in two places: the branch name, and the remote pushed to. The board is not one of
them — it follows the access probe `$start-task` ran, which is a separate fact from the role.
Everything else — the gates, the verification, the diff review, the pull request, and
`Closes #<issue>` — is identical, because a contribution is judged by what it does rather than by
where it came from.

## Required Gates

1. Confirm the work is on a branch based on the current base branch, never `main` or `master`. In the
   owner's checkout that branch is `agent/<short-description>` and is isolated in a linked worktree;
   in the fork role its name is the contributor's own.
2. Inspect status, stage only task files, and inspect the staged diff. Stop if any untracked or
   unrelated path remains.
3. Invoke `$check-docs-licenses`. Fix every `fail` and repeat the gate until all three verdicts pass
   or are `n/a`. Its changelog verdict is `n/a` for ordinary work: `CHANGELOG.md` is written by the
   release pull request alone, so a diff that edits it here is a defect rather than diligence.
4. Run `scripts/verify-full.sh`. Fix failures and rerun the complete script; partial results do not
   replace a successful one, and a run that failed records nothing, so the rerun is a real run. An
   *unchanged* tree is the one case that answers in under a second, because the script records what
   it verified and a second run over identical content would reprove it rather than prove it. Repair
   a formatting failure through
   `scripts/verify-fast.sh`, which rewrites the changed files, rather than through a hand-run
   `dotnet format` over the whole solution; a diagnostic no rewrite fixes is a build error there and
   names its own file and line.
5. Inspect status and the full diff for secrets, generated artifacts, unrelated edits, architecture
   violations, and missing tests or documentation. `scripts/review-obligations.sh` is what answers the
   last of those without reading the whole tree: it names the tests and pages the change obliges and
   whether it touched them. It gates nothing, so a row it reports is answered — by the test, by the
   page, or by saying why nothing is owed — rather than treated as a blocker or as a licence to skip
   the reading.

Do not proceed while a gate fails.

## Publish

1. Confirm the staged diff still contains exactly the task files.
2. Commit with a focused message.
3. Fetch the base remote and confirm the branch remains safely based on its current `main`. Integrate
   remote movement without discarding work, then repeat affected gates.
4. Push the branch. In the owner's checkout that is `origin`; in the fork role it is the fork's own
   `origin`, and nothing is ever pushed to `Krzysztof318/MailFathom`.
5. Create a pull request whose body contains `Closes #<issue>` for the issue the change
   completes. In the fork role it targets `main` on `Krzysztof318/MailFathom` from the fork's branch.
6. Confirm the reference is present in the published body. `gh pr edit` fails against this repository
   with a Projects-classic GraphQL error and silently drops the edit, so correct a missing reference
   through `gh api repos/<owner>/<repo>/pulls/<number> -X PATCH -f body=...`. That endpoint names the
   *base* repository even for a fork's pull request, and it is the author who is allowed to patch it
   rather than anyone with write access to that repository.
7. **Wherever the board probe in `$start-task` returned write access:** set `Queue: Next` on the
   issue the pull request closes, whether it was opened for this task or already existed, and
   confirm the value landed. No project automation can
   write that field, so a value that did not land is an incomplete gate in the same way a missing
   `Closes #<issue>` is. It sits outside the owner's five-slot cap and needs no clearing: the merge
   closes the issue out of every view that reads `Queue`. It is not written on an issue carrying the
   `parent` label, because a pull request closes the issue that does the work rather than the parent
   grouping it, and a `Closes` reference pointing at a parent is the defect to correct rather than an
   issue to move to `Next`.

   Without that access there is no board write and no gate here. The board is the owner's and a grant
   on it is theirs to make, so this is not a step that failed or was skipped for convenience — it is a
   step that does not exist in this session. Report it as `not applicable (no board write)` rather
   than as incomplete.

Confirm the issue is still placed, against `docs/operations/issue-tracking.md`, as far as this
session's access reaches: exactly one `type:*` label, a `backend` or `frontend` label where the work
landed in one of the two stacks, and a milestone if the release rule assigns one, all of which need
write access to the repository, and an `Area` and a `Size` on the board, the
`Size` estimated when the issue was opened and now corrected against the diff this pull request
actually produced. A change that grew past what the issue described may have outgrown its placement
too.

Leave the board's `Status` field to the project automation. Set a status by hand only for an issue
created already closed, which the automation does not add.

Report:

```text
Role: <owner's checkout or fork, and the board access the probe returned>
Docs: <pass or n/a with evidence>
Changelog: <pass or n/a with evidence>
Licenses: <pass or n/a with evidence>
Full verification: <command and result>
Diff review: <scope and exclusions>
Commit: <hash and subject>
Push: <remote and branch>
Pull request: <URL, and the base repository it targets>
Issue link: <Closes #N confirmed in the published body>
Queue: <Next confirmed on the board after the pull request existed, or not applicable (no board write)>
Placement: <type label, stack label or neither, Area, Size, milestone or none — or what this session's access left to triage>
```

Never claim completion without fresh evidence for every line.
