# syntax=docker/dockerfile:1

### The MailMcp container image.
#
# Two images come out of this file, and they are deliberately different things:
#
#   --target runtime     the service. It applies no schema change, ever.
#   --target migrations  the one-shot schema step an operator runs, and only when they mean to.
#
# Keeping them apart is what makes "the host never applies migrations" a property of the image rather than a rule
# somebody has to remember. The service image contains no migration tool, no SQL, and no credential that could apply
# one; `DatabaseSchemaStartupGate` refuses to start against a schema it does not recognize, and the operator's answer
# to that refusal is the second image.
#
# Both are built from the same context and the same restore, so what the service expects and what the migration step
# produces can never describe two different models.

# Base images are pinned to explicit patch versions rather than to a floating `10.0`, so a rebuild months from now
# resolves what this change was reviewed against. Every one of them is recorded in THIRD_PARTY_LICENSES.md.
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.302-noble
# `-extra` carries ICU and tzdata. MailMcp decodes internationalized headers, folds case for search, and formats
# instants for several time zones, so invariant globalization — what the plain chiseled image forces — would quietly
# change how mail from outside one alphabet is read. The size saved is not worth that.
ARG DOTNET_ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.10-noble-chiseled-extra
# The same server image the AppHost orchestrates locally, used here for its `psql` client. Sharing one pin means the
# client applying the schema and the server the schema was designed for never drift apart, and the register already
# reviews it.
ARG POSTGRES_IMAGE=pgvector/pgvector:0.8.2-pg17
# Matches the EF Core packages pinned in Directory.Packages.props. A design-time tool one minor version away from the
# runtime it generates for is a supported combination in EF, but not one this repository has reviewed.
ARG DOTNET_EF_VERSION=10.0.10


########################################################################################################################
# Build: restore, publish, and generate the schema script. Runs once, on the build platform.
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

# The restore inputs first, so editing a `.cs` file does not invalidate the restore layer. The lock files travel with
# the projects under src/.
COPY Directory.Build.props Directory.Packages.props NuGet.config global.json .editorconfig ./
COPY .config/BannedSymbols.txt .config/
COPY src/ src/

# Locked mode, like every other gate in this repository. The committed packages.lock.json files fix the transitive
# closure, so a republished package cannot change what a pinned version means between the branch's verification and
# this build; NU1004 fails here rather than a different graph being restored silently.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore src/Host/Host.csproj --locked-mode

# `UseAppHost=false` is what keeps the output architecture-neutral: an apphost is a native executable for one runtime
# identifier, and producing one here would tie the image to the build machine's. The entrypoint therefore invokes the
# managed assembly through `dotnet`.
#
# The XML documentation files are dropped: every project generates one, none is read at run time, and shipping them
# puts the repository's own commentary about internal contracts into an artifact an operator can unpack. The portable
# symbol files stay, because they are what turns a stack trace in a support report into file and line numbers, and
# they reveal nothing the assemblies beside them do not.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish src/Host/Host.csproj \
        --configuration Release \
        --no-restore \
        -p:UseAppHost=false \
        -p:PublishDocumentationFile=false \
        -p:PublishDocumentationFiles=false \
        -p:PublishReferencesDocumentationFiles=false \
        --output /app

# The deployment's migration artifact is an idempotent SQL script, which is what EF Core recommends for a production
# database and what this repository can review: it is text, it reads the same on every architecture, and it states the
# schema change in the language the change is actually made in. The alternative — a migration bundle — is a native
# executable per runtime identifier, needs a runtime-identifier-specific restore that the committed lock files reject,
# and takes its connection string on a command line where every process on the host can read it.
#
# `AGENTS.md` bars invoking `dotnet ef` by hand because a design-time command must see the connection string the
# orchestration issues rather than an ad-hoc one. `migrations script` is the command that does not: it reads the model
# and opens no connection, and `MailMcpDbContextDesignTimeFactory` is what answers it. There is no orchestration inside
# an image build to route it through, and generating the script here rather than committing it is what stops the
# script and the migrations from ever describing different schemas.
ARG DOTNET_EF_VERSION
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked <<'GENERATE_SCHEMA_SCRIPT'
set -eu
dotnet tool install --global dotnet-ef --version "${DOTNET_EF_VERSION}"
export PATH="${PATH}:/root/.dotnet/tools"
mkdir -p /schema
dotnet ef migrations script \
    --idempotent \
    --no-build \
    --configuration Release \
    --project src/Infrastructure/Infrastructure.csproj \
    --startup-project src/Host/Host.csproj \
    --output /schema/mailmcp-schema.sql

# EF writes the script as UTF-8 with a byte-order mark, and psql passes those three bytes to the parser as if they
# were SQL, which fails on the first statement. Nothing downstream needs the mark: the file is UTF-8 either way.
sed -i '1s/^\xEF\xBB\xBF//' /schema/mailmcp-schema.sql
GENERATE_SCHEMA_SCRIPT


