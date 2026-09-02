---
name: update-dependencies
description: Use when the task in front of you is to move dependency pins — packages in either stack, Rust crates, tools, SDKs, GitHub Action references, or container images — or to find out which of them are behind and whether any changed licence.
license: Apache-2.0
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/Krzysztof318/MailFathom
---

# Update Dependencies

Reads every pin in the repository against its upstream, decides which of them to move, moves the ones that are decided,
and leaves `THIRD_PARTY_LICENSES.md` telling the truth about the result.

`scripts/update-dependencies.sh` is the reading. This is the judgement, and the judgement is the whole of the work: the
script says a pin is behind, and nothing it prints says the newer version is one this repository would have chosen. That
is the half a scheduled updater never delivered and the reason there is no longer one — `docs/operations/agent-workflow.md`
§ *Dependency update pull requests* records the decision and the four questions it leaves behind.

**Enter this only when the task is a dependency update.** A pin being behind is not a defect discovered mid-task and not
a tidy-up to fold into an unrelated change: it is work the owner decides to take, with an issue and a pull request of its
own. Noticing one while doing something else is worth a sentence in the report, never a bump in the diff.

## The order, and why the survey comes first

1. **Survey, before anything else.** `scripts/update-dependencies.sh` writes nothing, needs no branch, and does not care
   whether the workspace is clean, so it runs before `$start-task` rather than after it. That order is what makes the
   issue body real: an issue opened first would describe an intention, and this one describes a hundred pins whose state
   is known.

   ```bash
   scripts/update-dependencies.sh
   ```

   It takes about a minute and reaches nuget.org, registry.npmjs.org, crates.io, the .NET release index, GitHub, and
   three container registries. Read the whole
   report, including the rows that came back `current` — a licence that moved under a pin nobody has to bump is exactly
   what this exists to catch, and it is reported on a `current` row like any other.

2. **Decide what moves,** by the section below. This is the step with no command in it.

3. **`$start-task`**, with the decision as the description. The issue names the pins being moved and, for each one that
   is a major or a licence change, why the newer version is acceptable. `type:workflow`, `Area: Platform`.

4. **Apply**, on the branch `$start-task` left you on:

   ```bash
   scripts/update-dependencies.sh --apply
   ```

   It rewrites the pins it can write mechanically and regenerates whichever of the three lock files those pins belong
   to — `packages.lock.json` through a `--force-evaluate` restore, `frontend/pnpm-lock.yaml` through pnpm, and
   `frontend/src-tauri/Cargo.lock` through `cargo update` named crate by crate. Then take out again anything
   step 2 decided not to move — `--apply` moves every behind pin it can, and it is not the place the decision was made.
   Read the lock diff before anything else: a bump moving one direct version and forty transitive ones is a different
   change from one moving only itself, and that difference is visible nowhere else.

5. **Update `THIRD_PARTY_LICENSES.md` by hand.** The script prints, for every pin it moved, the register lines still
   naming the version it moved from. Open each one. A row is a completed review written as prose — what the component is
   used for, what its terms oblige, which of them a distribution has to discharge — so the version is not the only thing
   in it that can stop being true. A row that names a transitive package the bump removed, or an argument that rested on
   the old version's behaviour, is the part a search-and-replace would leave standing.

   **A client pin costs a census as well as a row.** § *The client's two dependency closures* records each of the
   client's two graphs as a count — how many packages under which terms, and every one carrying a condition — and a pin
   that moved resolved a new graph. Re-run that section's enumeration commands and write what they printed; the script
   says so when it moves one, and nothing else will.

   **An npm pin costs a second file, and that one ships.** `frontend/src/Client.App/public/THIRD-PARTY-NOTICES.txt` is
   the notice the bundle itself carries — `pnpm build` copies it into the output that every image and every desktop
   package redistributes — and it names each redistributed package and its version. So a bump that moved `react`,
   `react-dom`, `scheduler`, or either Tauri npm package leaves a published artifact naming a version it no longer
   carries until that file is rewritten too, and a bump that put a new package into the bundle leaves it naming one
   package too few. `pnpm --dir frontend licenses list --prod` says which packages the bundle actually redistributes.
   The desktop shell's crates reach no bundle and owe nothing here.

