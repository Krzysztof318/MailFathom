# Documentation Instructions

These instructions apply under `docs/` in addition to the repository root instructions.

- Keep durable documentation under `docs/`. It is written in English, as everything in this repository is; root `AGENTS.md` states that rule once and it needs no restating here.
- Documentation describes verified implemented behavior, not intended implementation. Keep future intent on the issue that owns it.
- Document architecture, feature behavior, configuration, security assumptions, operational procedures, failure modes, and important implementation trade-offs when introduced or changed.
- Keep documentation discoverable under `architecture/`, `features/`, `operations/`, and `decisions/`; add an index when more than a few pages exist.
- `users/` is the audience-facing guide layer for people who install, configure, and use MailFathom. Its pages guide and link into the sections above for every contract; a limit, default, or rule stated in full belongs on the owning reference page, never duplicated in a guide where it would go stale silently.
- These pages are published as a site, so a new page joins the `toc.yml` of its section in the same change: a page in no table of contents is published and unreachable. `scripts/test-agent-workflow.sh` fails both halves of that — a page no table of contents lists, and an entry naming no page — and `operations/documentation-site.md` records what the site carries and which four kinds of file it deliberately leaves out.
- That entry carries a `description:` beside its `name:` and `href:`, in one sentence saying what the page answers rather than what it is about. The site is published a second time as artifacts an AI agent reads, and the map among them is written from these files — so the sentence is the whole of what an agent has to decide whether this is the page to fetch. Both the build and the contract suite fail a published page whose entry carries none, and `operations/documentation-site.md` § *What an agent reads* records what is written and what is deliberately left out.
- A link to another published page stays relative, and a link to anything the site does not carry — an ADR, the architecture draft under `specs/`, a deployment asset, a source file — is written as an absolute `https://github.com/Krzysztof318/MailFathom` URL. Both forms then work in both renderings, where a relative link out of the published set resolves on GitHub and reaches a 404 on the site. `scripts/build-docs-site.sh` fails on a link that resolves to nothing, and the same rule reaches a `<see href>` in a C# documentation comment, where a relative path resolves to nothing in an editor either.
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

<!-- describes: backend/src/Mcp/**, backend/src/Host/Security/**, backend/src/Infrastructure/Security/** -->
```

`Fathom review` reads those markers to tell a pull request which pages its change obliges. Nothing derives that mapping: documentation is written about configuration keys and behavior rather than about type names, so no search over the source finds the page that documents it. The declaration lives in the page for the same reason it is not a central index — it sits in the file somebody is editing, it conflicts with nothing, and both ways it can rot are checked rather than trusted.

- Exactly one marker, in the first fifteen lines. Below that is where a page *quoting* the syntax puts its example, and this file is one of them.
- Name what makes the page stale, not everything it mentions. `docs/architecture/solution-structure.md` describes the project files and the solution rather than `backend/src/**`, because a new project changes it and a new method does not.
- `*` stops at a directory separator and `**` crosses one, which is what separates `backend/src/*/*.csproj` from `backend/src/**`. Between two slashes `**` also matches *no* directory at all, so `backend/src/**/*Options.cs` covers `backend/src/FooOptions.cs` as well as `backend/src/Host/Configuration/McpOptions.cs`; a leading `**/` reaches the repository root the same way. These are git's own `:(glob)` rules, and `scripts/test-agent-workflow.sh` resolves every marker through git, so a pattern means here exactly what `git ls-files -- ':(glob)<pattern>'` says it means. Separate patterns with commas.
- `describes: none` for a page that documents no part of the repository. A `README`, an `AGENTS.md`, and the ADR templates need no marker at all.
- An ADR carries one too. It records a decision the code implements, so the code moving away from it is exactly what a reader needs to be told.

## Pages whose steps happen in somebody else's product

Some pages walk a reader through screens this project does not own — a cloud console, an identity provider's tenant, a service's API-keys page. Those products change their own navigation and their own requirements whenever they like, and nothing here can notice the day they do: no script reads them and no build fails over them. The reader meets that as a step naming a button that is no longer there, with no way to tell a stale page from their own mistake — so they retrace the page instead of going to the product's documentation, which is the worse of the two failures. A page that rests on somebody else's product says so before its first instruction.

The device is a GitHub-flavored alert, which renders as an alert in both places these pages are read: GitHub's own view, and the site, where docfx renders it through Markdig. Nothing about it needs raw HTML or a stylesheet of its own. The wording is fixed:

<!-- third-party-notice -->
```markdown
> [!WARNING]
> Some of the steps on this page are performed in a product this project does not control. Any screen, menu, or field
> named here can be renamed or moved there at any time. Where this page and that product's own documentation disagree,
> the product's documentation is right.
```

`scripts/test-agent-workflow.sh` reads that wording out of the block above rather than holding a second copy, the same arrangement the licensing header has with `.editorconfig`: the sentence is one decision recorded in one place, and a page wording it differently fails rather than quietly carrying its own variant.

- **It goes directly under the `describes:` marker**, above the page's first paragraph, and a page carries exactly one. The marker has to stay within the first fifteen lines, so the notice goes under it rather than above it. Both halves are checked — a second copy, and a copy anywhere but there.
- **It is never adapted to the page.** A notice written per page is text to keep accurate on every page carrying it, which is the cost this notice exists to reduce rather than add to. The page's subject is already in its title, so the notice names no vendor; it carries no date and no version, because neither answers the reader's question, which is *is this still true* rather than *when was it written*.
- **Which pages take it is a judgement about the page's subject**, not about whether a vendor is mentioned somewhere on it, which is why no script decides it and this rule carries that half. A page takes the notice when following it means working in somebody else's product: `users/mailbox-providers.md`, `operations/mailbox-oauth.md`, `operations/provider-endpoints.md`, and `operations/mcp-client-oauth.md` do, and a new page describing a third-party setup takes one without anybody remembering to ask. `operations/mcp-endpoint.md` deliberately does not: its subject is MailFathom's own endpoint and its own configuration, a notice at the top would be wrong about most of the page, and the one part of it describing a third-party connector is a section rather than the page.
- **The page says it once, in the notice.** Nothing else on the page is rewritten to accommodate it, and no page gains a second statement of it in its own prose. Prose already saying it — that a console may have been rearranged since, that the vendor's own documentation is the authority — is the per-page version this replaces, so it goes when the notice arrives rather than sitting under it saying the same thing again.
