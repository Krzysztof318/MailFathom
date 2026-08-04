---
name: prepare-release
description: Manual only. Invoked by the owner to cut a MailFathom release — composes the changelog, raises the declared version, and states the order the two pull requests and the tag have to land in.
disable-model-invocation: true
license: Apache-2.0
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/Krzysztof318/MailFathom
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
- leave `VersionPrefix` alone. It already reads `x.y.z`, which is what makes the tagged tree self-consistent;
- **bring the files that name a version in prose onto `x.y.z`**, per the list below;
- **sweep the tree for prose that describes the release state**, per the pass after it.

Nothing else belongs in this diff. **This is the pull request whose merge commit is tagged and published**, so it is
both the last point at which the release's contents are read as a whole and the thing the published artifact is built
from; an unrelated change in it is a change nobody reviewed as part of the release. `CHANGELOG.md` is a protected path,
which is what makes an edit to it outside this flow visible.

#### The files that name a version in prose

`<VersionPrefix>` is the only place a version is written for the *build*. Seven files additionally name one in prose,
where nothing derives it and nothing checks it, so they are read here by name rather than left to be noticed:

| File | What to bring onto `x.y.z` |
| --- | --- |
| `README.md` | The **Project status** paragraph — which release is current and what it ships — and the **Where the artifacts are published** table whenever a release starts or stops attaching one |
| `docs/users/installation.md` | The image references in the opening paragraph, which quote the version literally, and the sentence naming what a release publishes |
| `docs/users/README.md` | The **The state of the release** section — which release is current, and what a page is allowed to describe as already downloadable |
| `docs/operations/database-schema.md`, `docs/operations/deployment-compose.md`, `docs/operations/deployment-kubernetes.md` | The `mailfathom-schema-<x.y.z>.sql` filename the apply and verify commands quote literally. An operator copies those lines, so a stale one checksums and applies the previous release's schema |
| `SECURITY.md` | The **Supported versions** table. `x.y` becomes the supported line and the one it replaces moves down a row, per ADR 0004's rule that only the newest released minor is patched by default |

**They belong in this pull request rather than the bump one, and the reason is what the whole ordering rests on:** this
diff's merge commit is what gets tagged, so it is the tree an operator reads at `v<x.y.z>`. A `SECURITY.md` corrected
after the tag names the previous line in the artifact people actually download, and `docs/` at a tag is read far more
often than `docs/` on `main`. The bump pull request cannot carry them for the same reason it cannot carry the changelog.

Nothing gates this, deliberately: no check can tell prose describing the release from prose quoting a version as an
example, and one that tried would be satisfied by a search-and-replace through `docs/`. The list above is short and
fixed instead, and a file joining it is an edit to this table.

#### The release-state sweep

The list above catches a file that writes the version *number*. It cannot catch the other half, which is prose
asserting where the project stands relative to a release without naming one — "no versioned artifact exists yet", "a
release will attach it", "the first release is milestone `0.1.0`". A sentence like that is invisible to any search for
`x.y.z`, goes stale at the moment of the tag rather than gradually, and is read by exactly the people a release is
for. So it is swept for here rather than added to a list, because the sentences that will be wrong next time are not
the ones that were wrong last time.

The sweep is one reading pass over what the search below turns up. It is not a search-and-replace and its output is
not a list of edits:

```bash
git grep -nEi \
  'not (yet )?(been )?(released|published|distributed)|no (versioned |published |binary )?(artifact|release|image|chart) (exists|yet)|first release is|has not had|until (the )?(first )?release|is (still )?pre-?release|unreleased|no release (has|yet)|a release will' \
  -- ':(glob)**/*.md' ':(glob)**/*.yaml' ':(glob)**/*.yml' \
     ':(exclude)CHANGELOG.md' ':(exclude)docs/decisions/**' ':(exclude).agents/skills/prepare-release/**'
```

It is written to return a handful of lines rather than a page of them, because a pass that reports ninety hits is a
pass nobody reads to the end. Widen it when a release turns up a stale sentence it missed, and record what the new
alternative is for.

One shape is deliberately left to the table above rather than added here: prose naming a version as the threshold a
capability arrives at — "from `0.2.0` each release attaches". Catching it needs a pattern anchored on a version number,
and every such pattern also matches the package versions filling `THIRD_PARTY_LICENSES.md` and the specifications,
which is a page of noise to catch a sentence the table can simply name. A version threshold is a file to list, not a
phrase to search for.

