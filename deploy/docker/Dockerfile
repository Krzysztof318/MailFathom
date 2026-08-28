# syntax=docker/dockerfile:1

### The MailFathom container image.
#
# One image comes out of this file: the service, built with `--target runtime`. It applies no schema change, ever. It
# carries no migration tool, no SQL, and no credential that could apply one, which is what makes "the host never
# applies migrations" a property of the image rather than a rule somebody has to remember.
#
# `DatabaseSchemaStartupGate` refuses to start against a schema it does not recognize, and what answers that refusal is
# a file published beside this image rather than anything inside it: `mailfathom-schema-<version>.sql`, attached to
# every release and run by an operator who has read it and taken a backup. Nothing here produces or applies one, so a
# deployment built from this file reaches a database whose schema was established separately and deliberately. See
# docs/operations/database-schema.md, which also records why a command in this image was refused: the credentials this
# process runs with are not the ones that may create the `vector` extension or run DDL.
#
# The build context is the repository root, and the ignore-file beside this one is what bounds it:
#
#   docker build --target runtime --file deploy/docker/Dockerfile .

# Base images are pinned to explicit patch versions rather than to a floating `10.0`, so a rebuild months from now
# resolves what this change was reviewed against. Both are recorded in THIRD_PARTY_LICENSES.md.
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.302-noble
# `-extra` carries ICU and tzdata. MailFathom decodes internationalized headers, folds case for search, and formats
# instants for several time zones, so invariant globalization — what the plain chiseled image forces — would quietly
# change how mail from outside one alphabet is read. The size saved is not worth that.
ARG DOTNET_ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.11-noble-chiseled-extra


########################################################################################################################
# Build: restore and publish. Runs once, on the build platform.
########################################################################################################################

# Pinned to BUILDPLATFORM on purpose. The application is published framework-dependent and without an apphost, so its
# output is portable IL that any architecture's runtime loads — there is nothing per-architecture to produce, and
# emulating an SDK under QEMU to produce it twice would cost minutes for an identical result. The runtime stages below
# carry no `--platform`, so those are what buildx resolves per target platform.
FROM --platform=$BUILDPLATFORM ${DOTNET_SDK_IMAGE} AS build

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_XMLDOC_MODE=skip

WORKDIR /source

# The two inputs continuous integration contributes to the stamped version, and the only two. The number itself is
# declared in Version.props, which the context below carries, so nothing here can name a version the build
# would not stamp — see docs/decisions/0004-versioning-and-release-policy.md.
#
# `IMAGE_REVISION` is deliberately the same argument the runtime stage labels the image with, so the commit the
# assemblies report and the commit the registry indexes are one value rather than two that can drift. It has to be
# passed here rather than resolved: the SDK's source-link support reads the revision from a Git checkout, and the build
# context deliberately carries no .git, so without this the assemblies inside the image would name no commit while the
# label beside them named one. Its `unknown` default round-trips — an unsupplied revision stamps `+unknown`, which is
# exactly what the process then reports.
ARG IMAGE_REVISION=unknown
# Empty for a release build and for an ordinary local one, and `nightly.<run number>-<short revision>` for a nightly, per ADR 0004's
# build table. An empty value leaves the stamped version at the declared prefix.
ARG VERSION_SUFFIX=

# The restore inputs first, so editing a `.cs` file does not invalidate the restore layer. The lock files travel with
# the projects under backend/src/. The .NET build contract sits under backend/ beside them and the version it imports
# sits at the root, so both halves are copied to the places the build resolves them from.
COPY NuGet.config global.json .editorconfig Version.props ./
COPY .config/BannedSymbols.txt .config/
COPY backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/ backend/src/

# Locked mode, like every other gate in this repository. The committed packages.lock.json files fix the transitive
# closure, so a republished package cannot change what a pinned version means between the branch's verification and
# this build; NU1004 fails here rather than a different graph being restored silently.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore backend/src/Host/Host.csproj --locked-mode

# The project's own licensing, which `Host` publishes beside the assemblies. It is copied after the restore rather
# than with the layer above because it is not a restore input: a corrected NOTICE would otherwise discard the restore
# layer for nothing. `VerifyPublishedLicenseAndNotice` in Host.csproj inspects the publish output, so a context that
# omits these files fails this build instead of producing an image that ships without its license.
COPY LICENSE NOTICE ./

