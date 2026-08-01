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
| Prerelease identifier | `VersionSuffix` | `0.1.0` becomes `0.1.0-nightly.41` |
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
scripts/read-declared-version.sh nightly.41   # 0.1.0-nightly.41
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
| `Chart.appVersion` | the application version the chart deploys | yes |
| The published assemblies | `AssemblyInformationalVersion` | yes |

All of them come from the same stamp. The two runtime paths read the assembly's own metadata rather than a literal
restated in code, and unit tests assert that by deriving their expectation from that metadata; a reporting path that
regressed to a hardcoded string fails them rather than staying plausible while being wrong.

The native process deployment needs nothing further: `dotnet publish` writes the stamped assemblies, so the artifact on
disk carries its own version and revision without being started.

`scripts/verify-deployment-assets.sh` is what keeps the copies together. It fails a `Chart.appVersion` that has drifted
from the declared version, a build path that stopped naming the version and would therefore ship the
`0.0.0-unversioned` placeholder, and a Dockerfile whose publish stopped stamping the revision.

## What earns which increment

The release's increment is the **highest** increment any of the four surfaces requires — the MCP tool contract, the
configuration schema, the database schema, and the deployment contract. One question settles most cases: does something
that worked before the upgrade stop working after it, without the operator doing anything? If yes it is major; if
nothing breaks but something new is available it is minor; if neither, it is patch. The ADR's table applies that
question per surface and settles the recurring ambiguous cases.

`CHANGELOG.md` is what the increment is read from. An entry is written by the change that causes it, never
reconstructed at release time, and `$check-docs-licenses` is where that obligation is enforced.

## Cutting a release

Use `$prepare-release`, which reads the version, refuses the states that must not be released, opens both pull
requests as drafts, and prints the ordering. The sequence it prints is the whole procedure, and it is recorded here so
it survives the skill being unavailable:

1. **Merge the changelog pull request.** It closes `## [Unreleased]` into `## [x.y.z] - YYYY-MM-DD` and touches nothing
   else. It merges first because the tagged tree has to *contain* the released changelog rather than describe it
   afterwards.
2. **Push the annotated tag `v<x.y.z>` on that merge commit.** This is what makes the release real and what triggers
   the release workflow. Before publishing anything the workflow asserts the tag against the tagged commit's
   `VersionPrefix`, against the highest existing tag on the same `major.minor` line, and against the changelog section
   for that version.

   ```bash
   git tag --annotate v0.1.0 --message 'MailFathom 0.1.0'
   git push origin v0.1.0
   ```

3. **Merge the version-bump pull request.** It raises `VersionPrefix` to the next version and opens a fresh empty
   `Unreleased`. It merges after the tag, so `main` returns to naming the next release. Skipping it fails loudly rather
   than silently: the next tag push repeats a version that already exists, and step 2 rejects it.

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

## Which version is current

`latest` is chosen by excluding every version carrying a prerelease identifier and taking the highest of what remains,
**never** by taking a maximum. Because `VersionPrefix` on `main` names the next release, `main` carries
`0.3.0-nightly.N` as soon as `v0.2.0` is released, and a maximum over the tags present would select that nightly. The
same holds for any tooling, documentation example, or upgrade check that asks which version is current.
