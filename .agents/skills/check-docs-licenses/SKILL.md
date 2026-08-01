---
name: check-docs-licenses
description: Use when a change is nearing completion or affects behavior, configuration, operations, documentation, dependencies, tools, services, protocol APIs, container images, models, generated assets, or copied code.
---

# Check Documentation And Licenses

This is a mandatory completion gate. Inspect the complete diff and applicable repository instructions.

Running the gate is mandatory; finding something to change is not. `n/a` and `pass` are complete, successful verdicts, and a gate that reports `n/a` with evidence has done its whole job. Never register a dependency, expand documentation, or widen the diff so the gate looks exercised.

## Documentation

Confirm that durable documentation describes implemented behavior:

- update affected commands, examples, configuration, failure modes, security assumptions, and operations;
- keep future intent in specifications and actual behavior in `docs/`;
- treat stale guidance as a failure;
- do not create or modify an ADR without explicit owner approval.

Use `n/a` only when the change cannot affect user, operator, contributor, architectural, or security guidance.

## Changelog

`CHANGELOG.md` records what a consumer of a release would notice, and the entry is written by the change that causes it rather than reconstructed at release time. No mechanical check can tell a user-visible change from a refactor, so this gate is where the obligation lives.

An entry is required when the change reaches one of the four public surfaces [ADR 0004](../../../docs/decisions/0004-versioning-and-release-policy.md) versions — the MCP tool contract, the configuration schema, the database schema, or the deployment contract — or fixes a defect that was observable from outside, or has a security consequence. It goes under `## [Unreleased]`, in one of the six Keep a Changelog categories, and it references the issue or pull request that carried it.

A breaking entry opens with `**Breaking (<surface>)**` and states what the operator has to do, not only what changed. A change touching the database schema says whether a migration must be applied, whether it applies while the previous version still runs, and whether the release deploys over the previous release's data.

Everything else earns none. A refactor, a new test, a continuous-integration adjustment, a documentation edit, and an internal rename are `n/a`, and adding a line for one is the failure this rule guards against — a file recording them stops being read and then stops being written.

## Licenses

Inventory added, upgraded, replaced, bundled, or newly used third-party packages, tools, services, provider APIs, protocols, container images, models, generated assets, and copied samples.

For every affected item, verify current official upstream evidence for:

- exact name and version, license expression, commercial and redistribution suitability;
- upstream URL and required attribution or `NOTICE` handling;
- .NET compatibility when applicable;
- separate service, model, telemetry, data-use, trademark, and data-processing terms when applicable.

Ensure `THIRD_PARTY_LICENSES.md` is updated in the same change. A permissive SDK does not approve its hosted service. Unknown, conflicting, or unofficial evidence is a failure. Use `n/a` only when the inventory is empty.

`THIRD_PARTY_LICENSES.md` records what the repository actually pins, bundles, or calls, and is not the project's own `LICENSE` or a generated notice bundle. Put an entry in the section that matches the component's exposure — redistributed, build-time, test-only, orchestration, continuous integration, developer tooling, externally sourced source, or hosted service — because that is what decides whether a release obligation follows.

Register only what the change actually introduces. A component it mentions, plans, evaluates, or rejects gets no row anywhere in the file, in any wording: a row asserts a completed review of software in use, and a rejected or deferred candidate would have to be reviewed again at adoption. Record that reasoning in the specification, ADR, or issue that owns the future work. Removing a component removes its row in the same change.

## Verdict

Return exactly these headings with evidence:

```text
Docs: pass|n/a|fail
<files checked and required actions>

Changelog: pass|n/a|fail
<the surface reached and the entry added, or why the change is invisible from outside>

Licenses: pass|n/a|fail
<official evidence, THIRD_PARTY_LICENSES.md entries, and required actions>
```

Any `fail` blocks completion.