Read every hit against the tree being tagged and settle it one of three ways:

- **Stale.** The sentence describes a state this release ends. Correct it in this pull request.
- **Still true, and about a *later* release.** A page saying a capability arrives with the next version is accurate and
  stays. Confirm the version it names is still the right one — a feature deferred out of this release has to name where
  it went.
- **Not about the release at all.** The pattern is deliberately wide, so it matches specification prose, an ADR's
  reasoning, and an example. Leave it.

The exclusions are deliberate. `CHANGELOG.md` is the one file whose historical entries *should* read as claims about
past releases, and rewriting one would be falsifying a record. `docs/decisions/` is excluded because an accepted ADR is
closed: it records what was true when the decision was taken, and is replaced rather than brought up to date. This
skill excludes itself because the patterns above are quoted in it.

Three files reach further than `docs/` and are worth reading with the release in mind whether or not the search names
them: `THIRD_PARTY_LICENSES.md` states whether redistribution obligations are outstanding, which the *first* release of
a line changes; `CONTRIBUTING.md` and the root `README.md` both characterize the project's maturity to somebody
deciding whether to depend on it.

Report the count and the disposition — swept, corrected, left — in the pull request body, so a reviewer can see the
pass happened without re-running it. A release that corrects nothing is an ordinary outcome and is stated as one.

### 4. Open the version-bump pull request

On a second branch off the release branch, raise `<VersionPrefix>` in `Directory.Build.props` to the next version from
the table above. **That is the whole diff**, because that property is the only place in the repository where a version
number is written: the image's tags and labels arrive as build arguments, and the chart's `version` and `appVersion`
are both supplied at package time from the same declaration.

**Do not touch `deploy/helm/mailfathom/Chart.yaml`.** Its `version` is a `0.0.0` placeholder and it declares no
`appVersion` at all; the release run supplies both, as one number equal to the application's. A chart version counting
edits to the chart directory would need raising on every release anyway — a packaged chart embeds its `appVersion`, so
each release produces chart content that differs from the last, and a published chart version is immutable — and it
would leave an operator mapping two numbers onto one artifact.

**Do not bring the three prose files here either.** They name the release that has just been *published*, and this
pull request merges after the tag — so a `README.md` corrected here is a `README.md` that was wrong in the tagged tree.
Step 3 owns them.

### 5. Open both, and cross-reference them

Each body names the other by number, so neither is merged alone by accident. The changelog pull request carries
`Closes #<issue>` for the issue that tracks this release; the version-bump one closes nothing, because bumping is what
follows a release rather than what the release was for.

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
   It then publishes the image under vx.y.z to both registries as one manifest list under one digest, moves `latest`
   onto that digest — `latest` follows the newest release and never a nightly — and publishes the Helm chart against
   the digest the image publication produced, at the same version.

3. Merge the version-bump pull request (#B).
   After the tag, so main returns to naming the next release rather than the one just published.
```

## When a step fails

- **The tag is rejected.** Either step 1 did not merge, so the tagged commit's `VersionPrefix` is not `x.y.z`, or the
  two disagree for another reason. Check out the tagged commit and compare `scripts/read-declared-version.sh` against
  the tag. Do not force the tag; delete it, fix the disagreement, and tag again.
- **The tag names a version that already exists on its line.** The bump pull request from a previous release never
  merged. Merge it, then re-cut.
- **Publication fails partway.** Re-run the `Release` workflow on the same tag rather than rebuilding anything. It
  reconciles: a version both registries already carry from this commit is left alone, and one only a single registry
  carries is copied across by digest, so the artifact that reaches the second registry is the one the first published.
  A rebuild would produce a second artifact for one version, which is what the immutability assertion exists to
  prevent. `docs/operations/release-procedure.md` records the whole sequence and what each failure means.
- **The release is abandoned before the tag.** Close both pull requests. Nothing was published and no tag exists, so
  there is nothing to undo.
- **The release is abandoned after the tag.** It is not abandoned; it is released. Cut a patch from the release branch.

`docs/operations/release-procedure.md` records the same sequence for a reader who does not have this skill.
