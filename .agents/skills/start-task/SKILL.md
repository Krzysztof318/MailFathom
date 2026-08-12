---
name: start-task
description: Use when beginning repository work that may edit files, change dependencies, or require roadmap, documentation, or ADR context.
license: Apache-2.0
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/Krzysztof318/MailFathom
---

# Start Task

## Two roles

This repository is public and its roadmap board is not. The same contract therefore runs in two
places, and several steps below mean different things in each. Resolve which one applies before
step 2 rather than assuming, and state it in the brief.

1. Run `scripts/inspect-workspace.sh` and read `Base branch`. `origin/main` means `origin` is
   MailFathom itself, which is the **owner's checkout**. Anything else — `upstream/main`, or
   `unresolved` — means `origin` is a fork, which is the **fork role**.
2. Probe the board, in either role, because access to it is a separate fact from which repository
   this is: the owner grants read or write on project `4` to a contributor whenever they decide to,
   and a clone of MailFathom made without write access reads as the owner's checkout until something
   asks:

   ```bash
   gh api graphql -f query='{ user(login: "Krzysztof318") { projectV2(number: 4) { viewerCanUpdate } } }'
   ```

   `true` is write access and every board step below applies. `false` is read access: read the board
   for context and report each write as unavailable rather than attempting it. No access reads as
   `"projectV2": null` beside a `NOT_FOUND` error saying `Could not resolve to a ProjectV2 with the
   number 4` — GitHub hides a project the viewer cannot see rather than refusing it, so nothing in
   that answer says *permission* and it must not be reported as the board having been deleted or the
   number being wrong. A `CLAUDE.local.md` written by `$get-started-contributors` states the answer
   already, and this probe confirms it rather than replacing it.

   A negative is checked before it is reported, because the probe sees the credential's access rather
   than the account's: confirm `gh auth status` lists the `project` scope, and that no `GH_TOKEN` or
   `GITHUB_TOKEN` in the environment is displacing the stored credential — `gh` prefers one and cannot
   list its scopes. Without that scope the call fails identically whoever is running it, so report it
   as a credential to repair, never as the board being out of reach.

The fork role assigns no label and no milestone and never pushes to `Krzysztof318/MailFathom`,
because those are write access to the repository rather than to the board, and no board grant confers
them. Those are not degraded versions of the owner's steps — they are steps that belong to a
maintainer, and attempting one produces a permission error rather than a partial result. The board
fields — `Area`, `Queue`, `Size` — follow the probe instead of the role, so a contributor the owner
granted write to sets them exactly as this file says, and the owner's own checkout would stop setting
them if that access were ever removed.

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
5. Classify the task by what governs it: an ADR, the architecture draft, an existing issue, or
   documentation-only work. Work nothing backs is an ordinary case and a feature can be one, so never
   invent a governing document to supply the classification — say that nothing governs it and let the
   issue body carry the scope instead.
6. Read whatever step 5 named, plus affected implemented-behavior documentation and relevant ADRs.
7. Check the task against the protected paths before planning the work, not after the check refuses
   it. `.github/`, `.config/`, `.agents/`, `.claude/`, and `docs/decisions/`, an `.editorconfig`,
   `.gitattributes`, `.worktreeinclude`, `AGENTS.md`, or `CLAUDE.md` at any depth, and the
   repository-root `CHANGELOG.md`, `Directory.Build.props`, `LICENSE`, `NOTICE`, `NuGet.config`, and
   `global.json` are refused from any author but the owner. In the fork role, a task that needs one
   of them is a task to raise in an issue instead of to implement; say so now rather than after a
   session spent on a diff that cannot merge.
8. Identify the GitHub issue that governs the task, reading
   `docs/operations/issue-tracking.md` first. Create it when none exists; its body draws on what
   step 6 read.
9. Place the issue. The label and the milestone need write access to the repository, so they belong
   to the owner's checkout; the board fields need write access to the board, which step 2
   established. It carries exactly one `type:*` label, an `Area` and a `Queue` value on the board, a
   milestone when the milestone rule assigns one, and a `Size` value estimated from the scope the
   body describes. Decide each from the rules on that page rather than asking. `Queue: Next` is never
   one of them: the owner chooses it, and `$finish-change` writes it once the pull request exists, so
   a new issue takes `Later`, `Needs decision`, or `Parked`, and a parent takes one of those three by
   the same rules as any other issue. What marks a parent instead is the `parent` label and the `[P] `
   prefix every parent's title begins with, whatever `Queue` value it holds.
   Verify the values landed, because the built-in workflows set `Status` and nothing else, and an
   unplaced issue disappears from the views the owner reads.

   In the fork role, open the issue and stop at what the probe allows. Without board write that is the
   issue alone: an arrival carries no label, no milestone, and no board fields by design, and the
   maintainer's triage pass supplies them. With board write it is the issue plus its `Area`, `Queue`,
   and `Size`, and the label and milestone still wait for triage. Say which of the two happened in the
   brief, so nobody reads the absence as a step that failed.
10. Claim the issue, in the owner's checkout, once the brief below will read `safe`:

    ```bash
    gh issue edit <number> --repo Krzysztof318/MailFathom --add-label agent:claimed
    ```

    This is the step that says work has begun, so it belongs here rather than at step 8: an issue
    identified while the workspace turns out to be `blocked` was read and not taken. Nothing reads
    the label, so re-applying one already present is a harmless no-op and nothing has to look first,
    and it is never removed — `docs/operations/issue-tracking.md` § *Labels* holds what it claims and
    why the claim is the weaker of the two available. In the fork role this step does not exist, for
    the reason every label step does not: it is write access to this repository.
11. For dependency, CLI, protocol, service, or external API changes, consult current official
    documentation and flag licensing review.

Return:

```text
Role: <owner's checkout or fork, what resolved it, and what the board probe returned>
Workspace: <safe or blocked, branch, base branch>
Scope: <what governs the task, or that nothing does>
Protected paths: <none reached, or which and what that means for this role>
Issue: <number and title, or created with reason>
Placement: <type label, Area, Queue, Size, milestone or none — or what the board probe left to triage>
Claim: <agent:claimed applied, already present, or not applicable in the fork role>
Required context: <files read>
Assumptions or blockers: <none or explicit list>
Verification: <fast loop and final gate>
```

Do not begin edits while the brief says `blocked`.