########################################################################################################################
# Runtime: the service.
########################################################################################################################

FROM ${DOTNET_ASPNET_IMAGE} AS runtime

# Supplied by the build so a pulled image can be traced to the commit it came from without consulting anything mutable.
# The application does not yet report these at run time; stamping them into the assembly is #119, and the release
# semantics that decide what belongs in `version` are #116.
ARG IMAGE_VERSION=0.0.0-unversioned
ARG IMAGE_REVISION=unknown
ARG IMAGE_CREATED=1970-01-01T00:00:00Z

# `org.opencontainers.image.licenses` is deliberately absent. MailMcp has published no license of its own — #113 owns
# that decision — and a label naming one would make a distribution claim the copyright holder has not granted. Add it
# in the change that adds the LICENSE file, so the two can never disagree.
LABEL org.opencontainers.image.title="MailMcp" \
      org.opencontainers.image.description="Read-only MCP server over synchronized IMAP mailboxes." \
      org.opencontainers.image.source="https://github.com/Krzysztof318/MailMcp" \
      org.opencontainers.image.version="${IMAGE_VERSION}" \
      org.opencontainers.image.revision="${IMAGE_REVISION}" \
      org.opencontainers.image.created="${IMAGE_CREATED}"

# 8080 is what the .NET base images already listen on, restated here so the number is visible beside the EXPOSE that
# publishes it. The container speaks plain HTTP: TLS terminates at the ingress or the reverse proxy in front of it,
# which is also the only place a certificate needs to exist.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    # No diagnostic IPC socket. It would be the one thing this process writes outside /tmp's tmpfs, and a socket that
    # can request a process dump is a way to read secret material out of managed memory — the exposure
    # docs/operations/secret-provisioning.md documents and asks deployments to close. Set it back to 1 deliberately,
    # for one session, when a dump is genuinely needed.
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

# Owned by root and copied with the default mode, so the unprivileged account below can read and execute the
# application and write none of it. An explicit `--chmod` is deliberately not used: it applies one mode to directories
# as well as files, and a directory without its execute bit cannot be traversed by the runtime that has to load from
# it.
WORKDIR /app
COPY --from=build --chown=root:root /app .

# `app` is the unprivileged account the .NET base images define, and root owns everything under /app, so the process
# cannot rewrite its own code even before a read-only root filesystem is imposed on it. The image needs no writable
# path of its own; mount a tmpfs at /tmp, which is where the runtime expects to be able to write.
USER $APP_UID

# Docker and Podman run a health check as a command *inside* the container, and a chiseled image has no shell and no
# HTTP client for one to be written in. The runtime it already ships is the answer: the switch below asks the running
# host's own readiness endpoint over loopback and reports it as an exit code. Kubernetes needs none of this and the
# chart uses ordinary HTTP probes, which is why nothing here is on the container's critical path.
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD ["dotnet", "/app/MailMcp.Host.dll", "--health-probe"]

ENTRYPOINT ["dotnet", "/app/MailMcp.Host.dll"]


########################################################################################################################
# Migrations: the explicit, operator-invoked schema step.
########################################################################################################################

FROM ${POSTGRES_IMAGE} AS migrations

ARG IMAGE_VERSION=0.0.0-unversioned
ARG IMAGE_REVISION=unknown
ARG IMAGE_CREATED=1970-01-01T00:00:00Z

LABEL org.opencontainers.image.title="MailMcp schema migration" \
      org.opencontainers.image.description="Applies the MailMcp database schema. Run once, by an operator, never by the service." \
      org.opencontainers.image.source="https://github.com/Krzysztof318/MailMcp" \
      org.opencontainers.image.version="${IMAGE_VERSION}" \
      org.opencontainers.image.revision="${IMAGE_REVISION}" \
      org.opencontainers.image.created="${IMAGE_CREATED}"

# Without `--chmod`, for the reason the runtime stage gives: BuildKit applies the mode to the directories it creates
# along the way, and a `/schema` without its execute bit cannot be entered by the unprivileged account below. The
# default is a root-owned 0644 file in a 0755 directory, which is readable and not writable — what this needs.
COPY --from=build --chown=root:root /schema/mailmcp-schema.sql /schema/mailmcp-schema.sql
# One file into a directory that already exists, so the mode lands only on the file.
COPY --chown=root:root --chmod=555 deploy/migrations/apply-schema /usr/local/bin/apply-schema

# `postgres` is the unprivileged account the upstream image already creates. The entrypoint needs no more than the
# ability to read the script and open a connection.
USER postgres

ENTRYPOINT ["/usr/local/bin/apply-schema"]
