# Whole-Code Coverage Gate Design

## Goal

Configure reproducible code coverage for MailMcp and reject pull requests targeting `main` when aggregate line coverage across the complete testable production scope is below 85%.

The threshold applies to the whole configured codebase on every run. It does not use patch coverage, changed-line coverage, or a comparison with the target branch.

## Coverage Scope

The production coverage denominator includes all coverable code from these assemblies:

- `MailMcp.Domain`
- `MailMcp.Application`
- `MailMcp.Infrastructure`
- `MailMcp.AI`
- `MailMcp.Mcp`

The thin executable composition roots `MailMcp.Host` and `MailMcp.AppHost` are excluded. Their responsibilities are process startup, dependency composition, middleware and endpoint wiring, and development orchestration rather than application or domain behavior.

Test assemblies, generated code, compiler-generated code, and Coverlet infrastructure are excluded. Ordinary production behavior, branches, validation, mapping, policies, and invariants remain in scope.

`System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute` may be applied to a class that contains no executable application, domain, mapping, validation, policy, or infrastructure behavior. It must not be used to make the threshold pass or to hide code that can be meaningfully unit tested. The repository guidance in `AGENTS.md` will state this constraint.

## Architecture

### Collection

Every unit-test project references `coverlet.MTP` as a private test-only dependency. The package is the native Coverlet extension for the Microsoft Testing Platform already used by the repository.

A repository-level Coverlet configuration includes `MailMcp.*` production assemblies and excludes:

- `MailMcp.Host`
- `MailMcp.AppHost`
- `MailMcp.*.UnitTests`
- generated and compiler-generated code

Each unit-test project produces a Cobertura report during the existing solution-wide test run.

### Aggregation

ReportGenerator is installed as a repository-local .NET tool. It merges all Cobertura inputs into one report before the threshold is evaluated. Merging prevents shared assemblies referenced by several test projects from being counted more than once and produces one weighted result based on all covered and coverable lines in the configured scope.

The generated outputs include:

- one merged Cobertura report used by the gate;
- one human-readable HTML report uploaded as a workflow artifact.

### Enforcement

A small repository-owned .NET coverage verifier reads the merged Cobertura document, calculates `covered lines / valid lines`, and exits with a non-zero status when the result is below 85%.

The verifier:

- compares the aggregate line result, not per-project percentages;
- reports covered lines, valid lines, calculated percentage, and required percentage;
- fails when a report is expected but missing or malformed;
- handles the current empty scaffold explicitly: if the configured production boundaries contain no coverable source code, the gate reports the scope as empty and succeeds until the first production behavior is introduced.

The verifier is the only component that decides whether the threshold passes. ReportGenerator is responsible only for deterministic aggregation and presentation.

## GitHub Actions Flow

The existing `Build and unit test` pull-request check remains the enforcement point:

1. Check out the pull-request revision.
2. Install the SDK pinned by `global.json`.
3. Restore packages and repository-local tools.
4. Build `MailMcp.slnx` in `Release`.
5. Run every unit-test project with Coverlet enabled.
6. Merge all raw reports.
7. Enforce aggregate line coverage of at least 85%.
8. Upload test results and the coverage report even when the threshold fails.

The workflow runs for every pull request targeting `main`, rather than only when `src/**` or `tests/**` changes. This ensures the required check is always created and cannot disappear because of a path filter.

Coverage enforcement is part of the existing build-and-test job instead of a separate optional status. A below-threshold result therefore fails the same pull-request gate that already owns the solution build and unit-test run.

## Local Developer Flow

Repository documentation will provide one command sequence that uses the same collector, merger, configuration, and verifier as CI. Local and CI calculations therefore share:

- the same assembly scope;
- the same exclusions;
- the same 85% threshold;
- the same aggregate-line formula.

Raw and generated coverage files remain under `artifacts/`, which is already ignored by Git.

## Dependencies and Licensing

The centrally pinned dependencies are:

- `coverlet.MTP` 10.0.1, MIT, compatible with .NET 10 and Microsoft Testing Platform 2.x;
- `dotnet-reportgenerator-globaltool` 5.5.10, Apache-2.0, compatible with .NET 10.

Both are development-only. `LICENSES.md` will record their exact versions, purpose, license expressions, upstream sources, and notice expectations.

No hosted coverage service receives repository data.

## Verification

Implementation verification includes:

- a passing verifier scenario at exactly 85%;
- a failing verifier scenario below 85%;
- malformed or missing report failure;
- aggregate merging of more than one Cobertura input;
- the empty-scaffold behavior;
- `dotnet restore`;
- `dotnet build --no-restore`;
- `dotnet test --no-build`;
- the coverage command itself;
- `dotnet format --verify-no-changes`;
- final diff inspection for secrets, unrelated changes, generated artifacts, and incorrect dependency boundaries.

## Documentation Changes

After implementation is verified:

- `docs/operations/local-development.md` documents the coverage command, scope, exclusions, artifact locations, threshold behavior, and PR enforcement;
- `LICENSES.md` records the two development dependencies;
- `AGENTS.md` requires whole-scope aggregate coverage of at least 85% and narrowly permits `[ExcludeFromCodeCoverage]` only on classes without executable logic.

No ADR is created or modified because this change configures development quality tooling without changing a production architecture boundary.
