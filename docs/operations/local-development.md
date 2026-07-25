# Local development

Use the .NET SDK pinned in `global.json`. Test execution is configured for Microsoft Testing Platform through the repository-level `global.json` test runner setting.

## Commands

For the normal implementation loop, run:

```bash
bash eng/agent-workflow/verify-fast.sh
```

Before committing, run the complete local gate:

```bash
bash eng/agent-workflow/verify-full.sh
```

The fast script restores the solution, builds it in Release configuration, and
runs all unit tests without rebuilding. The full script additionally restores
repository tools, executes the aggregate coverage gate, verifies formatting,
and checks the Git diff. See [Agent workflow](agent-workflow.md) for the
workspace inspection command and shared skills.

Run the web host directly:

```bash
dotnet run --project src/Host/Host.csproj
```

Run the Aspire orchestration host:

```bash
dotnet run --project src/AppHost/AppHost.csproj
```

The AppHost PostgreSQL resource uses the `pgvector/pgvector:0.8.2-pg17` image so local development starts with a PostgreSQL server that can support the `vector` extension required by the RAG and embedding slices.

## Command-line tooling

The repository provisions no development environment, so install the SDK and any command-line tools on the developer machine. Repository-local tools declared in `.config/dotnet-tools.json` come from `dotnet tool restore` and are limited to what the coverage gate needs.

Two tools are installed globally when their workflows are needed:

```bash
dotnet tool install --global dotnet-ef --version 10.0.10
dotnet tool install --global Aspire.Cli --version 13.4.6
```

`dotnet ef` runs EF Core migrations and design-time commands. `aspire` is only required for Aspire CLI workflows against the AppHost. Both versions are recorded in `LICENSES.md`; keep the register aligned when you move to a newer one.

## Code coverage

The full verification script collects and enforces coverage. To run only the
underlying coverage target after a Release build:

```bash
dotnet tool restore
dotnet msbuild .config/CodeCoverage.proj -t:Collect
```

The command produces one uniquely prefixed Cobertura report per unit-test project, merges the reports, and requires at least 85% aggregate line coverage across `Domain`, `Application`, `Infrastructure`, `AI`, and `Mcp`. The result always represents the whole configured scope, not only changed lines. `Host` and `AppHost` are excluded as thin executable composition roots.

Raw Cobertura reports and TRX files are written under `artifacts/coverage/raw/`. The merged Cobertura and HTML reports are written under `artifacts/coverage/report/`.

## Pull request checks

Pull requests targeting `main` run two GitHub Actions checks after they are marked ready for review. Draft pull requests skip both jobs without allocating a runner. Marking a draft ready for review starts the applicable checks immediately, and later commits continue to start them. Converting a ready pull request back to draft through the `converted_to_draft` activity cancels the superseded active run and skips the replacement job. Both workflows remain available through manual dispatch regardless of pull request state:

- `Build and unit test` runs for pull requests to `main` that change production code, tests, the solution or SDK selection, shared build and package configuration, coverage tooling, or the workflow itself. It restores `MailMcp.slnx` and repository-local tools, builds the solution in Release configuration, runs all unit-test projects through Microsoft Testing Platform with unique coverage prefixes, merges their Cobertura reports, and fails below 85% aggregate line coverage for the complete configured production scope. It uploads raw and merged coverage artifacts and TRX results even when the threshold fails.
- `dotnet format` restores `MailMcp.slnx` and verifies repository formatting without applying changes.

The `main` branch protection rule requires a pull request and the `Build and unit test` check, requires the branch to be current with `main`, applies to administrators, and requires review conversations to be resolved. It does not require an approving review because the repository currently has one maintainer. Force-pushes and deletion of `main` are disabled. The GitHub repository coverage rule must remain disabled because GitHub Code Quality coverage uploads are unavailable for this user-owned repository; the required repository-owned check enforces the same 85% minimum against the complete configured code scope.

Both workflows use the SDK pinned in `global.json`, cancel superseded runs for the same pull request, request read-only repository permissions, and avoid credentials or service-specific secrets. A job-level draft guard skips both jobs unless a pull request is non-draft or the workflow was manually dispatched. The formatting workflow remains limited to `src/**` and `tests/**` and is not a required status check.
