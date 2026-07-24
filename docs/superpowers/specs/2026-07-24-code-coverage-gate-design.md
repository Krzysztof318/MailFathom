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

Test assemblies, externally generated code marked with `GeneratedCodeAttribute`, and Coverlet infrastructure are excluded. Compiler-generated state machines and members are not broadly excluded because doing so could remove source behavior such as asynchronous methods from the denominator. Ordinary production behavior, branches, validation, mapping, policies, and invariants remain in scope.

`[ExcludeFromCodeCoverage]` may be applied to a class that contains no executable application, domain, mapping, validation, policy, or infrastructure behavior. It must not be used to make the threshold pass or to hide code that can be meaningfully unit tested. The repository guidance in `AGENTS.md` will state this constraint.

## Architecture

### Collection

Every unit-test project references `coverlet.MTP` as a private test-only dependency. The package is the native Coverlet extension for the Microsoft Testing Platform already used by the repository.

A repository-level Coverlet configuration includes `MailMcp.*` production assemblies and excludes:

- `MailMcp.Host`
- `MailMcp.AppHost`
- `MailMcp.*.UnitTests`
- externally generated code marked with `GeneratedCodeAttribute`

Each unit-test project runs explicitly with a project-specific `--coverlet-file-prefix` and produces a uniquely named Cobertura report. This prevents parallel or same-directory output collisions from silently removing a project from the aggregate.

### Aggregation

ReportGenerator is installed as a repository-local .NET tool. It merges all Cobertura inputs into one report before GitHub evaluates the threshold. Merging prevents shared assemblies referenced by several test projects from being counted more than once and produces one weighted result based on all covered and coverable lines in the configured scope.

The generated outputs include:

- one merged Cobertura report used by the gate;
- one human-readable HTML report uploaded as a workflow artifact.

### Enforcement

A small repository-owned MSBuild target reads the merged Cobertura document, validates its required aggregate fields, and reports `covered lines / valid lines`.

The local validator:

- validates the aggregate line result, not per-project percentages;
- reports covered lines, valid lines, and the calculated percentage;
- fails when a report is expected but missing or malformed;
- handles the current empty scaffold explicitly: if the configured production boundaries contain no coverable source code, the gate reports the scope as empty and succeeds until the first production behavior is introduced.

The workflow uploads the merged Cobertura report through `actions/upload-code-coverage`. The active GitHub `main` ruleset is the single component that decides whether the 85% minimum passes. This avoids duplicating the numeric threshold in repository automation while retaining a reproducible whole-scope report.

## GitHub Actions Flow

The existing `Build and unit test` pull-request check remains the enforcement point:

1. Check out the pull-request head revision or the pushed `main` revision.
2. Install the SDK pinned by `global.json`.
3. Restore packages and repository-local tools.
4. Build `MailMcp.slnx` in `Release`.
5. Run every unit-test project with Coverlet enabled.
6. Merge all raw reports.
7. Validate the aggregate report.
8. Upload the merged Cobertura report to GitHub Code Quality.
9. Upload test results and coverage artifacts for diagnostics.

The workflow runs for pull requests targeting `main` that change production code, tests, the solution or SDK selection, shared build and package configuration, coverage tooling, or the workflow itself. The path filter intentionally excludes ordinary documentation while ensuring that every file capable of changing the coverage calculation or build result triggers the gate.

The workflow also runs on matching pushes to `main` so GitHub Code Quality has the default-branch baseline required for branch comparisons. Pull requests upload the same whole-scope report, and the GitHub coverage ruleset blocks results below 85%.

The `main` branch protection rule requires pull requests and the existing `Build and unit test` status check, requires branches to be current before merge, applies enforcement to administrators, and requires review conversations to be resolved. It does not require an approving review while the repository has a single maintainer. Force-pushes and branch deletion remain disabled. GitHub's repository coverage minimum is configured as 85%, while the repository-owned report remains responsible for defining the whole-code measurement scope.

## Local Developer Flow

Repository documentation will provide one command sequence that uses the same collector, merger, configuration, and validator as CI. Local and CI calculations therefore share:

- the same assembly scope;
- the same exclusions;
- the same aggregate-line formula.

The local command intentionally does not enforce a numeric minimum. The active GitHub ruleset owns the single 85% threshold.

Raw and generated coverage files remain under `artifacts/`, which is already ignored by Git.

## Dependencies and Licensing

The centrally pinned dependencies are:

- `coverlet.MTP` 10.0.1, MIT, compatible with .NET 10 and Microsoft Testing Platform 2.x;
- `dotnet-reportgenerator-globaltool` 5.5.10, Apache-2.0, compatible with .NET 10.

Both are development-only. `LICENSES.md` will record their exact versions, purpose, license expressions, upstream sources, and notice expectations.

GitHub Code Quality receives the merged Cobertura report through the owner-approved official `actions/upload-code-coverage` integration. Its use is governed by GitHub's service terms; the action repository does not publish a standalone open-source license and must not be vendored or redistributed.

## Verification

Implementation verification includes:

- malformed or missing report failure;
- aggregate merging of more than one Cobertura input;
- unique Coverlet filenames for every unit-test project;
- the empty-scaffold behavior;
- `dotnet restore`;
- `dotnet build --no-restore`;
- `dotnet test --no-build`;
- the coverage command itself;
- `dotnet format --verify-no-changes`;
- final diff inspection for secrets, unrelated changes, generated artifacts, and incorrect dependency boundaries.

## Documentation Changes

After implementation is verified:

- `docs/operations/local-development.md` documents the coverage command, scope, exclusions, artifact locations, GitHub threshold behavior, and PR enforcement;
- `LICENSES.md` records the two development dependencies and the GitHub-hosted coverage integration;
- `AGENTS.md` requires whole-scope aggregate coverage of at least 85% and narrowly permits `[ExcludeFromCodeCoverage]` only on classes without executable logic.

No ADR is created or modified because this change configures development quality tooling without changing a production architecture boundary.
