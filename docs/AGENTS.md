# Documentation Instructions

These instructions apply under `docs/` in addition to the repository root instructions.

- Write repository documentation in English and keep durable documentation under `docs/`.
- Documentation describes verified implemented behavior, not intended implementation. Keep future intent in specifications.
- Document architecture, feature behavior, configuration, security assumptions, operational procedures, failure modes, and important implementation trade-offs when introduced or changed.
- Keep documentation discoverable under `architecture/`, `features/`, `operations/`, and `decisions/`; add an index when more than a few pages exist.
- `users/` is the audience-facing guide layer for people who install, configure, and use MailFathom. Its pages guide and link into the sections above for every contract; a limit, default, or rule stated in full belongs on the owning reference page, never duplicated in a guide where it would go stale silently.
- Create or modify ADRs under `decisions/` only with explicit owner approval.
- An ADR whose `status` is `accepted` is closed, and that approval does not reopen it: the text is never corrected, extended, or brought up to date. Replace the decision with a new ADR and mark the old one `superseded` with a pointer to it. The only edits an accepted ADR takes are that transition and its `describes:` marker, which states where the code lives rather than anything about the decision. An ADR still at `proposed` is editable, which is what that status is for. `docs/decisions/` is a protected path in `.github/workflows/protected-paths.yml`, so a pull request from any other author that touches one — a record, a template, or the index — is refused within seconds of the push.
- Update examples, configuration snippets, command names, and diagrams with their corresponding behavior.
- Check whether `AGENTS.md` files need updates when workflows, structure, tooling, or documentation rules change.
- Explain purpose, contracts, invariants, data flow, operational impact, and reasons for decisions. Do not merely repeat type names or folder structure.
- Every page states the part of the repository it describes, in the marker below. `scripts/test-agent-workflow.sh` fails a page that carries none and a marker that names nothing, so a new page without one does not merge.

## What a page describes

A page under `docs/` opens with one marker naming the paths it is written about:

```markdown
# The MCP endpoint and what protects it

<!-- describes: src/Mcp/**, src/Host/Security/**, src/Infrastructure/Security/** -->
```

`Fathom review` reads those markers to tell a pull request which pages its change obliges. Nothing derives that mapping: documentation is written about configuration keys and behavior rather than about type names, so no search over the source finds the page that documents it. The declaration lives in the page for the same reason it is not a central index — it sits in the file somebody is editing, it conflicts with nothing, and both ways it can rot are checked rather than trusted.

- Exactly one marker, in the first fifteen lines. Below that is where a page *quoting* the syntax puts its example, and this file is one of them.
- Name what makes the page stale, not everything it mentions. `docs/architecture/solution-structure.md` describes the project files and the solution rather than `src/**`, because a new project changes it and a new method does not.
- `*` stops at a directory separator and `**` crosses one, which is what separates `src/*/*.csproj` from `src/**`. Between two slashes `**` also matches *no* directory at all, so `src/**/*Options.cs` covers `src/FooOptions.cs` as well as `src/Host/Configuration/McpOptions.cs`; a leading `**/` reaches the repository root the same way. These are git's own `:(glob)` rules, and `scripts/test-agent-workflow.sh` resolves every marker through git, so a pattern means here exactly what `git ls-files -- ':(glob)<pattern>'` says it means. Separate patterns with commas.
- `describes: none` for a page that documents no part of the repository. A `README`, an `AGENTS.md`, and the ADR templates need no marker at all.
- An ADR carries one too. It records a decision the code implements, so the code moving away from it is exactly what a reader needs to be told.