# `UseAppHost=false` is what keeps the output architecture-neutral: an apphost is a native executable for one runtime
# identifier, and producing one here would tie the image to the build machine's. The entrypoint therefore invokes the
# managed assembly through `dotnet`.
#
# The XML documentation files are dropped: every project generates one, none is read at run time, and shipping them
# puts the repository's own commentary about internal contracts into an artifact an operator can unpack. The portable
# symbol files stay, because they are what turns a stack trace in a support report into file and line numbers, and
# they reveal nothing the assemblies beside them do not.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish backend/src/Host/Host.csproj \
        --configuration Release \
        --no-restore \
        -p:VersionSuffix="${VERSION_SUFFIX}" \
        -p:SourceRevisionId="${IMAGE_REVISION}" \
        -p:UseAppHost=false \
        -p:PublishDocumentationFile=false \
        -p:PublishDocumentationFiles=false \
        -p:PublishReferencesDocumentationFiles=false \
        --output /app


########################################################################################################################
# Client: the browser head, published as static files. Runs once, on the build platform.
########################################################################################################################

# A stage of its own, and the one place in this repository where both stacks are built by one command. Nothing is
# shared with the stage above: a separate solution, a separate build contract, separate package pins, and no project
# reference in either direction — `backend/MailFathom.slnx` names nothing under frontend/ and `Host` references no
# project there, which HostDependencyBoundaryTests asserts. What crosses is a directory of files, copied into the runtime image
# below, which is why serving it costs the composition root a static-file middleware and nothing else.
#
# Pinned to BUILDPLATFORM for the same reason the stage above is, and more strongly: what comes out is WebAssembly and
# JavaScript, which no architecture of the runtime image has anything to do with.
FROM --platform=$BUILDPLATFORM ${DOTNET_SDK_IMAGE} AS client

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_XMLDOC_MODE=skip

WORKDIR /source

ARG IMAGE_REVISION=unknown
ARG VERSION_SUFFIX=

