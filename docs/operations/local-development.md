# Local development

Use the .NET SDK pinned in `global.json`. Test execution is configured for Microsoft Testing Platform through the repository-level `global.json` test runner setting.

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

The full script fetches `origin main` and refuses to continue when the branch
does not contain that base, so it needs access to the remote and cannot run
offline. Rebase onto the fetched base when it reports the branch is behind.
The fast script queries only local Git state and remains available offline.

Two further scripts cover the deployment assets, and neither is part of either gate above: they need a Docker daemon,
and a change that touches no deployment file has nothing for them to say.

```bash
bash scripts/verify-deployment-assets.sh   # seconds; reads the files
bash scripts/smoke-deployment.sh compose   # minutes; starts the real deployment
bash scripts/smoke-deployment.sh kubernetes
```

The first answers what can be decided by reading everything under `deploy/`: that base images are pinned, that the image
drops to an unprivileged account and carries no schema tool, that the Compose files and the chart render, that rendering
is deterministic, and that the chart's schema still rejects the values that must never install. It uses `helm` from the
PATH when it is there and a pinned container image otherwise, so it works without installing anything.

The second starts a deployment and asserts what only a running one can answer. Its `kubernetes` mode additionally needs
`kind`, `kubectl`, and `helm` installed. Neither script runs on a pull request; the `Deployment assets` workflow that
runs both, plus the two-architecture image build, is manual dispatch only, like the integration suite.
[The container image](container-image.md) describes what they verify.

Both scripts stop immediately when `HEAD` resolves to `main` or `master`,
because verification on the integration branch reports on code that no change
is about to touch. Check out the branch that carries the change first. A
detached `HEAD` and any other branch name are accepted, in the primary checkout
as well as in a linked worktree.

Run the web host directly:

```bash
dotnet run --project src/Host/Host.csproj
```

Run the Aspire orchestration host:

```bash
dotnet run --project src/AppHost/AppHost.csproj
```

The AppHost PostgreSQL resource uses the `pgvector/pgvector:0.8.2-pg17` image so local development starts with a PostgreSQL server that can support the `vector` extension required by the RAG and embedding slices. It keeps its data in a named Docker volume, so synchronized mail survives a restart instead of costing a full IMAP synchronization every time the orchestration stops.

That volume is why `src/AppHost/AppHost.csproj` declares a `UserSecretsId` and `src/AppHost/Properties/launchSettings.json` sets `DOTNET_ENVIRONMENT=Development`. Aspire generates the PostgreSQL password and keeps it stable by writing it to user secrets, which are only loaded in the Development environment. Without both, every run generates a new password while the volume keeps the one it was initialized with, and the container never becomes healthy. If it ever does report `password authentication failed`, the volume and the current password have diverged; remove the volume and start again:

```bash
docker volume ls --filter name=-postgres-data
aspire stop --apphost src/AppHost/AppHost.csproj --non-interactive
docker rm -f $(docker ps -aq --filter volume=<volume>)
docker volume rm <volume>
```

Aspire names that volume after the AppHost project's path, so every clone and every worktree owns a different one and the name has to be read rather than assumed. List them first and take the one belonging to the checkout being repaired; removing another one destroys a database the repair was not about.

## Development secrets

Secrets are never written into configuration as values, in development either. `appsettings.Development.json` sets the interpretation mode to `ReferenceOrInline`, which keeps `plaintext:` references convenient without weakening the shipped `ReferenceOnly` default:

```json
{
  "Secrets": { "Interpretation": "ReferenceOrInline" }
}
```

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

The repository provisions no development environment, so install the SDK and any command-line tools on the developer machine. Repository-local tools declared in `.config/dotnet-tools.json` come from `dotnet tool restore` and are limited to what the coverage gate needs: `reportgenerator` merges the per-assembly Cobertura reports the coverage run produces.

Three tools are installed globally when their workflows are needed:

```bash
dotnet tool install --global dotnet-ef --version 10.0.10
dotnet tool install --global Aspire.Cli --version 13.4.6
dotnet tool install --global csharp-ls --version 0.26.0
```

