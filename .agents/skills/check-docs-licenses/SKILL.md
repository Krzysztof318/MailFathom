---
name: check-docs-licenses
description: Use when a change is nearing completion or affects behavior, configuration, operations, documentation, dependencies, tools, services, protocol APIs, container images, models, generated assets, or copied code.
---

# Check Documentation And Licenses

This is a mandatory completion gate. Inspect the complete diff and applicable repository instructions.

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

Ensure `LICENSES.md` is updated in the same change. A permissive SDK does not approve its hosted service. Unknown, conflicting, or unofficial evidence is a failure. Use `n/a` only when the inventory is empty.

## Verdict

Return exactly these headings with evidence:

```text
Docs: pass|n/a|fail
<files checked and required actions>

Licenses: pass|n/a|fail
<official evidence, LICENSES.md entries, and required actions>
```

Any `fail` blocks completion.
