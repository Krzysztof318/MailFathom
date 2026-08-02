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

Start from `scripts/review-obligations.sh`, whose documentation section names every page whose `describes:` marker covers a path this change touched and whether the change touched the page. That answers which pages are candidates without a search over `docs/`, so the reading left to do is the part only a reader can do: whether the page still says something true about the part that moved.

It narrows the search and never settles the verdict. A page it lists may owe nothing, and a page it does not list may still be wrong — a marker is a declaration, so a page that was never given one, or given one too narrow, is invisible to it. When that happens, widening the marker belongs in this change, because a documentation gate that trusts a declaration is only as good as the declaration.

Use `n/a` only when the change cannot affect user, operator, contributor, architectural, or security guidance.

## Changelog

**Do not touch `CHANGELOG.md`.** It is written by the release pull request and by nothing else, because a changelog is a statement about a release rather than about a change, and that pull request is the one whose merge commit is tagged and published. `$prepare-release` composes each section from the work merged since the previous tag; ordinary work leaves the file alone, and `CHANGELOG.md` is a protected path so an edit to it is visible as the exception it is.

The verdict below is therefore `n/a` for every ordinary change, and `fail` for a diff that edits the file outside a release.

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

Changelog: n/a|fail
<n/a for ordinary work, which never touches the file; fail when the diff edits it outside a release>

Licenses: pass|n/a|fail
<official evidence, THIRD_PARTY_LICENSES.md entries, and required actions>
```

Any `fail` blocks completion.
