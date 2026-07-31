# Whole-Code Coverage Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce at least 85% aggregate line coverage across all testable MailFathom production libraries for pull requests targeting `main` that change code, tests, or files capable of changing the build or coverage result.

**Architecture:** Coverlet's native Microsoft Testing Platform extension emits one uniquely prefixed Cobertura report per unit-test project. A repository-local ReportGenerator tool merges those reports, and an MSBuild target reads the merged report and fails the existing build-and-test check when whole-scope line coverage is below 85%.

**Tech Stack:** .NET 10, Microsoft Testing Platform 2, xUnit.net v3, coverlet.MTP 10.0.1, ReportGenerator 5.5.10, GitHub Actions.

## Global Constraints

- The minimum aggregate line coverage is 85%.
- Coverage is calculated from the entire configured production scope, never only changed lines.
- Included production boundaries are `Domain`, `Application`, `Infrastructure`, `AI`, and `Mcp`.
- Excluded executable composition roots are `Host` and `AppHost`.
- `[ExcludeFromCodeCoverage]` is allowed only on classes without executable logic and must never hide testable behavior.
- Third-party packages must be centrally pinned, permissively licensed, and recorded in `LICENSES.md`.
- No co-author trailers may be added to commits or pull requests.

---

### Task 1: Configure coverage collection and report aggregation dependencies

**Files:**
- Create: `.config/dotnet-tools.json`
- Create: `testconfig.json`
- Modify: `Directory.Packages.props`
- Modify: `Directory.Build.props`

**Interfaces:**
- Consumes: Microsoft Testing Platform configuration discovery and central package management.
- Produces: Cobertura reports for the complete configured production assembly scope and a repository-local `reportgenerator` command.

- [ ] **Step 1: Add the pinned Coverlet package version**

Add this central package version to `Directory.Packages.props`:

```xml
<PackageVersion Include="coverlet.MTP" Version="10.0.1" />
```

- [ ] **Step 2: Reference Coverlet only from unit-test projects**

Add this package reference inside the existing unit-test-only item group in `Directory.Build.props`:

```xml
<PackageReference Include="coverlet.MTP" PrivateAssets="all" />
```

Copy the repository configuration into every unit-test output directory:

```xml
<None Include="$(MSBuildThisFileDirectory)testconfig.json"
      Link="testconfig.json"
      CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 3: Add the authoritative Coverlet configuration**

Create `testconfig.json` with:

```json
{
  "platformOptions": {
    "Coverlet": {
      "Include": "[MailFathom.*]*",
      "Exclude": "[MailFathom.Host]*,[MailFathom.AppHost]*,[MailFathom.*.UnitTests]*,[coverlet.*]*,[xunit.*]*,[Microsoft.Testing.*]*,[Microsoft.TestPlatform.*]*,[Microsoft.VisualStudio.TestPlatform.*]*,[testhost*]*",
      "ExcludeByAttribute": "ExcludeFromCodeCoverage,ExcludeFromCodeCoverageAttribute,GeneratedCodeAttribute",
      "Format": "cobertura",
      "IncludeTestAssembly": false,
      "DeterministicReport": true,
      "ExcludeAssembliesWithoutSources": "MissingAll"
    }
  }
}
```

- [ ] **Step 4: Pin ReportGenerator as a local tool**

Create `.config/dotnet-tools.json` with:

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-reportgenerator-globaltool": {
      "version": "5.5.10",
      "commands": [
        "reportgenerator"
      ]
    }
  }
}
```

- [ ] **Step 5: Restore and verify dependency compatibility**

Run:

```bash
dotnet tool restore
dotnet restore MailFathom.slnx
```

Expected: both commands exit 0; restore resolves `coverlet.MTP` 10.0.1 with the repository's .NET 10 SDK and MTP runner.

- [ ] **Step 6: Inspect resolved assets**

Run:

```bash
rg -n '"coverlet.MTP/10.0.1"' tests/*/obj/project.assets.json
dotnet tool list
```

Expected: every unit-test project contains the pinned Coverlet package and the local tool list contains ReportGenerator 5.5.10.

### Task 2: Add one cross-platform aggregate coverage command

