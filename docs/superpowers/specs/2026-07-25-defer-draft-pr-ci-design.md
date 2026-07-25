# Defer pull request CI until ready for review

## Goal

Avoid consuming GitHub Actions runners for draft pull requests while preserving the existing build, unit-test, coverage, and formatting checks once a pull request is ready for review.

## Workflow behavior

Both pull request workflows will explicitly listen for `opened`, `reopened`, `synchronize`, `ready_for_review`, and `converted_to_draft` activity. Each job will run when either:

- the workflow was started manually with `workflow_dispatch`; or
- the pull request is not a draft.

Opening or updating a draft pull request may create a skipped workflow result, but no runner or build, test, coverage, or formatting step will execute. Marking the pull request ready for review will trigger the workflows immediately. Later commits to a ready pull request will continue to trigger them through `synchronize`. Converting a ready pull request back to draft creates a skipped replacement run that cancels the superseded active run through the existing concurrency group.

The existing target-branch filters, path filters, concurrency behavior, permissions, and manual dispatch support remain unchanged.

## Implementation

Add the activity types and the same job-level condition to:

- `.github/workflows/build-and-unit-test.yml`
- `.github/workflows/dotnet-format.yml`

Update `docs/operations/local-development.md` so the documented pull request behavior matches the workflows.

## Verification

Verify that both workflow files:

- include `ready_for_review` and `converted_to_draft` alongside the standard pull request activity types;
- guard the complete job rather than individual steps;
- allow `workflow_dispatch` regardless of pull request state;
- retain the existing branch and path filters.

Run the repository-required restore, build, unit-test, coverage, and formatting checks, then inspect the final diff before publishing a draft pull request.
