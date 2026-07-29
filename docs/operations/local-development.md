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
aspire stop --apphost src/AppHost/AppHost.csproj --non-interactive
docker rm -f $(docker ps -aq --filter volume=mailmcp.apphost-9beaf2538a-postgres-data)
docker volume rm mailmcp.apphost-9beaf2538a-postgres-data
```

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

`csharp-ls` is installed globally rather than pinned in `.config/dotnet-tools.json` because a manifest-local tool is only reachable as `dotnet tool run csharp-ls`; it never lands on `PATH`, so a client that launches the bare `csharp-ls` executable still fails with `ENOENT`. A global install puts it in `~/.dotnet/tools`, which is on `PATH`, and keeps the language server out of the `dotnet tool restore` that continuous integration runs for the coverage gate. All three versions are recorded in `LICENSES.md`; keep the register aligned when you move to a newer one.

### EF Core design-time commands

**Do not invoke `dotnet ef` directly.** Design-time and migration commands run through the AppHost's `mailmcp-migrations` resource, so they use the connection string the AppHost issues rather than a local environment that can differ from every real one.

Aspire 13 has no `aspire exec` command; earlier versions offered one, and it is gone. Its replacement is the `Aspire.Hosting.EntityFrameworkCore` package, which declares a migration resource in the app model. `src/AppHost/Program.cs` adds it against the host project, points it at `src/Infrastructure` for the migrations, and calls `RunDatabaseUpdateOnStart`, so a local run applies pending migrations before the host starts and the host waits for that to finish.

Commands are executed against the resource:

```bash
aspire resource mailmcp-migrations ef-database-status --apphost src/AppHost/AppHost.csproj --non-interactive
aspire resource mailmcp-migrations ef-database-update --apphost src/AppHost/AppHost.csproj --non-interactive
aspire resource mailmcp-migrations ef-database-reset  --apphost src/AppHost/AppHost.csproj --non-interactive
aspire resource mailmcp-migrations ef-migrations-add  --apphost src/AppHost/AppHost.csproj --non-interactive -- --name Initial
```

The same commands are available from the dashboard. `dotnet-ef` itself is fetched by the tool resource, so the global install is only needed by an editor that runs design-time commands of its own.

`Host` is the startup project, because it is the resource the orchestration issues the connection string to, and it therefore carries a design-time-only reference to `Microsoft.EntityFrameworkCore.Design`. `Infrastructure` owns the context, the design-time factory, and the migrations under `src/Infrastructure/Persistence/Migrations/`.

`MailMcpDbContextDesignTimeFactory` gives EF Core a context without starting the host, which matters because the host composes its connection string during startup and design-time tooling never runs that. It reads `ConnectionStrings__mailmcp` when the orchestration supplies it, then `MAILMCP_DESIGN_TIME_CONNECTION_STRING` for a command run outside it, and falls back to `Host=localhost;Database=mailmcp;Username=mailmcp`. The orchestrated value wins so a stale override left in a shell cannot point a migration at a different database than the one being migrated.

While MailMcp is pre-release the repository keeps exactly one migration, `Initial`, and a model change regenerates it rather than adding a second one. The `add-migration` skill is that workflow, including the database reset it needs and the SQL review it requires; `scripts/dump-local-schema.sh` produces the schema dump that review reads. Making the workflow additive, and deciding how a released instance applies migrations, is tracked for the first release.

#### Apply policy

The host never applies migrations, in any environment. It reads the migration history at startup and fails fast when the database has not applied every migration the running build defines, so an instance either serves traffic against a known schema or does not serve traffic at all. A pending migration reports error code `32001` and an unreadable migration history `32002`.

It then checks one thing the migration identifiers cannot express. `Persistence:TextSearchConfiguration` is compiled into the search vector's stored generated column when the table is created, and the identifier of the migration that created it is the same whichever configuration produced it. A host configured for `english` against an index built with `simple` would stem its queries one way and read lexemes built the other, returning fewer results rather than an error, so the host compares its configured value against the expression PostgreSQL actually holds and fails with `32003` when they differ.

Generating a migration for a non-default configuration is therefore a deliberate act: export `Persistence__TextSearchConfiguration` before running the `add-migration` workflow, and rebuild the search documents afterwards. The design-time factory reads that variable, which is the double-underscore encoding of the setting a deployment already has.

Applying is one mechanism per environment: `mailmcp-migrations` locally, and an explicit deployment step elsewhere. A host that mutates schema while starting could race a second instance, could apply a destructive change nobody reviewed at deploy time, and would leave the operator no point at which to take a backup.

The GitHub CLI (`gh`) is installed separately through the operating system package manager and is required for the issue and pull-request workflow in root `AGENTS.md`. It needs the `project` scope on top of its default scopes so it can read and update the roadmap board.

On a machine that has never authenticated, log in and request the scope in the same step:

```bash
gh auth login -s project
```

On a machine that is already authenticated, add the scope to the stored credentials instead; `gh auth refresh` only expands existing credentials and fails when no host is authenticated:

```bash
gh auth refresh -s project
```

Confirm the result with `gh auth status`, which must list `project` among the token scopes. Its reviewed version is recorded in `LICENSES.md` alongside the other developer tooling.

## Code coverage

The full verification script collects and enforces coverage. To run only the
underlying coverage target after a Release build:

```bash
dotnet tool restore
dotnet msbuild .config/CodeCoverage.proj -t:Collect
```

The command runs the whole solution in one test invocation, which produces one uniquely named Cobertura report per unit-test assembly, merges the reports, and requires at least 85% aggregate line coverage across `Domain`, `Application`, `Infrastructure`, `AI`, and `Mcp`. The result always represents the whole configured scope, not only changed lines. `Host` and `AppHost` are excluded as thin executable composition roots.

Two attributes take code out of that denominator, and `testconfig.json` configures the collector to honor both. `[ExcludeFromCodeCoverage]` marks code that should never participate in coverage. `[RequiresIntegrationCoverage]`, declared in `src/shared/RequiresIntegrationCoverageAttribute.cs`, marks code whose verification needs a real database, a real mail server, or a composed host: the EF Core context and its entities, the persistence stores, the MailKit client adapter, the file-system and environment secret readers, and the infrastructure registration extensions carry it today. Integration tests will prove that code once they exist, and they will collect no coverage, so a marked class is measured by neither run. Removing the marker from a class puts every line of it back into the denominator, which is how to check that the exclusion is still earned.

A third exclusion is applied by path rather than by attribute: `.config/CodeCoverage.proj` filters `**/Persistence/Migrations/*.cs` out of the merged report. EF Core generates those files and the `add-migration` workflow regenerates them, so they carry no attribute the generator would preserve, and no unit test may execute them — a migration is proven by applying it to a real PostgreSQL server and reviewing the resulting schema. Leaving them in put roughly a thousand uncoverable lines in the denominator and moved the aggregate by more than twenty points, which would have masked a real regression anywhere else.

Raw Cobertura reports and TRX files are written under `artifacts/coverage/raw/`. The merged Cobertura and HTML reports are written under `artifacts/coverage/report/`.

## Pull request checks

Pull requests targeting `main` run two GitHub Actions checks after they are marked ready for review. Draft pull requests skip both jobs without allocating a runner. Marking a draft ready for review starts the applicable checks immediately, and later commits continue to start them. Converting a ready pull request back to draft through the `converted_to_draft` activity cancels the superseded active run and skips the replacement job. Both workflows remain available through manual dispatch regardless of pull request state:

- `Build and unit test` runs for pull requests to `main` that change production code, tests, the solution or SDK selection, shared build and package configuration, coverage tooling, or the workflow itself. It restores `MailMcp.slnx` and repository-local tools, builds the solution in Release configuration, runs all unit-test projects through Microsoft Testing Platform with unique coverage prefixes, merges their Cobertura reports, and fails below 85% aggregate line coverage for the complete configured production scope. It uploads raw and merged coverage artifacts and TRX results even when the threshold fails.
- `dotnet format` restores `MailMcp.slnx` and verifies repository formatting without applying changes.

The `main` branch protection rule requires a pull request and the `Build and unit test` check, requires the branch to be current with `main`, applies to administrators, and requires review conversations to be resolved. It does not require an approving review because the repository currently has one maintainer. Force-pushes and deletion of `main` are disabled. The GitHub repository coverage rule must remain disabled because GitHub Code Quality coverage uploads are unavailable for this user-owned repository; the required repository-owned check enforces the same 85% minimum against the complete configured code scope.

Both workflows restore from a cached `~/.nuget/packages` keyed on `Directory.Packages.props`, `global.json`, and `.config/dotnet-tools.json`. Because every version is pinned centrally, those three files decide what restore downloads, so a changed pin misses the cache rather than resolving against a stale package set.

Both workflows use the SDK pinned in `global.json`, cancel superseded runs for the same pull request, request read-only repository permissions, and avoid credentials or service-specific secrets. A job-level draft guard skips both jobs unless a pull request is non-draft or the workflow was manually dispatched. The formatting workflow remains limited to `src/**` and `tests/**` and is not a required status check.
