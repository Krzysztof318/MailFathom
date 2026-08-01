---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-01
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Version the four public surfaces with SemVer, stamp builds from one declared prefix, and cut a release with a Git tag

## Context and Problem Statement

Nothing in MailFathom sets a version. `Directory.Build.props` declares no version property, so every assembly compiles to the MSBuild default of `1.0.0.0`; `deploy/helm/mailfathom/Chart.yaml` carries `appVersion: "0.0.0-unreleased"`; and `deploy/docker/Dockerfile` defaults `IMAGE_VERSION` to `0.0.0-unversioned`. All three are placeholders written deliberately, each with a comment pointing at this decision, and none of them can be filled in until it is made.

The decision question has three parts that are usually conflated: what a version number *promises*, where the number *comes from*, and which event turns an ordinary commit on `main` into a *release*. The first part is not answered by "pick SemVer", because MailFathom's public surface is not a compiled API. Its consumers bind to an MCP tool contract, a configuration schema, a database schema, and a deployment contract, and a versioning policy that talks about assembly compatibility makes a promise about the one surface nobody uses.

Recorded on issue 116. No numbered specification under `specs/` backs it. Issue 119 stamps the number this ADR defines, issue 156 publishes the artifacts, issue 187 joins the Helm chart to the same release run, issue 117 records the branching model this policy assumes, and issue 53 (specification 19) owns the migration baseline whose pre-release policy this decision ends.

## Decision Drivers

- The public surface is four contracts, none of which is a compiled API, and each of which breaks differently.
- An artifact must be rebuildable from the commit it names. `Directory.Build.props` already sets `Deterministic` and `ContinuousIntegrationBuild`, and issue 119 requires an artifact on disk to identify itself.
- The repository prefers platform capabilities to packages, and issue 119 requires that a shallow clone with no tags neither fails the build nor silently yields `0.0.0`.
- Development is trunk-based: `main` is the only long-lived branch, worked by a single maintainer through agents at irregular times.
- The release channel and the nightly channel must be unmistakably separate, in triggers, registries, tags, and metadata (issue 156).
- Nothing is published yet, so the scheme costs one edit now and a migration path later.

## Considered Options

The decision has two independent axes, and an option on one does not constrain an option on the other.

**What the number means:**

- SemVer 2.0.0 interpreted over the four public surfaces.
- CalVer, for example `2026.8.0`.
- A hybrid: a marketing-style `major.minor` plus a monotonic build number.

**Where the number comes from, and what cuts a release:**

- A. A declared `VersionPrefix` in `Directory.Build.props`, with an annotated Git tag as the release trigger.
- B. MinVer: Git tags are the only source of truth, and the version between tags is derived from commit height.
- C. Nerdbank.GitVersioning: a `version.json` file plus commit-height derivation and a build toolchain.
- D. GitVersion: a configuration file describing a branching model, from which the version is inferred.
- E. The version-bump commit is itself the release trigger — bump the declared base and let that commit publish the previous number.

## Decision Outcome

Chosen options: **SemVer 2.0.0 over the four surfaces**, and **A, a declared `VersionPrefix` with a Git tag as the release trigger**.

SemVer wins because MailFathom's version has to answer a compatibility question, and that is the only thing SemVer encodes. CalVer answers "when was this built", which the `org.opencontainers.image.created` label and the commit SHA already answer, and it would leave an operator with no way to tell an upgrade that requires reconfiguration from one that does not — which is the exact question issue 116 opens with.

Option A wins because it keeps the number and the release decision as two separate signals, which is what every other option either conflates or pays a dependency to separate.

### What the number promises

A release's increment is the **highest increment any of the four surfaces requires**. A minor change to the tool contract shipped alongside a database schema that cannot be applied over the previous release's data is a major release.

| Surface | Major | Minor | Patch |
| --- | --- | --- | --- |
| MCP tool contract | A tool or argument is removed or renamed; an accepted argument range narrows; a result field is removed or changes type | A tool is added; an optional argument is added; a result gains a field | Behavior is corrected within the contract already documented |
| Configuration schema | A key is removed or renamed; validation tightens so a previously valid value now fails startup; a default changes such that an unchanged configuration behaves differently | An optional key is added; validation widens; an enumeration gains a member | Validation is corrected to match documented behavior it never matched |
| Database schema | The release cannot be deployed over the previous release's data | A migration applies forward without operator action | No schema change |
| Deployment contract | An environment variable, port, volume, or process account changes in a way that an unchanged deployment does not survive | An optional environment variable or port is added | The image is rebuilt with no contract change |