**Files:**
- Create: `.config/CodeCoverage.proj`

**Interfaces:**
- Consumes: `MailFathom.slnx`, the local `reportgenerator` tool, raw Coverlet Cobertura reports, and the five configured source boundaries.
- Produces: uniquely prefixed raw reports, `artifacts/coverage/report/Cobertura.xml`, `artifacts/coverage/report/index.html`, TRX results, and a failing process exit code below 85%.

- [ ] **Step 1: Create the coverage orchestration project**

Create `.config/CodeCoverage.proj` with:

```xml
<Project DefaultTargets="Collect">
  <PropertyGroup>
    <RepositoryRoot>$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)..'))</RepositoryRoot>
    <CoverageArtifactsDirectory>$(RepositoryRoot)artifacts/coverage</CoverageArtifactsDirectory>
    <RawCoverageDirectory>$(CoverageArtifactsDirectory)/raw</RawCoverageDirectory>
    <CoverageReportDirectory>$(CoverageArtifactsDirectory)/report</CoverageReportDirectory>
    <MergedCoverageReport Condition="'$(MergedCoverageReport)' == ''">$(CoverageReportDirectory)/Cobertura.xml</MergedCoverageReport>
    <Configuration Condition="'$(Configuration)' == ''">Release</Configuration>
    <MinimumLineCoveragePercent>85</MinimumLineCoveragePercent>
    <MinimumLineCoverageRate>$([MSBuild]::Divide($(MinimumLineCoveragePercent), 100.0))</MinimumLineCoverageRate>
  </PropertyGroup>

  <ItemGroup>
    <UnitTestProject Include="$(RepositoryRoot)tests/**/*.csproj" />
  </ItemGroup>

  <Target Name="Collect">
    <RemoveDir Directories="$(CoverageArtifactsDirectory)" />
    <MakeDir Directories="$(RawCoverageDirectory)" />

    <Error Condition="'@(UnitTestProject)' == ''"
           Text="No unit-test projects were found under $(RepositoryRoot)tests." />

    <Exec Command="dotnet test --project &quot;%(UnitTestProject.Identity)&quot; --configuration $(Configuration) --no-build --results-directory &quot;$(RawCoverageDirectory)&quot; -- --report-xunit-trx --coverlet --coverlet-file-prefix &quot;%(UnitTestProject.Filename)&quot;" />
    <CallTarget Targets="GenerateAndEnforceReport" />
  </Target>

  <Target Name="GenerateAndEnforceReport">
    <Exec Command="dotnet tool run reportgenerator &quot;-reports:$(RawCoverageDirectory)/**/*.cobertura.*.xml&quot; &quot;-targetdir:$(CoverageReportDirectory)&quot; &quot;-reporttypes:Cobertura;HtmlInline&quot; &quot;-assemblyfilters:+MailFathom.*;-MailFathom.Host;-MailFathom.AppHost;-MailFathom.*.UnitTests&quot;" />
    <CallTarget Targets="Enforce" />
  </Target>

  <Target Name="Enforce">
    <Error Condition="!Exists('$(MergedCoverageReport)')"
           Text="Merged coverage report was not found at $(MergedCoverageReport)." />

    <XmlPeek XmlInputPath="$(MergedCoverageReport)" Query="/coverage/@line-rate">
      <Output TaskParameter="Result" PropertyName="ActualLineCoverageRate" />
    </XmlPeek>
    <XmlPeek XmlInputPath="$(MergedCoverageReport)" Query="/coverage/@lines-covered">
      <Output TaskParameter="Result" PropertyName="CoveredLineCount" />
    </XmlPeek>
    <XmlPeek XmlInputPath="$(MergedCoverageReport)" Query="/coverage/@lines-valid">
      <Output TaskParameter="Result" PropertyName="ValidLineCount" />
    </XmlPeek>

    <Error Condition="'$(ActualLineCoverageRate)' == ''"
           Text="Merged coverage report does not contain a line-rate value." />

    <PropertyGroup>
      <ActualLineCoveragePercent>$([MSBuild]::Multiply($(ActualLineCoverageRate), 100.0))</ActualLineCoveragePercent>
    </PropertyGroup>

    <Message Text="No coverable production lines exist in the configured boundaries; aggregate line coverage is vacuously complete."
             Importance="high"
             Condition="$(ValidLineCount) == 0" />

    <Message Text="Aggregate line coverage: $(ActualLineCoveragePercent)% ($(CoveredLineCount)/$(ValidLineCount)); required: $(MinimumLineCoveragePercent)%."
             Importance="high" />

    <Error Condition="$(ActualLineCoverageRate) &lt; $(MinimumLineCoverageRate)"
           Text="Aggregate line coverage $(ActualLineCoveragePercent)% is below the required $(MinimumLineCoveragePercent)%." />
  </Target>
</Project>
```

