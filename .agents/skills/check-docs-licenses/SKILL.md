---
name: check-docs-licenses
description: Use when a change is nearing completion or affects behavior, configuration, operations, documentation, dependencies, tools, services, protocol APIs, container images, models, generated assets, or copied code.
license: Apache-2.0
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/Krzysztof318/MailFathom
---

# Check Documentation And Licenses

This is a mandatory completion gate. Inspect the complete diff and applicable repository instructions.

Running the gate is mandatory; finding something to change is not. `n/a` and `pass` are complete, successful verdicts, and a gate that reports `n/a` with evidence has done its whole job. Never register a dependency, expand documentation, or widen the diff so the gate looks exercised.

## Documentation

Confirm that durable documentation describes implemented behavior:

- update affected commands, examples, configuration, failure modes, security assumptions, and operations;
- keep future intent on the issue that owns it and actual behavior in `docs/`;
- treat stale guidance as a failure;
- do not create or modify an ADR without explicit owner approval.

Start from `scripts/review-obligations.sh`, whose documentation section names every page whose `describes:` marker covers a path this change touched and whether the change touched the page. That answers which pages are candidates without a search over `docs/`, so the reading left to do is the part only a reader can do: whether the page still says something true about the part that moved.

It narrows the search and never settles the verdict. A page it lists may owe nothing, and a page it does not list may still be wrong — a marker is a declaration, so a page that was never given one, or given one too narrow, is invisible to it. When that happens, widening the marker belongs in this change, because a documentation gate that trusts a declaration is only as good as the declaration.

Use `n/a` only when the change cannot affect user, operator, contributor, architectural, or security guidance.

## Changelog

**Do not touch `CHANGELOG.md`.** It is written by the release pull request and by nothing else, because a changelog is a statement about a release rather than about a change, and that pull request is the one whose merge commit is tagged and published. `$prepare-release` composes each section from the work merged since the previous tag; ordinary work leaves the file alone, and `CHANGELOG.md` is a protected path so an edit to it is visible as the exception it is.

The verdict below is therefore `n/a` for every ordinary change, and `fail` for a diff that edits the file outside a release.

## The project's own license

MailFathom is licensed under the Apache License, Version 2.0. That decision is recorded in five places and each one has a single job, so a change that touches licensing keeps them consistent rather than picking whichever is nearest.

- The root `LICENSE` is the unmodified official Apache-2.0 text. Never edit it, never append attribution, third-party terms, or commentary to it: GitHub detects the license by matching that file against the known text, and an edit turns a detected `Apache-2.0` into `NOASSERTION`.
- The root `NOTICE` names Krzysztof Kasprowicz as the original author, which is what Apache-2.0 section 4(d) asks a derivative distribution to preserve, and the repository the project lives in, for the reason the file header below carries the same URL: `NOTICE` travels beside the binaries into the publish output and the container image, so it is often the only thing a reader has to work back from. It stays informational — it adds no use restriction, changes no license term, and claims nothing about contributions by other copyright holders. Third-party notice text may join the distributed bundle beside it, never inside it.
- `backend/Directory.Build.props` carries the machine-readable form: `PackageLicenseExpression`, the copyright, and the SPDX assembly metadata that ships in the assemblies. `backend/src/Host` copies `LICENSE` and `NOTICE` into its publish output, and `VerifyPublishedLicenseAndNotice` in `backend/src/Host/Host.csproj` fails the publish when either is missing, so a native artifact cannot ship without them.
- `.editorconfig` holds the three-line `file_header_template`: the copyright, the grant, and the repository URL that tells a reader who meets one file outside this checkout where the project lives. IDE0073 enforces it on every C# source file and reaches nothing else, so the same three lines are written by hand into every other kind of file the repository writes, each in the form its own readers parse — root `AGENTS.md` § *Documentation and test obligations* holds the whole list, and both stacks are in it: `# ` lines on YAML, a `.toml` manifest, a Quadlet unit source, and under a `.sh` shebang; a `{{- /* ... */ -}}` comment in a Helm template so the header stays out of the rendered manifest; `license` and a `metadata` block in a `SKILL.md` frontmatter; `// ` lines opening a `.js`, `.mjs`, `.cjs`, `.ts`, `.tsx`, or `.rs` module; one `/* ... */` block opening a `.css` file; and one `<!-- ... -->` comment opening an `.html` document. A `.json` file carries none, and neither does a generated lock file. `scripts/test-agent-workflow.sh` compares every one of those against this template, which is what keeps them one header rather than seven that merely resemble each other. Changing the template therefore rewrites the whole repository twice over — one whole-solution `dotnet format` pass for the C# files and a hand pass over the rest — so treat it as a repository-wide change rather than a formatting tweak, and never hand-edit a C# header.
- The deployment assets state the same identifier where their own ecosystems read it: `org.opencontainers.image.licenses` in `deploy/docker/Dockerfile` and `artifacthub.io/license` in the chart's `Chart.yaml`. Those are claims about terms rather than the terms themselves, and nothing asserts either one mechanically, so a change touching either is read: check that both still name `Apache-2.0` and that the build context still admits `LICENSE` and `NOTICE`. A label that outlived the files it names is the failure worth catching, and the publish check above is what catches the files' half of it.