Removal above `1.0.0` requires a deprecation window: the thing being removed is announced as deprecated in a minor release, remains functional for at least one further minor release, and is removed in the next major. The announcement lives in `CHANGELOG.md` and, where the surface can carry it, in the surface itself — a tool description, an option's XML documentation, a startup warning.

### Deciding the increment

The table is read as a procedure rather than as a menu. Ask all four surfaces, take the highest answer, and stop; there is no weighing of how *much* of a surface moved, because the question is whether an unchanged consumer survives, and that has no degrees.

One question settles most cases: **does something that worked before the upgrade stop working after it, without the operator doing anything?** If yes, it is major. If nothing breaks but something new is available, it is minor. If nothing breaks and nothing is new, it is patch. The table's rows are that question applied to each surface.

The cases where two readings are defensible recur, so they are settled here rather than re-argued per release:

- **A new configuration key is minor only while it is optional.** A required key is major, because an unchanged configuration fails startup, and "it is only one line to add" describes the fix, not the break.
- **A changed default is major when an unchanged deployment behaves differently after the upgrade**, and minor when it only affects deployments created afterwards.
- **Correcting behavior is patch only when the documented contract already described the corrected behavior.** When the contract itself moves, the change is minor or major even though the code looks like a fix — a consumer bound to what the software did, not to what the documentation said, is still a consumer.
- **A result gaining a field is minor; a field changing type or meaning is major**, even when the field name survives. A rename is a removal and an addition.
- **Validation widening is minor, narrowing is major.** A value that started a host yesterday and refuses to today is a break regardless of whether it should ever have been accepted.
- **A migration that applies forward unattended is minor; one that cannot be applied over the previous release's data is major**, whatever the code around it did.
- **Performance, logging, telemetry, and internal structure carry no increment of their own.** They earn one only when they change something the four surfaces publish.

When a case still reads both ways after those, take the higher increment. An unnecessary major costs an operator one careful upgrade; an unmarked break costs them an outage, and only one of the two is recoverable by reading the changelog.

### What `0.x` suspends, and what it does not

The first release is `0.1.0`, matching the `0.1.0 — first public release` milestone. SemVer 2.0.0 section 4 makes `0.y.z` carry no compatibility promise at all, which is honest for a product whose schema is still settling. MailFathom narrows that deliberately rather than accepting it wholesale:

- Within `0.x`, a **minor** bump may break any of the four surfaces. Every break is named in that release's changelog entry, against the surface it breaks.
- Within `0.x.y`, a **patch** is compatible on all four surfaces. This is a real promise, not a `0.x` disclaimer.
- The deprecation window does not exist below `1.0.0`. Something may be removed in the next minor with a changelog entry, without a release of notice.

The most consequential clause is about data. Root `AGENTS.md` currently states that the repository keeps exactly one migration, `Initial`, and that a model change regenerates it rather than adding a second one, destroying local data by design. **That policy ends the moment `v0.1.0` is tagged.** From the first release onward a schema change is a new migration, never a regenerated baseline, and every release must be deployable over the previous release's data unless its changelog entry states otherwise and the increment reflects it. Accepting this ADR therefore obliges a matching edit to `AGENTS.md`, to `docs/operations/`, and to the `$add-migration` skill, and it is the point at which specification 19's baseline stops being disposable.

### Where the number comes from

`<VersionPrefix>` in `Directory.Build.props` is the only place in the repository where a version number is written. Everything else is derived there, centrally, for every project: `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion`, the last carrying the commit SHA through `SourceRevisionId`. No project sets a version of its own, and no code restates one as a literal — the runtime reads it from assembly metadata, which is issue 119's obligation.

Continuous integration contributes exactly two inputs, `VersionSuffix` and `SourceRevisionId`, which yields four build kinds:

| Build | `VersionSuffix` | `InformationalVersion` | Published to |
| --- | --- | --- | --- |
| Local or pull request | none | `0.2.0+3f1c9ab` | nothing |
| Nightly | `nightly.<run number>` | `0.2.0-nightly.41+3f1c9ab` | Docker Hub and GHCR |
| Release candidate (reserved) | `rc.<n>` | `0.2.0-rc.1+3f1c9ab` | nothing, for now |
| Release | none | `0.2.0+3f1c9ab` | Docker Hub and GHCR |

Within one version the ordering works out without further rules. SemVer compares alphanumeric prerelease identifiers in ASCII order, so `0.2.0-nightly.41` precedes `0.2.0-rc.1`, which precedes `0.2.0`. A nightly is therefore always a preview of the release it will become and never sorts above it.