- [ ] **Step 2: Verify the exact-threshold pass behavior**

Create a temporary Cobertura file outside the repository:

```bash
mkdir -p /tmp/mailfathom-coverage-test
printf '%s\n' '<?xml version="1.0" ?><coverage line-rate="0.85" lines-covered="85" lines-valid="100" />' > /tmp/mailfathom-coverage-test/Cobertura.xml
dotnet msbuild .config/CodeCoverage.proj -t:Enforce -p:MergedCoverageReport=/tmp/mailfathom-coverage-test/Cobertura.xml
```

Expected: exit 0 and a diagnostic showing `85% (85/100); required: 85%`.

- [ ] **Step 3: Verify the below-threshold failure behavior**

Run:

```bash
printf '%s\n' '<?xml version="1.0" ?><coverage line-rate="0.8499" lines-covered="8499" lines-valid="10000" />' > /tmp/mailfathom-coverage-test/Cobertura.xml
dotnet msbuild .config/CodeCoverage.proj -t:Enforce -p:MergedCoverageReport=/tmp/mailfathom-coverage-test/Cobertura.xml
```

Expected: non-zero exit and an error stating that `84.99%` is below the required `85%`.

- [ ] **Step 4: Verify unique project prefixes**

Run:

```bash
dotnet msbuild .config/CodeCoverage.proj -t:Collect
find artifacts/coverage/raw -maxdepth 1 -type f -name '*.cobertura.*.xml' -printf '%f\n' | sort
```

Expected: one report for every unit-test project, each beginning with its project name.

- [ ] **Step 5: Verify malformed and missing report failures**

Run:

```bash
mkdir -p /tmp/mailfathom-coverage-test
printf '%s\n' '<?xml version="1.0" ?><coverage />' > /tmp/mailfathom-coverage-test/Cobertura.xml
dotnet msbuild .config/CodeCoverage.proj -t:Enforce -p:MergedCoverageReport=/tmp/mailfathom-coverage-test/Cobertura.xml
dotnet msbuild .config/CodeCoverage.proj -t:Enforce -p:MergedCoverageReport=/tmp/mailfathom-coverage-test/missing.xml
```

Expected: both invocations fail, respectively for a missing `line-rate` and a missing report.

- [ ] **Step 6: Verify the current empty scaffold path**

Run:

```bash
dotnet build MailFathom.slnx --configuration Release --no-restore
dotnet msbuild .config/CodeCoverage.proj -t:Collect
```

Expected: unit-test execution succeeds and the target reports that no coverable production source currently exists in the five configured boundaries.

### Task 3: Make aggregate coverage part of the pull-request gate

**Files:**
- Modify: `.github/workflows/build-and-unit-test.yml`

**Interfaces:**
- Consumes: the `.config/CodeCoverage.proj` command and the existing `Build and unit test` status context.
- Produces: a status check on pull requests to `main` that change code, tests, or build and coverage inputs, fails below 85%, and preserves diagnostic artifacts.

- [ ] **Step 1: Keep the pull-request path filter**

Retain the trigger:

```yaml
on:
  pull_request:
    branches:
      - main
    paths:
      - 'src/**'
      - 'tests/**'
      - '.config/dotnet-tools.json'
      - '.editorconfig'
      - '.github/workflows/build-and-unit-test.yml'
      - 'Directory.Build.props'
      - 'Directory.Build.targets'
      - 'Directory.Packages.props'
      - 'MailFathom.slnx'
      - 'NuGet.config'
      - '.config/**'
      - 'global.json'
      - 'testconfig.json'
  workflow_dispatch:
```