`README.md` and `CONTRIBUTING.md` carry none of those jobs and are still written to match them. Each explains the decision to a reader rather than recording it — that contributions arrive under the license by section 5 and that a contributor also accepts `CLA.md` once, that contributors keep their copyright either way, that the header the `.editorconfig` template applies is one project's mark rather than a claim about who wrote a line, and that sections 7 and 8 give the software with no warranty and no contributor liability. That last one is there because `README.md` is the only one of these files rendered outside the repository, where a reader who never opens `LICENSE` would otherwise meet the grant without the disclaimer; it summarizes and points at the text, and never restates a term as though the summary were operative. A change to the five above therefore reads both, because prose that contradicts the record is what a contributor will act on.

`THIRD_PARTY_LICENSES.md` is none of the above. It reviews what MailFathom consumes, and the section below governs it.

## Licenses

MailFathom must remain compatible with both commercial closed-source distribution and open-source publication. Only use third-party components whose licenses permit commercial use and do not require MailFathom itself to be relicensed or distributed as source code; prefer permissive licenses such as MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, and the PostgreSQL License. Do not introduce GPL, AGPL, SSPL, BUSL, Commons Clause, PolyForm Noncommercial, source-available-only, field-of-use-restricted, or otherwise non-permissive dependencies without explicit owner approval.

Inventory added, upgraded, replaced, bundled, or newly used third-party packages, tools, services, provider APIs, protocols, container images, models, generated assets, and copied samples.

For every affected item, verify current official upstream evidence for:

- exact name and version, license expression, commercial and redistribution suitability;
- upstream URL and required attribution or `NOTICE` handling;
- compatibility with the runtime that resolves it — .NET for a NuGet package, the Node version `frontend/package.json` requires in `engines` for an npm one, and the Rust edition `frontend/src-tauri/Cargo.toml` declares for a crate;
- separate service, model, telemetry, data-use, trademark, and data-processing terms when applicable.

Ensure `THIRD_PARTY_LICENSES.md` is updated in the same change. A permissive SDK does not approve its hosted service. Unknown, conflicting, or unofficial evidence is a failure. Use `n/a` only when the inventory is empty.

When a dependency is pinned in `backend/Directory.Packages.props`, record the exact package name, version, license expression, upstream URL, and any required attribution or `NOTICE` handling. Record the version the artifact's own graph resolves when nearest-wins resolution raises a pin that is only a floor.

The client stack pins in three more files and costs the register one thing the service's does not. `frontend/package.json` and `frontend/src/*/package.json` hold the npm pins that `frontend/pnpm-lock.yaml` resolves, and `frontend/src-tauri/Cargo.toml` holds the crate pins that `frontend/src-tauri/Cargo.lock` resolves. Record each direct pin under its **package identifier** rather than its project's own capitalisation — `react-dom` and `vitest`, never *ReactDOM* or *Vitest* — because that is what a manifest, a registry, and `scripts/update-dependencies.sh` all read. Then read what moved behind it: `THIRD_PARTY_LICENSES.md` § *The client's two dependency closures* records each of those two graphs as a census, and a census is a count of a closure that a changed pin re-resolves. A client dependency change is not complete until that section's enumeration commands have been re-run and the section agrees with what they printed. Nothing recomputes it, and no gate fails on a stale one.

An npm pin costs one thing further, and it is the one part of the register's client rows that travels to a user rather than staying in the repository. `frontend/src/Client.App/public/THIRD-PARTY-NOTICES.txt` reproduces the licence text of the packages the bundle actually redistributes, named and versioned, and `pnpm build` copies it verbatim into the output that every published image and every desktop package carries. So a moved npm pin is read against that file as well as against the register: the version it names, whether the package still reaches the bundle at all, and whether a package that newly does is missing from it. `pnpm --dir frontend licenses list --prod` is what says which packages those are, and it is the same command the register's own rows cite. The desktop shell's crates are outside this — they are a separate component that reaches no bundle, so a `Cargo.toml` bump owes the register row and the census and nothing here. `scripts/review-obligations.sh` reports the pair, and reporting it is not confirming it: the file is opened and read.

`THIRD_PARTY_LICENSES.md` records what the repository actually pins, bundles, or calls, and is not the project's own `LICENSE` or a generated notice bundle. Put an entry in the section that matches the component's exposure — redistributed, called on the machine an artifact runs on, build-time, test-only, orchestration, continuous integration, developer tooling, externally sourced source, or hosted service — because that is what decides whether a release obligation follows.

Register only what the change actually introduces. A component it mentions, plans, evaluates, or rejects gets no row anywhere in the file, in any wording: a row asserts a completed review of software in use, and a rejected or deferred candidate would have to be reviewed again at adoption. Record that reasoning in the ADR or issue that owns the future work. Removing a component removes its row in the same change.

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