The guarantee stops at that version, and stating it any wider would be false. Because `VersionPrefix` on `main` names the *next* release, `main` carries `0.3.0-nightly.N` as soon as `v0.2.0` is released, and SemVer settles `0.3.0-nightly.1` against `0.2.0` on the major-minor-patch comparison before any prerelease rule applies — the nightly wins. Anything that selects the newest version by taking the maximum of the tags present therefore selects a nightly. **`latest` is chosen by excluding every version carrying a prerelease identifier and taking the highest of what remains, never by taking a maximum.** The same holds for any tooling, documentation example, or upgrade check that asks which version is current.

Two format constraints shape the artifact tags rather than the version itself. An OCI tag admits only `[a-zA-Z0-9_][a-zA-Z0-9._-]{0,127}`, so `+3f1c9ab` cannot appear in an image tag; the revision travels in `org.opencontainers.image.revision` and, for nightlies, in the tag's own identifier. Helm requires a full three-part SemVer for `version` and `appVersion`, so a two-part base such as `0.2` is not expressible anywhere the chart is involved.

### What cuts a release

An annotated tag `v<major>.<minor>.<patch>` pushed on a commit reachable from `main`, or from a `release/<major>.<minor>.x` branch. `main` produces every major and minor release; a `release/*` branch produces every patch and nothing else, under *The patch flow* below. Both are admitted by the trigger here rather than left to issue 117 to discover, because a commit on a release branch is by construction not reachable from `main`, and a trigger accepting only `main` would reject every patch this ADR requires.

The tag is preceded by one reviewed pull request and followed by one generated one:

1. **Prepare.** A release-preparation pull request closes `## [Unreleased]` in `CHANGELOG.md` into `## [x.y.z] - YYYY-MM-DD`. `VersionPrefix` already reads `x.y.z` and is not touched. This is the last point at which the release's contents are read as a whole before anyone can install them.
2. **Tag.** The annotated tag is pushed on the merge commit, so the tagged tree contains the released changelog rather than describing the release after it happened.
3. **Publish.** The release workflow runs, and before it publishes anything it asserts that the tag's version equals the `VersionPrefix` of the tagged commit, that the version is not a regression against the highest existing tag **on the same `major.minor` line** rather than against the highest tag overall — otherwise `v0.2.1`, cut after `0.3.0` has shipped, would be rejected as a regression when it is the ordinary shape of a hotfix — that `CHANGELOG.md` carries a non-empty section whose heading matches the tag, and that no artifact already exists under that version with different content. It then runs the build, test, container smoke, license, and vulnerability gates, publishing nothing if any fails, and publishes the image to both registries with the chart alongside it in one run, per issues 156 and 187.
4. **Reopen.** The workflow opens the follow-up pull request, which raises `VersionPrefix` to the next minor — or to the next major when the accumulated work already requires one — and opens a fresh empty `Unreleased`. **It is never raised to a patch**, because `main` does not produce patches; the next section says what does.

Step 4 is what keeps the declared prefix from being a thing somebody has to remember. If it is skipped, the failure is loud rather than silent: the next tag push repeats a version that already exists and step 3 rejects it.

Between the tag and the merge of that follow-up, nightlies briefly carry `-nightly.N` of an already released version. That is harmless — a prerelease identifier never occupies a stable version, and the acknowledgement gates in front of the nightly channel do not depend on the number — and the window is as short as merging one generated pull request.

The alternative shape considered, option E, was rejected on one point that has no workaround: it releases version *N* from a commit whose source tree already says *N+1*, so rebuilding from the revision recorded in the published artifact produces a different version. That contradicts the determinism the build already claims and the self-identification issue 119 requires. Its two-commit variant avoids the contradiction but needs a separate marker to say which commit is the release — which is what a tag already is, with immutability and a natural mapping onto the image tag, the chart version, and the GitHub release thrown in.

### The patch flow

**A patch is never cut from `main`.** `main` carries one line of development, and its `VersionPrefix` only ever moves forward to the next minor or major. Patching there is not merely discouraged — it is unavailable, because by the time `0.2.0` needs a fix, `main` already contains everything intended for `0.3.0`, and a patch released from it would ship that work under a number promising nothing had changed. That is the failure a patch number exists to rule out.

A patch lives on a **permanent branch named `release/<major>.<minor>.x`**, cut from the release tag of the line it patches.

