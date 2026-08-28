# The container image

<!-- describes: deploy/docker/**, .github/workflows/container-image.yml, .github/workflows/publish-container-image.yml, .github/workflows/release.yml, .github/workflows/nightly.yml -->

`deploy/docker/Dockerfile` is the only image definition MailFathom has. Every deployment shape in `deploy/` runs what it
produces — the Compose file builds from it, and the Helm chart and the Podman Quadlet units name a published image built
from it — and nothing else produces an image, so what this page describes is what runs wherever MailFathom runs in a
container.

The build context is the repository root, so the definition is named rather than found:

```bash
docker build --target runtime --file deploy/docker/Dockerfile --tag mailfathom:local .
```

It produces one image: the service. It carries no migration tool, no SQL, and no credential that could apply one, which
is what makes "the host never applies a migration" a property of what was built rather than a rule someone has to
remember. `DatabaseSchemaStartupGate` refuses to start against a schema this build does not recognize, in every
environment, and the reviewed artifact that answers that refusal is the idempotent SQL file each release ships rather
than anything in here. [Applying the database schema](database-schema.md) documents it.

## What is inside, and what is not

The runtime image is built on `mcr.microsoft.com/dotnet/aspnet:10.0.11-noble-chiseled-extra` and is about 105 MB, of
which about 19 MB is the client's browser bundle described below.
Chiseled means there is no shell, no package manager, and no HTTP client: a process that reaches the container finds
almost nothing to use. `-extra` carries ICU and tzdata, which the plain chiseled image does not — MailFathom decodes
internationalized headers, folds case for search, and formats instants for several time zones, and the invariant
globalization the smaller image forces would quietly change how mail from outside one alphabet is read.

It contains the published application, the client's browser bundle under `/app/wwwroot`, and nothing else — plus the
two files that licensing requires travel with it, `/app/LICENSE` and `/app/NOTICE`. No SDK, no source tree, no
repository history, no test
artifacts, no build cache, no credential, and no certificate. The XML documentation files every project generates are
dropped at publish, because none is read at run time and shipping them would put the repository's commentary about its
own internal contracts into an artifact an operator can unpack. The portable symbol files stay, because they are what
turns a stack trace in a support report into file and line numbers.

`deploy/docker/Dockerfile.dockerignore` is an allow-list rather than a deny-list: it excludes everything and then names
what may reach the build. The build context is the repository root, which is also where a developer's `.env`, a mounted
secret, and a certificate live, and a rule that only excluded what someone remembered would send all of them to the
daemon. Docker looks for an ignore-file named after the Dockerfile before it looks for one at the context root, and
prefers it, so the file bounding the context travels with the definition that uses it.

Every base image is pinned to an explicit patch version rather than to a floating `10.0`, so a rebuild months from now
resolves what the change was reviewed against.

## The client travels in it

`/app/wwwroot` is the MailFathom client's browser head — the WebAssembly bundle, about 51 MB across 205 files, which
compresses into about 19 MB of image. It is built by a stage of this same Dockerfile, so one `docker build` still
produces the published image: the stage installs the `wasm-tools` workload, restores
`frontend/src/Client/Client.csproj` in locked mode — the application project rather than the solution, because the
image needs one head and not the test suite beside it — and publishes the browser head in Release. Release is not a preference here — the Uno SDK drops its
proprietary designer and App MCP assets only when `Optimize` is on, and it decides that at restore time, so both the
restore and the publish state it. The pre-compressed `.br` and `.gz` copies the WebAssembly SDK emits are deleted
before the bundle is copied into the runtime image: the static-file middleware serving it negotiates no content
encoding, so they would be a second copy of the whole bundle that no request can reach.

The stage carries two files from outside `frontend/` as well, and the allow-list above names them for that reason:
`assets/icon-1254.png` and `assets/icon-900.png`, which are the product mark the client's build renders its favicon,
its web manifest icons, and its splash screen from. They are build inputs rather than anything the bundle carries — the
artwork lives once, under `assets/`, and `frontend/src/Client/Client.csproj` stages it for the Uno resizetizer instead
of keeping a second copy beside the sources.

**This couples the two stacks at build time and nowhere else.** No project under `backend/` references one under
`frontend/`, neither solution names a project in the other, and the service assembly's reference list is asserted by a
unit test. What the image carries is a directory of files copied in beside the application, and the composition root's
whole knowledge of the client is whether that directory has an `index.html` in it.

**Serving it is off**, in this image and in every deployment asset built on it. `ClientEndpoint:Application:Enabled`
turns it on and [the client endpoint](client-endpoint.md#serving-the-client-from-the-deployment) is the page; an image
whose bundle nobody serves costs its size and nothing else.

## How it runs

| Property | Value |
| --- | --- |
| User | `1654`, the unprivileged `app` account the .NET base images define |
| Ports | `8080`, plain HTTP, for `/mcp`, which `McpEndpoint:Port` moves; `8081` for the probes, on a listener of its own |
| Ports once TLS is configured | The HTTPS profiles' own ports, plus `8080` again — which becomes [a redirect to them](mcp-endpoint.md#redirecting-a-client-still-pointed-at-http) rather than serving the routes, so the port this image already publishes keeps answering |
| Writable paths | `/tmp` only, which a deployment supplies as a tmpfs or an `emptyDir` |
| Entrypoint | `dotnet /app/MailFathom.Host.dll` |
| Health check | None. See [the health endpoints](#the-health-endpoints) below. |

The application directory is owned by `root` and the process is not, so the service cannot rewrite its own code even
before the deployment imposes a read-only root filesystem on it. Both deployments do impose one, and both drop every
Linux capability.

**The container speaks plain HTTP and terminates no TLS.** A certificate belongs to the reverse proxy or the ingress in
front of it, which is also the only place one has to exist. An MCP endpoint reached over plain HTTP hands its API key
and every message it serves to anything on the network path. The public scheme and host reach the process from any peer
until you name that proxy in `ReverseProxy:TrustedProxies`, which is what stops every other container on the bridge
network from setting them — see
[behind a TLS-terminating reverse proxy](mcp-endpoint.md#behind-a-tls-terminating-reverse-proxy).

**The image carries no listener-shaped variable of its own.** Where a surface is served is its own section's question,
so the host refuses `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`, and `ASPNETCORE_HTTPS_PORTS` rather than ignoring them —
and the aspnet base image sets `ASPNETCORE_HTTP_PORTS=8080` in its own config. The runtime stage clears that variable to
an empty value, which is what the refusal treats as absent, so a container serves what `McpEndpoint:Port` and
`HealthEndpoints:Port` say and no deployment has to unset anything. The other two are absent. [Where each surface is
served](configuration-endpoints.md#where-each-surface-is-served) is the contract, and [Verification](#verification) is
the gate that reads all three back off the built image so a base-image bump cannot reintroduce one unnoticed.

`DOTNET_EnableDiagnostics=0` is set, so no diagnostic IPC socket is created. That socket can request a process dump,
and a dump is a way to read secret material out of managed memory — the residual exposure
[secret provisioning](secret-provisioning.md#secret-material-in-process-memory) documents and asks deployments to
close. Set it back to `1` deliberately, for one session, when a dump is genuinely needed.

### The health endpoints

The host serves three probes on a listener of their own — port `8081` unless a deployment configures another — in every
environment. Kubernetes probes them over HTTP from the kubelet and needs nothing inside the container, which is what
the chart's probes use.

**The image declares no `HEALTHCHECK`.** Docker and Podman run one as a command *inside* the container, and a chiseled
image has no shell and no HTTP client for one to be written in. Adding either so the container could ask an endpoint
that is already reachable from outside would grow its attack surface for nothing; under Compose, the published probe
port is asked from the host instead.

The three answer different questions and are wired to different probes on purpose:

- **`/started`** reports whether the host's startup gates have completed: every secret reference resolved, the database
  schema verified, and — only where its own switch is on — the spam scanner naming the corpus it scores under. It is what
  an orchestrator's startup probe reads, so a slow first start extends the grace period rather than counting as a failure.
- **`/health`** consults the dependencies a request needs: the database, and — only where its switch is on — the
  personal-data analyzer answering for every configured category. It is readiness: a process that cannot reach either
  stops receiving requests it cannot fulfil, and is not restarted for it.
- **`/alive`** consults only process-local state. It is liveness: a database outage must never become a restart loop
  that cannot fix what is actually broken.

All three are unauthenticated, and none of them is served on port 8080 — a probe path asked there is answered with
`404`, and `/mcp` asked on the probe port is too. The exposure control is which network the probe port is published on.
[The health endpoints](health-endpoints.md) records the full contract, including the TLS transports and the switch that
turns the surface off.

### Shutdown

`SIGTERM` starts a graceful stop. The host's own budget comes from `MailSynchronization:ShutdownDrainTimeout`, and a
deployment's grace period has to be longer than it or the process is killed with the drain still running. Both
deployments in `deploy/` allow 60 seconds against a 10-second default; raise them together.

### Labels

The image carries the OCI labels that let a pulled image be traced back to the commit it was built from —
`org.opencontainers.image.source`, `.revision`, `.version`, `.created` — supplied as build arguments.

`IMAGE_VERSION` has no useful default, and its `0.0.0-unversioned` placeholder says so. The version is declared once,
as `VersionPrefix` in `Version.props`, and every build reads it with `scripts/read-declared-version.sh` rather
than restating it — which is what keeps a labelled version from drifting away from the stamped one, because there is no
second copy to drift. [ADR 0004](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md) records why the number lives in one
reviewed line.

`IMAGE_REVISION` is passed to the publish inside the build as `SourceRevisionId`, so the assemblies report the same
commit the label names rather than a second claim about it. The running process then reports its version and revision
in its [startup record](host-startup-telemetry.md), and its version to an MCP client during `initialize`. A published
artifact identifies itself both ways: from outside, through the labels, without being run; and from inside, once it is.

```bash
docker build --target runtime --file deploy/docker/Dockerfile \
  --build-arg "IMAGE_VERSION=$(bash scripts/read-declared-version.sh)" \
  --build-arg "IMAGE_REVISION=$(git rev-parse HEAD)" \
  --build-arg "IMAGE_CREATED=$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --tag mailfathom:local .
```

`io.mailfathom.release-channel` states which channel produced the image: `release`, `nightly`, or `local` for a build
nobody published. The version identifier already separates the channels — a nightly always carries a `-nightly.<n>`
prerelease identifier and a release never carries one — and this label is what still answers the question once a
reference has been re-tagged, mirrored, or reduced to a digest, which is the state a reference reaches long before
anyone asks what it is. It arrives as `IMAGE_RELEASE_CHANNEL`, and its default is deliberately neither channel.

`org.opencontainers.image.documentation` names the documentation site's directory for the version this image carries,
so `docker inspect` answers where to read about what you are holding without anybody having to work out which version's
pages apply. It is the version's own directory rather than the site root, because the site opens on the newest release
and an older tag would otherwise be read against pages describing settings it does not accept. `IMAGE_DOCUMENTATION_VERSION`
is where it comes from, and its default is the whole derivation for a release: the site names a version's directory
after that version's tag, so a version reads as `v<version>`. The nightly publication is the one build that passes something
else, `latest`, because a nightly is named after a release the site publishes nothing for yet and what it actually
carries is `main` — which is what `latest` documents.

`org.opencontainers.image.licenses` is fixed rather than passed in, at `Apache-2.0`, because it states MailFathom's own
license and a build must not be able to say otherwise. The label is only the claim a registry indexes; the terms
themselves are `/app/LICENSE` and `/app/NOTICE`, which arrive as part of the publish output the runtime stage copies.
`Host` fails its own publish when either is missing, so the image cannot be built without them. The third-party
notices that must accompany them are not in the image yet — see `THIRD_PARTY_LICENSES.md` and issue #191.

`org.opencontainers.image.description` is the sentence a registry shows beside the image, and it is also what the
release pushes as the Docker Hub repository's short description — that field is read off the published manifest rather
than written a second time in the workflow, so the registry page and the label every other registry indexes cannot come
to describe different products. Docker Hub accepts 100 bytes there and truncates anything longer without failing, so
the label is written to fit and the release asserts that it does; [Verification](#verification) states what happens when
it stops fitting.

`io.artifacthub.package.logo-url` is the only label the image carries that OCI does not define. The specification has no
field for a project icon, so a listing reads a vendor label instead. It points at `assets/icon-1254.png` in the
repository, which is the asset the Helm chart's `icon` names as well, and it stays a URL because a label carries no
payload of its own.

## The schema

The image applies none and carries nothing that could: no migration tool, no SQL, and no credential that could reach a
database with DDL. What a released installation applies is a file beside the image rather than something inside it —
`mailfathom-schema-<version>.sql`, attached to the release — and running it is an explicit operator action each
deployment page describes.

Applying it can need more privilege than serving does: the schema installs the `vector` extension, which PostgreSQL
does not permit an ordinary role to create. That asymmetry is why the step is separate, and why a command inside this
image would be the wrong shape for it whatever else it cost — the credentials this process runs with are not the ones
that decide what may run DDL. Where the extension is installed out of band the asymmetry disappears and one role can
both apply the schema and serve, which is what the Compose deployment does during initialization while a superuser is
still connected; where it is not, give the schema step a role that may create an extension and grant the service one
that can read and write rows and nothing more.

[Applying the database schema](database-schema.md) is the whole path, including the ownership grants a separate role
leaves behind and the three startup failures a schema problem reports.

## Published images

Images are published to two registries:

| Registry | Reference |
| --- | --- |
| GitHub Container Registry | `ghcr.io/krzysztof318/mailfathom` |
| Docker Hub | `docker.io/krzysztof318/mailfathom` |

Both carry the same manifest list under the same digest, for every version and on both channels. One build produces one
manifest list and the publishing run pushes it to both, so they are mirrors rather than two artifacts that happen to
share a name — which is why the registry you pull from is not part of what you have to trust, and why a network that
can reach only one of them is not a reason a MailFathom version is unreachable. The run verifies that equality rather
than assuming it, and a run that reached only one registry is a failed publication.

GHCR is the canonical reference to quote, because it is where the repository, the package page, and the chart all sit.
Docker Hub is the convenience mirror.

**The package is public**, so a pull needs no authentication and no GitHub account:

```bash
docker pull ghcr.io/krzysztof318/mailfathom:nightly
```

A package's visibility is a setting of its own rather than something it inherits from the repository, and it is
configured to follow the repository's access. Nothing in a workflow reads it back, so the settings page is where it is
confirmed.

Two channels are published, and what separates them is the version identifier rather than where the image sits:

| Tag | Channel | Moves | Published by |
| --- | --- | --- | --- |
| `<major>.<minor>.<patch>` | release | never | `Release`, on an annotated `v<major>.<minor>.<patch>` tag |
| `latest` | release | to the highest release that carries no prerelease identifier | the same run |
| `<major>.<minor>.<patch>-nightly.<n>-<short revision>` | nightly | never, until it is pruned | `Nightly` |
| `nightly` | nightly | to the newest published nightly | the same run |

`latest` is chosen by excluding every version carrying a prerelease identifier and taking the highest of what remains,
never by taking a maximum — because `main` names the *next* release, a maximum would select a nightly. A patch to an
older line is a supported release and still does not move `latest`.

A nightly's identifier carries both the run number and the commit it was built from, so the tag says what it is without
anything having to be inspected; they travel in one dot-separated part because an OCI tag admits no `+` build metadata.
The consequence is worth knowing: `41-3f1c9ab` contains letters, so SemVer compares it as text rather than as a number,
and **nightlies do not sort against each other numerically**. Nothing here depends on that — every nightly still sorts
below the release it previews, `nightly` names the newest one, and retention works from when a version was published —
but a tool that picks "the newest nightly" by sorting versions will get it wrong. Ask the registry for `nightly`
instead.

`Release` runs only on a tag push. It refuses to publish before it has checked that the tag is annotated and carries no
prerelease identifier, that the tagged commit is reachable from `main` or from that line's `release/<major>.<minor>.x`
branch, that the version equals the tagged commit's own `VersionPrefix`, that it advances its own `major.minor` line,
and that `CHANGELOG.md` has a non-empty section for it. `scripts/assert-release-tag.sh` is that check, and it runs
before anything is built.

`Nightly` runs at 00:00 UTC — 02:00 in Europe/Warsaw under CEST, 01:00 under CET — and **publishes nothing when `main`
has not moved since the last published nightly**. It reads that from the revision the published `nightly` image itself
carries, so a deleted tag or a pruned package leaves it right rather than stuck. A `workflow_dispatch` builds a snapshot
whether or not `main` moved, and refuses a ref that is not reachable from `main`.

The newest 30 nightly versions are kept and older ones are deleted, so a channel that publishes every night does not
grow without bound. Only versions whose every tag is a nightly identifier are ever deleted: a release, `latest`, and an
attestation manifest are out of that step's reach by construction.

### What a nightly build risks

A nightly is whatever `main` was that night. It is published so a change can be tried, and running one is a decision
rather than a default — which is why both deployment shapes put an acknowledgement in front of it. What you take on:

- **The database schema may be ahead of any release.** A nightly can carry a schema change no release's script applies,
  and MailFathom refuses to start against a schema it does not recognize. Its own script is on the `Nightly` run that
  built it, under `schema-artifact`, and that run is the only place it exists; recovering from a schema a nightly
  established usually means restoring the database rather than downgrading the image.
- **There is no upgrade path, in either direction.** Nothing tests that yesterday's nightly upgrades to today's, that a
  nightly upgrades to the release that follows it, or that a release can be put back after one. A production database
  that a nightly has touched may not be usable by a release.
- **The four public surfaces may move without notice.** A configuration key can be renamed, a tool contract can change,
  a default can flip, and none of it earns a changelog entry until the release that contains it is prepared.
- **It is not supported and carries no promise.** No defect report about a nightly is a release defect, and no nightly
  is patched — the fix is the next nightly.
- **It disappears.** Only the newest 30 are kept, so a nightly you deployed can stop being pullable, which is enough to
  break a node that has to re-pull the image.
- **The vulnerability scan does not block it.** A `HIGH` or `CRITICAL` finding refuses to publish a release and is only
  reported on a nightly, so a nightly may carry a finding a release never would.

Use a release for anything you are not prepared to rebuild from scratch. Where a nightly is the right answer — trying a
change, reproducing a defect, previewing what the next release will contain — pin the exact `-nightly.<n>-<short revision>` tag or the
digest rather than the moving `nightly` tag, so what you are running does not change under you.

## Verification

The `Container image` workflow builds this file for `linux/amd64` and `linux/arm64` and stops there. It is manual
dispatch only and publishes nothing; no registry credential reaches it, and it is what proves a Dockerfile change still
builds without waiting for a release.

Publication runs the gates instead, in an order that spends the cheap ones first:

1. **`Build, test, and migrations`**, against the commit being published rather than against a branch — the build,
   the unit-test and coverage gate, and the check that no model change outran its migration. Both channels wait for
   all three. `CI` calls the same workflow for a pull request, so "this image passed CI" is one claim about one
   definition rather than about a copy of it. What `CI` keeps to itself is the part that is about a pull request:
   skipping work the changed files cannot affect, and waiting for a draft to be marked ready. A publication skips
   neither. The fourth thing that workflow can do — `dotnet format` — is the one a publication turns off, on the same
   argument `CI` uses to exempt a push to `main`: formatting is a property of the files a change wrote, so the pull
   request that wrote them settled it, and no commit reaches a tag or a nightly without having been one.

   The migration check is the one worth naming here rather than leaving to `CI`: an image whose committed model
   snapshot describes a schema no migration produces would refuse to start against any database an operator can
   actually have, and a nightly is installable long before a tag exists to catch it.
2. **The client stack's build and unit suite, and the repository contracts**, both against the same commit and both
   beside the gate above rather than behind it: the two solutions share no project, and the contract suite installs
   nothing and answers in twenty seconds, so putting either in sequence would delay a verdict without sharpening one.
   The client's gate blocks the push on both channels, because the image carries the browser head published out of
   that stack. The contract gate — the licensing headers, the page contracts, and the chart rendered against the
   manifests committed beside it — blocks a release and reports on a nightly, which is the same difference the
   vulnerability scan draws and for the same reason: a release is one claim about one commit, and a nightly exists to
   be tried.
3. **The integration suite**, for a release only, and only after CI has passed. It starts PostgreSQL, applies the
   baseline migration, and asserts against the result, which is minutes of container time no commit a unit test would
   have rejected is worth. A nightly does not run it.
4. **The image gates.** The image is built for one architecture, started, and required to report the version and
   revision its labels claim, to run as the unprivileged `1654` account, to expose both listeners, and to carry none of
   the three listener-shaped variables the host refuses; then Trivy scans it, which refuses to publish a release
   carrying a fixable `HIGH` or `CRITICAL` finding and only reports one on a nightly.

   The listener check reads `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`, and `ASPNETCORE_HTTPS_PORTS` back off the built
   image's config rather than reading the Dockerfile, because a value under any of them can arrive from the base image
   as well as from a line somebody wrote — the base image sets one today, and a bump can introduce another. The startup
   line the same step waits for is no evidence here: it is logged before configuration is validated, so a container that
   reports it can still fail on the next line. What this does not cover is a pull request: the gate runs where the image
   is built, so a change that put a variable back is caught by the next nightly rather than by the branch that made it.

**Nothing is built from the commit until the verification gates have passed** — not the image, not the schema script,
not the command binaries, and not the desktop client. They are jobs in `Release` and `Nightly` themselves rather than
steps inside the workflow that pushes the image, because the image is one of several things a channel builds and a gate
inside it would gate only that one. The contract gate is the one that holds back less than the rest, and deliberately:
it blocks the image and leaves the other artifacts alone, since what it asserts says nothing about whether a schema
script or a command binary builds. Everything above is therefore the order of a whole publication rather than of the image alone: what a red run
costs is the gate that refused it, not four `dotnet publish` invocations and a schema generation beside a failing
build. `scripts/test-agent-workflow.sh` reads both workflows' job graphs and fails one whose publishing job does not
wait for the gates, so a further artifact is gated by being added rather than by being reviewed.

Both channels also build the schema artifact, from one shared definition, and differ only in what they do with it. A
release **waits** on it and refuses to push when it cannot be produced: an operator handed an image and no way to reach
the schema it requires has a deployment that starts, fails the startup gate, and stays down. A nightly builds it beside
the push rather than in front of it, for the same reason its vulnerability scan reports instead of blocking, and leaves
the file on the workflow run — there is no release for it to be attached to. [Applying the database
schema](database-schema.md) is what that artifact is and how it is applied.

Only then is the multi-architecture manifest list built and pushed — once, to both registries, because every reference
it takes in either of them is in one tag list. After the push it is inspected by digest and required to carry both
platforms, to identify itself as the channel and version it was published as, to name the documentation address that
channel and version compose to, and to resolve to the same digest in each registry. A failure anywhere above publishes
nothing. The documentation address is asserted there rather than trusted because it is the label somebody reads instead
of asking anyone: composed wrongly, it sends every reader of the image to a page that does not exist, and nothing
downstream of a publication would ever report that.

A release additionally synchronizes `deploy/docker/README.md` onto the Docker Hub repository page, which is the one
registry overview that is not rendered from the repository itself, together with the short description that sits above
it — taken from the published image's own `org.opencontainers.image.description`. GHCR reads the repository through the
image's `org.opencontainers.image.source` label and needs nothing pushed to it.

That page exists rather than the root README being pushed, because the two are read by different people. Somebody on
Docker Hub already has an image reference and wants to run a container: the tags, what the image needs before it starts,
how it runs, and how to verify it. Somebody on the repository is deciding whether to adopt the project at all. The chart
listing has a page of its own for the same reason and a third reader again — somebody who has already chosen Kubernetes
and Helm — which is `deploy/helm/mailfathom/README.md`, packaged with the chart rather than pushed anywhere.
[Deploying to Kubernetes](deployment-kubernetes.md#installing) records what else that listing renders.

Docker Hub's two limits are checked before that write rather than left to the action performing it, which truncates
over-long content and reports success: a release fails if the overview exceeds 25000 bytes or if the description label
exceeds 100. The second is the one worth failing for. A truncated overview is visibly broken and would be noticed, while
a truncated short description is a sentence cut mid-word that reads like a sentence the project meant to write.
`scripts/test-agent-workflow.sh` reads the overview's length as well, so a page that outgrew the limit fails on the pull
request that wrote it rather than here, where the image is published by the time the check runs.

A re-run of a publication is safe and does not rebuild. A version already present in both registries from the same
commit is reported and its image left untouched; a version present in one and missing from the other — what a partial publication
leaves behind — is copied across by digest, so the artifact that reaches the second registry is the one the first
already answers for rather than a second build of the same source. A version present under a *different* commit is
refused outright, because a published tag is immutable.

The attestation is the one thing a re-run always redoes, because whether a digest is in a registry and whether it has
been attested are different questions. A run whose push succeeded and whose attestation did not would otherwise be
recovered by a re-run that skipped the attestation for good and reported success, leaving an image this page says can
be verified and cannot. Re-attesting a digest adds a second valid statement, and verification accepts any of them.

A published image can be verified the same way from outside:

```bash
docker buildx imagetools inspect ghcr.io/krzysztof318/mailfathom:latest
gh attestation verify oci://ghcr.io/krzysztof318/mailfathom:latest --repo Krzysztof318/MailFathom
```

The first prints the manifest list, its platforms, and the labels above. The second checks the signed provenance
statement that says this digest was built by this repository's workflow from a named commit; a release carries that
attestation in the registry as well, so verification does not depend on reaching GitHub's attestation store.

Both commands take the Docker Hub reference in place of the GHCR one and answer the same, because the digest is the
same and the run attests it under each repository name it published to:

```bash
docker buildx imagetools inspect docker.io/krzysztof318/mailfathom:latest
gh attestation verify oci://docker.io/krzysztof318/mailfathom:latest --repo Krzysztof318/MailFathom
```

Comparing the two digests is the check that the mirrors agree, and it needs no credential:

```bash
docker buildx imagetools inspect ghcr.io/krzysztof318/mailfathom:latest --format '{{ .Manifest.Digest }}'
docker buildx imagetools inspect docker.io/krzysztof318/mailfathom:latest --format '{{ .Manifest.Digest }}'
```

What no gate covers is the deployment around the image — that it reads its mounted configuration, resolves its mounted
secret, reaches a real database, and then refuses an unrecognized schema. A change there is reviewed by reading it and,
where it is worth running, by starting the Compose deployment by hand as
[Deploying with Docker Compose](deployment-compose.md) describes.

## Where the deployments are

- [Docker Compose](deployment-compose.md)
- [Podman Quadlet](deployment-quadlet.md)
- [Kubernetes and Helm](deployment-kubernetes.md)
- [Applying the database schema](database-schema.md)
