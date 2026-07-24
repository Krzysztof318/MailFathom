# Local development

Use the .NET SDK pinned in `global.json`. Test execution is configured for Microsoft Testing Platform through the repository-level `global.json` test runner setting.

## Commands

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format --verify-no-changes
```

Run the web host directly:

```bash
dotnet run --project src/Host/Host.csproj
```

Run the Aspire orchestration host:

```bash
dotnet run --project src/AppHost/AppHost.csproj
```

The AppHost PostgreSQL resource uses the `pgvector/pgvector:0.8.2-pg17` image so local development starts with a PostgreSQL server that can support the `vector` extension required by the RAG and embedding slices.

## Code coverage

After a Release build, collect and enforce coverage with:

```bash
dotnet tool restore
dotnet msbuild eng/CodeCoverage.proj -t:Collect
```

The command produces one Cobertura report per unit-test project, merges the reports, and requires at least 85% aggregate line coverage across `Domain`, `Application`, `Infrastructure`, `AI`, and `Mcp`. The result always represents the whole configured scope, not only changed lines. `Host` and `AppHost` are excluded as thin executable composition roots.

Raw Cobertura reports and TRX files are written under `artifacts/coverage/raw/`. The merged Cobertura and HTML reports are written under `artifacts/coverage/report/`.

## Pull request checks

Pull requests targeting `main` run two GitHub Actions checks:

- `Build and unit test` runs for pull requests to `main` that change `src/**` or `tests/**`. It restores `MailMcp.slnx` and repository-local tools, builds the solution in Release configuration, runs all unit-test projects through Microsoft Testing Platform, merges their Cobertura reports, and fails below 85% aggregate line coverage for the complete configured production scope. It uploads the raw and merged coverage reports and the TRX results even when the threshold fails.
- `dotnet format` restores `MailMcp.slnx` and verifies repository formatting without applying changes.

The `main` branch protection rule requires a pull request and the `Build and unit test` check, requires the branch to be current with `main`, applies to administrators, and requires review conversations to be resolved. It does not require an approving review because the repository currently has one maintainer. Force-pushes and deletion of `main` are disabled. Because the required workflow has a path filter, a pull request that changes neither `src/**` nor `tests/**` must run `Build and unit test` manually against its head branch before merge.

Both workflows use the SDK pinned in `global.json`, are limited to pull requests that change `src/**` or `tests/**`, cancel superseded runs for the same pull request, request read-only repository permissions, and avoid credentials or service-specific secrets. The formatting workflow is not a required status check.