- **It is created on demand**, the first time that line needs a fix, from the tag rather than from any later commit. A line that never needs a patch never gets a branch, so the branch list stays a record of what was actually maintained rather than one entry per release.
- **It is never deleted.** Once a line has been patched, its branch is where that line's history lives, and a later patch to the same line reuses it rather than re-cutting from a tag that no longer reflects what shipped.
- **It carries its own `VersionPrefix`**, reading `<major>.<minor>.<patch>`. This is the only place in the repository where a patch number is ever written.
- **It publishes through the same three-step sequence** as any other release — preparation pull request, tag, generated follow-up — and its follow-up raises `VersionPrefix` to the next *patch* on that branch, which is the one place that is correct.
- **It never produces a nightly.** The nightly channel previews `main` and nothing else.

**The fix reaches `main` first.** Where the code being fixed still exists there, the change merges to `main` through the ordinary flow and is then cherry-picked onto the release branch, so a fix cannot be lost when the next minor ships. Only where `main` no longer contains the code — the fix applies to something already replaced — does the branch carry a change of its own, and its changelog entry says so explicitly, because that is the case a reader would otherwise assume was an oversight.

**Only the newest released minor is patched by default.** Reaching further back is a deliberate decision recorded on the issue that asks for it, not something that follows from a branch still existing. A single maintainer cannot support an unbounded number of lines, and a policy that implies otherwise makes a promise the project cannot keep.

This flow **breaks the repository's own final gate as it currently stands**, and that has to be fixed with it rather than discovered during an incident. `scripts/verify-full.sh` requires `origin/main` to be an ancestor of `HEAD` and computes its diff validation as `origin/main..HEAD`. A branch cut from `v0.2.0` while `main` sits on `0.3.0` contains no such ancestor, so the gate refuses to run before it builds anything, and the diff it would check is every change `main` made since the tag. The check's purpose is to prove a change was verified against the base it will actually merge into, so it generalizes rather than gets an exception: on a `release/*` branch that base is the release branch's own upstream, and only on an `agent/*` branch is it `origin/main`. Exempting hotfix work from the gate is the alternative, and it is the wrong one — a patch is the change most likely to be written under time pressure and least likely to be re-reviewed.

### Channels

Both channels publish to **both** registries. A release appears on Docker Hub and on GHCR under the same version and the same digest; so does a nightly. One build produces one manifest list, which is pushed twice, so the two registries are mirrors rather than two independently built artifacts that happen to share a name. An operator who is already authenticated against one registry, or whose cluster may only pull from one, is never the reason a MailFathom version is unreachable.

That decision moves the burden of separating the channels, because the registry hostname no longer carries it. The separation lives in the version identifier instead, which is a stronger place for it: **a nightly always carries a `-nightly.<n>` prerelease identifier and a release never carries any prerelease identifier at all**. This is decidable from the reference alone, by a human reading it and by a schema validating it, and it holds regardless of which registry the image came from. It is also what was always actually true — an image was never a nightly because of its hostname.

Two moving tags exist in each registry and are the only mutable references: `latest` follows the highest release selected by the prerelease-excluding rule above rather than by a maximum, and `nightly` follows the newest nightly. Every other tag is immutable.

A channel label backs that up for a reference that has been rewritten, re-tagged, or reduced to a digest — but **it does not exist yet**, and this ADR creates it rather than describing it. `deploy/docker/Dockerfile` declares only `org.opencontainers.image.*`, so no image built today states its channel at all. What exists is two labels of a different kind and a different value: `deploy/compose/compose.nightly.yaml` sets the *container* label `io.mailfathom.release-channel: ghcr-nightly-unsupported` on one overlay, and the chart's `_helpers.tpl` emits the *Kubernetes object* label `io.mailfathom/release-channel` — a slash, not a dot — with the same value.

Accepting this ADR adds `io.mailfathom.release-channel` to the image itself, valued `release` or `nightly`, and moves the two existing labels to those same values. The two spellings both stay: a dot is the OCI convention for an image label and a slash is what Kubernetes requires of a label key prefix, so they are the same name written the way each ecosystem reads it, not a divergence to be tidied up.

This **changes issue 156** in three places, all amended in the change set that accepts this ADR.

- Its central premise — "A stable release is never published to GHCR, and a nightly build is never published to Docker Hub" — is reversed. That premise existed to make a development snapshot impossible to mistake for a supported release, which the prerelease identifier and the channel label do without also making one registry the single point of failure for reaching a release.
- Its acceptance forbids a `schedule` trigger outright. The reason it forbade one was that no nightly identifier had been defined, so a scheduled build had nothing meaningful to call itself; this ADR defines it and the constraint expires with its cause. Nightlies run daily, guarded so a run publishes nothing when `main` has not moved since the last published nightly, with `workflow_dispatch` retained for an out-of-band snapshot.
- Its release trigger is written as "an accepted version bump reaches the protected default branch", deferring to whatever this issue decided. The answer is the tag, so that clause becomes an annotated `v<x.y.z>` tag on a commit reachable from `main` or from a `release/<major>.<minor>.x` branch.

