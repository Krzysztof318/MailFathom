# Local development

<!-- describes: scripts/**, global.json, .config/dotnet-tools.json, .config/typos.toml, src/AppHost/**, .github/workflows/**, .github/dependabot.yml -->

Use the .NET SDK pinned in `global.json`. Test execution is configured for Microsoft Testing Platform through the repository-level `global.json` test runner setting.

**Linux is the only officially supported platform**, for development as much as for deployment: the orchestration starts Linux containers, the deployment shapes are a container, Kubernetes, and a systemd service, and TLS goes through the system OpenSSL. Development on Windows may work — the solution is ordinary .NET — but **expect problems and a setup of your own**, and nothing in this repository is verified against it.

A development machine also needs **OpenSSL 3.0 or later**, because every TLS connection a running MailFathom makes — to the mail server, to PostgreSQL — is handshaked by the system library rather than by .NET, and its security policy decides which servers are reachable at all. **1.1.1 is the hard floor**: .NET 10 requires it on Unix and [fails to start](https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/openssl-version-requirement) without it. **Anything between 1.1.1 and 3.0 may work and may not** — it is out of upstream support and nothing in this repository is verified against it, so treat a failure that reproduces only there as an environment problem rather than a defect.

Nothing has to be configured for a mail server that clears the distribution's default policy, which is nearly all of them: a checkout that sets nothing runs at that full-strength policy and negotiates the newest TLS both ends support. Relaxing it is an opt-in for one process — a development mailbox on a server the policy refuses is what [the platform TLS policy](platform-tls-policy.md) is for, and it applies to the host however the host is started.

## Commands

For the normal implementation loop, run:

```bash
bash scripts/verify-fast.sh
```

Before committing, run the complete local gate:

```bash
git add <task-files>
bash scripts/verify-full.sh
```

While reviewing the change, ask what it obliges elsewhere:

```bash
bash scripts/review-obligations.sh
```

That prints the tests naming each changed type, the pages whose `describes:`
marker covers each changed path, and the registers whose trigger moved, each
saying whether the change touched it. It is the same index `Fathom review` runs
on a pull request, reached through an adapter that hands it a local diff, so the
answer is the one the pipeline will give rather than an approximation of it. It
reports and never gates: nothing it prints is a finding until it is confirmed in
the file it points at, and it names the untracked paths no diff contains rather
than describing less than the change while looking complete.

The fast script restores the solution, builds it in Release configuration, runs
all unit tests without rebuilding, and formats the C# files the branch changed.
Formatting runs in both scripts on purpose: style diagnostics such as `IDE0005`
come from `dotnet format` rather than from the build, and leaving them to the
final gate means discovering them only after tool restore and the whole coverage
collection have run.

The fast script is the only one that rewrites source files. It runs
`dotnet format` twice over the changed files: a repairing pass applies every
available code fix, and a `--verify-no-changes` pass names by file and line what
had none. Neither pass replaces the other, because the repairing pass exits `0`
and identifies no file when a diagnostic such as `IDE0060` has no code fix.
Restricting both passes to the changed files is what keeps the loop usable:
`dotnet format` reloads the MSBuild workspace on every invocation, which costs
roughly 15 seconds, and analyzing the whole solution costs about 70 seconds
against about 30 for a handful of files. The final gate still formats the whole
solution, so a defect outside the changed files cannot merge.

The full script additionally restores repository tools,
runs the workflow contract suite, executes the aggregate coverage gate, and
checks the Git diff. It rejects remaining untracked files, so inspect the
staged diff before running it. See [Agent workflow](agent-workflow.md) for the
workspace inspection command and shared skills.

The full script fetches the base branch and refuses to continue when the branch
does not contain it, so it needs access to the remote and cannot run offline.
Rebase onto the fetched base when it reports the branch is behind. The fast
script queries only local Git state and remains available offline.

The base is `main` on whichever remote points at `Krzysztof318/MailFathom` —
`origin` here, and conventionally `upstream` in a fork, where `origin` is the
fork and its `main` is whatever was last synced.
[Which remote is the base](agent-workflow.md#which-remote-is-the-base) describes
how that is resolved and what the gate prints when nothing resolves.

Neither gate covers the deployment assets, and no script here does either. Testing, building, and publishing what
`deploy/` produces is one pipeline's job rather than several local scripts' — a developer would otherwise need a Docker
daemon, a Kubernetes cluster, and Helm on the machine to learn what a runner can decide once. `Release` and `Nightly`
are that pipeline: `Release` publishes the image to both registries and the Helm chart beside it, and `Nightly`
publishes the image alone. [The container image](container-image.md) and [Kubernetes and Helm](deployment-kubernetes.md)
describe what each has to establish.

What is useful locally is reading the chart, which needs only Helm:

```bash
helm lint     deploy/helm/mailfathom --values deploy/helm/mailfathom/ci/release-values.yaml
helm template verification deploy/helm/mailfathom --values deploy/helm/mailfathom/ci/nightly-values.yaml
```

The `Container image` workflow builds the image for both supported architectures and does nothing else. It is manual
dispatch only, like the integration suite, and it publishes nothing.

What does publish is `Release`, on an annotated version tag, and `Nightly`, on its schedule. Neither is something to
start as part of a task, and neither has a local equivalent: they run `Build, test, format, and migrations` — the same
workflow `CI` calls for a pull request — then, for a release, the integration suite, and only then build and push. [The container image](container-image.md#published-images) records
what they produce and how a published image is verified.

Both scripts stop immediately when `HEAD` resolves to `main` or `master`,
because verification on the integration branch reports on code that no change
is about to touch. Check out the branch that carries the change first. A
detached `HEAD` and any other branch name are accepted, in the primary checkout
as well as in a linked worktree.

Run the web host directly, against a PostgreSQL server you provide yourself:

```bash
dotnet run --project src/Host/Host.csproj
```

The host inherits the environment it is started in, so a development mailbox on a mail server the platform's TLS
policy refuses is reached by prefixing that command with `OPENSSL_CONF=<path>` — see
[the platform TLS policy](platform-tls-policy.md#pointing-the-host-at-it). The host says at startup that it is running
under a configured file, which is also how to confirm it received one.

## Running locally with Aspire

The Aspire orchestration is the intended local start: it provisions the database, applies the schema, and starts the
host in the right order, so a working MailFathom is one command on a machine with the pinned SDK and a running Docker
daemon:

```bash
dotnet run --project src/AppHost/AppHost.csproj
```

Three resources come up, in dependency order. The `postgres` container starts first and has to report healthy; the
`mailfathom-migrations` resource then applies every pending migration to it; and `mailfathom-host` waits for that run
to complete before starting, which is why the schema gate that fails a fresh deployment on purpose never fires on a
local run — the explicit schema step the deployments require is performed here by the orchestration, before the host
looks. The host runs in the `Development` environment on ports the app model states rather than allocates, so the
application listener answers at `http://localhost:8080`, the TLS one at `https://localhost:8443`, and the probes at
`http://127.0.0.1:8081`. An MCP client's configuration therefore names an address once instead of following whatever a
launch profile or the orchestrator's port allocation produced that run. None of the three is proxied either: the socket
a client connects to is the socket Kestrel opened, which is what keeps a TLS handshake — and a client certificate — a
conversation with the host itself. The probe listener is pinned to loopback deliberately, because the probes answer
without a credential and nothing on a local network has any business asking them.

All three ports are stated by the app model, but not in the same way. The two application ones are HTTP endpoints, and
the probe port is a TCP one that injects itself into the host's `HealthEndpoints:Port` setting — declared once rather
than declared and then configured again beside it. The scheme is what makes that possible: Aspire builds
`ASPNETCORE_URLS` from HTTP and HTTPS endpoints, so an HTTP endpoint on the probe port would make it an application
listener, and the host refuses to start when the two collide. That refusal is the design working, not a limitation to
route around; it is also why the resource carries no `WithHttpHealthCheck`, which derives its address from an endpoint
and would need the HTTP one that stops the host from starting.

`8080` and `8081` are the ports [the container image](container-image.md) publishes, so a local run and a deployed one
answer on the same numbers. `8443` belongs to this topology alone: the image serves no TLS listener, and `443` is a
privileged port a developer's process cannot bind without a capability nothing here should require.

A fixed port is one port, so two ordinary orchestrations cannot run at once on one machine — a second one fails to bind
and says so. The integration-test topology is left on allocated ports for exactly that reason, which is what keeps a
suite run and a developer's run able to coexist.

That TLS listener presents the ASP.NET Core development certificate, which Kestrel uses for an address no endpoint
configuration claims. Create and trust it once per machine — without a certificate at all the host fails to start with
nothing to present, and with an untrusted one it starts while every client refuses the handshake:

```bash
dotnet dev-certs https --trust
```

The MCP endpoint's own HTTPS profiles are the deployed shape and stay unconfigured locally, so a checkout needs no
certificate material of its own. [The MCP endpoint](mcp-endpoint.md) describes what a deployment configures instead.

The AppHost prints the dashboard address, including a one-time login link, as it starts. The dashboard is where a
local run is observed: per-resource console output, structured logs, traces, and metrics, all delivered over OTLP
because Aspire injects `OTEL_EXPORTER_OTLP_ENDPOINT` into the resources it starts. That injection is currently the
only place telemetry export is configured at all — a deployment exports nothing until an operator sets the variable
themselves. [Telemetry](telemetry.md) records what the host emits and where it goes.

A freshly started host synchronizes nothing and serves no MCP endpoint, because both defaults are the shipped ones.
Configure a development mailbox through user secrets as [development secrets](#development-secrets) below shows, and
enable the endpoint the same way when a tool call is what is being tested — in Development the `ReferenceOrInline`
interpretation keeps the credential a one-liner:

```bash
dotnet user-secrets --project src/Host/Host.csproj set "McpEndpoint:Enabled" "true"
dotnet user-secrets --project src/Host/Host.csproj set "McpEndpoint:Authentication" "ApiKey"
dotnet user-secrets --project src/Host/Host.csproj set "McpEndpoint:ApiKeys:0:Name" "dev"
dotnet user-secrets --project src/Host/Host.csproj set "McpEndpoint:ApiKeys:0:SecretReference" "plaintext:dev-key"
```

A development mailbox served by a mail server whose TLS parameters the platform refuses needs one more thing, and the
symptom sends most people the wrong way: the handshake fails, but reads as an authentication failure. Export
`OPENSSL_CONF` in the shell the orchestration starts from and the AppHost passes it through to `mailfathom-host`, which
then reports at startup that it is running under it. [The platform TLS policy](platform-tls-policy.md) covers the file,
what it costs, and how to confirm the handshake is what failed. The AppHost passes an exported value through and never
sets one, so a checkout that exports nothing runs under the platform default; the integration-test topology receives it
under no circumstances, because a suite whose handshakes depended on the machine that ran it would prove nothing.

An MCP client then connects to `http://localhost:8080/mcp`, or to `https://localhost:8443/mcp`, with
`Authorization: Bearer dev-key`. Stopping the orchestration with `Ctrl+C` — or
`aspire stop --apphost src/AppHost/AppHost.csproj --non-interactive` — leaves the synchronized mail in place, because
the database volume outlives the container and the container outlives the run. The container is stopped rather than
left running, so the next start is a restart of the server that was there.

The AppHost PostgreSQL resource uses the `pgvector/pgvector:0.8.2-pg17` image so local development starts with a PostgreSQL server that can support the `vector` extension required by the RAG and embedding slices. It keeps its data in a named Docker volume, so synchronized mail survives a restart instead of costing a full IMAP synchronization every time the orchestration stops.

The resource is also given a persistent container lifetime, which the ephemeral integration-test topology deliberately
is not. A session lifetime removes the server on every shutdown and builds it again on the next start — an image check,
an initialization pass, and a health wait, several times a day, against data that was never in question. A persistent
container is reattached instead, so the server a developer stops is the server they get back, and removing it is an
explicit `docker rm -f` rather than something stopping the app host does.

Aspire's persistent lifetime also leaves the container *running* after the app host exits, which is not what a stopped
orchestration should leave behind, and the lifetime has no third value between that and destroying the container. So
`PersistentContainerStopper` stops it during shutdown: what outlives an orchestration is the container and its data
rather than a PostgreSQL process and the port it holds. A shutdown that runs — `aspire stop`, or an ordinary
termination — stops it; a process killed outright runs nothing and leaves the container up, which is then stopped with
`docker stop` like any other.

The server is reached on the conventional port with conventional credentials, so a database tool needs nothing this
repository has to tell it:

| | |
| --- | --- |
| Host and port | `localhost:5432` |
| Database | `mailfathom` |
| User and password | `postgres` / `postgres` |

Both credentials are stated by the app model rather than generated. A generated password has to be persisted to stay
stable, and PostgreSQL applies a password once — when it initializes an empty data directory — so a persisted password
and a volume that outlives it can drift apart and the server then refuses a database nothing was wrong with. A value
that never changes cannot drift from itself. It authenticates one local development database whose port Aspire
publishes on the loopback address alone, and no deployment is reached this way: a deployed MailFathom takes its
connection string from [secret provisioning](secret-provisioning.md), which this app model has no part in.

The fixed port is a convenience with one consequence worth knowing: a PostgreSQL already listening on `5432`, whether a
system service or another orchestration, takes the port first and the container then fails to start with an
address-in-use error naming it.

`src/AppHost/AppHost.csproj` still declares a `UserSecretsId`, and `src/AppHost/Properties/launchSettings.json` still
sets `DOTNET_ENVIRONMENT=Development`, because that is where Aspire persists what it generates for the app model
itself — the dashboard and OTLP API keys — and user secrets are loaded in the `Development` environment only.

To discard the local database and start from an empty one, remove the container and its volume:

```bash
aspire stop --apphost src/AppHost/AppHost.csproj --non-interactive
docker volume ls --filter name=-postgres-data
docker rm -f $(docker ps -aq --filter volume=<volume>)
docker volume rm <volume>
```

Aspire names that volume after the AppHost project's path, so every clone and every worktree owns a different one and the name has to be read rather than assumed. List them first and take the one belonging to the checkout being reset; removing another one destroys a database the reset was not about.

## Development secrets

Secrets are never written into configuration as values, in development either. `appsettings.Development.json` sets the interpretation mode to `ReferenceOrInline`, which keeps `plaintext:` references convenient without weakening the shipped `ReferenceOnly` default:

```json
{
  "Secrets": { "Interpretation": "ReferenceOrInline" }
}
```

`src/Host/Host.csproj` declares the `UserSecretsId` those commands write into. It is a fixed identifier rather than one
generated per clone, so every checkout reads the same store and the commands below can be named here at all. The secret
store is loaded by the framework in the `Development` environment only, which is the environment the orchestration and
both launch profiles run the host in.

Configure a development account in `appsettings.Development.json` or, better, in user secrets:

```bash
dotnet user-secrets --project src/Host/Host.csproj set \
  "MailSynchronization:Accounts:0:Secrets:Password:Name" "dev-password"
dotnet user-secrets --project src/Host/Host.csproj set \
  "MailSynchronization:Accounts:0:Secrets:Password:SecretReference" "plaintext:dev-password"
```

The block shape is identical to production, so moving a working development configuration to a real deployment is one string edit — `plaintext:dev-password` becomes `systemd-credential:imap-primary-password` — rather than a restructuring.

Neither file nor user secrets is a production secret store. User secrets are stored unencrypted in the developer's profile directory and exist only to keep credentials out of the repository; `appsettings.Development.json` is committed and must never hold a real credential. [Secret provisioning](secret-provisioning.md) describes the deployment paths.

## Command-line tooling

The repository provisions no development environment, so install the SDK and any command-line tools on the developer machine. Repository-local tools declared in `.config/dotnet-tools.json` come from `dotnet tool restore`: `reportgenerator` merges the per-assembly Cobertura reports the coverage run produces, and `dotnet-ef` generates and scripts migrations. Both are pinned there because both run in continuous integration, which is also what keeps `dotnet-ef` at one version across every machine instead of at whichever one a developer installed.

Two tools are installed globally when their workflows are needed:

```bash
dotnet tool install --global Aspire.Cli --version 13.4.6
dotnet tool install --global csharp-ls --version 0.26.0
```

`aspire` is only required for Aspire CLI workflows against the AppHost. `csharp-ls` is the C# language server that editors and agent tooling launch to resolve symbols before editing, instead of discovering a misspelled type at build time.

`csharp-ls` is installed globally rather than pinned in `.config/dotnet-tools.json` because a manifest-local tool is only reachable as `dotnet tool run csharp-ls`; it never lands on `PATH`, so a client that launches the bare `csharp-ls` executable still fails with `ENOENT`. A global install puts it in `~/.dotnet/tools`, which is on `PATH`, and keeps the language server out of the `dotnet tool restore` that continuous integration runs. All versions are recorded in `THIRD_PARTY_LICENSES.md`; keep the register aligned when you move to a newer one.

### EF Core design-time commands

Which mechanism a command uses is decided by one question: whether it needs a database.

**A command that reaches a database goes through the AppHost's `mailfathom-migrations` resource**, so it uses the server the orchestration provisions and the connection string it issues rather than a local environment that can differ from every real one.

Aspire 13 has no `aspire exec` command; earlier versions offered one, and it is gone. Its replacement is the `Aspire.Hosting.EntityFrameworkCore` package, which declares a migration resource in the app model. `src/AppHost/Program.cs` adds it against the host project, points it at `src/Infrastructure` for the migrations, and calls `RunDatabaseUpdateOnStart`, so a local run applies pending migrations before the host starts and the host waits for that to finish.

```bash
aspire resource mailfathom-migrations ef-database-status --apphost src/AppHost/AppHost.csproj --non-interactive
aspire resource mailfathom-migrations ef-database-update --apphost src/AppHost/AppHost.csproj --non-interactive
aspire resource mailfathom-migrations ef-database-reset  --apphost src/AppHost/AppHost.csproj --non-interactive
```

The same commands are available from the dashboard. `ef-database-reset` drops the database and replays every migration into it, which is how local data is cleared; it changes no file in the repository.

**A command that reads only the checkout calls `dotnet ef` directly**, because it has no database it could see wrongly. Generating a migration, scripting one to SQL, and asking whether the model has outrun its migrations all compare the compiled model against the committed model snapshot, and they produce identical output against a database that does not exist. `scripts/add-migration.sh` and `scripts/script-migration.sh` are those commands. Both export a design-time connection string pointing at a port nothing listens on when the environment carries none, so a future version that starts requiring a connection fails there instead of silently reaching whichever database the shell happened to name.

That split is why generating a migration needs no Docker and takes seconds, while applying one needs the orchestration running. It also keeps the two failures apart: a migration that generates cleanly and fails to apply is worth seeing as two separate outcomes.

`dotnet-ef` is pinned in `.config/dotnet-tools.json` and arrives with `dotnet tool restore`. The migration resource fetches its own copy, so a global install is only needed by an editor that runs design-time commands of its own.

`Host` is the startup project, because it is the resource the orchestration issues the connection string to, and it therefore carries a design-time-only reference to `Microsoft.EntityFrameworkCore.Design`. `Infrastructure` owns the context, the design-time factory, and the migrations under `src/Infrastructure/Persistence/Migrations/`.

`MailFathomDbContextDesignTimeFactory` gives EF Core a context without starting the host, which matters because the host composes its connection string during startup and design-time tooling never runs that. It reads `ConnectionStrings__mailfathom` when the orchestration supplies it, then `MAILFATHOM_DESIGN_TIME_CONNECTION_STRING` for a command run outside it, and falls back to `Host=localhost;Database=mailfathom;Username=mailfathom`. The orchestrated value wins so a stale override left in a shell cannot point a migration at a different database than the one being migrated.

Every migration in the repository is permanent. A model change appends one with `scripts/add-migration.sh <MigrationName>` and never regenerates, renames, reorders, or deletes an existing one, because a migration identifier that a database has written into its `__EFMigrationsHistory` can never be reached again once it is regenerated: that database can then only be recreated, destroying whatever it held. Nothing in the repository deletes a migration, and no command offers to.

`scripts/script-migration.sh` writes the SQL for a migration range to standard output, which is what a review reads — the generated C# hides the destructive operation, the rewrite EF inferred from a rename, and the lock a column change takes. `scripts/dump-local-schema.sh` then shows the schema PostgreSQL actually holds after the migration is applied. The `add-migration` skill is the surrounding workflow, including the review, which no script performs.

`Pending model changes` in CI runs `dotnet ef migrations has-pending-model-changes` on every pull request touching `src/`, so a model change merged without its migration fails there rather than at a host's startup. Configuration that produces no SQL — a constraint name, an index filter — still moves the model snapshot, so that job can fail on a change that alters no schema; the snapshot is regenerated by EF and never hand-edited.

The baseline migration also installs the `vector` extension. The `pgvector/pgvector` image ships it but does not install it, so without this the first vector column would fail on a type PostgreSQL does not know.

#### Apply policy

The host never applies migrations, in any environment. It reads the migration history at startup and fails fast when the database has not applied every migration the running build defines, so an instance either serves traffic against a known schema or does not serve traffic at all. A pending migration reports error code `32001` and an unreadable migration history `32002`.

It then checks one thing the migration identifiers cannot express. `Persistence:TextSearchConfiguration` is compiled into the search vector's stored generated column when the table is created, and the identifier of the migration that created it is the same whichever configuration produced it. A host configured for `english` against an index built with `simple` would stem its queries one way and read lexemes built the other, returning fewer results rather than an error, so the host compares its configured value against the expression PostgreSQL actually holds and fails with `32003` when they differ.

Generating a migration for a non-default configuration is therefore a deliberate act: export `Persistence__TextSearchConfiguration` before running the `add-migration` workflow, and rebuild the search documents afterwards. The design-time factory reads that variable, which is the double-underscore encoding of the setting a deployment already has.

Applying is one mechanism per environment: `mailfathom-migrations` locally, and an explicit deployment step elsewhere. A host that mutates schema while starting could race a second instance, could apply a destructive change nobody reviewed at deploy time, and would leave the operator no point at which to take a backup.

That deployment step is one idempotent SQL file, and `scripts/build-schema-artifact.sh` produces it:

```bash
scripts/build-schema-artifact.sh      # artifacts/schema/mailfathom-schema-<version>.sql, and its .sha256
```

It runs `aspire publish`, which reads the `PublishAsMigrationScript` declaration in `src/AppHost/Program.cs`, so the file a release attaches and the file this produces come from one statement rather than two. Like the other commands that only read the checkout it reaches no database: the SQL is generated from the migration assembly, so it produces identical output against a server that does not exist. Unlike them it needs the Aspire CLI rather than `dotnet-ef`, because the declaration it reads lives in the app model. [Applying the database schema](database-schema.md) is what an operator then does with it.

The GitHub CLI (`gh`) is installed separately through the operating system package manager and is required for the issue and pull-request workflow in [Issue tracking and the roadmap board](issue-tracking.md). It needs the `project` scope on top of its default scopes so it can read and update the roadmap board.

On a machine that has never authenticated, log in and request the scope in the same step:

```bash
gh auth login -s project
```

On a machine that is already authenticated, add the scope to the stored credentials instead; `gh auth refresh` only expands existing credentials and fails when no host is authenticated:

```bash
gh auth refresh -s project
```

Confirm the result with `gh auth status`, which must list `project` among the token scopes. Its reviewed version is recorded in `THIRD_PARTY_LICENSES.md` alongside the other developer tooling.

## Package sources and lock files

Three files decide what a restore produces, and each answers a different question. `Directory.Packages.props` pins the version of every directly referenced package. The repository-root `NuGet.config` decides which sources those packages may come from. Each project's `packages.lock.json` records the transitive closure the pins resolve to, one `resolved` version and one content hash per package.

`NuGet.config` exists because NuGet merges every configuration file on the path from the drive root down to the working directory. Without a repository-owned file the source list is whatever the developer machine defines, so a privately configured feed would be searched for every package here and a restore could resolve a dependency from a source `THIRD_PARTY_LICENSES.md` never reviewed. The file clears that inherited list and declares `nuget.org` alone. Its package source mapping then requires every package identifier, transitive ones included, to match a pattern before it can be restored; the single `*` pattern costs nothing while there is one source, and it makes a second source fail closed rather than silently join the search.

Lock files close the gap central pinning leaves open. The 46 pins in `Directory.Packages.props` are direct references; `src/Infrastructure` alone resolves 47 further packages transitively, and nothing recorded those before. The content hash also means a package republished under a version already pinned no longer passes unnoticed, and a dependency bump shows every transitive move in the pull request diff.

Fourteen of the sixteen projects carry one. `AppHost` and `IntegrationTests` do not, because `Aspire.AppHost.Sdk` adds `Aspire.Dashboard.Sdk.<rid>` and `Aspire.Hosting.Orchestration.<rid>` as references chosen from `NETCoreSdkRuntimeIdentifier`. That part of the graph describes the machine running restore rather than this repository, so a lock file written on Linux names packages a Windows, macOS, or Linux ARM64 developer never asks for, and locked mode there fails with `NU1004: A new package reference was found Aspire.Dashboard.Sdk.win-x64` before a build can start. `IntegrationTests` follows `AppHost` because it references the project and inherits those packages transitively, and a lock file cannot exclude a subtree. Both ship nowhere, and their versions stay pinned centrally like every other project's.

The lock files are committed. Both verification scripts restore in locked mode, `scripts/run-integration-tests.sh` does the same for the integration project — where the flag still enforces the lock files of every project it references — and the `CI` workflow does it in both of its restoring jobs; the `Integration tests` workflow inherits it through the script it calls. A restore that would have to rewrite a lock file fails there instead:

```text
NU1004: The package reference Roslynator.Analyzers version has changed from [4.13.1, ) to [4.13.0, ).
The packages lock file is inconsistent with the project dependencies so restore can't be run in locked mode.
```

That is the expected result of moving a pin without regenerating. Regenerate deliberately, in the same change:

```bash
dotnet restore MailFathom.slnx --force-evaluate
```

Then read the resulting diff before committing it. A bump that moves one direct version and forty transitive ones is a different review from one that moves only itself, and locked mode exists so that difference is visible rather than discovered later.

## Code coverage

The full verification script collects and enforces coverage. To run only the
underlying coverage target after a Release build:

```bash
dotnet tool restore
dotnet msbuild .config/CodeCoverage.proj -t:Collect
```

The command runs the whole solution in one test invocation, which produces one uniquely named Cobertura report per unit-test assembly, merges the reports, and requires at least 85% aggregate line coverage across `Domain`, `Application`, `Infrastructure`, `AI`, and `Mcp`. The result always represents the whole configured scope, not only changed lines. `Host` and `AppHost` are excluded as thin executable composition roots.

Two attributes take code out of that denominator, and `.config/testconfig.json` configures the collector to honor both. `[ExcludeFromCodeCoverage]` marks code that should never participate in coverage. `[RequiresIntegrationCoverage]`, declared in `src/shared/RequiresIntegrationCoverageAttribute.cs`, marks code whose verification needs a real database, a real mail server, or a composed host: the EF Core context and its entities, the persistence stores, the file-system and environment secret readers, and the infrastructure registration extensions carry it today. The MailKit adapter deliberately does not, even though the integration suite now exercises it against a real IMAP server, because MailKit publishes `IImapClient` and `IMailFolder` and the adapter is reachable from a unit test through them; it stays in the enforced denominator and the integration suite proves the wire behavior a substitute cannot. Marked code is measured by the integration suite instead, in a separate report that enforces nothing — see [Integration tests](#integration-tests) below. The marker stays once the class is covered there: it records where the verification lives, not whether it has been written, and a class a unit test cannot reach stays unreachable afterwards. Remove it only when unit-testable logic enters the class, which puts every line back into this denominator and is how to check that the exclusion is still earned.

A third exclusion is applied by path rather than by attribute: `.config/CodeCoverage.proj` filters `**/Persistence/Migrations/*.cs` out of the merged report. EF Core generates those files, so they carry no attribute the generator would preserve, and no unit test may execute them — a migration is proven by applying it to a real PostgreSQL server and reviewing the resulting schema. Leaving them in put roughly a thousand uncoverable lines in the denominator and moved the aggregate by more than twenty points, which would have masked a real regression anywhere else.

Raw Cobertura reports and TRX files are written under `artifacts/coverage/raw/`. The merged Cobertura and HTML reports are written under `artifacts/coverage/report/`.

## Integration tests

`tests/IntegrationTests` verifies what a unit test structurally cannot: EF Core mappings, the baseline migration, database constraints, transaction and concurrency behavior, the SQL PostgreSQL actually runs and the plans it chooses, the two readers that reach the file system and the process environment, and what MailKit puts on the wire against a real IMAP server. It starts the repository's own app model through `Aspire.Hosting.Testing`, so the orchestration under test is the one `aspire run` starts rather than a second container topology maintained beside it. [The stored email schema](../architecture/stored-email-schema.md#what-the-integration-suite-proves) lists what the persistence half of the suite establishes.

Run it on request:

```bash
bash scripts/run-integration-tests.sh
```

Arguments are forwarded to Microsoft Testing Platform, so `bash scripts/run-integration-tests.sh --filter-class '*RemoteSeenFlag*'` narrows the run to the flag-preservation tests. xUnit v3 names the option `--filter-class`, with `--filter-method`, `--filter-namespace`, and their `--filter-not-*` counterparts beside it; a plain `--filter` is not one of them and makes the run print its help and exit non-zero.

The whole suite takes a little over a minute after the images are pulled, and a filtered run is not much faster: the orchestration, the migration, and both containers start once for the assembly whatever the filter selects.

The suite needs a container runtime. The script uses `docker`; set `MAILFATHOM_CONTAINER_RUNTIME` to use another one.

It is deliberately not part of any other command. `scripts/verify-fast.sh` and `scripts/verify-full.sh` never start it, the 85% coverage gate never measures it, and no pull-request workflow runs it. The mechanism is one MSBuild property: `IsTestingPlatformApplication` is `false` for the project, which is what a solution-wide `dotnet test` uses to discover test projects, so neither the fast loop nor the coverage collection finds it. The project stays in `MailFathom.slnx` regardless, so it is built, analyzed, and formatted by exactly the same gates as everything else — a compile or style error in an integration test still fails an ordinary pull request.

### Ephemeral resources

The app host is started with the argument `IntegrationTesting=true`, which selects a second topology in `src/AppHost/Program.cs`:

- every container and volume is named `mailfathom-integrationtests-<run>-…`, where `<run>` is eight hex characters
  generated for that run, rather than taking Aspire's random postfix and the path-derived volume name a developer's
  orchestration uses. The shared leading part is what a filter finds them all by; the run identifier is what keeps two
  suites started on one machine from racing for one name, and what lets a run remove exactly what it created;
- the PostgreSQL container is therefore `mailfathom-integrationtests-<run>-postgres` and its data volume
  `mailfathom-integrationtests-<run>-postgres-data`. The volume is new on every run by construction, which is what the
  baseline migration has to apply to for a run to prove it applies cleanly at all;
- a `mailserver` container named `mailfathom-integrationtests-<run>-mailserver` is added, which a developer's orchestration never gets — it exists so the suite has a real IMAP server to synchronize against, and starting one beside a developer's own accounts would advertise a mailbox nothing points at;
- the `mailfathom-host` project resource is added to the model but never started, because the suite exercises classes against real infrastructure and a running MailFathom would synchronize mail underneath the data a test is asserting on;
- a second project resource, `mailfathom-mtls-host`, is added on the same terms and started by a collection of its own, `MutualTlsHostCollectionDefinition`, which the assembly's orderer places after the collection that starts the host above — starting a second project process must not be what a rate limit is measured against. It serves the endpoint over an HTTPS profile behind a `Required` client-certificate profile, which is what lets the suite prove the mTLS rules against a real handshake; a certificate requirement is one answer for a whole process, so it cannot be a posture applied to the host above. Its server certificate, private key, and trust anchor are issued in memory per run by the test suite and injected into the environment variables the app model's `env:` secret references name, so nothing of the kind is committed and a developer's orchestration never gets this resource at all.

The prefix comes from `OrchestrationContract` in `src/AppHost`, and nothing else in the repository uses it. The run
identifier is generated by `scripts/run-integration-tests.sh` and passed to the app model in
`MAILFATHOM_INTEGRATIONTESTS_RUN_ID`; the script needs it before the suite starts, because it is what scopes the
removal afterwards. An unset variable makes the app model generate one, which is what a suite started by hand gets —
and what nothing then removes, since only the script cleans up.

The script removes this run's containers and volumes when the run ends, whether it passed, failed, or was interrupted,
because nothing the suite creates is meant to outlive it. It removes nothing beforehand: a name no earlier run could
have used is already an empty database, and a sweep of the shared prefix would destroy a concurrent run's containers to
establish what this run's own name already establishes.

A run killed with `SIGKILL` skips that removal. What it leaves cannot be cleaned up automatically by a later run, which
has no way to tell a dead run's containers from a live one's, so the script reports them at the end instead and prints
the command that removes them once nothing else is running. To look by hand:

```bash
docker ps --all --filter name=mailfathom-integrationtests
docker volume ls --filter name=mailfathom-integrationtests
```

A developer's own orchestration is untouched by any of this: its container name and its volume name are derived from the AppHost project path and never carry the test prefix.

### The mail server

The `mailserver` resource is `greenmail/standalone:2.1.11`, configured through `GREENMAIL_OPTS` to start only SMTP on 3025, IMAP on 3143, and the API server on 8080. The API server is what the resource's health check polls — `/api/service/readiness` — so the suite waits for a server that is accepting rather than for a container that has started; without it the first test would race the listener and fail as a connection refusal that says nothing about the behavior under test.

It serves one throwaway mailbox, `mailfathom` / `mailfathom@mailfathom.test`, whose credentials are constants in `OrchestrationContract`. They exist only in the ephemeral topology, unlock nothing outside the container, and are declared once so the app model that configures the server and the suite that logs into it cannot drift apart. GreenMail's own verbose logging stays off, because it transcribes the whole IMAP conversation — password included — into the orchestration log.

Two behaviors of this server are worth knowing when reading a failure:

- It advertises `AUTH=XOAUTH2` and nothing else, so both the adapter under test and the suite's own observations empty MailKit's advertised mechanism set and authenticate with the IMAP `LOGIN` command. That is a legal server shape, and `MailKitTransportSecurityMapping` permits the fallback exactly when the account's policy already allows a clear-text mechanism.
- Each folder carries a real UIDVALIDITY, `System.currentTimeMillis() / 1000` at creation, so replacing a folder hands out a new one. That is how the suite produces a UIDVALIDITY change without simulating anything. Two consequences are built into `OrchestratedMailbox.RecreateFolderAsync`: it waits past the next whole second, because a folder replaced inside the same second is handed back the value it just had, and it retires the old folder by renaming it rather than deleting it. GreenMail 2.1.11 crashes on `DELETE` of a folder an earlier session had selected — it notifies the folder's listeners and dereferences a response object a disconnected session no longer holds — and every folder this suite replaces has been selected by a synchronization run.

smtp4dev was evaluated first and rejected. It advertises no SASL mechanism at all, which is workable, but its INBOX reports a hard-coded UIDVALIDITY that can never change, so the specification's UIDVALIDITY scenario would have been unverifiable. Separately, a `UID SEARCH UID 1:*` against it exhausts the container's memory and kills the process; MailFathom never sends that shape, because it computes a concrete upper bound from `UIDNEXT`, but it is worth recording for anyone who reaches for that image again.

### Coverage

The suite collects its own coverage report, and nothing enforces it. The 85% gate above stays the repository's only coverage threshold, and this report never merges into it.

Its scope is the classes marked `[RequiresIntegrationCoverage]` and nothing else, which is the debt the suite exists to pay off, so the number reads as progress through that inventory. The two runs therefore need opposite collector configurations of the same attribute: `.config/testconfig.json` excludes marked code because a unit test cannot reach it, and `tests/IntegrationTests/testconfig.json` does not exclude it. `scripts/run-integration-tests.sh` then narrows the report to exactly the files carrying the marker, deriving that filter by searching for the marker rather than keeping a second list, so a newly marked class enters the report on its own.

That number is currently 93.9% of the lines in the 22 marked classes, up from the 26.9% the harness started at. What remains uncovered is the failure paths of a database that is behaving: an unreadable migration history, a catalogue the configured user may not read, a generated column that is absent. Reaching them means breaking the orchestrated database rather than exercising it, so they stay uncovered deliberately, and the percentage is read as progress rather than as a target to close.

The script prints the summary at the end of a run and writes the full output under `artifacts/integration-tests/`: TRX and raw Cobertura under `raw/`, and the merged Cobertura, HTML, and text summary under `report/`. The directory is removed at the start of each run, so a report never merges numbers an earlier run produced. A failing run still produces the report, because that is when it is worth reading.

A covered class keeps its marker. The marker records where a class's verification lives rather than whether it has been written, and a class no unit test can execute stays that way once its integration test exists; dropping the marker would remove the class from this report and add it to the enforced denominator at nearly zero, so writing an integration test would lower the aggregate and hide the coverage it just produced. Progress is the percentage here rising, not the inventory shrinking.

### Continuous integration

The `Integration tests` workflow runs the same script and is `workflow_dispatch` only, with an optional `ref` input. It is not a required status check and never runs on a pull request. Start it from the Actions tab when a change is one this suite can speak to; it uploads the TRX results and the coverage report as artifacts, and enforces no threshold on either.

## Pull request checks

Four workflows run for every pull request targeting `main`. Two of them always run; `Typo check` and `CodeQL` always run except on a draft. `CI` carries four jobs:

- `Detect changes` reads the pull request's changed files through the GitHub REST API with `dorny/paths-filter` and publishes three decisions: whether the change can affect the build, whether it can affect formatting, and whether it can affect the EF Core model. It checks nothing out, needs `contents: read` and `pull-requests: read`, and takes seconds. A manual dispatch has no pull request to compare against, so all three decisions are `true` there and an explicitly started run always does the work.
- `Build and unit test` runs when the change touches production code, tests, the solution or SDK selection, shared build and package configuration, coverage tooling, or the workflow file. It restores `MailFathom.slnx` in locked mode and repository-local tools, builds the solution in Release configuration, runs all unit-test projects through Microsoft Testing Platform with unique coverage prefixes, merges their Cobertura reports, and fails below 85% aggregate line coverage for the complete configured production scope. It uploads raw and merged coverage artifacts and TRX results even when the threshold fails.
- `dotnet format` runs when the change touches `src/**`, `tests/**`, `.editorconfig`, the workflow file, the shared build files, `Directory.Packages.props`, `MailFathom.slnx`, or `global.json`. It restores `MailFathom.slnx` in locked mode and verifies repository formatting without applying changes. The command runs its analyzer pass as well as its whitespace and style passes, so a centrally pinned analyzer version, a property set in a shared build file, a project added to the solution, or a different SDK can move its verdict without a single C# file changing; the trigger covers all four. `.config/**` and `NuGet.config` stay out, because they decide what the build rejects, restores, runs, and measures rather than how code is written.
- `Pending model changes` runs when the change touches `src/**`, `.config/dotnet-tools.json`, the workflow file, or `Directory.Packages.props`. It restores in locked mode, restores local tools, builds `src/Host` in Release configuration, and runs `dotnet ef migrations has-pending-model-changes`, which fails when the EF Core model has moved without a migration recording it. The command opens no connection — it compares the compiled model against the committed model snapshot — so no database is provisioned for this job. Production code is the only thing that can move the model, which is why tests and documentation are not triggers; `Directory.Packages.props` is one because raising the EF Core version can change what the generator emits for an unchanged model. `Persistence__TextSearchConfiguration` is deliberately left unset, so a migration generated under a non-default configuration fails here by design rather than by accident.
- `Required CI` is this workflow's one required status check, and the only conclusion the ruleset reads from it. It depends on the other four, runs under `if: always()` so a cancelled or skipped dependency cannot skip it in turn, and reads their results: `Detect changes` must have succeeded, and each of the other three must have either succeeded or been skipped. `failure` and `cancelled` fail it.

The second workflow, `Protected paths`, carries one job of the same name and answers a different question: not whether the change builds, but whether its author may make it at all. It reads the pull request's changed files through the GitHub REST API, checks nothing out, and fails when the pull request adds, modifies, deletes, or renames a protected path and its author is not the repository owner. A rename is read from both ends, so moving a file out of a protected directory counts as changing it. A pull request larger than the 3000 files that endpoint reports fails rather than passing on a list that may be missing the change it was asked about. Everything else it sees passes in seconds, including drafts, which run it for the same reason: the fact it reports is worth having in the first minute rather than at the moment a draft is marked ready.

The protected set is matched in three shapes, and the shape follows from what the entry is rather than from how it is spelled.

Five **directory prefixes** cover a directory and everything beneath it, because each decides how every other change is judged rather than being judged by it. `.github/` names who approves a change and which checks the ruleset waits for. `.config/` decides which API calls `BannedSymbols.txt` rejects, what `CodeCoverage.proj` demands, which local tools `dotnet-tools.json` restores, how the test runner is configured, and which spellings `typos.toml` accepts. `.agents/` holds the skills that define the task, review, verification, and completion contract, and the tracked `.claude/skills` symlink points into it, so repointing that one link redirects all of them. `docs/decisions/` holds the architectural decision records, the two templates that shape the next one, and the process that admits it: an ADR is what a later change to architecture, boundaries, persistence, configuration, or security-sensitive behavior is written to be consistent with, so rewriting one moves what the next change is judged against. The owner-approval rule `docs/AGENTS.md` and `docs/decisions/README.md` both state is what this prefix makes mechanical.

Five **file names** are matched at the repository root and after any `/`, so a copy at any depth is covered. `.editorconfig` decides which analyzer and style diagnostics `TreatWarningsAsErrors` turns into build failures and which header IDE0073 requires; `.gitattributes` decides how the diff a reviewer reads is produced, down to whether a path has reviewable content at all; `.worktreeinclude` decides which gitignored files, local secrets among them, are copied into every worktree an agent works in. `AGENTS.md` carries the architecture, conventions, verification gates, and workflow contract every agent-authored change is written and judged against, which is the same kind of instruction the skills under `.agents/` carry, and `CLAUDE.md` is the tracked entry point to it exactly as `.claude/` is to `.agents/` — so protecting the directories and not these files would protect neither. Depth is what makes them a name match rather than a root-file match: a nested copy overrides the root one for its own subtree, so `src/Infrastructure/Persistence/Migrations/.editorconfig` can relax for one directory what the root file enforces everywhere, and `tests/AGENTS.md` states the test rules that the root file does not. The anchoring is to a whole path segment, so `docs/my.editorconfig` is not caught and neither is `docs/CONTRIBUTING-AGENTS.md`.

Six **repository-root files** are matched whole, so a file of the same name elsewhere is not caught and a longer name beginning the same way is not either. `Directory.Build.props` carries the analyzer and warning policy every project inherits, the declared version every build is stamped with, and the SPDX and copyright metadata that ships inside the assemblies. `LICENSE` is the grant itself, detected by matching the file against the known Apache-2.0 text, so any edit turns a detected `Apache-2.0` into `NOASSERTION`; `NOTICE` is the attribution Apache-2.0 section 4(d) preserves. `NuGet.config` decides which feeds a package may come from, and it clears the inherited source list, so adding a source is a supply-chain and licensing decision. `global.json` pins the SDK every build and every gate resolves against. `CHANGELOG.md` is there for a different reason: it is written by the release pull request alone, so an edit arriving through ordinary work is out of band by construction.

The set is written out rather than expressed as a pattern: an entry joins it because a change to it moves what the repository enforces, what the project is published under, or what a release claims it shipped — not because of how it is spelled. A dotted directory is not protected merely for being dotted, and `docs/decisions/` is the entry showing the converse: prose joins the set when a later change is judged against it, while the rest of `docs/` stays outside it, because documentation that describes implemented behavior is corrected by the change that made it wrong. `Directory.Packages.props` is deliberately not in it either, because a version bump there is ordinary contribution-shaped work and the review it needs is `THIRD_PARTY_LICENSES.md`'s rather than this gate's.

Whichever way it decides, the job prints the protected paths the pull request touched, to the step log and to the job summary, and a refusal additionally annotates each one in the Files changed view. The pass needs that list as much as the refusal does: the owner is allowed to change these paths, not assumed to have meant to, and a `.editorconfig` that arrived with a rebase is only visible if something says so.

What it reads is the pull request's author, not the author of each commit, so a commit pushed by someone else onto a pull request the owner opened passes it. That case is the code-owner review's to catch, and `Require approval of the most recent reviewable push` is the ruleset setting that would tighten it; [Code owners](#code-owners) below records why it stays off until a second code owner exists.

The third workflow, `Typo check`, carries one job of the same name and spell-checks the words a pull request changes. It checks out the merge commit the pull request would produce, reads the changed files through the GitHub REST API, and hands that list to `crate-ci/typos`, pinned to a commit. Every finding becomes an annotation in the Files changed view, and the job fails when there is one. It reports no required status check and is not in the `main` ruleset: a misspelling is worth surfacing on the pull request, not worth blocking a merge over.

It is the one pull-request workflow with a draft exemption, and the only one. A draft is a change still being written, where a half-finished sentence is expected rather than reportable; marking the pull request ready starts the job through `ready_for_review`, and later commits keep starting it through `synchronize`. There is no path filter, because there is no such thing as a change this check does not concern — prose is what it reads, and prose is in a C# doc comment, a workflow's own comments, and a Helm value's description alike.

Two situations leave the job unable to pass a list it can trust, and both widen its scope rather than narrowing it. A pull request larger than the 3000 files the changed-files endpoint reports would arrive incomplete. A changed path containing whitespace or a glob character would arrive as different paths altogether, because the action receives its file list as one unquoted string: whitespace splits one path into two, and `docs/a[1].md` becomes `docs/a1.md` where that file exists. Either case checks the whole checkout instead, which is more than the pull request changed and never less; a pull request that only deletes files leaves nothing to read and skips the check entirely. Scanning everything is only a workable fallback because the tree is kept clean, which is the job the configuration below does.

`.config/typos.toml` is that configuration, and it separates two kinds of entry that a single list would blur. Accepted vocabulary is spellings MailFathom uses on purpose — `unparseable`, `requeueing`, `HashiCorp` — where correcting the dictionary's objection would be a repository-wide rename in service of no reader. Fixtures are the opposite: `Directroy`, `Enabeld`, `Authentcation`, `Passwrod`, and `MaxAttemps` are misspelled because that is their job. Every security-sensitive configuration section binds strictly, so a key nobody defined fails startup instead of binding silently, and the tests that prove it and the documentation that explains it have to name the misspelling the rule catches; correcting one deletes the example and, in a test, its assertion. The file also turns off the tool's default of skipping hidden files, because most of the prose that decides how this repository works sits behind a leading dot — `.github/workflows/`, the skills under `.agents/`, `.editorconfig`. Version-control metadata under `.git/` stays excluded regardless.

The workflow names that path rather than relying on it being found, because `typos` looks for a configuration file only under a fixed set of names and only alongside or above the file it is checking; the checking step states the rule it is working around. Two consequences follow for a reader rather than for the workflow. A `typos` run started by hand needs `--config .config/typos.toml`, or it applies none of the above. And the path puts the vocabulary under the `.config/` prefix `Protected paths` covers, so adding a word is a change only the owner can merge — `CONTRIBUTING.md` says so where a contributor meets it.

The fourth workflow, `CodeQL`, carries one job, `Analyze C#`, and is the only check here that reads what the code *does* with a value rather than how it is written. It restores in locked mode, initializes CodeQL in `manual` build mode, builds `MailFathom.slnx` in Release configuration inside the traced window, and runs GitHub's C# security query pack over the resulting database. It runs for a pull request, for a push to `main`, weekly on a schedule, and on manual dispatch, and it carries the same draft exemption `Typo check` does — for a stronger reason, since it is the one check that occupies a runner for minutes.

Three of its decisions are the ones a reader would otherwise have to reconstruct, and the workflow file argues each at length. It is an advanced setup rather than GitHub's default setup, so the check that reads this repository's source is a file a pull request can change and a reviewer can read, and so it can see the SDK pin and the locked restore. Its build mode is `manual` rather than `none`, so the analysis sees the graph the committed lock files fix instead of one CodeQL resolved for itself. And its last step compares the extracted source archive against what `src/` contains, because a bundle that cannot extract this SDK's output produces an empty database and a green check — an answer that looks like "no findings" and means "no analysis". The weekly run exists for a fourth reason that has nothing to do with this repository: a query pack updates upstream, so a commit that was clean when it merged can become a finding with nothing here having changed.

On a pull request from a fork the run gets the token GitHub grants that event, which is not the token a branch in this repository gets, and whether the alert upload succeeds there follows from GitHub's rules rather than from anything in this file. The check is required by nothing either way, so no merge waits on how it resolves, and the push to `main` after the merge analyses the same code under a token that certainly can upload.

### Why the typo check is a third workflow

The reasoning is the protected-paths one applied to a different verdict. `Required CI` says the change is sound and `Protected paths` says its author may make it; this says a word is misspelled, which is a third unlike answer and deserves its own status line rather than a share of one. Folding it into `CI` would also tie a job that needs no SDK, no restore, no cache, and no build to the concurrency group, the change detection, and the aggregate of the jobs that need all four.

It is also the workflow whose difference from the other two is worth keeping visible. `Protected paths` deliberately has no draft exemption, because the fact it reports is most useful in the first minute. This one has the exemption for the opposite reason: what it reports about a draft is mostly noise about sentences the author has not finished writing.

The remaining workflow, `Integration tests`, is manual dispatch only and never runs for a pull request. See [Integration tests](#integration-tests) above.

### Why one workflow with a conditional interior

GitHub reports a workflow that an `on.pull_request.paths` filter excluded as neither successful nor failed. A required check then never arrives, and a documentation-only pull request waits indefinitely on a run that was never created. The filtering therefore moved inside the workflow: the trigger has no path filter, `Detect changes` decides what the change can affect, and the expensive jobs skip themselves through `if` conditions. A skipped job reports `skipped`, which is an answer the aggregate can act on, unlike a workflow that was never instantiated.

`Required CI` is one job in one workflow for the same reason a required check is identified by its job name. Two workflows each publishing a job by that name would leave the ruleset ambiguous about which run it is waiting for, so the two former workflows, `Build and unit test` and `dotnet format`, became jobs of this one. Its name must stay stable and independent of the event, the changed files, the source branch, and any matrix dimension, because that name is the entire contract with the branch ruleset.

### Why the protected-paths guard is a second workflow

The rule above is about a required check that aggregates jobs which are allowed to skip, and it is the reason `Build and unit test` and `dotnet format` are jobs of `CI` rather than workflows of their own. `Protected paths` is not that shape. It has no path filter, no `if` condition, and no draft exemption, so it always runs and always reports a conclusion; there is nothing for an aggregate to make an answer out of, and adding one would only put a second name in front of the same verdict.

Keeping it out of `CI` keeps two unlike verdicts apart. `Required CI` says the change is sound. `Protected paths` says the author is allowed to make it. Folding the second into the first would leave one red check meaning either thing, and would tie a governance answer to the build pipeline's concurrency group, draft conditions, and change detection, each of which exists to let work be skipped.

The check is deliberately not a security boundary on its own. A `pull_request` run uses the workflow file as the pull request would leave it, so a pull request that rewrites this workflow is judged by the rewritten one. What closes the gap is the pair rather than either half: weakening the check means editing `.github/`, which `CODEOWNERS` sends to the repository owner for approval, and deleting or renaming the job leaves the required check permanently unreported, which the ruleset refuses to merge past. The two exits are covered by different mechanisms, which is why both are needed.

An outside contributor whose change genuinely needs one of these directories — a new local tool, a coverage setting, a workflow step — splits it out and asks the owner for that part. The guard is deliberately blunt about this: a change to what the repository enforces is worth a separate conversation, not a line inside a feature's diff.

### Draft pull requests

Draft pull requests skip the build, formatting, model-check, typo-check, and code-scanning jobs without allocating a runner; only the seconds-long `Detect changes`, `Required CI`, and `Protected paths` jobs do any work, and `Required CI` succeeds because a skipped job is a valid outcome. Skipping is not disappearing: each skipped job still reports a `skipped` conclusion and is listed among the pull request's checks, `Typo check` and `CodeQL` included. Its workflow puts no draft condition on the trigger, for the same reason `CI` puts no path filter on one — the run is instantiated and the decision is taken inside, where a job that declines to work still says so. A draft cannot be merged regardless. Marking a draft ready for review starts the applicable jobs immediately through the `ready_for_review` activity, and later commits continue to start them through `synchronize`. Converting a ready pull request back to draft cancels the superseded active run through the concurrency group and skips the replacement jobs. `CI` and `CodeQL` remain available through manual dispatch regardless of pull request state; `Typo check` and `Protected paths` carry no `workflow_dispatch` and run only for a pull request.

### Branch protection

The `main` branch ruleset requires a pull request with one approving review from a code owner, dismisses stale approvals when a new commit is pushed, requires review conversations to be resolved, permits squash as the only merge method, and requires the branch to be current with `main` and the `Required CI` and `Protected paths` status checks to pass. Creation, deletion, and force-pushes of `main` are refused. The repository admin role bypasses the rules when merging a pull request, for the reason [Code owners](#code-owners) below gives. The GitHub repository coverage rule must remain disabled because GitHub Code Quality coverage uploads are unavailable for this user-owned repository; the required repository-owned check enforces the same 85% minimum against the complete configured code scope.

The required checks are exactly `Required CI` and `Protected paths`, and both are added to the ruleset by hand under **Require status checks to pass**. Requiring any other job reintroduces exactly the problem this arrangement removes: a job that legitimately skipped never reports a conclusion the ruleset accepts. `Typo check` is the live example — it skips on every draft — and it would not be required even if it never skipped, because a misspelling is a thing to fix rather than a thing to block a merge on. Those two never skip, which is what qualifies them and what their workflows are written to preserve — neither name may become conditional on the event, the changed files, the source branch, or a matrix dimension, because the name is the entire contract with the ruleset.

`CodeQL` skips on a draft for the same mechanical reason, but its case against being required is a different one and worth stating separately, because the severity argument runs the other way: a taint path is worth more than a misspelling, and if severity decided this it would be required. What decides it is that the verdict moves without a commit. The query pack is maintained upstream and updates on its own schedule, so the same tree can be clean one week and a finding the next; requiring the check would let somebody else's release stop MailFathom's, and it would do so at the moment a release is being cut rather than while the code is being written. GitHub's separate **code scanning merge protection**, which blocks on alerts above a chosen severity, is off for the same reason. What answers a security question here is the code-owner review the ruleset already requires, reading an alert that is on the pull request either way. Revisit both once the pack's false-positive rate against this code has been observed rather than guessed at.

`Protected paths` is required for a reason `Required CI` does not share. Its value when it passes is small; its value is that it cannot be removed. A pull request that deletes or renames the job stops the check from ever reporting, and a required check that never reports blocks the merge, so the only way to disable the guard is a change the guard's other half already sends to the repository owner. Leaving it unrequired would turn that into a red check anyone could ignore.

The ruleset lives in repository settings rather than in this repository, so a maintainer changes it there and this section is the record of what it has to say.

### Code owners

`.github/CODEOWNERS` names `@Krzysztof318` as the owner of every path, and it is the half of the review requirement that lives in the repository. Requiring code-owner review without that file requires nobody: the ruleset asks for the approval of whoever owns the changed paths, and a repository with no `CODEOWNERS` has no owner for any path, so the condition is satisfied vacuously. The two settings are only a gate together.

Naming an owner is deliberately not the same as requiring one approval from anybody. An arbitrary approving review satisfies the count and says nothing about who gave it; the code-owner requirement is what makes the approval have to come from the maintainer. Both stay on, because the count alone would be a weaker rule wearing the same name.

The repository is on a personal account rather than in an organization, so the owner is a user. A GitHub Team is not available here and is not a substitute to reach for.

The file's ordering carries a rule of its own. GitHub applies the last matching pattern, so the repository-wide entry is first and a path-specific entry added below it replaces ownership for that path instead of adding to it. A directory that must still require the maintainer names them among its owners rather than relying on the global line.

The paths `Protected paths` guards are deliberately not restated here. The repository-wide rule already makes the owner their code owner, so an entry naming them would change nothing, and it would not survive a path rule added below it either: the last matching pattern wins over a restatement exactly as it wins over the global line. A path earns an entry when a rule gives it other owners as well, and that entry is where the owner has to be named alongside them.

That leaves the two halves of the protection doing different work rather than the same work twice. This file decides whose approval merges a change, and it is read from the base branch, so a pull request cannot alter the owners its own merge requires. The `Protected paths` check decides whether the change belongs in that pull request at all, and it answers within seconds of a push, before a reviewer is involved.

GitHub does not let the author of a pull request approve it. Every pull request the maintainer opens is therefore unapprovable by the only code owner, which is why the ruleset lists the repository admin role as a bypass actor in `pull_request` mode: the maintainer merges their own pull request through the bypass, and a pull request from anyone else has no bypass available and waits for the code-owner review. Removing that bypass without adding a second code owner would make the repository unmergeable rather than more careful.

`Require approval of the most recent reviewable push` stays off for the same reason. It requires that the approval come from someone other than whoever pushed last, so on a single-maintainer repository it removes the one path a self-authored pull request has to a satisfied rule while adding nothing to a pull request that already needs an outside owner's review. Turn it on when a second code owner exists.

### Shared workflow behavior

Both expensive jobs restore from a cached `~/.nuget/packages` keyed on `Directory.Packages.props`, `global.json`, `NuGet.config`, `.config/dotnet-tools.json`, and every `packages.lock.json`. Those files decide the versions, the permitted sources, and the resolved transitive closure, which together are the whole of what restore downloads, so a changed pin or a changed source policy misses the cache rather than resolving against a stale package set.

The workflow uses the SDK pinned in `global.json`, cancels superseded runs for the same pull request, requests read-only repository permissions, and avoids credentials or service-specific secrets.

## GitHub Actions policy

Half of what governs Actions here is committed and half is a repository setting, and neither half is
worth anything alone: restricting the settings while the YAML references a mutable tag leaves the
reference mutable, and hardening the YAML while the settings admit any action lets the next workflow
introduce one nobody reviewed. This section records both, so the half that no diff shows is written
down somewhere a change to the other half will read.

**What the contract suite asserts**, on every pull request, through
`scripts/test-agent-workflow.sh`:

| Contract | What it refuses |
|---|---|
| `every_external_action_names_an_approved_owner` | An action from an owner outside the reviewed set: `actions`, `github`, `Krzysztof318`, `dorny`, `anthropics`, `docker`, `crate-ci`, `aquasecurity` |
| `every_workflow_job_declares_its_permissions` | A job that inherits the repository default instead of declaring a `permissions:` block, at the workflow level or its own |
| `every_write_scope_is_one_the_policy_records` | A write scope appearing anywhere the list in that contract does not already name |
| `every_checkout_refuses_to_persist_credentials` | An `actions/checkout` step that leaves the workflow token in `.git/config` for the steps after it |
| `only_the_reviewer_workflow_uses_pull_request_target` | A second `pull_request_target` trigger beside the one `fathom-review.yml` holds |

Every write scope in the repository but one belongs to a publishing job: `packages: write` with
`id-token: write` and `attestations: write` in `nightly.yml`, `publish-container-image.yml`,
`publish-helm-chart.yml`, and `release.yml` — which carries each of the three twice, because it calls
both publishing workflows and a caller states the permissions it hands down — plus `packages: write`
on the nightly prune job and `contents: write` on the job that writes the release announcement.

The exception is `security-events: write` in `codeql.yml`, and it is the only write scope held by a
job that runs for a pull request. It is what the analysis is for: the scope writes code-scanning
alerts and nothing else — not repository contents, not a package, not a release — and an analysis
that cannot record an alert produces a log line instead of a check. The contract above is what keeps
the list honest, so adding a second such scope is an edit somebody argues rather than a line nobody
notices.

**What lives in the repository settings**, which no check here can read:

| Setting | Value | Why |
|---|---|---|
| Default workflow token | `read` | A job that needs more says so in its own `permissions:` block, which is reviewable; a permissive default is not |
| Actions may create or approve pull requests | disabled | A workflow approving its own change would satisfy the ruleset's review requirement without a person |
| Allowed actions | `all` today; #160 owns narrowing it | The allowlist is the settings-side twin of the owner contract above. The contract suite already refuses an owner outside the reviewed set on every pull request, so what the setting adds is coverage of a workflow that reaches the repository some other way |
| Require actions pinned to a full-length commit SHA | off, and deliberately | A repository-wide setting would impose one answer where this repository already gives two. A job holding registry write, a signing identity, or the token that publishes a release pins its steps to a commit, because what a moving tag resolves to is otherwise decided by somebody else and would reach a published artifact; every other job follows the major tag, and `crate-ci/typos` is pinned for a third reason its own workflow states. `security-events: write` in `codeql.yml` is a write scope and not a publishing one, so it does not pull the pinning rule with it. `THIRD_PARTY_LICENSES.md` records which step is pinned which way and why. The setting stays off because turning it on would collapse that distinction into a sweep, not because the pins are unfinished |
| Artifact and log retention | 30 days | The REST API exposes no retention field, so the settings page is both where it is set and the only evidence it was |
| Cache retention and size | 7 days, 10 GB | Unchanged unless measured eviction pressure argues otherwise |
| Fork pull request approval | `Require approval for first-time contributors` | The workflows a fork's push can start hold a read-only token and no repository secret, so a wider setting protects nothing this one does not, and a narrower one turns every first contribution into a maintainer's click. The REST API exposes no field for it, so the settings page is the only place it can be read |
| GHCR package access | inherited from the repository | A package's visibility is its own setting rather than one it takes from the repository, so it is configured to follow the repository's access instead of being set again beside it. A private package would break the anonymous `docker pull` every installation path documents |

The retention rows, the fork approval, and the package access are the ones to re-read after any
settings change, because no API exposes them and nothing else will notice them moving.

**A fork's pull request** runs `CI`, `Protected paths`, `Typo check`, and `CodeQL` on the
`pull_request` event with a read-only token and no repository secret, which is what makes running a
contribution's code safe at all. `Fathom review` is the exception and stays one: it holds an App
private key, so a fork's own pushes never start it and only a maintainer's `fathom-review` label or
comment does.
[Why `pull_request_target` is a granted exception](agent-workflow.md#why-pull_request_target-is-a-granted-exception)
records the reasoning, and the contract above is the automated half of it.

### Keeping the pinned actions current

`.github/dependabot.yml` is the only thing in this repository that updates a dependency it has
pinned, and it covers the `github-actions` ecosystem alone. It proposes minor and patch updates as
one grouped pull request each Monday, a major on its own, at most three open at a time, and nothing
newer than a week old; the file states why each of those numbers is what it is and why the `nuget`
ecosystem stays off, with the upstream issue that decides it.

It ignores one thing, and that entry is about a spelling rather than about a dependency.
`github/codeql-action` is referenced only as `@v4`, so the moving tag *is* how it updates: a run
already executes whatever the newest `v4` release is, and a proposal to write that release's number
into the reference converts the tag the pinning row above argues for into a third spelling. The file
carries the whole argument, including why the entry names one dependency instead of the ecosystem and
why a major stays proposed.

Three things about those pull requests belong here rather than there. They edit `.github/workflows/**`
by definition, so `Protected paths` recognises `dependabot[bot]` for that directory and refuses it
everywhere else — that workflow carries the argument for why the exception removes a signal rather
than an approval. `Fathom review` declines them, by author, because what decides a bump is the
upstream release notes and the register rather than the diff; the `fathom-review` label still
reaches one, and
[Dependency update pull requests](agent-workflow.md#dependency-update-pull-requests) carries the
argument. And they are exempt from nothing else: the `main` ruleset asks the same code-owner review
of them as of any other pull request, `Required CI` still has to pass, nothing auto-merges, and the
updater holds no write-capable token.

## Repository security features

Every setting below is a repository setting rather than a file, so no check here can read one and no
diff will show it moving. This section is the record; the commands beside it are how the state is
read back, which is the only thing that confirms a setting rather than confirming that a call
succeeded.

```bash
gh api repos/Krzysztof318/MailFathom --jq '.security_and_analysis'
gh api repos/Krzysztof318/MailFathom/private-vulnerability-reporting
gh api repos/Krzysztof318/MailFathom/vulnerability-alerts -i | head -1   # 204 enabled, 404 disabled
gh api repos/Krzysztof318/MailFathom/automated-security-fixes
```

| Feature | State | Why |
|---|---|---|
| Private vulnerability reporting | enabled | `SECURITY.md` names the *Report a vulnerability* button as one of its two channels, and a policy naming a button that is not there sends the first researcher who reads it to the fallback. A report arriving this way carries a draft advisory, a private fork for the fix, and a CVE request path, instead of an email thread to convert by hand |
| Secret scanning alerts | enabled | An alert says a credential is already public and must be rotated. It is the half that covers what push protection did not stop, including anything a bypass let through |
| Push protection | enabled | The other half, and the more valuable one: it refuses the object before it reaches GitHub, which is the only point in a credential's life where the remedy is free. `CONTRIBUTING.md` states what a contributor sees when it fires and what to do about it |
| Secret scanning validity checks | unavailable | Part of paid GitHub Secret Protection. Recorded so a later reader reads the configuration as complete for what a free public repository gets rather than as half-finished |
| Non-provider (generic) patterns | unavailable | The same entitlement. Provider patterns — the credential formats GitHub's partners publish detectors for — are what a free public repository does get, and they are what the two rows above run on |
| Push-protection bypass | nobody but the repository owner | Delegated bypass, which routes a bypass request to a reviewer, is a paid feature. Without it the question reduces to who holds write access, and that is the owner alone. A contributor who believes a block is a false positive says so in the pull request; there is deliberately nothing for them to click |
| Dependabot alerts | enabled | The advisory database's opinion about the pinned closure, which is worth having whatever can be done about it automatically |
| Dependabot malware alerts | enabled | The same shape of thing for a different failure: a package that is not vulnerable but hostile. It reports and opens nothing, so the lock-file argument below does not reach it |
| Dependabot version updates | enabled | The switch `.github/dependabot.yml` needs to do anything at all. The file decides what is proposed and when; this decides whether it runs |
| Dependabot security updates | off, and deliberately | This is the half that opens a pull request, and for NuGet it would open one that cannot go green: the fix edits a central pin without regenerating the lock files, and every gated restore runs in locked mode. The alert is what the owner acts on; the bump is made by hand |
| Code scanning | advanced setup, `.github/workflows/codeql.yml` | Described under [Pull request checks](#pull-request-checks) above |
| Code scanning merge protection | off | The reasoning is [Branch protection](#branch-protection)'s: a query pack updates upstream, so a required verdict here can change with no commit on either side |
| Copilot Autofix | off, and deliberately | It drafts a patch for a CodeQL alert by sending the code around it to a hosted model. Every AI service this repository uses carries a row in `THIRD_PARTY_LICENSES.md` naming exactly what a run submits and under whose terms, and a suggested patch on a repository where one maintainer reads every finding anyway does not earn that row. Turning it on means writing the row in the same change |
| Code scanning check-run failure threshold | `High or higher` for security alerts, `Only errors` for standard ones | This decides when the `CodeQL` check reports failure, not when a merge is refused — the second would take a branch ruleset, which the row above declines. A high-severity finding is worth a red check somebody looks at |
| Automatic dependency submission | off | It submits dependencies observed during a build, for ecosystems that resolve at build time. The committed lock files already give the dependency graph the exact closure, so there is nothing here for it to discover |
