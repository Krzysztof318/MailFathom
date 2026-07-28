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
all unit tests without rebuilding, and verifies formatting. Formatting runs in
both scripts on purpose: style diagnostics such as `IDE0005` come from
`dotnet format` rather than from the build, and leaving them to the final gate
means discovering them only after tool restore and the whole coverage
collection have run. The full script additionally restores repository tools,
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

The AppHost PostgreSQL resource uses the `pgvector/pgvector:0.8.2-pg17` image so local development starts with a PostgreSQL server that can support the `vector` extension required by the RAG and embedding slices.

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

The repository provisions no development environment, so install the SDK and any command-line tools on the developer machine. Repository-local tools declared in `.config/dotnet-tools.json` come from `dotnet tool restore` and are limited to what the coverage gate needs: `reportgenerator` merges the per-project Cobertura reports.

Three tools are installed globally when their workflows are needed:

```bash
dotnet tool install --global dotnet-ef --version 10.0.10
dotnet tool install --global Aspire.Cli --version 13.4.6
dotnet tool install --global csharp-ls --version 0.26.0
```

`dotnet ef` runs EF Core migrations and design-time commands. `aspire` is only required for Aspire CLI workflows against the AppHost. `csharp-ls` is the C# language server that editors and agent tooling launch to resolve symbols before editing, instead of discovering a misspelled type at build time.

`csharp-ls` is installed globally rather than pinned in `.config/dotnet-tools.json` because a manifest-local tool is only reachable as `dotnet tool run csharp-ls`; it never lands on `PATH`, so a client that launches the bare `csharp-ls` executable still fails with `ENOENT`. A global install puts it in `~/.dotnet/tools`, which is on `PATH`, and keeps the language server out of the `dotnet tool restore` that continuous integration runs for the coverage gate. All three versions are recorded in `LICENSES.md`; keep the register aligned when you move to a newer one.

### EF Core design-time commands

**Do not invoke `dotnet ef` directly.** Design-time and migration commands must run through `aspire exec`, so they see the orchestrated resources and the connection strings the AppHost issues rather than a local environment that can differ from every real one. That path is not wired up yet, so EF Core tooling is not to be run at all for now, and work that needs a migration is blocked until it is.

The pieces the tooling will need are already in place. `MailMcpDbContextDesignTimeFactory` gives EF Core a context without starting the host, which matters because the host composes its connection string during startup and design-time tooling never runs that. It resolves no secret: `migrations add` and `dbcontext script` need the model rather than a reachable server, and a deployment credential does not belong on a workstation. A command that does need a live database reads `MAILMCP_DESIGN_TIME_CONNECTION_STRING`, falling back to `Host=localhost;Database=mailmcp;Username=mailmcp`.

`Infrastructure` is its own startup project for these commands. It owns the context, the factory, and the only reference to `Microsoft.EntityFrameworkCore.Design`; `Host` references that package nowhere, so naming it as the startup project fails.

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

The command produces one uniquely prefixed Cobertura report per unit-test project, merges the reports, and requires at least 85% aggregate line coverage across `Domain`, `Application`, `Infrastructure`, `AI`, and `Mcp`. The result always represents the whole configured scope, not only changed lines. `Host` and `AppHost` are excluded as thin executable composition roots.

Two attributes take code out of that denominator, and `testconfig.json` configures the collector to honor both. `[ExcludeFromCodeCoverage]` marks code that should never participate in coverage. `[RequiresIntegrationCoverage]`, declared in `src/shared/RequiresIntegrationCoverageAttribute.cs`, marks code whose verification needs a real database, a real mail server, or a composed host: the EF Core context and its entities, the persistence stores, the MailKit client adapter, the file-system and environment secret readers, and the infrastructure registration extensions carry it today. Integration tests will prove that code once they exist, and they will collect no coverage, so a marked class is measured by neither run. Removing the marker from a class puts every line of it back into the denominator, which is how to check that the exclusion is still earned.

Raw Cobertura reports and TRX files are written under `artifacts/coverage/raw/`. The merged Cobertura and HTML reports are written under `artifacts/coverage/report/`.

## Pull request checks

Pull requests targeting `main` run two GitHub Actions checks after they are marked ready for review. Draft pull requests skip both jobs without allocating a runner. Marking a draft ready for review starts the applicable checks immediately, and later commits continue to start them. Converting a ready pull request back to draft through the `converted_to_draft` activity cancels the superseded active run and skips the replacement job. Both workflows remain available through manual dispatch regardless of pull request state:

- `Build and unit test` runs for pull requests to `main` that change production code, tests, the solution or SDK selection, shared build and package configuration, coverage tooling, or the workflow itself. It restores `MailMcp.slnx` and repository-local tools, builds the solution in Release configuration, runs all unit-test projects through Microsoft Testing Platform with unique coverage prefixes, merges their Cobertura reports, and fails below 85% aggregate line coverage for the complete configured production scope. It uploads raw and merged coverage artifacts and TRX results even when the threshold fails.
- `dotnet format` restores `MailMcp.slnx` and verifies repository formatting without applying changes.

The `main` branch protection rule requires a pull request and the `Build and unit test` check, requires the branch to be current with `main`, applies to administrators, and requires review conversations to be resolved. It does not require an approving review because the repository currently has one maintainer. Force-pushes and deletion of `main` are disabled. The GitHub repository coverage rule must remain disabled because GitHub Code Quality coverage uploads are unavailable for this user-owned repository; the required repository-owned check enforces the same 85% minimum against the complete configured code scope.

Both workflows use the SDK pinned in `global.json`, cancel superseded runs for the same pull request, request read-only repository permissions, and avoid credentials or service-specific secrets. A job-level draft guard skips both jobs unless a pull request is non-draft or the workflow was manually dispatched. The formatting workflow remains limited to `src/**` and `tests/**` and is not a required status check.