The larger consequence is for the deployment assets, which encode the old premise as working enforcement in four places. None of it is in the values schema: `deploy/helm/mailfathom/values.schema.json` declares `image.registry` as a bare `{"type": "string"}` and says so itself, noting that cross-field constraints "are enforced by the chart's own validation helper".

- `deploy/helm/mailfathom/templates/_helpers.tpl` holds both halves of the chart's guard. `mailfathom.validate` fails a nightly install whose `image.registry` is anything but `ghcr.io`, and `mailfathom.image` then substitutes `ghcr.io` on the nightly channel regardless of what was configured — so even removing the rejection would leave the reference silently rewritten. Both have to move.
- `deploy/compose/compose.nightly.yaml` hard-codes `ghcr.io` for its nightly image.
- `scripts/verify-deployment-assets.sh` asserts the premise mechanically, and root `AGENTS.md` makes that script the gate for any change under `deploy/`, so the accepting change set fails its own gate until the script moves with it. It expects a nightly render pointed at `docker.io` to be rejected with "published only to ghcr.io"; it fails the run if `compose.yaml` names `ghcr.io` at all, which is now where a release is published too; and it asserts that a nightly render still contains `ghcr-nightly-unsupported`, which the label change above supersedes. `AGENTS.md` describes that script as rejecting "a Compose file that reaches the nightly registry", and that sentence goes with it.
- `docs/operations/deployment-compose.md` and `docs/operations/deployment-kubernetes.md` document the GHCR-only nightly, the forced registry, and the old label value as implemented behavior, which `docs/AGENTS.md` requires to stay true.

The two enforcement points move to the check the identifier already supports — the nightly channel requires a `-nightly.` reference and the release channel rejects one — which is a stricter guard than the hostname it replaces, because it holds whichever registry the image came from. The `values.schema.json` description of `image.channel` still says nightly is "published to GHCR" and is corrected with them. The acknowledgement gates in front of the nightly channel are untouched; nothing about reaching a nightly gets easier.

### Release candidates

No release-candidate channel exists for `0.1.0`. `-rc.<n>` is reserved in the scheme so that adopting one later requires no renumbering and no change to the ordering rules, but nothing produces or publishes an rc build. A single maintainer with a nightly channel already has a pre-release path, and a candidate nobody installs is ceremony that still has to be built, tagged, retained, and documented.

### Changelog

`CHANGELOG.md` at the repository root, hand-written in [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) form, with an `Unreleased` section that accumulates entries as changes merge.

GitHub release notes are generated from the changelog rather than the other way round. Automatically generated notes are a list of pull-request titles, and no arrangement of pull-request titles can state that a release cannot be deployed over the previous release's data — which is the single most important sentence a MailFathom release note will ever contain.

Choosing the file settles nothing on its own; a changelog decays unless the rules for keeping it are as explicit as the rules for the version number it documents. The following are those rules.

**An entry is written in the change set that causes it, never at release time.** The pull request that changes behavior adds its own line under `Unreleased`, in the same diff, reviewed by the same reader. Reconstructing a release's entries afterwards from merged titles produces exactly the list that generated release notes already produce for free, which is the thing this file exists not to be.

**What earns an entry is what a consumer of a release would notice.** That means anything reaching one of the four surfaces: a tool contract, a configuration key, the database schema, or the deployment contract; plus a fixed defect that was observable from outside, and any change with a security consequence. Nothing else does. A refactor, a new test, a CI adjustment, a documentation edit, and an internal rename get no entry, because a file that records them stops being read and then stops being written. When a change is genuinely invisible from outside, the correct entry is none.

**Entries are grouped by the six Keep a Changelog categories** — `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security` — and each entry references the issue or pull request that carried it.

**A breaking entry names its surface and states the operator's action.** It opens with `**Breaking (<surface>)**`, using the four surface names from the table above, and it says what has to be done rather than only what changed. "Renamed `Mail:Accounts:Folders` to `Mail:Accounts:MailboxFolders`" is a fact; "…; rename the key in your configuration before upgrading, or the host fails startup" is the entry. `Deprecated` entries name the release in which the thing will be removed.

**A release that touches the database schema says so, in the terms an upgrade is planned in:** whether a migration must be applied, whether it can be applied while the previous version is still running, and whether the release can be deployed over the previous release's data at all. This is the one entry an operator reads before scheduling a window, and it is the clause the database row of the surface table exists to force.