`dotnet ef` runs EF Core migrations and design-time commands. `aspire` is only required for Aspire CLI workflows against the AppHost. `csharp-ls` is the C# language server that editors and agent tooling launch to resolve symbols before editing, instead of discovering a misspelled type at build time.

`csharp-ls` is installed globally rather than pinned in `.config/dotnet-tools.json` because a manifest-local tool is only reachable as `dotnet tool run csharp-ls`; it never lands on `PATH`, so a client that launches the bare `csharp-ls` executable still fails with `ENOENT`. A global install puts it in `~/.dotnet/tools`, which is on `PATH`, and keeps the language server out of the `dotnet tool restore` that continuous integration runs for the coverage gate. All three versions are recorded in `THIRD_PARTY_LICENSES.md`; keep the register aligned when you move to a newer one.

### EF Core design-time commands

**Do not invoke `dotnet ef` directly.** Design-time and migration commands run through the AppHost's `mailfathom-migrations` resource, so they use the connection string the AppHost issues rather than a local environment that can differ from every real one.

Aspire 13 has no `aspire exec` command; earlier versions offered one, and it is gone. Its replacement is the `Aspire.Hosting.EntityFrameworkCore` package, which declares a migration resource in the app model. `src/AppHost/Program.cs` adds it against the host project, points it at `src/Infrastructure` for the migrations, and calls `RunDatabaseUpdateOnStart`, so a local run applies pending migrations before the host starts and the host waits for that to finish.

Commands are executed against the resource:

```bash
aspire resource mailfathom-migrations ef-database-status --apphost src/AppHost/AppHost.csproj --non-interactive
aspire resource mailfathom-migrations ef-database-update --apphost src/AppHost/AppHost.csproj --non-interactive
aspire resource mailfathom-migrations ef-database-reset  --apphost src/AppHost/AppHost.csproj --non-interactive
aspire resource mailfathom-migrations ef-migrations-add  --apphost src/AppHost/AppHost.csproj --non-interactive -- --name Initial
```

The same commands are available from the dashboard. `dotnet-ef` itself is fetched by the tool resource, so the global install is only needed by an editor that runs design-time commands of its own.

`Host` is the startup project, because it is the resource the orchestration issues the connection string to, and it therefore carries a design-time-only reference to `Microsoft.EntityFrameworkCore.Design`. `Infrastructure` owns the context, the design-time factory, and the migrations under `src/Infrastructure/Persistence/Migrations/`.

`MailFathomDbContextDesignTimeFactory` gives EF Core a context without starting the host, which matters because the host composes its connection string during startup and design-time tooling never runs that. It reads `ConnectionStrings__mailfathom` when the orchestration supplies it, then `MAILFATHOM_DESIGN_TIME_CONNECTION_STRING` for a command run outside it, and falls back to `Host=localhost;Database=mailfathom;Username=mailfathom`. The orchestrated value wins so a stale override left in a shell cannot point a migration at a different database than the one being migrated.

While MailFathom is pre-release the repository keeps exactly one migration, `Initial`, and a model change regenerates it rather than adding a second one. `scripts/regenerate-migration.sh` does that in one command — it reuses a running orchestration, waits for the startup migration run to settle, regenerates the migration, and resets the database — and `scripts/dump-local-schema.sh` produces the schema dump the review then reads. The `add-migration` skill is the surrounding workflow, including that review, which no script performs. Making the workflow additive, and deciding how a released instance applies migrations, is tracked for the first release.

The baseline migration also installs the `vector` extension. The `pgvector/pgvector` image ships it but does not install it, so without this the first vector column would fail on a type PostgreSQL does not know.

#### Apply policy

The host never applies migrations, in any environment. It reads the migration history at startup and fails fast when the database has not applied every migration the running build defines, so an instance either serves traffic against a known schema or does not serve traffic at all. A pending migration reports error code `32001` and an unreadable migration history `32002`.