# Emscripten's compiler driver is a Python program, and the .NET WebAssembly workload ships no Python of its own on
# Linux: it invokes whatever `python` is on the path. The SDK image has none, so the publish below fails while
# compiling the runtime's own C sources with `unable to find python in $PATH` — a message about the toolchain rather
# than about anything in this repository. `python-is-python3` is the alias, because the workload asks for `python` and
# Ubuntu ships the interpreter as `python3`. Nothing of this reaches the runtime image, which is a chiseled base with
# no package manager and no interpreter in it.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends python3 python-is-python3 \
    && rm -rf /var/lib/apt/lists/*

# The browser head compiles the runtime's own native assets and SkiaSharp's through the Emscripten toolchain this
# workload carries, and an SDK image ships no workload at all. Without this the publish fails at
# `net10.0-browserwasm` with NETSDK1147, naming the workload rather than anything about this repository. It is
# installed before the sources are copied so that editing a file does not reinstall it.
RUN dotnet workload install wasm-tools

# The client stack's own build contract, which sits under frontend/ beside its sources, and the two root files both
# stacks share. The Uno SDK version is one of the pins in global.json, so the copy above is what fixes it here too.
COPY NuGet.config global.json .editorconfig Version.props ./
COPY .config/BannedSymbols.txt .config/
COPY frontend/Directory.Build.props frontend/Directory.Packages.props frontend/
COPY frontend/src/ frontend/src/

# The product mark the application icon, the favicon, the web manifest's icons, and the splash screen are all
# generated from. `Client.csproj` stages these two out of assets/ for the Uno resizetizer rather than carrying a copy
# of the artwork beside the sources, so a context without them fails this copy before the publish reaches them.
COPY assets/icon-1254.png assets/icon-900.png assets/

# Locked, like every other restore in this repository. What it fixes here is `Client.Backend`'s closure: the
# application project deliberately carries no lock file, because a WebAssembly target's restore graph is partly the
# SDK's own, and locked mode passes over a project without one rather than failing on it.
#
# The configuration is stated here as well, and it is load-bearing rather than tidy. The Uno SDK decides whether Uno
# Platform Studio's tooling — the designer and the App MCP, neither of which is open source and one of which forbids
# distribution outright — enters the graph at all by reading `Optimize` **while restoring**, and a restore that was not
# told the configuration is a Debug restore. A later Release publish then has those assemblies in its graph and copies
# them into the bundle, which is an image shipping somebody else's proprietary code. The two properties beside it say
# the same thing directly, so this does not depend on the SDK continuing to read that one property; frontend/src/AGENTS.md
# is where the rule lives and THIRD_PARTY_LICENSES.md carries the verdict.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore frontend/src/Client/Client.csproj \
        --locked-mode \
        -p:Configuration=Release \
        -p:UnoDisableHotDesign=true \
        -p:UnoDisableMCPSupport=true

# The same two continuous-integration inputs the service is stamped with, so the page reports the version the process
# behind it reports rather than a second number.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish frontend/src/Client/Client.csproj \
        --framework net10.0-browserwasm \
        --configuration Release \
        --no-restore \
        -p:VersionSuffix="${VERSION_SUFFIX}" \
        -p:SourceRevisionId="${IMAGE_REVISION}" \
        -p:UnoDisableHotDesign=true \
        -p:UnoDisableMCPSupport=true \
        --output /client

# The pre-compressed copies of every asset, which the publish writes beside the originals and this image has nothing to
# serve them with: content negotiation is a reverse proxy's job here, and `Host` adds a static-file middleware and no
# more. Keeping them would carry a second, compressed copy of the whole bundle in every layer, pull, and node for a
# file nothing ever requests. A deployment that wants the bytes on the wire compressed enables it where TLS terminates.
RUN find /client/wwwroot -type f \( -name '*.br' -o -name '*.gz' \) -delete


########################################################################################################################
# Runtime: the service.
########################################################################################################################

FROM ${DOTNET_ASPNET_IMAGE} AS runtime

# Supplied by the build so a pulled image can be traced to the commit it came from without consulting anything mutable.
# The same two values are stamped into the assemblies by the build stage above, so what the label says and what the
# running process reports are the same build rather than two claims about it.
#
# `IMAGE_VERSION` has no useful default and its placeholder says so: the version is declared once, in
# Version.props, and `scripts/read-declared-version.sh` is what every build reads it with. A default that named
# a real version here would be a second place to remember, which is the failure ADR 0004 exists to avoid. An image
# labelled 0.0.0-unversioned is therefore one nobody passed a version to, and is not publishable.
ARG IMAGE_VERSION=0.0.0-unversioned
ARG IMAGE_REVISION=unknown
ARG IMAGE_CREATED=1970-01-01T00:00:00Z
# `release` or `nightly` for a published image, and `local` for anything else. The version identifier already separates
# the channels — a nightly always carries a `-nightly.<n>` prerelease identifier and a release never carries one — and
# this label is what still answers the question after a reference has been re-tagged, mirrored, or reduced to a digest,
# which is the state an image reaches long before anyone asks what it is. Its default is deliberately neither channel:
# an image nobody published belongs to neither.
ARG IMAGE_RELEASE_CHANNEL=local
# The directory the documentation site publishes this build's pages under, which is what
# `org.opencontainers.image.documentation` below is composed into an address from. It is derived from `IMAGE_VERSION`
# rather than declared beside it, and its default is the whole derivation for a release: the site names a version's
# directory after that version's tag.
#
# The one build that overrides it is the nightly, which is why this is an argument rather than the interpolation
# written inline. A nightly is named after the release it will become, and the site publishes no directory for a
# version nobody has released — what a nightly actually carries is whatever `main` was, which is what `latest`
# documents. A Dockerfile cannot decide that for itself, so the publication that already knows the channel passes it,
# exactly as it passes the version and the revision. `MailFathom.Common`'s DocumentationAddress reads the same rule off
# a version at run time for the surfaces that can.
ARG IMAGE_DOCUMENTATION_VERSION=v${IMAGE_VERSION}

# `org.opencontainers.image.licenses` is the SPDX identifier a registry and a scanner read without unpacking the
# image, and it says the same thing as the root LICENSE, the SPDX assembly metadata, and every source header. The
# label is only the claim; what discharges the obligation is the LICENSE and NOTICE copied in with the publish output
# below, which is where a reader of a pulled image finds the terms themselves.
#
# `org.opencontainers.image.description` is the sentence every registry that indexes labels shows beside the image, and
# it is also what the publish workflow pushes as Docker Hub's short description rather than keeping a second literal
# there. Docker Hub accepts 100 bytes for that field and silently truncates a longer one, so this line stays inside that
# budget and the workflow fails the release if it ever stops fitting — see docs/operations/container-image.md.
#
# `org.opencontainers.image.documentation` is where somebody holding only an image reference finds the pages for what
# they are holding. It names the version's own documentation directory rather than the site root, because the site
# opens on the newest release and a reader who pulled an older tag would otherwise be shown pages describing settings
# their image does not accept.
#
# `io.artifacthub.package.logo-url` is the one label here that OCI does not define: the specification has no field for a
# project icon, so a listing reads a vendor label instead. It names the same asset as the Helm chart's `icon`, and it is
# a URL rather than a copied file because a label carries no payload.
LABEL org.opencontainers.image.licenses="Apache-2.0" \
      org.opencontainers.image.title="MailFathom" \
      org.opencontainers.image.description="A brain for your mail: self-hosted and AI-native, IMAP synchronized and served over MCP." \
      org.opencontainers.image.source="https://github.com/Krzysztof318/MailFathom" \
      org.opencontainers.image.documentation="https://krzysztof318.github.io/MailFathom/${IMAGE_DOCUMENTATION_VERSION}/" \
      org.opencontainers.image.version="${IMAGE_VERSION}" \
      org.opencontainers.image.revision="${IMAGE_REVISION}" \
      org.opencontainers.image.created="${IMAGE_CREATED}" \
      io.mailfathom.release-channel="${IMAGE_RELEASE_CHANNEL}" \
      io.artifacthub.package.logo-url="https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/icon-1254.png"

# The MCP endpoint's own port, stated in the section that owns it. MailFathom refuses ASPNETCORE_HTTP_PORTS and every
# other URL-shaped address, because each surface states where it is served in its own configuration section. 8080 is
# restated here so the number is visible beside the EXPOSE that publishes it. The container speaks plain HTTP: TLS
# terminates at the ingress or the reverse proxy in front of it, which is also the only place a certificate needs to
# exist.
ENV McpEndpoint__Port=8080 \
    # Cleared rather than omitted, and this line is load-bearing. The aspnet base image sets ASPNETCORE_HTTP_PORTS=8080
    # in its own config, and the refusal above is a refusal rather than an "ignore" — so an image that inherits the
    # value fails every container it produces, against a variable no operator wrote and no deployment asset sets. A
    # Dockerfile cannot unset an inherited variable, and an empty value is what the guard treats as absent, so this is
    # the whole of the fix. Do not delete it as redundant with the refusal: the refusal is what it exists to keep from
    # firing. `ASPNETCORE_URLS` and `ASPNETCORE_HTTPS_PORTS` are absent from the base image and are deliberately not
    # cleared here — the smoke test in .github/workflows/publish-container-image.yml reads all three back off the built
    # image, so a base-image bump that introduces one fails the publication instead of being neutralized unnoticed.
    ASPNETCORE_HTTP_PORTS= \
    # No diagnostic IPC socket. It would be the one thing this process writes outside /tmp's tmpfs, and a socket that
    # can request a process dump is a way to read secret material out of managed memory — the exposure
    # docs/operations/secret-provisioning.md documents and asks deployments to close. Set it back to 1 deliberately,
    # for one session, when a dump is genuinely needed.
    DOTNET_EnableDiagnostics=0

# Two ports, because the image serves two listeners: 8080 for `/mcp`, which McpEndpoint:Port moves, and 8081 for the
# probes, which HealthEndpoints:Port moves. Declaring only the first would leave `docker image inspect`, `docker run -P`, and a
# registry's port list describing an image that answers on one socket more than it admits to.
#
# The administrative endpoint is off in this image and gets no port of its own here, deliberately. Its own default is
# 8080 as well, so a deployment that simply enables it serves it on the socket above — which is a decision worth taking
# rather than inheriting, because administering the service is a different authority from reading the mailbox. Give it
# AdminEndpoint:Port of its own and publish that port when the two should not share one; docs/operations/admin-endpoint.md
# is the page.
EXPOSE 8080 8081

# Owned by root and copied with the default mode, so the unprivileged account below can read and execute the
# application and write none of it. An explicit `--chmod` is deliberately not used: it applies one mode to directories
# as well as files, and a directory without its execute bit cannot be traversed by the runtime that has to load from
# it.
WORKDIR /app
COPY --from=build --chown=root:root /app .

# The client's browser head, under the web root the runtime resolves beneath the content root. It is copied
# unconditionally and served only where ClientEndpoint:Application:Enabled says so, which is what makes an image that
# serves no client cost its size and nothing else — no route, no middleware, and nothing reachable. Owned by root like
# everything else here, so the process reads the page it serves and cannot rewrite it.
COPY --from=client --chown=root:root /client/wwwroot ./wwwroot

# `app` is the unprivileged account the .NET base images define, and root owns everything under /app, so the process
# cannot rewrite its own code even before a read-only root filesystem is imposed on it. The image needs no writable
# path of its own; mount a tmpfs at /tmp, which is where the runtime expects to be able to write.
USER $APP_UID

# No HEALTHCHECK, deliberately. The host serves `/started`, `/health`, and `/alive` on a listener of their own — port
# 8081 unless a deployment configures another — and Kubernetes probes them over HTTP without needing anything from the
# image. Docker and Podman run a health check as a command *inside* the container instead, and a chiseled image has no
# shell and no HTTP client for one to be written in; adding either to reach an endpoint that is already reachable from
# outside would grow the image's attack surface for nothing. Compose publishes the probe port and asks from there.
ENTRYPOINT ["dotnet", "/app/MailFathom.Host.dll"]