**Nightly and prerelease builds get no section of their own.** They are, by definition, whatever `Unreleased` currently describes.

**Releasing is what closes the section, and it is a reviewed step.** A release-preparation pull request renames `## [Unreleased]` to `## [x.y.z] - YYYY-MM-DD` and updates the link references at the foot of the file; `VersionPrefix` already reads `x.y.z` and is not touched. Once it merges, the tag is pushed on that merge commit, so the tagged tree contains the released changelog rather than describing it afterwards. The follow-up pull request the release workflow opens then does two things together: it raises `VersionPrefix` to the next minor and it opens a fresh empty `Unreleased`.

**Enforcement is review, with one mechanical check.** No gate can distinguish a user-visible change from a refactor, so requiring every pull request that touches `src/` to touch `CHANGELOG.md` would train everyone to add filler. The obligation belongs to `$check-docs-licenses` and `$finish-change`, beside the documentation obligation it resembles. What *is* mechanical, and therefore is checked, is the release: the release workflow refuses to publish when `CHANGELOG.md` has no section whose heading matches the tag being released, or when that section is empty.

### What accepting this ADR obliges

Nothing here is done by proposing it. Moving the status to `accepted` commits the project to a change set that:

- sets `VersionPrefix` to `0.1.0` in `Directory.Build.props` and derives the four version properties centrally there (issue 119);
- creates `CHANGELOG.md` with an `Unreleased` section, and adds the entry obligation and its exclusions to `$check-docs-licenses` and `$finish-change`;
- amends issue 156 on the registry premise, the schedule, and the release trigger, and issue 187 on how the chart's version derives from the release;
- moves the nightly guard off the registry hostname and onto the prerelease identifier in all four places that hold it — the `mailfathom.validate` rejection and the `mailfathom.image` registry default in `deploy/helm/mailfathom/templates/_helpers.tpl`, the hard-coded registry in `deploy/compose/compose.nightly.yaml`, the three assertions in `scripts/verify-deployment-assets.sh`, and the `image.channel` description in `values.schema.json`;
- updates `docs/operations/deployment-compose.md` and `docs/operations/deployment-kubernetes.md`, which document the GHCR-only nightly and the forced registry as implemented behavior, and the sentence in root `AGENTS.md` describing the verification script as rejecting a Compose file that reaches the nightly registry;
- adds the `io.mailfathom.release-channel` label to `deploy/docker/Dockerfile`, which carries none today, and moves the existing container and Kubernetes labels from `ghcr-nightly-unsupported` to `release` or `nightly`, together with the `verify-deployment-assets.sh` assertion that still expects the old value;
- replaces root `AGENTS.md`'s single-regenerated-migration rule with the freeze described above, and updates `docs/operations/` and the `$add-migration` skill to match;
- generalizes the base check in `scripts/verify-full.sh` from `origin/main` to the branch the change will merge into, so a `release/*` branch can pass the gate at all, and updates the branch rules in root `AGENTS.md` alongside it;
- adds the release and nightly workflows that assert the prefix, the same-line regression rule, the changelog section, and the immutability before publishing anything.

Until then, `0.0.0-unreleased` in `Chart.yaml` and `0.0.0-unversioned` in the `Dockerfile` stay exactly as they are.

### Consequences

- Good, because the number lives in one reviewed line: raising a major becomes a decision visible in a pull-request diff, reviewed against the four surfaces, rather than a tag typed from a shell.
- Good, because no build ever depends on tag history. A shallow clone with no tags produces `0.2.0+<sha>`, which is the correct answer rather than a degraded one, and issue 119's `0.0.0` failure mode cannot occur.
- Good, because the release is reproducible: the tagged commit's tree declares the version the artifact carries, and rebuilding it produces the same number.
- Good, because no dependency is added, no lock file moves, and `THIRD_PARTY_LICENSES.md` gains no row.
- Neutral, because the release becomes three steps — a preparation pull request, a tag, and a generated follow-up — of which only the first two are manual, and both are the deliberate acts a release should consist of.
- Neutral, because `VersionPrefix` on `main` names the *next* release rather than the last one, so a build from `main` is always a preview. This is the same convention MinVer's `MinVerMinimumMajorMinor` expresses, arrived at without the package.
- Good, because publishing both channels to both registries makes neither registry a single point of failure for reaching a MailFathom version, and the prerelease identifier separates the channels more reliably than a hostname did.
- Bad, because the prefix and the tag can disagree, and only the workflow's assertion prevents a disagreement from publishing. The check is mandatory rather than advisory for this reason.
- Good, because keeping patches off `main` means a patch cannot accidentally ship the next minor's work under a number that promises nothing changed, which is the whole reason a patch number is trusted.
- Bad, because a permanent branch per patched line is real maintenance: a fix that matters to two lines is written once and cherry-picked twice, and the support window has to be stated and then honoured rather than implied by a branch still existing.
- Bad, because two registries mean two credentials, two pushes, and a state where one succeeded and the other did not. The release run pushes the same manifest list to both and reports a partial publication as a failed release, so the recovery is a documented re-push by digest rather than a rebuild.
- Bad, because deciding a release's increment requires judging four surfaces rather than reading a diff, and nothing mechanical can make that judgement. The changelog's per-surface structure is what makes the omission visible in review.
- Bad, because freezing the migration baseline at `0.1.0` removes the regeneration workflow that has been convenient throughout development, and every later schema change costs a reviewed forward migration.