6. **`$review-change`, `$check-docs-licenses`, `$finish-change`**, as any other task. Never touch `CHANGELOG.md`.

## What decides whether a pin moves

Four questions answer a version bump, and `docs/operations/agent-workflow.md` § *Dependency update pull requests* states
them in full. They are the same four whether the diff was written by a person, by `--apply`, or by an updater:

1. **Is the new revision one this repository would have chosen?** Read the upstream release notes rather than the
   version numbers. `gh api repos/<owner>/<repo>/releases/tags/<tag> --jq '.body'` is the whole of it for an action, and
   a package's release notes or repository is the equivalent. A major is where this matters, and a major with nothing to
   gain is a bump not worth taking.
   The survey marks the rows this applies to: a `behind` row whose leading segment moved carries a `MAJOR` line, and
   so does a `0.y` line whose minor moved, that being where SemVer stops promising anything. The mark says where a break
   is permitted rather than that one happened, so it decides which notes to open and never what they say.
2. **Does the owner stay inside the reviewed set?** A transfer, a rename, or a fork under the same name is the case no
   contract sees and a reader does.
3. **Does the register still describe the truth?** Step 5 above.
4. **Do the checks pass on their own terms?** `scripts/verify-full.sh` and then the pull request's own checks. A red one
   is a red one.

Two more decide the shape of the change rather than the content:

- **A major and a patch do not belong in one pull request.** Forty patch bumps are one mechanical change a reader can
  scan; a major that renames an input, drops a target framework, or changes an analyzer's default is a change with a
  behaviour to argue about. Splitting them is what keeps the second reviewable.
- **A bump is judged by what it moves, not by what is available.** Being behind is not a reason. A pin nothing needs and
  nothing has reported an advisory against is fine where it is, and the survey's job is to make that a decision instead
  of an oversight.

## The two the script refuses to write, and what to do instead

Both are surveyed and reported, and neither is rewritten by `--apply`. Moving either is a separate task with its own
issue, and neither belongs inside a package bump.

**`global.json`'s `sdk.version`** is a floor under `rollForward: latestFeature`, so it decides which SDK a machine has to
have installed rather than which one a build uses. Raising it can stop a contributor's clone from restoring at all,
which is why it is a decision about the project rather than a version bump. `docs/operations/local-development.md`
records what the floor is for.

**A container image pin** is written in up to four assets in four syntaxes — a Compose default, a Helm value split across
`repository` and `tag`, a Quadlet unit source, an AppHost call — and two of them are digests rather than tags. The survey
prints every file carrying each reference so the extent is visible before the first edit. Moving one also obliges the
manifests under `deploy/helm/mailfathom/ci/golden/`, which are written by `scripts/render-helm-manifests.sh --update` and
by nothing else. Two of the pins additionally decide what the software *concludes* rather than only what it runs — the
Presidio analyzer image and the SpamAssassin digest — so a finding produced under one is not a finding produced under
another, and the deployment pages say so.

## What this never does

- **Never re-add an automated updater.** `.github/dependabot.yml` was deleted deliberately and is not coming back.
  Dependabot *alerts* stay on and answer a different question: a published advisory against something pinned here, which
  is worth an interruption, where merely being behind is not.
- **Never edit `THIRD_PARTY_LICENSES.md` from a script,** including a script written for one bump. Step 5 is a reading.
- **Never move a pin the survey could not resolve.** An `unresolved` row is a host that did not answer, not a component
  with no newer version, and the two are opposite conclusions.
- **Never let `--apply` decide.** It writes what is behind because that is all a machine can know; the branch it leaves
  behind is a draft of the decision made in step 2 and is corrected against it before the diff is reviewed.
