---
name: start-task
description: Use when beginning repository work that may edit files, change dependencies, or require roadmap, documentation, or ADR context.
---

# Start Task

## Two roles

This repository is public and its roadmap board is not. The same contract therefore runs in two
places, and several steps below mean different things in each. Resolve which one applies before
step 2 rather than assuming, and state it in the brief.

1. Run `scripts/inspect-workspace.sh` and read `Base branch`. `origin/main` means `origin` is
   MailFathom itself, which is the **owner's checkout**. Anything else — `upstream/main`, or
   `unresolved` — means `origin` is a fork, which is the **fork role**.
2. When it says `origin/main`, confirm board access with
   `gh project item-list 4 --owner Krzysztof318 --limit 1`. A clone of MailFathom made without write
   access looks identical to the owner's checkout until that call fails; when it does, the fork role
   applies even though the remote is right.

The fork role never opens the project board, never assigns a label, a milestone, `Track`, or
`Queue`, and never pushes to `Krzysztof318/MailFathom`. Those are not degraded versions of the
owner's steps — they are steps that belong to a maintainer, and attempting one produces a permission
error rather than a partial result.

## Workflow

1. Resolve the repository root and run `scripts/inspect-workspace.sh`.
2. **Owner's checkout only:** rename the branch with `git branch -m agent/<short-description>` when
   `Worktree` is `linked worktree` and `Branch` matches `worktree-*`. That combination is the harness
   naming its own scratch branch, which this workflow rejects, so renaming it is the expected first
   move rather than a sign that the workspace is wrong. Never rename any other branch: in the primary
   checkout the branch is the developer's own, and renaming `main` there would destroy it while
   making no progress on the task. Any other mismatch is corrected by creating or entering a
   worktree, not by renaming. In the fork role the branch name is the contributor's to choose and
   nothing here renames it.
3. Fetch the base the change will merge into, then run workspace inspection again. In the owner's
   checkout that is `git fetch origin main`. In the fork role it is `git fetch upstream main`, and
   when `Base branch` reported `unresolved` the corrective step comes first:

   ```bash
   git remote add upstream https://github.com/Krzysztof318/MailFathom.git
   git fetch upstream main
   ```

   That is a fix rather than a blocker. Without it every gate would compare the branch against the
   fork's own `main`, which is whatever was last synced rather than what the pull request merges
   into, and a green run would prove nothing.
4. Stop before edits unless `Contains base branch` is `yes` and the working tree is either clean or
   fully inventoried under a user-approved preservation plan. In the owner's checkout also require
   `Branch` matching `agent/*` and `Worktree` reporting `linked worktree`; in the fork role neither
   applies, and an ordinary clone on a contributor-named branch is `safe`.
   - If the working tree is dirty, run `git status --short --untracked-files=all` and inventory every
     existing path.
   - Return `blocked` until every path is inventoried and the user explicitly approves its
     preservation plan, or the changes are moved to a separate worktree. Never assume existing
     changes are unrelated.
5. Classify the task as a numbered roadmap specification, maintenance outside the roadmap, or
   documentation-only work.
6. Read the selected specification, affected implemented-behavior documentation, and relevant ADRs.
7. Check the task against the protected paths before planning the work, not after the check refuses
   it. `.github/`, `.config/`, `.agents/`, `.claude/`, and `docs/decisions/`, an `.editorconfig`,
   `.gitattributes`, `.worktreeinclude`, `AGENTS.md`, or `CLAUDE.md` at any depth, and the
   repository-root `CHANGELOG.md`, `Directory.Build.props`, `LICENSE`, `NOTICE`, `NuGet.config`, and
   `global.json` are refused from any author but the owner. In the fork role, a task that needs one
   of them is a task to raise in an issue instead of to implement; say so now rather than after a
   session spent on a diff that cannot merge.
8. Identify the GitHub issue that governs the task, reading
   `docs/operations/issue-tracking.md` first. Create it when none exists; its body draws on the
   specification read in step 6. A change set that adds a numbered specification also creates that
   specification's issue.
9. Place the issue — **owner's checkout only**. It carries exactly one `type:*` label, a `Track` and a
   `Queue` value on the board, a milestone when the milestone rule assigns one, and a `Size` value
   once the work is planned. Decide each from the rules on that page rather than asking. `Queue: Next`
   is never one of them: the owner chooses it, and `$finish-change` writes it once the pull request
   exists, so a new issue takes `Later`, `Needs decision`, or `Parked`. Verify the values landed,
   because the built-in workflows set `Status` and nothing else, and an unplaced issue disappears
   from the views the owner reads.

   In the fork role, open the issue and stop there. An arrival carries no label, no milestone, and no
   board fields by design, and the maintainer's triage pass supplies them; say so in the brief so
   nobody reads the absence as a step that failed.
10. For dependency, CLI, protocol, service, or external API changes, consult current official
    documentation and flag licensing review.

Return:

```text
Role: <owner's checkout or fork, and what resolved it>
Workspace: <safe or blocked, branch, base branch>
Scope: <specification or maintenance classification>
Protected paths: <none reached, or which and what that means for this role>
Issue: <number and title, or created with reason>
Placement: <type label, Track, Queue, milestone or none, Size or deferred — or left to triage in the fork role>
Required context: <files read>
Assumptions or blockers: <none or explicit list>
Verification: <fast loop and final gate>
```

Do not begin edits while the brief says `blocked`.
