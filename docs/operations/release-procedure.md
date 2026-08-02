# The release procedure

MailFathom's version number is a compatibility promise over four public surfaces, and it is written in one place. This
page records how a build acquires that number, where it is observable, and the sequence that turns a commit on a
release branch into a release. [ADR 0004](../decisions/0004-versioning-and-release-policy.md) records why each of those
is the way it is.

## Where the number comes from

`<VersionPrefix>` in `Directory.Build.props` is the only place in the repository where a version number is written.
Every project derives `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion` from it centrally, and no
project sets a version of its own.

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
| MCP `initialize` | the server's implementation version | no |
| `org.opencontainers.image.version` and `.revision` | the image's version and commit | yes |
| The image's tags, including `latest` | which release this is | yes |
| A packaged chart's `appVersion` | the application version the chart deploys | yes |
| The release's `mailfathom-schema-<version>.sql` | which schema that version expects | yes |
| The published assemblies | `AssemblyInformationalVersion` | yes |

All of them come from the same declaration. The two runtime paths read the assembly's own metadata rather than a
literal restated in code, and unit tests assert that by deriving their expectation from that metadata; a reporting path
that regressed to a hardcoded string fails them rather than staying plausible while being wrong.

The native process deployment needs nothing further: `dotnet publish` writes the stamped assemblies, so the artifact on
disk carries its own version and revision without being started.

**Nothing else in the repository writes a version number**, which is what makes drift impossible rather than merely
checked. The image's labels and tags arrive as build arguments, and the chart's `appVersion` arrives at package time:

```bash
helm package deploy/helm/mailfathom --app-version "$(bash scripts/read-declared-version.sh)"
```

`Chart.yaml` therefore carries no `appVersion` of its own. Its `version` field is not an exception — that is the
chart's own version, which counts edits to the chart directory and never follows the application's.

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

The `Release` and `Nightly` workflows implement this rule, publishing to `ghcr.io/krzysztof318/mailfathom`. Docker Hub
carries the same manifest list under the same digest once issue #235 lands.
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

**`CHANGELOG.md` is written by the release pull request and by nothing else.** Ordinary work never touches it: a
changelog is a statement about a release, and until someone decides to cut one there is no release for a line to belong
to. The file is a protected path, so an edit arriving through ordinary work is visible as the exception it is, and
`$check-docs-licenses` reports `n/a` for it on every change that is not a release.

## Cutting a release

The owner invokes `$prepare-release`, which reads the version, refuses the states that must not be released, opens both
pull requests as drafts, and prints the ordering. It is manual-invocation only — no agent can reach it — because when a
version becomes real is a decision rather than a consequence of work looking finished. The sequence it prints is the
whole procedure, and it is recorded here so it survives the skill being unavailable:

1. **Merge the changelog pull request.** It adds `## [x.y.z] - YYYY-MM-DD` with the release's entries, composed from
   what merged since the previous tag, and touches nothing else. It merges first because **its merge commit is what
   gets tagged and published**, so the tagged tree contains the released changelog rather than describing it
   afterwards.
2. **Push the annotated tag `v<x.y.z>` on that merge commit.** This is what makes the release real and what triggers
   the release workflow. Before publishing anything the workflow asserts the tag against the tagged commit's
   `VersionPrefix`, against the highest existing tag on the same `major.minor` line, and against the changelog section
   for that version — `scripts/assert-release-tag.sh` is that check. It then builds and gates the image, pushes it
   under `<x.y.z>`, moves `latest` onto the same digest, attests it, and opens the GitHub release with that changelog
   section as its notes.

   ```bash
   git tag --annotate v0.1.0 --message 'MailFathom 0.1.0'
   git push origin v0.1.0
   ```

   It also builds the release's schema artifact and attaches it, and it blocks the push when that fails: an image
   published without the SQL an operator needs to reach its schema is an image that starts, fails the startup gate, and
   stays down. [Applying the database schema](database-schema.md) is what the artifact is; the release notes record its
   name, its checksum, and the migrations it carries.

3. **Merge the version-bump pull request.** It raises `VersionPrefix` to the next version. It merges after the tag, so
   `main` returns to naming the next release. Skipping it fails loudly rather than silently: the next tag push repeats
   a version that already exists, and step 2 rejects it.

No step is safe out of order, and nothing in a pull request can express that, which is why the ordering is printed
rather than automated. Nothing here pushes a tag on the owner's behalf.

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