## Validation

- The release workflow's assertion that the tag version equals the tagged commit's `VersionPrefix` is the primary machine check, and it gates publication.
- A unit test asserts that the version reported at host startup and in the MCP `initialize` response is read from assembly metadata, so the reporting path cannot regress to a plausible-looking literal (issue 119).
- The Helm chart's existing drift check compares `image.tag` against `Chart.appVersion` on the release channel and activates on its own the moment a real `appVersion` replaces `0.0.0-unreleased`. Nothing has to be written for it; stamping the version is what switches it on. It is a default rather than a binding validation, and an audit against this ADR must not read it as one: it refuses by default, is turned off by `image.allowVersionMismatch`, and does not apply at all to a deployment that names the image by `image.digest`, because there is then no tag to compare.
- The release workflow refuses to publish when `CHANGELOG.md` carries no section whose heading matches the tag, or when that section is empty. This is the only mechanical check on the changelog, deliberately: a rule requiring every pull request touching `src/` to touch the file would be satisfied by filler.
- Review enforces the rest: an entry exists for every change a consumer of a release would notice and for nothing else, a breaking entry names its surface and the operator's action, and the increment matches the highest surface affected.

## Pros and Cons of the Options

### SemVer 2.0.0 over the four surfaces

The increment encodes a compatibility promise; the promise is defined per surface, and a release takes the highest.

- Good, because it answers the operator's actual question — can this upgrade break my configuration, my clients, or my database.
- Good, because every consuming ecosystem already parses it: OCI tooling, Helm, NuGet, and dependency scanners.
- Neutral, because it says nothing about age, which the OCI `created` label and the revision cover instead.
- Bad, because assigning an increment requires judging four contracts, which no tool can check.

### CalVer

The version is the release date, for example `2026.8.0`.

- Good, because it needs no judgement and cannot be argued about.
- Good, because it makes the age of a deployed artifact obvious at a glance.
- Neutral, because a project releasing irregularly gets version numbers with visible gaps, which is accurate but reads as neglect.
- Bad, because it makes no compatibility statement, so a breaking change to the configuration schema is indistinguishable from a typo fix — the failure issue 116 was opened to prevent.
- Bad, because Helm and OCI consumers that sort versions get an ordering with no meaning attached to it.

### A hybrid `major.minor` plus a build number

A marketing-style pair with a monotonic counter, in the shape Ubuntu and some commercial products use.

- Good, because the counter is trivially derivable from CI and never collides.
- Neutral, because it can be made SemVer-shaped with effort.
- Bad, because it invites the reading that the counter is a patch level while it actually counts builds, so `0.2.41` and `0.2.42` may differ by a breaking change or by nothing.
- Bad, because it needs a second, informal convention to say what `major.minor` means, which is the policy this ADR exists to write down.

### A. A declared `VersionPrefix` with a Git tag as the release trigger

One `VersionPrefix` line in `Directory.Build.props`; CI adds `VersionSuffix` and `SourceRevisionId`; an annotated `v<x.y.z>` tag on `main` cuts the release, verified against the prefix.

- Good, because it adds no package, no lock-file movement, and no register entry, which is the ordering root `AGENTS.md` asks for.
- Good, because it works in a shallow clone with no tags and no Git history at all, including inside the container build context.
- Good, because raising the version is a reviewed diff, which suits a version number that is a compatibility promise over four contracts.
- Good, because the tag maps one-to-one onto the image tag, the chart's `appVersion`, and the GitHub release.
- Neutral, because the prefix names the next release rather than the last, so every `main` build is a preview.
- Bad, because two things can disagree — the prefix and the tag — and a workflow assertion rather than the type system is what keeps them aligned.
- Bad, because a release is three steps rather than one: a preparation pull request that closes the changelog, the tag, and a generated follow-up that must merge before the next release.