It then checks one thing the migration identifiers cannot express. `Persistence:TextSearchConfiguration` is compiled into the search vector's stored generated column when the table is created, and the identifier of the migration that created it is the same whichever configuration produced it. A host configured for `english` against an index built with `simple` would stem its queries one way and read lexemes built the other, returning fewer results rather than an error, so the host compares its configured value against the expression PostgreSQL actually holds and fails with `32003` when they differ.

Generating a migration for a non-default configuration is therefore a deliberate act: export `Persistence__TextSearchConfiguration` before running the `add-migration` workflow, and rebuild the search documents afterwards. The design-time factory reads that variable, which is the double-underscore encoding of the setting a deployment already has.

Applying is one mechanism per environment: `mailfathom-migrations` locally, and an explicit deployment step elsewhere. A host that mutates schema while starting could race a second instance, could apply a destructive change nobody reviewed at deploy time, and would leave the operator no point at which to take a backup.

The GitHub CLI (`gh`) is installed separately through the operating system package manager and is required for the issue and pull-request workflow in root `AGENTS.md`. It needs the `project` scope on top of its default scopes so it can read and update the roadmap board.

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

Thirteen of the fifteen projects carry one. `AppHost` and `IntegrationTests` do not, because `Aspire.AppHost.Sdk` adds `Aspire.Dashboard.Sdk.<rid>` and `Aspire.Hosting.Orchestration.<rid>` as references chosen from `NETCoreSdkRuntimeIdentifier`. That part of the graph describes the machine running restore rather than this repository, so a lock file written on Linux names packages a Windows, macOS, or Linux ARM64 developer never asks for, and locked mode there fails with `NU1004: A new package reference was found Aspire.Dashboard.Sdk.win-x64` before a build can start. `IntegrationTests` follows `AppHost` because it references the project and inherits those packages transitively, and a lock file cannot exclude a subtree. Both ship nowhere, and their versions stay pinned centrally like every other project's.

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

A third exclusion is applied by path rather than by attribute: `.config/CodeCoverage.proj` filters `**/Persistence/Migrations/*.cs` out of the merged report. EF Core generates those files and the `add-migration` workflow regenerates them, so they carry no attribute the generator would preserve, and no unit test may execute them — a migration is proven by applying it to a real PostgreSQL server and reviewing the resulting schema. Leaving them in put roughly a thousand uncoverable lines in the denominator and moved the aggregate by more than twenty points, which would have masked a real regression anywhere else.

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

- the PostgreSQL container is named `mailfathom-integrationtests-postgres` and its data volume `mailfathom-integrationtests-postgres-data`, rather than taking Aspire's random postfix and the path-derived volume name a developer's orchestration uses;
- a `mailserver` container named `mailfathom-integrationtests-mailserver` is added, which a developer's orchestration never gets — it exists so the suite has a real IMAP server to synchronize against, and starting one beside a developer's own accounts would advertise a mailbox nothing points at;
- the `mailfathom-host` project resource is added to the model but never started, because the suite exercises classes against real infrastructure and a running MailFathom would synchronize mail underneath the data a test is asserting on.

Both names come from `OrchestrationContract` in `src/AppHost`, and nothing else in the repository uses that prefix. The script removes every container and volume carrying it before the run as well as after it: before, because the baseline migration is only proven to apply cleanly when it applies to an empty database; after, because nothing the suite creates is meant to outlive it. A run killed with `SIGKILL` skips the trap, and the next run's opening removal is what cleans up after it. To check by hand:

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

Two workflows run for every pull request targeting `main`, and both of them always run. `CI` carries four jobs:

