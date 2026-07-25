# Defer Draft Pull Request CI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent build, unit-test, coverage, and formatting jobs from running for draft pull requests while running them immediately when a pull request becomes ready for review.

**Architecture:** Keep the existing workflows and filters, add explicit pull request activity types including `ready_for_review` and `converted_to_draft`, and gate each complete job on manual dispatch or a non-draft pull request. Update the operational documentation in the same change so it describes the implemented behavior.

**Tech Stack:** GitHub Actions workflow YAML, Markdown, .NET 10 repository verification commands

## Global Constraints

- `workflow_dispatch` must remain available regardless of pull request state.
- Draft pull requests may produce a skipped workflow result, but must not allocate a runner or execute build, test, coverage, or formatting steps.
- Ready pull requests must run on `opened`, `reopened`, `synchronize`, and `ready_for_review`; `converted_to_draft` must cancel the superseded active run through a skipped replacement run.
- Existing target-branch filters, path filters, concurrency behavior, permissions, and job steps must remain unchanged.
- Pull requests must be created as drafts.

---

### Task 1: Gate pull request checks until ready for review

**Files:**
- Modify: `.github/workflows/build-and-unit-test.yml`
- Modify: `.github/workflows/dotnet-format.yml`
- Modify: `docs/operations/local-development.md`
- Create: `docs/superpowers/plans/2026-07-25-defer-draft-pr-ci.md`

**Interfaces:**
- Consumes: GitHub's `pull_request` activity types, `github.event_name`, and `github.event.pull_request.draft`.
- Produces: Both existing CI jobs run only for manual dispatch or non-draft pull requests, including the transition to ready for review.

- [ ] **Step 1: Demonstrate the missing ready-for-review trigger and job guards**

Run:

```bash
rg -n "ready_for_review|github\.event_name == 'workflow_dispatch'|github\.event\.pull_request\.draft == false" \
  .github/workflows/build-and-unit-test.yml \
  .github/workflows/dotnet-format.yml
```

Expected: exit code `1` with no matches because neither workflow contains the trigger or guard.

- [ ] **Step 2: Add the explicit pull request activity types**

In both workflow files, change the start of the `pull_request` configuration to:

```yaml
  pull_request:
    types:
      - opened
      - reopened
      - synchronize
      - ready_for_review
      - converted_to_draft
    branches:
      - main
```

Retain every existing `paths` entry.

- [ ] **Step 3: Guard each complete job while preserving manual dispatch**

Add this condition directly below each job identifier and before `name`:

```yaml
    if: github.event_name == 'workflow_dispatch' || github.event.pull_request.draft == false
```

Apply it to `build-and-unit-test` and `dotnet-format`. Do not add conditions to individual steps.

- [ ] **Step 4: Document the pull request lifecycle behavior**

Update the start of `docs/operations/local-development.md` section `Pull request checks` to say:

```markdown
Pull requests targeting `main` run two GitHub Actions checks after they are marked ready for review. Draft pull requests skip both jobs without allocating a runner. Marking a draft ready for review starts the applicable checks immediately, and later commits continue to start them. Converting a ready pull request back to draft cancels the superseded active run and skips the replacement job. Both workflows remain available through manual dispatch regardless of pull request state:
```

Retain the existing descriptions of both checks and their path filters. Update the final shared-behavior paragraph to include the job-level draft guard.

- [ ] **Step 5: Verify the trigger and guard are present in both workflows**

Run:

```bash
rg -n "ready_for_review|converted_to_draft|github\.event_name == 'workflow_dispatch' \|\| github\.event\.pull_request\.draft == false" \
  .github/workflows/build-and-unit-test.yml \
  .github/workflows/dotnet-format.yml
```

Expected: two `ready_for_review` matches, two `converted_to_draft` matches, and two identical job-guard matches.

- [ ] **Step 6: Run repository verification**

Run:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet msbuild eng/CodeCoverage.proj -t:Collect
dotnet format --verify-no-changes
git diff --check
```

Expected: all commands exit with code `0`; all unit tests pass; aggregate configured line coverage is at least 85%; formatting has no changes; the diff contains no whitespace errors.

- [ ] **Step 7: Review and commit the implementation**

Run:

```bash
git diff --check
git diff --stat origin/main...HEAD
git diff origin/main...HEAD
git status --short
```

Confirm that only the two workflows, operational documentation, design specification, and implementation plan are included and that no co-author trailer is present.

Then run:

```bash
git add .github/workflows/build-and-unit-test.yml \
  .github/workflows/dotnet-format.yml \
  docs/operations/local-development.md \
  docs/superpowers/plans/2026-07-25-defer-draft-pr-ci.md
git commit -m "ci: defer pull request checks until ready"
```

Expected: one implementation commit containing the workflow behavior, matching documentation, and plan.

- [ ] **Step 8: Publish a draft pull request**

Push `agent/defer-draft-pr-ci` to `origin`, then create a draft pull request targeting `main` with a concise summary and the verification results. Confirm through GitHub that the pull request is a draft and return its URL.