### B. MinVer

MinVer 7.0.0, Apache-2.0, <https://github.com/adamralph/minver>. A build-only MSBuild package that derives the version from the nearest Git tag and the commit height above it.

- Good, because the version is genuinely derived, with nothing to keep in sync and nothing to forget.
- Good, because it is small, build-only, and adds no runtime surface, and its license is the project's own.
- Good, because `MinVerMinimumMajorMinor` expresses "the version we are working toward" — which is exactly the base this decision wanted.
- Neutral, because adopting it later would not change any published number, so this rejection is reversible.
- Bad, because it needs full Git history and tags in every build. `actions/checkout` defaults to a shallow clone without tags, and the container build context carries no `.git` at all.
- Bad, because with no reachable tag it yields `0.0.0-alpha.0.<height>`, which is precisely the silent `0.0.0` that issue 119 rules out.
- Bad, because once `MinVerMinimumMajorMinor` is set to keep that from happening, a literal lives in `Directory.Build.props` anyway — option A with a package attached.

### C. Nerdbank.GitVersioning

Nerdbank.GitVersioning 3.10.91, MIT, <https://github.com/dotnet/Nerdbank.GitVersioning>. A `version.json` file plus commit-height derivation, an `nbgv` CLI, and cloud-build integration.

- Good, because `version.json` is an explicit declared base, so the shallow-clone problem is smaller than MinVer's.
- Good, because it emits build variables for several CI systems without hand-written glue.
- Neutral, because its per-branch version rules would go unused under a trunk-based model.
- Bad, because it is a second toolchain — a file format, a CLI, and MSBuild integration — to learn, pin, document, and register, for a repository that publishes one artifact from one branch.
- Bad, because the commit-height component makes the version depend on history depth, so a rebase changes it.

### D. GitVersion

GitVersion.MsBuild 6.8.2, MIT, <https://github.com/GitTools/GitVersion>. A configuration file describing a branching model, from which the version is inferred per branch.

- Good, because it handles complex branching models, including release and hotfix branches, without hand-written rules.
- Neutral, because its GitFlow assumptions can be configured away.
- Bad, because its value is proportional to branching complexity, and MailFathom has one long-lived branch by decision — most of what it does would be configuration to switch off.
- Bad, because it is the heaviest of the three: full history, a configuration file whose semantics are their own subject, and version output that has historically shifted between major versions.

### E. The version-bump commit as the release trigger

Raising the declared base from `0.2.0` to `0.3.0` publishes `0.2.0` from that same commit; subsequent nightlies become `0.3.0-nightly.N`.

- Good, because releasing is a single act with no tag and no second step.
- Good, because the intent is visible in the pull request that raises the number.
- Neutral, because the two-commit variant — one commit marks the release, a second raises the base — removes the worst consequence below.
- Bad, because the released artifact's source tree declares a different version than the artifact carries, so rebuilding from the revision the artifact records produces a different number. This contradicts `ContinuousIntegrationBuild` determinism and issue 119's self-identification requirement, and no amount of labelling repairs it.
- Bad, because the two-commit variant still needs a marker identifying which commit is the release, which is a tag under another name and without a tag's immutability.
- Bad, because a bump merged for any other reason — a correction, a revert, a rebase that replays it — publishes a release.

## More Information

Normative references consulted for this decision: [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html) for precedence and the meaning of `0.y.z`; the [OCI distribution tag grammar](https://github.com/opencontainers/distribution-spec/blob/main/spec.md) for what an image tag may contain; the [Helm chart `version` and `appVersion` fields](https://helm.sh/docs/topics/charts/), which require full SemVer 2; and [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/).

Related issues: 116 records this decision; 119 stamps the number into builds and reports it at runtime; 156 publishes the container image, and is amended here on the registry premise, the schedule, and the release trigger; 187 joins the Helm chart to the same release run; 117 records the branching model and inherits the patch flow settled here — the permanent `release/<major>.<minor>.x` branch, its `VersionPrefix`, the cherry-pick direction, and the support window — leaving 117 the questions that are genuinely branching rather than versioning: branch protection on a release branch, who may push to it, and how its existence interacts with the board automation; 53 owns the migration baseline this decision freezes at the first release.

Revisit this ADR at `1.0.0`, when the deprecation window becomes binding and the `0.x` clauses expire; if a second maintainer joins, which changes what the tag-plus-bump sequence costs; or if a release cadence emerges that makes a candidate channel worth its cost.