- `Detect changes` reads the pull request's changed files through the GitHub REST API with `dorny/paths-filter` and publishes two decisions: whether the change can affect the build, and whether it can affect formatting. It checks nothing out, needs `contents: read` and `pull-requests: read`, and takes seconds. A manual dispatch has no pull request to compare against, so both decisions are `true` there and an explicitly started run always does the work.
- `Build and unit test` runs when the change touches production code, tests, the solution or SDK selection, shared build and package configuration, coverage tooling, or the workflow file. It restores `MailFathom.slnx` in locked mode and repository-local tools, builds the solution in Release configuration, runs all unit-test projects through Microsoft Testing Platform with unique coverage prefixes, merges their Cobertura reports, and fails below 85% aggregate line coverage for the complete configured production scope. It uploads raw and merged coverage artifacts and TRX results even when the threshold fails.
- `dotnet format` runs when the change touches `src/**`, `tests/**`, `.editorconfig`, the workflow file, the shared build files, `Directory.Packages.props`, `MailFathom.slnx`, or `global.json`. It restores `MailFathom.slnx` in locked mode and verifies repository formatting without applying changes. The command runs its analyzer pass as well as its whitespace and style passes, so a centrally pinned analyzer version, a property set in a shared build file, a project added to the solution, or a different SDK can move its verdict without a single C# file changing; the trigger covers all four. `.config/**` and `NuGet.config` stay out, because they decide what the build rejects, restores, runs, and measures rather than how code is written.
- `Required CI` is this workflow's one required status check, and the only conclusion the ruleset reads from it. It depends on the other three, runs under `if: always()` so a cancelled or skipped dependency cannot skip it in turn, and reads their results: `Detect changes` must have succeeded, and each of the other two must have either succeeded or been skipped. `failure` and `cancelled` fail it.

The second workflow, `Protected paths`, carries one job of the same name and answers a different question: not whether the change builds, but whether its author may make it at all. It reads the pull request's changed files through the GitHub REST API, checks nothing out, and fails when the pull request adds, modifies, deletes, or renames anything under `.github/`, `.config/`, `.agents/`, or `.claude/` and its author is not the repository owner. A rename is read from both ends, so moving a file out of one of those directories counts as changing it. A pull request larger than the 3000 files that endpoint reports fails rather than passing on a list that may be missing the change it was asked about. Everything else it sees passes in seconds, including drafts, which run it for the same reason: the fact it reports is worth having in the first minute rather than at the moment a draft is marked ready.

The four directories are one set because each decides how every other change is judged rather than being judged by it. `.github/` names who approves a change and which checks the ruleset waits for. `.config/` decides which API calls `BannedSymbols.txt` rejects, what `CodeCoverage.proj` demands, which local tools `dotnet-tools.json` restores, and how the test runner is configured. `.agents/` holds the skills that define the task, review, verification, and completion contract, and the tracked `.claude/skills` symlink points into it, so repointing that one link redirects all of them. The list is written out rather than expressed as a pattern over dotted directories: an entry joins it because a change to it moves what the repository enforces, not because of how it is spelled.

