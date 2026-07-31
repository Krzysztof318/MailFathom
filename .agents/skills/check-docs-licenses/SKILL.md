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

## Licenses

Inventory added, upgraded, replaced, bundled, or newly used third-party packages, tools, services, provider APIs, protocols, container images, models, generated assets, and copied samples.

For every affected item, verify current official upstream evidence for:

- exact name and version, license expression, commercial and redistribution suitability;
- upstream URL and required attribution or `NOTICE` handling;
- .NET compatibility when applicable;
- separate service, model, telemetry, data-use, trademark, and data-processing terms when applicable.

Ensure `THIRD_PARTY_LICENSES.md` is updated in the same change. A permissive SDK does not approve its hosted service. Unknown, conflicting, or unofficial evidence is a failure. Use `n/a` only when the inventory is empty.

`THIRD_PARTY_LICENSES.md` records what the repository actually pins, bundles, or calls, and is not the project's own `LICENSE` or a generated notice bundle. Put an entry in the section that matches the component's exposure — redistributed, build-time, test-only, orchestration, continuous integration, developer tooling, externally sourced source, or hosted service — because that is what decides whether a release obligation follows. A component the change only mentions, plans, or might adopt later does not belong in the register at all, because a row implies a review that a future adoption would have to redo anyway. The `Components considered and not adopted` section holds those, and states that it carries no review; do not add to it for a dependency the change does not introduce.

## Verdict

Return exactly these headings with evidence:

```text
Docs: pass|n/a|fail
<files checked and required actions>

Licenses: pass|n/a|fail
<official evidence, THIRD_PARTY_LICENSES.md entries, and required actions>
```

Any `fail` blocks completion.