- [ ] **Step 2: Restore the local tool**

Add after package restore:

```yaml
      - name: Restore local tools
        run: dotnet tool restore
```

- [ ] **Step 3: Replace the direct unit-test step with whole-scope collection**

Replace the existing test step with:

```yaml
      - name: Run unit tests and enforce code coverage
        run: dotnet msbuild .config/CodeCoverage.proj -t:Collect
```

- [ ] **Step 4: Preserve diagnostics on success or failure**

Keep the TRX upload and point it at:

```yaml
          path: 'artifacts/coverage/raw/**/*.trx'
```

Add:

```yaml
      - name: Upload code coverage
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: code-coverage
          path: |
            artifacts/coverage/raw/**/*.cobertura.*.xml
            artifacts/coverage/report/**
          if-no-files-found: ignore
```

- [ ] **Step 5: Validate workflow syntax and final job identity**

Run:

```bash
git diff --check
rg -n "name: Build and unit test|name: Run unit tests and enforce code coverage|pull_request:|paths:" .github/workflows/build-and-unit-test.yml
```

Expected: the workflow and job names remain stable, the coverage step is present, and the pull-request `paths` filter covers production code, tests, solution and SDK selection, shared build and package configuration, coverage tooling, and the workflow itself.

- [ ] **Step 6: Require the coverage-owning check on `main`**

Configure GitHub branch protection for `main` to:

- require pull requests;
- require the existing `Build and unit test` check;
- require branches to be current before merge;
- apply enforcement to administrators;
- require review conversations to be resolved;
- allow zero approving reviews while the repository has a single maintainer;
- reject force-pushes and branch deletion.

Read the protection settings back from GitHub after the update.

Expected: `Build and unit test` is the required strict status check and the pull-request rule applies to administrators. The GitHub repository coverage rule is disabled because the repository-owned status check enforces the 85% whole-code threshold.

### Task 4: Document policy, operation, and licensing

**Files:**
- Modify: `AGENTS.md`
- Modify: `docs/operations/local-development.md`
- Modify: `LICENSES.md`

**Interfaces:**
- Consumes: the verified implementation behavior from Tasks 1-3.
- Produces: durable repository guidance for agents, developers, reviewers, and license audits.

- [ ] **Step 1: Add repository coverage rules to AGENTS.md**

Add a `Code coverage` section under unit testing policy:

```markdown
## Code coverage

- Maintain at least 85% aggregate line coverage across the complete configured production scope: `Domain`, `Application`, `Infrastructure`, `AI`, and `Mcp`.
- Calculate the threshold from the whole configured codebase on every run. Do not substitute patch coverage, changed-line coverage, or per-project thresholds for the aggregate gate.
- Keep `Host` and `AppHost` excluded as thin executable composition roots. Do not add other assembly, namespace, file, type, or member exclusions merely to make the threshold pass.
- Add `using System.Diagnostics.CodeAnalysis;` and apply `[ExcludeFromCodeCoverage]` to a class only when it contains no executable application, domain, mapping, validation, policy, or infrastructure logic. Do not fully qualify the attribute name.
- Never use `[ExcludeFromCodeCoverage]` to hide behavior that can be meaningfully unit tested. If logic is added to an excluded class, remove the attribute and cover the behavior in the same change.
- Run `dotnet msbuild .config/CodeCoverage.proj -t:Collect` before committing a change that affects production or test code. The command enforces the 85% whole-scope threshold locally and in CI.
```

- [ ] **Step 2: Document local and CI operation**

Update `docs/operations/local-development.md` with:

```markdown
## Code coverage

After a Release build, collect and enforce coverage with:

```bash
dotnet tool restore
dotnet msbuild .config/CodeCoverage.proj -t:Collect
```

The command merges uniquely prefixed unit-test Cobertura reports and requires at least 85% aggregate line coverage across `Domain`, `Application`, `Infrastructure`, `AI`, and `Mcp`. The result always represents the whole configured scope, not only changed lines. `Host` and `AppHost` are excluded as composition roots.

Raw reports and TRX files are written under `artifacts/coverage/raw/`. The merged Cobertura and HTML reports are written under `artifacts/coverage/report/`.
```

