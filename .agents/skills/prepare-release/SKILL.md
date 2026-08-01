---
name: prepare-release
description: Use when a MailFathom release is being cut, to close the changelog, raise the declared version, and state the order the two pull requests and the tag have to land in.
---

# Prepare Release

Prepares the two pull requests a release consists of and then hands the sequence back to the owner.

A release under [ADR 0004](../../../docs/decisions/0004-versioning-and-release-policy.md) is three steps, and only the
middle one is a single command: a reviewed pull request closes the changelog, an annotated tag on that merge commit is
what makes the release real, and a second pull request returns `main` to naming the *next* release. The tag has to land
between the two, and nothing inside a pull request can express that ordering — which is the whole reason this skill
prints it rather than automating it.

**This skill pushes no tag and merges nothing.** Tagging is the moment a release becomes real and stays a deliberate act
by the owner. Everything here is reversible by closing two pull requests.

## What the version is

Read, never asked for. The version being released is the `VersionPrefix` declared in `Directory.Build.props`, because
that is the number the build already stamps into every assembly and every artifact; asking would let this skill name a
release the build would not produce.

```bash
scripts/read-declared-version.sh
```

The version being bumped *to* depends on which branch the release is cut from:

| Branch | Releases | Bumps to | Overridable |
| --- | --- | --- | --- |
| `main` | every major and minor | the next **minor** | the next major, on request |
| `release/<major>.<minor>.x` | every patch, and nothing else | the next **patch** | no |

**Never propose a patch from `main`.** By the time `0.2.0` needs a fix, `main` already carries everything intended for
`0.3.0`, and a patch released from it would ship that work under a number promising nothing had changed. A patch is cut
from the permanent `release/<major>.<minor>.x` branch, which the ADR's *patch flow* section describes.

## Refusals

Stop and report, without creating anything, when any of these holds:

- **The working tree is dirty.** The release describes a tree, and an uncommitted change is not in it.
- **`## [Unreleased]` is empty.** There is nothing to release. An empty section means either the work was not entered
  as it merged — which is the changelog obligation in `$check-docs-licenses`, not something to fix here — or this
  version has already been closed.
- **A tag `v<x.y.z>` already exists** for the version being released, locally or on the remote. A released version is
  never re-cut; the release workflow rejects it anyway, and finding out here costs nothing.
- **The branch is not `main` or `release/<major>.<minor>.x`.** No other branch releases.
- **`Directory.Build.props` and `deploy/helm/mailfathom/Chart.yaml` disagree** on the version.
  `scripts/verify-deployment-assets.sh` is the check; a chart documenting another version would reject the image this
  release publishes.

## Workflow

### 1. Establish the two versions and confirm the increment

```bash
git status --porcelain            # must be empty
git branch --show-current
scripts/read-declared-version.sh  # the version being released
git tag --list 'v*'               # must not contain the version being released
git fetch --tags origin
```

The increment to bump *to* follows the table above. Confirm it against the four surfaces before continuing: the highest
increment any of the MCP tool contract, the configuration schema, the database schema, or the deployment contract
requires is the release's own, and the `## [Unreleased]` entries are what that is read from. Raise the question with the
owner when the entries and the proposed increment disagree — an unnecessary major costs one careful upgrade, an
unmarked break costs an outage.

### 2. Open the changelog pull request

On a branch off the release branch, and touching nothing else:

- rename `## [Unreleased]` to `## [x.y.z] - YYYY-MM-DD`, using the release date in UTC;
- update the link references at the foot of the file so the new section resolves and `Unreleased` still does;
- leave `VersionPrefix` alone. It already reads `x.y.z`, which is what makes the tagged tree self-consistent.

Nothing else belongs in this diff. It is the last point at which the release's contents are read as a whole before
anyone can install them, and an unrelated change in it is a change nobody reviewed as part of the release.

### 3. Open the version-bump pull request

On a second branch off the release branch:

- raise `<VersionPrefix>` in `Directory.Build.props` to the next version from the table above;
- raise `appVersion` in `deploy/helm/mailfathom/Chart.yaml` to the same value, and `version` if anything under the chart
  directory changed;
- open a fresh, empty `## [Unreleased]` section above the one that was just closed.

### 4. Draft both, and cross-reference them

Both open as drafts, per the repository rule, and each body names the other by number, so neither is merged alone by
accident. Neither carries `Closes #<issue>`: a release closes no issue, and the issue that asked for the release is
closed by the work in it rather than by the release of it.

### 5. Print the ordering, and stop

Report exactly this to the owner, with the numbers filled in:

```text
Release x.y.z is prepared. Merge in this order — the tag has to land between the two.

1. Merge the changelog pull request (#A).
   The tagged tree has to contain the released changelog, not describe it afterwards.

2. Push the annotated tag on that merge commit:
       git tag --annotate vx.y.z --message 'MailFathom x.y.z'
       git push origin vx.y.z
   This is what triggers the release workflow. Before publishing anything it asserts the tag against VersionPrefix,
   against the highest existing tag on the same major.minor line, and against the changelog section for this version.

3. Merge the version-bump pull request (#B).
   After the tag, so main returns to naming the next release rather than the one just published.
```

## When a step fails

- **The tag is rejected.** Either step 1 did not merge, so the tagged commit's `VersionPrefix` is not `x.y.z`, or the
  two disagree for another reason. Check out the tagged commit and compare `scripts/read-declared-version.sh` against
  the tag. Do not force the tag; delete it, fix the disagreement, and tag again.
- **The tag names a version that already exists on its line.** The bump pull request from a previous release never
  merged. Merge it, then re-cut.
- **Publication fails after the image is built.** Retry by digest rather than by rebuilding — a rebuild produces a
  second artifact for one version, which is what the release workflow's immutability assertion exists to prevent. The
  release workflows and their recovery are #156's.
- **The release is abandoned before the tag.** Close both pull requests. Nothing was published and no tag exists, so
  there is nothing to undo.
- **The release is abandoned after the tag.** It is not abandoned; it is released. Cut a patch from the release branch.

`docs/operations/release-procedure.md` records the same sequence for a reader who does not have this skill.
