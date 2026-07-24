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

## Pull request gates

Pull requests targeting `main` run two GitHub Actions gates:

- `Build and unit test` restores `MailMcp.slnx`, builds the solution in Release configuration, and runs the solution unit test projects without rebuilding.
- `dotnet format` restores `MailMcp.slnx` and verifies repository formatting without applying changes.

Both workflows use the SDK pinned in `global.json`, cancel superseded runs for the same pull request, request read-only repository permissions, and avoid credentials or service-specific secrets.