What it reads is the pull request's author, not the author of each commit, so a commit pushed by someone else onto a pull request the owner opened passes it. That case is the code-owner review's to catch, and `Require approval of the most recent reviewable push` is the ruleset setting that would tighten it; [Code owners](#code-owners) below records why it stays off until a second code owner exists.

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

Draft pull requests skip the build and formatting jobs without allocating a runner; only the seconds-long `Detect changes`, `Required CI`, and `Protected paths` jobs run, and `Required CI` succeeds because a skipped job is a valid outcome. A draft cannot be merged regardless. Marking a draft ready for review starts the applicable jobs immediately through the `ready_for_review` activity, and later commits continue to start them through `synchronize`. Converting a ready pull request back to draft cancels the superseded active run through the concurrency group and skips the replacement jobs. The workflow remains available through manual dispatch regardless of pull request state.

### Branch protection

The `main` branch ruleset requires a pull request with one approving review from a code owner, dismisses stale approvals when a new commit is pushed, requires review conversations to be resolved, permits squash as the only merge method, and requires the branch to be current with `main` and the `Required CI` and `Protected paths` status checks to pass. Creation, deletion, and force-pushes of `main` are refused. The repository admin role bypasses the rules when merging a pull request, for the reason [Code owners](#code-owners) below gives. The GitHub repository coverage rule must remain disabled because GitHub Code Quality coverage uploads are unavailable for this user-owned repository; the required repository-owned check enforces the same 85% minimum against the complete configured code scope.

The required checks are exactly `Required CI` and `Protected paths`, and both are added to the ruleset by hand under **Require status checks to pass**. Requiring any other job reintroduces exactly the problem this arrangement removes: a job that legitimately skipped never reports a conclusion the ruleset accepts. Those two never skip, which is what qualifies them and what their workflows are written to preserve — neither name may become conditional on the event, the changed files, the source branch, or a matrix dimension, because the name is the entire contract with the ruleset.

`Protected paths` is required for a reason `Required CI` does not share. Its value when it passes is small; its value is that it cannot be removed. A pull request that deletes or renames the job stops the check from ever reporting, and a required check that never reports blocks the merge, so the only way to disable the guard is a change the guard's other half already sends to the repository owner. Leaving it unrequired would turn that into a red check anyone could ignore.

The ruleset lives in repository settings rather than in this repository, so a maintainer changes it there and this section is the record of what it has to say.

### Code owners

`.github/CODEOWNERS` names `@Krzysztof318` as the owner of every path, and it is the half of the review requirement that lives in the repository. Requiring code-owner review without that file requires nobody: the ruleset asks for the approval of whoever owns the changed paths, and a repository with no `CODEOWNERS` has no owner for any path, so the condition is satisfied vacuously. The two settings are only a gate together.

Naming an owner is deliberately not the same as requiring one approval from anybody. An arbitrary approving review satisfies the count and says nothing about who gave it; the code-owner requirement is what makes the approval have to come from the maintainer. Both stay on, because the count alone would be a weaker rule wearing the same name.

The repository is on a personal account rather than in an organization, so the owner is a user. A GitHub Team is not available here and is not a substitute to reach for.

The file's ordering carries a rule of its own. GitHub applies the last matching pattern, so the repository-wide entry is first and a path-specific entry added below it replaces ownership for that path instead of adding to it. A directory that must still require the maintainer names them among its owners rather than relying on the global line.

The directories `Protected paths` guards are deliberately not restated here. The repository-wide rule already makes the owner their code owner, so an entry naming them would change nothing, and it would not survive a path rule added below it either: the last matching pattern wins over a restatement exactly as it wins over the global line. A directory earns an entry when a rule gives it other owners as well, and that entry is where the owner has to be named alongside them.

That leaves the two halves of the protection doing different work rather than the same work twice. This file decides whose approval merges a change, and it is read from the base branch, so a pull request cannot alter the owners its own merge requires. The `Protected paths` check decides whether the change belongs in that pull request at all, and it answers within seconds of a push, before a reviewer is involved.

GitHub does not let the author of a pull request approve it. Every pull request the maintainer opens is therefore unapprovable by the only code owner, which is why the ruleset lists the repository admin role as a bypass actor in `pull_request` mode: the maintainer merges their own pull request through the bypass, and a pull request from anyone else has no bypass available and waits for the code-owner review. Removing that bypass without adding a second code owner would make the repository unmergeable rather than more careful.

`Require approval of the most recent reviewable push` stays off for the same reason. It requires that the approval come from someone other than whoever pushed last, so on a single-maintainer repository it removes the one path a self-authored pull request has to a satisfied rule while adding nothing to a pull request that already needs an outside owner's review. Turn it on when a second code owner exists.

### Shared workflow behavior

Both expensive jobs restore from a cached `~/.nuget/packages` keyed on `Directory.Packages.props`, `global.json`, `NuGet.config`, `.config/dotnet-tools.json`, and every `packages.lock.json`. Those files decide the versions, the permitted sources, and the resolved transitive closure, which together are the whole of what restore downloads, so a changed pin or a changed source policy misses the cache rather than resolving against a stale package set.

The workflow uses the SDK pinned in `global.json`, cancels superseded runs for the same pull request, requests read-only repository permissions, and avoids credentials or service-specific secrets.
