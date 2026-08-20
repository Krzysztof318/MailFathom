# The release procedure

<!-- describes: Directory.Build.props, server.json, .github/workflows/release.yml, .github/workflows/publish-helm-chart.yml, .github/workflows/submit-winget-manifest.yml, scripts/assert-release-tag.sh, scripts/read-declared-version.sh, scripts/build-winget-manifests.sh, .agents/skills/prepare-release/SKILL.md -->

MailFathom's version number is a compatibility promise over four public surfaces. This page records how a build
acquires that number, how the official MCP Registry metadata records the released one, where both are observable, and
the sequence that turns a commit on a release branch into a release.
[ADR 0004](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md)
records why the build and release use their declared version and tag.

## Where the number comes from

`<VersionPrefix>` in `Directory.Build.props` is the only place where a version is declared for the build. Every project
derives `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion` from it centrally, and no project sets a
version of its own. The top-level `version` in `server.json` serves a different purpose: it always records the latest
stable MailFathom release and the version published to the official MCP Registry, so it remains on the current release
while `VersionPrefix` on `main` names the next one.

It names the **next** release rather than the last one, so every build from `main` is a preview of what the next tag
will carry. Raising it is a reviewed diff.

Continuous integration contributes exactly two inputs and nothing else:

| Input | Supplied as | Effect |
| --- | --- | --- |
| Prerelease identifier | `VersionSuffix` | `0.1.0` becomes `0.1.0-nightly.41-3f1c9ab` |
| Source revision | `SourceRevisionId` | `InformationalVersion` gains `+3f1c9ab` |

That yields the four build kinds the ADR tabulates. Neither is set in the repository. A build inside a Git worktree
resolves the revision on its own, because the SDK's source-link support reads it from the checkout; the container build
has no repository in its context, which is why `deploy/docker/Dockerfile` passes `IMAGE_REVISION` through to the
publish as `SourceRevisionId` rather than letting the image's assemblies fall silent about their commit while the label
beside them names one.

**Nothing reads Git tags**, so a shallow clone stamps exactly what a full clone does; there is no degraded fallback and
no way to silently produce `0.0.0`. What a shallow clone changes is the revision's availability, not the version's.

Read the number rather than retyping it:

```bash
scripts/read-declared-version.sh              # 0.1.0
scripts/read-declared-version.sh nightly.41-3f1c9ab   # 0.1.0-nightly.41-3f1c9ab
```

The script parses `Directory.Build.props` rather than evaluating it through MSBuild, so it works inside a container
build context and on a machine with nothing restored. It rejects a prefix that is not a three-part version, because
Helm accepts nothing else for `version` and `appVersion`, and a suffix that is not a SemVer prerelease identifier,
because an OCI tag would reject it later and further from the cause.

## Where the version is observable

| Where | What it says | Read without running the artifact |
| --- | --- | --- |
| Host startup record | `ServiceVersion` and `ServiceRevision` | no |
| Exported telemetry | `service.version` and `vcs.ref.head.revision` on the resource of every log record, metric point, and span | no |
| MCP `initialize` | the server's implementation version | no |
| `org.opencontainers.image.version` and `.revision` | the image's version and commit | yes |
| The image's tags, including `latest` | which release this is | yes |
| A packaged chart's `version` and `appVersion` | the chart release, and the application version it deploys | yes |
| The release's `mailfathom-schema-<version>.sql` | which schema that version expects | yes |
| The published assemblies | `AssemblyInformationalVersion` | yes |
| `server.json` and the official MCP Registry | the latest stable MailFathom release published for agent discovery | yes |

Every build and artifact surface comes from the same declaration. The runtime paths read the assembly's own metadata
rather than a literal restated in code, and unit tests assert that by deriving their expectation from that metadata; a
reporting path that regressed to a hardcoded string fails them rather than staying plausible while being wrong.
`server.json` is copied onto that version by the release-preparation pull request because it describes a published
Registry entry rather than a preview build.

