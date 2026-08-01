---
name: prepare-release
description: Manual only. Invoked by the owner to cut a MailFathom release — composes the changelog, raises the declared version, and states the order the two pull requests and the tag have to land in.
disable-model-invocation: true
---

# Prepare Release

**Manual invocation only.** Cutting a release is the owner's decision about when a version becomes real, not something to infer from a task looking release-shaped. `disable-model-invocation` in the frontmatter above is what enforces that: an agent cannot reach this skill on its own, and a release begins when the owner asks for one.

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
- **Nothing has merged since the previous tag.** There is nothing to release. Re-publishing an identical tree under a
  new number makes the number mean less rather than more.
- **A tag `v<x.y.z>` already exists** for the version being released, locally or on the remote. A released version is
  never re-cut; the release workflow rejects it anyway, and finding out here costs nothing.
- **`CHANGELOG.md` already carries a `## [x.y.z]` section.** That version has been prepared, and possibly released.
- **The branch is not `main` or `release/<major>.<minor>.x`.** No other branch releases.

## Workflow

### 1. Establish the two versions and confirm the increment

```bash
git fetch --tags origin
git status --porcelain            # must be empty
git branch --show-current
scripts/read-declared-version.sh  # the version being released
git tag --list 'v*'               # must not contain the version being released
```

### 2. Read what merged since the previous tag

This is the whole substance of the release, and it is read now rather than accumulated as work went along.
`CHANGELOG.md` is written here and nowhere else: an entry added while implementing a task would be a claim about a
release nobody had yet decided to cut.

```bash
git log --merges --pretty='%h %s' "$(git describe --tags --abbrev=0)..HEAD"
gh pr list --state merged --search 'base:main merged:>=<date of previous tag>' --limit 100 \
  --json number,title,url,closingIssuesReferences
```

Keep what a consumer of a release would notice — anything reaching the MCP tool contract, the configuration schema, the
database schema, or the deployment contract, plus a defect that was observable from outside and anything with a
security consequence. Drop the rest. A refactor, a test, a continuous-integration adjustment, a documentation edit, and
an internal rename earn no entry, and a changelog that lists them stops being read.

**The increment follows from what this reading found**, not the other way round: the highest increment any of the four
surfaces requires is the release's own. Raise the question with the owner when the entries and the version already
declared disagree — an unnecessary major costs one careful upgrade, an unmarked break costs an outage.

### 3. Open the changelog pull request

On a branch off the release branch, and touching nothing else:

- add `## [x.y.z] - YYYY-MM-DD` above the previous section, using the release date in UTC, with the entries from step 2
  grouped into the six Keep a Changelog categories and each referencing the pull request or issue that carried it;
- open a breaking entry with `**Breaking (<surface>)**` and state the operator's action, not only the fact;
- say, when the database schema moved, whether a migration must be applied, whether it applies while the previous
  version still runs, and whether the release deploys over the previous release's data at all;
- update the link references at the foot of the file so the new section resolves;
- leave `VersionPrefix` alone. It already reads `x.y.z`, which is what makes the tagged tree self-consistent.

Nothing else belongs in this diff. **This is the pull request whose merge commit is tagged and published**, so it is
both the last point at which the release's contents are read as a whole and the thing the published artifact is built
from; an unrelated change in it is a change nobody reviewed as part of the release. `CHANGELOG.md` is a protected path,
which is what makes an edit to it outside this flow visible.

### 4. Open the version-bump pull request

On a second branch off the release branch:

- raise `<VersionPrefix>` in `Directory.Build.props` to the next version from the table above. That is the only file
  carrying a version number; the chart's `appVersion` and the image's tags and labels are all derived from it at
  package and build time;
- raise `version` in `deploy/helm/mailfathom/Chart.yaml` if anything under the chart directory changed, which is the
  chart's own version and never follows the application's.

### 5. Draft both, and cross-reference them

Both open as drafts, per the repository rule, and each body names the other by number, so neither is merged alone by
accident. The changelog pull request carries `Closes #<issue>` for the issue that tracks this release; the version-bump
one closes nothing, because bumping is what follows a release rather than what the release was for.

### 6. Print the ordering, and stop

Report exactly this to the owner, with the numbers filled in:

```text
Release x.y.z is prepared. Merge in this order — the tag has to land between the two.

1. Merge the changelog pull request (#A).
   Its merge commit is what gets tagged and published, so the tagged tree contains the released changelog rather
   than describing it afterwards.

2. Push the annotated tag on that merge commit:
       git tag --annotate vx.y.z --message 'MailFathom x.y.z'
       git push origin vx.y.z
   This is what triggers the release workflow. Before publishing anything it asserts the tag against VersionPrefix,
   against the highest existing tag on the same major.minor line, and against the changelog section for this version.
   It then publishes the image under vx.y.z and moves `latest` onto it, in both registries — `latest` follows the
   newest release and never a nightly.

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