Update the pull-request checks section so `Build and unit test` explicitly includes whole-scope coverage enforcement and runs for pull requests targeting `main` that change code, tests, or build and coverage inputs.

- [ ] **Step 3: Register third-party licensing**

Add a development-tooling row to `LICENSES.md` recording:

```markdown
| coverlet.MTP 10.0.1 and dotnet-reportgenerator-globaltool 5.5.10 | Microsoft Testing Platform coverage collection and deterministic aggregation/report generation for the 85% whole-code pull-request gate | MIT for Coverlet; Apache-2.0 for ReportGenerator in upstream repository and NuGet metadata | Allowed as development-only tooling. Preserve the MIT and Apache-2.0 notices when redistributing the tools or their source. | <https://www.nuget.org/packages/coverlet.MTP/10.0.1>, <https://github.com/coverlet-coverage/coverlet>, <https://www.nuget.org/packages/dotnet-reportgenerator-globaltool/5.5.10>, <https://github.com/danielpalme/ReportGenerator> |
```

- [ ] **Step 4: Verify documentation matches the implementation**

Run:

```bash
rg -n "85%|ExcludeFromCodeCoverage|CodeCoverage.proj|Domain.*Application.*Infrastructure.*AI.*Mcp" AGENTS.md docs/operations/local-development.md
rg -n "coverlet.MTP 10.0.1|dotnet-reportgenerator-globaltool 5.5.10|MIT|Apache-2.0" LICENSES.md
```

Expected: the exact threshold, scope, command, exclusion constraint, versions, and licenses are discoverable.

### Task 5: Complete verification and publish the change

**Files:**
- Inspect: all changed files

**Interfaces:**
- Consumes: all implementation tasks.
- Produces: a verified feature branch and a draft pull request targeting `main`.

- [ ] **Step 1: Run the repository verification suite**

Run:

```bash
dotnet restore MailFathom.slnx
dotnet build MailFathom.slnx --configuration Release --no-restore
dotnet test --solution MailFathom.slnx --configuration Release --no-build
dotnet msbuild .config/CodeCoverage.proj -t:Collect
dotnet format MailFathom.slnx --verify-no-changes --verbosity diagnostic
```

Expected: all commands exit 0; coverage reports the explicit empty-scaffold behavior until production code appears in the included boundaries.

- [ ] **Step 2: Inspect the complete diff**

Run:

```bash
git diff --check
git status --short
git diff --stat main...HEAD
git diff main...HEAD
```

Expected: no secrets, unrelated edits, generated artifacts, or dependency-boundary violations.

- [ ] **Step 3: Commit implementation intentionally**

Stage only the task files and commit without co-author trailers:

```bash
git add .config/dotnet-tools.json testconfig.json Directory.Packages.props Directory.Build.props .config/CodeCoverage.proj .github/workflows/build-and-unit-test.yml AGENTS.md docs/operations/local-development.md LICENSES.md
git commit -m "ci: enforce whole-code coverage"
```

- [ ] **Step 4: Push the feature branch**

Run:

```bash
git push -u origin agent/code-coverage-gate
```

Expected: the remote branch is created without force.

- [ ] **Step 5: Create and inspect a draft pull request**

Create a draft PR targeting `main` with:

```text
Title: Enforce 85% whole-code coverage

Summary:
- collect coverage through the native Microsoft Testing Platform Coverlet extension
- merge all uniquely prefixed boundary reports and enforce 85% aggregate line coverage across the complete testable production scope
- run the existing build-and-test check for matching PRs to main and publish coverage diagnostics
- document the narrow ExcludeFromCodeCoverage policy and register development-tool licenses

Verification:
- dotnet restore MailFathom.slnx
- dotnet build MailFathom.slnx --configuration Release --no-restore
- dotnet test --solution MailFathom.slnx --configuration Release --no-build
- dotnet msbuild .config/CodeCoverage.proj -t:Collect
- dotnet format MailFathom.slnx --verify-no-changes --verbosity diagnostic
```

Then inspect the PR base, head, draft state, changed files, and initial check state.