Four of those surfaces go one step further and say what the version *means for the reader*: where to read about it. The
image's `org.opencontainers.image.documentation` label, the chart's install notes, `mfctl status`, and the instructions
an MCP session carries each name the documentation site's directory for the version in front of them — one derivation,
applied by whichever of them is speaking, with no configuration key anywhere in it. [The addresses that outlive a
release](documentation-site.md#the-addresses-that-outlive-a-release) is where that convention and its one exception for
a prerelease are stated.

The native process deployment needs nothing further: `dotnet publish` writes the stamped assemblies, so the artifact on
disk carries its own version and revision without being started.

**No other build input writes a version number**, which is what makes drift among artifacts impossible rather than
merely checked. The image's labels and tags arrive as build arguments, and the chart's `appVersion` arrives at package
time:

```bash
version="$(bash scripts/read-declared-version.sh)"
helm package deploy/helm/mailfathom --version "$version" --app-version "$version"
```

`Chart.yaml` therefore carries no `appVersion` of its own, and its `version` is a `0.0.0` placeholder that the release
overrides — as close as a required field can come to declaring nothing.

**The chart's version and the application's version are one number.** A packaged chart embeds its `appVersion`, so
every release produces chart content that differs from the last, and a published chart version is immutable; a chart
version counting edits to the chart directory would therefore need bumping on every release regardless, while leaving
an operator to map two numbers onto one artifact. Making them equal costs nothing that was worth keeping: a chart
change that ships without an application change is rare, and it ships under the release that carries it.

## The moving tags

Every published release carries its own immutable `<x.y.z>` image tag **and** moves `latest` onto the same digest. The
image tag drops the `v` the Git tag carries, because that is what an OCI reference is written as everywhere else and
what the Helm chart compares against `appVersion`; the `v` belongs to the Git tag, which is a different thing pointing
at the same commit. `latest` is what an operator gets by not choosing, so it must never be a preview:

- **`latest` follows the newest release and never a nightly.** It is chosen by excluding every version carrying a
  prerelease identifier and taking the highest of what remains, **never** by taking a maximum. Because `VersionPrefix`
  on `main` names the *next* release, `main` carries `0.3.0-nightly.N` as soon as `v0.2.0` is released, and a maximum
  over the tags present would select that nightly. The same holds for any tooling, documentation example, or upgrade
  check asking which version is current.
- **`latest` does not move for a patch to an older line.** `v0.2.1` cut after `0.3.0` has shipped is a supported
  release of the `0.2.x` line, and it is not the newest one; the prerelease-excluding rule above already gives the
  right answer, because `0.3.0` is higher.
- **`nightly` follows the newest nightly**, and is the only other mutable reference. Every other tag is immutable.

The `Release` and `Nightly` workflows implement this rule, publishing to `ghcr.io/krzysztof318/mailfathom` and
`docker.io/krzysztof318/mailfathom`. Both registries carry every version of both channels under the same digest.
[The container image](container-image.md#published-images) records what each tag means and how a published image is
verified.

## What earns which increment

The release's increment is the **highest** increment any of the four surfaces requires — the MCP tool contract, the
configuration schema, the database schema, and the deployment contract. One question settles most cases: does something
that worked before the upgrade stop working after it, without the operator doing anything? If yes it is major; if
nothing breaks but something new is available it is minor; if neither, it is patch. The ADR's table applies that
question per surface and settles the recurring ambiguous cases.

The reading that answers it happens at release time, from what merged since the previous tag, and it is the same
reading that produces the changelog section.

**The increment is judged on what those four surfaces actually moved, whether or not the feature a change belongs to is
finished.** A release delivering three of a feature's ten parts still moves the database schema when one of the three
added a table, and an increment lowered because the capability is not usable yet would understate an upgrade the
operator has to plan for. Incompleteness governs how the changelog *describes* a release, which `$prepare-release`
holds; it never governs the number.

**`CHANGELOG.md` is written by the release pull request and by nothing else.** Ordinary work never touches it: a
changelog is a statement about a release, and until someone decides to cut one there is no release for a line to belong
to. The file is a protected path, so an edit arriving through ordinary work is visible as the exception it is, and
`$check-docs-licenses` reports `n/a` for it on every change that is not a release.

## Cutting a release

The owner invokes `$prepare-release`, which reads the version, refuses the states that must not be released, settles
the milestones, opens both pull requests, and prints the ordering. It is manual-invocation only — no agent can reach
it — because when a version becomes real is a decision rather than a consequence of work looking finished.

**Settling the milestones comes before the pull requests**, because the milestone is the release's gate: the next
milestone is created if it does not exist, the issue tracking the *next* release is opened in it unless one already is,
whatever is still open in the one being released moves into it *except the issue tracking this release*, and the one
being released is closed. It is created *if it does not exist* because a parent issue targeting a later release opens
that milestone earlier, which is the other place one comes from and the reason the release being worked is the lowest
version among the open milestones rather than the only one; `docs/operations/issue-tracking.md` holds the rule and the
reasoning. The tracking issue is open and in that milestone at this point, so it is what a query for what to move
returns; it stays where it is and stays open, because the last of the three steps below closes it.

A release and the issue that tracks it therefore arrive together, which is what keeps a release from depending on
somebody remembering to open one: the step above opens that issue whether it created the milestone or found one a
parent had already opened as its target. The issue is deliberately short: it states the ordering, what gets published,
and what follows the tag, and leaves what the release *carries* to the changelog section written when it is cut.
Nothing about it is a precondition either — what is still open in the milestone moves as part of this same step, so a
release never waits on a cleared milestone.

The sequence the skill prints is the whole of what follows, and it is recorded here so it survives the skill being
unavailable:

1. **Merge the changelog pull request, titled `[#<issue>] Prepare release x.y.z`.** It adds
   `## [x.y.z] - YYYY-MM-DD` with the release's entries, composed from what merged since the previous tag, and it
   brings `server.json` and the three files that name a version in prose onto that version: the Registry metadata's
   top-level `version`, the **Project status** paragraph and the **Where the artifacts are published** table in
   `README.md`, the **state of the release** section in `docs/users/README.md`, and the **Supported versions** table in
   `SECURITY.md`. It changes no other Registry metadata unless the release carries a separately reviewed metadata
   change, which is read here for the same reason the version is — the tagged tree is what the Registry publishes, so a
   field that went stale during the cycle would be published as this release's own description of the server — and
   `mcp-publisher validate` runs on any such edit. It merges first because **its merge commit is what gets tagged
   and published**, so the tagged tree contains the released changelog, the metadata published to the Registry, and
   the files describing the release they ship inside rather than describing them afterwards.

   Those four files are the whole of what `<VersionPrefix>` does not reach *by name*: `server.json` asserts which
   stable release is current in the Registry, and the other three assert *which release is current*. Everywhere a page
   quotes a version because a reader substitutes one — an image
   reference, the `mailfathom-schema-<version>.sql` filename in the apply commands — it writes the placeholder and a
   release touches it not at all. Everything a build stamps derives from that one declaration; prose does not, and
   nothing checks it, which is why the short list is stated rather than searched
   for. The skill additionally sweeps the tree for prose that describes the release *state* without naming a version —
   "no versioned artifact exists yet", "a release will attach it" — because that kind of sentence goes stale at the
   moment of the tag and no search for the version number would ever find it. Each hit is read and either corrected in
   this same pull request or left alone; the sweep reports and never gates.

   The same pull request reads the two committed pages a registry renders — `deploy/docker/README.md`, pushed onto the
   Docker Hub repository page, and `deploy/helm/mailfathom/README.md`, packaged into the chart — against what this
   release actually publishes. Neither is on the list above and neither joins it, because neither asserts which release
   is current: one names no version at all and the other writes the placeholder. They are read here anyway because the
   tagged tree is what each listing renders, so a claim that went stale during the cycle would be published as this
   release's own description of the artifact.
2. **Push the annotated tag `v<x.y.z>` on that merge commit.** This is what makes the release real and what triggers
   the release workflow — which starts only for a version-shaped tag, so a tag that is not one starts nothing at all
   rather than a run that fails. **Read a pushed tag that produced no run as a malformed tag**, and check its spelling
   before looking anywhere else. Before publishing anything the workflow asserts the tag against the tagged commit's
   `VersionPrefix`, against the highest existing tag on the same `major.minor` line, and against the changelog section
   for that version — `scripts/assert-release-tag.sh` is that check. It then runs the same build, unit-test,
   formatting, and migration checks a pull request runs, followed by the integration suite, and builds nothing at all
   until both have passed. Only then does it build and gate the image, push it to
   both registries under `<x.y.z>`, move `latest` onto the same digest, attest it, publish the Helm chart against
   that digest, and open the GitHub release with that changelog section as its notes. Under the section it links
   `CHANGELOG.md` at the tag, so a reader of an older release reaches the file as that release shipped it rather than a
   copy already describing versions they have not upgraded to.

   ```bash
   git tag --annotate v0.1.0 --message 'MailFathom 0.1.0'
   git push origin v0.1.0
   ```

   It also builds the release's schema artifact and attaches it, and it blocks the push when that fails: an image
   published without the SQL an operator needs to reach its schema is an image that starts, fails the startup gate, and
   stays down. [Applying the database schema](database-schema.md) is what the artifact is; the release notes record its
   name, its checksum, and the migrations it carries.

   After that workflow succeeds, validate and publish the `server.json` from the tagged tree with the official
   `mcp-publisher`, then query the Registry API for `io.github.Krzysztof318/mailfathom` and confirm it returns version
   `x.y.z`. Publishing before the workflow succeeds would advertise a release whose installable artifacts do not yet
   exist; publishing from the later bump would advertise the next, unreleased version instead.

3. **Merge the version-bump pull request, titled `[#<issue>] Bump main version to <next>`.** It raises `VersionPrefix`
   to the next version, and it names the same issue the changelog pull request does and carries the `Closes` line for
   it, because a release is one unit of work and is finished when `main` names the next version rather than when the
   changelog merged. It merges after the tag, so `main` returns to naming the next release. Skipping it fails loudly
   rather than silently: the next tag push repeats a version that already exists, and step 2 rejects it — and the
   release's tracking issue is still open, which is the same failure visible a step earlier.

   It leaves `server.json` on `x.y.z`, because that file always records the latest stable MailFathom release while
   `VersionPrefix` returns `main` to naming the next release.

   It also brings the lock files with it. Each `packages.lock.json` records the version of every `MailFathom.*`
   project it references, so raising the declaration leaves them naming the release just published;
   `dotnet restore MailFathom.slnx --force-evaluate` rewrites them, and the diff is confirmed to hold nothing but
   those project lines. Nothing gates that half, because locked-mode restore does not compare a project reference's
   version — which is why skipping it costs the next dependency change rather than this one, where a regeneration
   would otherwise carry the skipped bumps beside the pin that was actually moved.

No step is safe out of order, and nothing in a pull request can express that, which is why the ordering is printed
rather than automated. Nothing here pushes a tag on the owner's behalf.

## What a release publishes, and what it needs to

Four artifacts leave one run, and a failure in the first three leaves the release incomplete rather than half-published:

| Artifact | Where | Depends on |
| --- | --- | --- |
| The image | `ghcr.io/krzysztof318/mailfathom` and `docker.io/krzysztof318/mailfathom` | the schema artifact building |
| The Helm chart | `ghcr.io/krzysztof318/charts/mailfathom` | the image's digest |
| `mailfathom-schema-<version>.sql` | the GitHub release's assets | nothing else the release produces |
| `mfctl-<version>-<rid>` for `linux-x64`, `linux-arm64`, `win-x64`, and `win-arm64`, plus one `.sha256` covering all of them | the GitHub release's assets | nothing else the release produces |

The column names what each artifact needs from the other three. What all four need is the same and comes before any of
them: the tag assertion, then the build, unit-test, formatting, and migration checks, then the integration suite. **No
artifact is built until every one of those has passed**, so a commit a unit test rejects costs the gate that rejected
it rather than four `dotnet publish` invocations and a schema generation beside a red build.
[The container image](container-image.md#verification) records the whole gate order, including the two gates that
belong to the image alone.

The command binaries are the one artifact that gates nothing, and that is deliberate: a release whose image and schema
are correct is one an operator can deploy, so a build failure in the command is left visible on the run rather than
holding back the thing they are waiting for. They are self-contained and trimmed, so an operator downloads one file and
runs it — and they are built for the machine an operator administers *from*, which is why Windows is among them while
the service itself is Linux-only.

Nothing between the build and the release page rewrites a binary. No command binary carries a code signature or a build
provenance attestation, so the checksum file the build takes over exactly the published bytes is what an operator
verifies a download against, and it is the whole of what the release offers for that.
[The administrative endpoint](admin-endpoint.md#getting-the-command) is where an operator is told so and how to check.

The chart is published **after** the image and **against the digest it produced**, because a chart names the image it
deploys: before pushing, the run renders the packaged chart against that digest and refuses to publish one that would
deploy anything else. The chart goes to GHCR alone, which is the one place the two artifacts diverge — Docker Hub's
namespace is `namespace/name` and nothing deeper, so a chart pushed there would land in the repository the image
already occupies and collide with its tags. A chart is pulled once and then lives in the repository an operator
deploys from, so it carries less of the availability argument that puts the image in both registries.

The nightly channel publishes no chart. An operator running a nightly installs the most recent released chart and names
the nightly image through `image.tag`, which the chart supports behind the acknowledgement it already requires.

**Credentials.** GHCR authenticates with the workflow token and needs nothing configured. Docker Hub authenticates with
the `DOCKERHUB_TOKEN` repository secret — a personal access token with read, write, and delete scope: write pushes the
mirror, delete lets the nightly channel prune it, and the repository-overview sync needs all three. The publishing job
refuses to start when the secret is missing, before it logs in or builds anything, so the failure names the missing
configuration rather than arriving as a rejected credential half-way through a push. `GHCR_RETENTION_TOKEN` is optional
and only affects nightly pruning on GHCR; without it that step warns and deletes nothing. `WINGET_PKGS_TOKEN` is the
third, described below; its job is paused, so a release currently needs it for nothing.

### The Windows Package Manager, which is paused

**A release submits no winget manifest today.** `Submit the winget manifest` carries `if: false` in
`.github/workflows/release.yml` and appears in the run graph as skipped, and deleting that one condition is the whole
of what resumes it. Everything else below describes a step that is configured and not running, which is what it is
there for: the reader of this section is whoever ends the pause.

The pause is a wait rather than a decision. Acceptance belongs to the community repository's review, MailFathom has
submissions open that it has not reached, and winget-pkgs allows exactly one pull request per package version — so a
release cannot shorten that wait by submitting again, only queue a version behind the ones already waiting. What ends
it is those being accepted.

While it is paused, `winget install MailFathom.mfctl` finds nothing, and no page here offers winget as a way to get the
command. Restoring the job restores that too: it is a paragraph in
[administering a deployment](admin-endpoint.md#getting-the-command) and a row in the repository-root `README.md`.

A submission is a submission rather than a publication, which is the shape of the step the rest of this section
describes. `Submit the winget manifest` opens a pull request against
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) carrying the three manifest files
`scripts/build-winget-manifests.sh` renders, and somebody else's review and automated validation decide when
`MailFathom.mfctl` starts answering.

It runs last, after `Publish the GitHub release`, and that ordering is a requirement rather than a preference: winget
downloads the installer from the URL the manifest names, and that URL is a release asset which does not resolve until
the release exists. Each `InstallerSha256` is computed from the bytes `Build the CLI binaries` produced rather than
from a download of the published asset, so the hash a Windows machine verifies against is the hash of the file this
pipeline built. The two agree in every case except the one worth catching.

The submission is idempotent for the reason the registry publications are. A version the community repository already
carries is reported and skipped, and a re-run whose branch already has an open pull request reports that rather than
opening a second one — which matters more here than elsewhere, because winget-pkgs allows exactly one pull request per
package version. The branch itself lives on `Krzysztof318/winget-pkgs`, since nobody can push a branch to
microsoft/winget-pkgs. That fork is part of the release setup rather than something a run creates: it has to exist
before the first submission, and each run force-syncs it from upstream before branching, so it stays a staging area
rather than a repository with state of its own.

**The credential is the part worth reading twice.** `WINGET_PKGS_TOKEN` is a *classic* personal access token with the
`public_repo` scope, and nothing narrower does this job: a fine-grained token creates the commit on the fork and then
fails on the pull request, because it cannot be granted a permission on a repository the account does not own. That
scope is account-wide — write access to every public repository the account has, not to the fork alone — which is the
same breadth that kept a classic `project` token out of this repository's secrets for writing the roadmap board. It is
accepted here as a deliberate trade against a manual submission per release, and it is the one credential in this
pipeline whose reach extends beyond MailFathom's own artifacts. Rotate it accordingly.

microsoft/winget-pkgs also asks a contributor to have signed Microsoft's open-source Contributor License Agreement.
That is a one-time act by the account the token belongs to, and it says nothing about this repository's own position:
MailFathom asks for no contributor agreement, and this is somebody else's repository under somebody else's rules.

Whenever the job runs again, the page it obliges is the one named above, and `winget search MailFathom.mfctl` is what
answers which versions the community repository actually carries — a release newer than what the review has reached is
the ordinary state rather than a fault, so the page states the channel as a condition rather than as a claim about
today.

### When one registry published and the other did not

Re-run the release workflow on the same tag. It rebuilds nothing: a version already in both registries from that commit
is reported and left alone, and a version in one but not the other is **copied across by digest**, so the artifact that
reaches the second registry is the one the first already answers for. Rebuilding would produce a second digest for a
version one registry already published, which is exactly what pushing one manifest list to both exists to prevent.

The same holds for the chart. A chart version already published under this release's application version is left alone;
one published under a different application version is refused, because a published chart version is immutable.

**The attestations and the Artifact Hub ownership claim are redone on every run**, for both artifacts, because whether
something is in the registry and whether it has been attested or claimed are different questions. A run whose push
succeeded and whose attestation failed is exactly the run somebody re-runs, and a re-run that skipped the attestation
because the push had already landed would report success while leaving an artifact permanently unverifiable.

A version that exists under a *different commit* is refused outright in either registry. That is not a state to
recover from — the tag has already been published as something else, and the answer is a new version.

## Major, minor, and patch branches

`main` produces every major and minor release. Its `VersionPrefix` only ever moves forward, and **a patch is never cut
from it**: by the time `0.2.0` needs a fix, `main` already contains everything intended for `0.3.0`, and a patch
released from it would ship that work under a number promising nothing had changed.

A patch lives on a permanent `release/<major>.<minor>.x` branch, cut from the release tag of the line it patches,
created on demand the first time that line needs a fix and never deleted. It carries its own `VersionPrefix`, publishes
through the same three steps, and produces no nightly. Where the code being fixed still exists on `main`, the fix
merges there first and is cherry-picked onto the release branch, so a fix cannot be lost when the next minor ships.
Only the newest released minor is patched by default; reaching further back is a decision recorded on the issue that
asks for it.

## What `0.x` promises

Within `0.x` a **minor** bump may break any of the four surfaces, and every break is named in that release's changelog
entry against the surface it breaks. Within `0.x.y` a **patch** is compatible on all four — that is a real promise, not
a disclaimer. The deprecation window does not exist below `1.0.0`.
