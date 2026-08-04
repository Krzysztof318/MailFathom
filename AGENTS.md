# MailFathom Development Instructions

These instructions apply to the entire repository.

The product and solution name is `MailFathom`. The solution file is `MailFathom.slnx`; project directory and file names use short boundary names such as `Domain`, `Application`, and `Host`, while `Directory.Build.props` applies the `MailFathom.*` prefix to assembly names and root namespaces.
## Project status

First-phase development is over and `0.1.0` has shipped. The tag is pushed, the image and the Helm chart are published, and the GitHub release carries the schema artifact that brings a database to it; #210 owned those acts and is closed. A file may therefore say so, and every file describes something a reader can install rather than something being prepared for them. What follows is how a change against it is judged.

- A breaking change to a configuration key, a database schema, an MCP tool contract, or a public API is no longer free. Treat all four public surfaces as consumed and argue the break rather than assuming it. Under `0.x`, ADR 0004 permits one in a minor version, but permitted is not costless, and "nobody depends on this yet" has stopped being the reason.
- The schema is frozen the way the freeze was always going to work: a change appends a migration and never regenerates a baseline.
- Documentation, deployment assets, and the third-party register describe a product somebody is running, so an inaccuracy in one is a defect against a user rather than a note to fix before release.
- Still refused, and unchanged by any of the above: compatibility shims, deprecation machinery, versioning scaffolding, and migration paths for versions that never existed. Owning the current contract is not the same as inventing history behind it.

None of this relaxes the rest of these instructions.

## Where the rest of the contract lives

This file is loaded into every agent session, so it holds what has to be true before a file is read: the non-negotiables, the boundaries, and the obligations that govern every change. Every other rule lives in the one place it is acted on, and no rule is stated twice. A row below is a pointer to the whole rule, never a summary of one.

| Where | Read when | What it holds |
|---|---|---|
| `src/AGENTS.md` | A change under `src/` | The .NET and C# conventions, API and failure design, asynchronous return types, dependency injection and configuration |
| `tests/AGENTS.md` | A change under `tests/` | Unit-test policy, coverage, and what belongs in the integration suite. It points at the C# conventions, which govern test code too |
| `src/Infrastructure/AGENTS.md` | A change under `src/Infrastructure/` | Persistence and EF Core rules, and email-protocol safety |
| `docs/AGENTS.md` | A change under `docs/` | Documentation rules and the `describes:` marker every page carries |
| `docs/operations/issue-tracking.md` | `$start-task` opens or places an issue, `$finish-change` links one | Which work needs an issue, what its body contains, the one `type:*` label, the milestone, the board's `Track`, `Queue`, and `Size` fields and its views, and how an arrival from outside the project is triaged |
| `docs/operations/agent-workflow.md` | Any of the workflow skills runs | The verification scripts at length, what each skill does, and how `Fathom review` behaves on a pull request |
| `docs/operations/local-development.md` | The SDK, the database, packages, or the deployment assets are involved | The environment, the EF Core command split, the pull-request checks, package sources, and lock files |
| `$check-docs-licenses` | Every change, as the mandatory completion gate | MailFathom's own Apache-2.0 record across its five files, and the third-party licensing rules and register |
| `$add-migration` | A model change needs a migration | The additive migration workflow and the SQL review that is part of it |

## Critical repository rules

- Never commit directly on `main` or `master`. Branch before committing; in the owner's checkout that branch is named `agent/<short-description>`.
- Preserve unrelated user changes. Stage only files that belong to the current task.
- Make architectural decisions before implementation. Keep changes small, reviewable, and aligned with the architecture draft in `specs/`.
- Treat ADRs under `docs/decisions/` as required architectural context for AI agents. Before changing architecture, boundaries, configuration, persistence, provider integration, governance, security-sensitive behavior, or cross-cutting infrastructure, read the relevant ADRs and keep the change consistent with their current status and rationale.
- Treat MailFathom as an enterprise-grade system even during early scaffolding: preserve seams for governance, auditability, privacy controls, operational hardening, compliance evidence, and future Agent Governance Toolkit (AGT) adoption without prematurely adding runtime dependencies.

## The two roles this contract is written for

The repository is public and its roadmap board is not, so these rules run in two places and a few of them belong to only one. `$start-task` resolves which applies, from the workspace rather than from a question, and states it in the brief.

**The owner's checkout** is where `origin` is `Krzysztof318/MailFathom` and project `4` answers. Everything here applies, including the rules that exist nowhere else: the `agent/<short-description>` branch name, the linked worktree, the `type:*` label and the milestone on an issue, the board's `Track`, `Queue`, and `Size` fields, and pushing to this repository.

**The fork role** is an ordinary clone of a fork, on a branch the contributor named, with `Krzysztof318/MailFathom` configured as a second remote so every gate resolves the base it will actually merge into. Each rule above is absent there rather than relaxed: an issue from outside the project carries no label and no board fields by design, triage supplies them, and attempting a board write returns a permission error rather than a partial result. The protected paths — `.github/`, `.config/`, `.agents/`, `.claude/`, `docs/decisions/`, the named root files, and any `AGENTS.md`, `CLAUDE.md`, `.editorconfig`, `.gitattributes`, or `.worktreeinclude` at any depth — are refused from any other author, which is why `$start-task` checks a task against them before the work rather than after the check does.

Everything else is one contract. The architecture boundaries, the conventions, the privacy and licensing obligations, the tests, the documentation, and both verification scripts hold identically, and so do `$review-change`, `$check-docs-licenses`, `$closed-enumeration`, and `$add-migration`, none of which needs authority over this repository at all. `docs/operations/agent-workflow.md` records the split; `CONTRIBUTING.md` states the contributor's half of it.

## Documentation and test obligations

- Before using or changing a library, framework, protocol, CLI, or external API, consult its latest official documentation. Prefer Microsoft Learn, official project documentation, specifications, and upstream repositories.
- Confirm .NET 10 compatibility and pin package versions centrally in `Directory.Packages.props`. Do not use floating versions.
- Regenerate the lock files in the same change that moves a pin. `Directory.Packages.props` fixes the direct versions and the committed `packages.lock.json` files fix the transitive closure those versions resolve to, so the two are one decision recorded in two places. `AppHost` and `IntegrationTests` deliberately carry none, because the Aspire SDK picks part of their graph from the host platform's runtime identifier; do not add one back. Restore runs in locked mode everywhere it is gated, which fails with `NU1004` rather than quietly rewriting the closure; `dotnet restore MailFathom.slnx --force-evaluate` is what updates it. Review the resulting transitive diff, because that is the part central pinning never showed.
- Unit tests are part of every behavior change, feature, and bug fix. Read `tests/AGENTS.md` before adding or changing tests.
- Develop and verify tests and production code before documenting the implemented behavior. Update affected durable documentation in the same reviewable change set; stale guidance is a defect.
- Read `docs/AGENTS.md` before changing documentation. It holds the rule that an accepted ADR is closed and is replaced rather than edited, which is why an ADR needs the owner's explicit approval and why `docs/decisions/` is a protected path.
- Every link in the repository-root `README.md` is absolute, including the ones that point back into this repository and the two that point at its own headings. That file is the one piece of documentation rendered outside the repository — a container registry description, a chart listing, a package page, anything that copies the Markdown — and a relative path resolves against the wrong root everywhere except GitHub's own blob view, so it arrives as a broken link rather than as a link to somewhere else. A page the documentation site publishes links to the site, at the version-agnostic address `https://krzysztof318.github.io/MailFathom/<path without docs/>.html`, because a reader arriving from a registry description wants the readable form rather than a Markdown file in a tree; the heading anchor carries over unchanged, since docfx and GitHub derive the same slug from a heading. Everything else links into the repository: a file as `https://github.com/Krzysztof318/MailFathom/blob/main/<path>`, a directory as `.../tree/main/<path>`, an image at its `raw.githubusercontent.com` URL, and a heading on the README itself as `https://github.com/Krzysztof318/MailFathom#<anchor>`. `docs/README.md` and the architectural decision records are in that second group deliberately — the site publishes neither, and `docs/operations/documentation-site.md` says why. `scripts/test-agent-workflow.sh` fails a site link naming a page that does not exist and a repository link to a page the site does publish, so the two groups cannot quietly swap. This applies to the root `README.md` alone: pages under `docs/` are read inside the repository and keep relative links, which is what lets a directory be moved without rewriting every reference into it.
- Never edit `CHANGELOG.md` during ordinary work. It states what a release shipped, so the release pull request writes it and nothing else does; `$check-docs-licenses` holds the reasoning and reports `n/a` for the file on every ordinary change.
- Write the licensing header by hand in every file that is not C#. IDE0073 reads C# only, so `dotnet format` and `scripts/verify-fast.sh` apply `.editorconfig`'s `file_header_template` to `.cs` files and to nothing else — a new workflow, script, Helm template, skill, or documentation-site asset gets no header from either, and neither says so. A new file carries the same three lines in the form its own readers parse: `# ` comment lines opening a `.yml` or `.yaml` file, the same comment lines under the shebang of a `.sh` file, a `{{- /* ... */ -}}` comment in a Helm template so the header stays out of the rendered manifest, `license` plus a `metadata` block naming the author and the repository in a `SKILL.md` frontmatter, `// ` lines opening a `.js` module, and one `/* ... */` block opening a `.css` file, which has no line comment to use instead. `scripts/test-agent-workflow.sh` fails when one is missing and reads the expected text out of `.editorconfig`, so the header is one decision recorded in one place and applied in six forms.
- `$check-docs-licenses` is a mandatory completion gate, including when its verdict is `n/a`.

## Architecture

- Build a clean-architecture modular monolith with clear `Domain`, `Application`, `Infrastructure`, `AI`, `Mcp`, `Host`, and `Cli` boundaries.
- `Domain` contains business concepts and invariants and has no dependency on infrastructure frameworks.
- `Application` contains use cases and ports and depends only on `Domain`.
- `Infrastructure` implements persistence, IMAP/SMTP, message-content storage, security, and observability ports.
- `AI` owns retrieval, chunking, embeddings, and agent-framework composition without leaking provider-specific types into `Application` or `Domain`.
- `Mcp` maps protocol inputs and outputs to application use cases. It contains no persistence or email-protocol logic.
- `Host` is a composition root only: configuration, dependency injection, middleware, endpoints, workers, and process lifetime.
- Prefer direct, explicit dependencies and small interfaces at architectural boundaries. Do not introduce abstractions without a current testing, protocol, or replacement need.
- Keep a directory small enough to be one thing. Around fifteen files is where a listing stops reading as a group and starts reading as a list, so treat that as the point to ask whether the directory still holds one thing rather than several — and when it holds several, give each a subdirectory that names it. The count is a proxy for cohesion and nothing more: no script, no verification gate, and no review check enforces it, and a directory whose files genuinely are one concept stays flat past the number rather than being split into four two-file directories to satisfy it. `src/Domain/Emails` is the worked example of ignoring it correctly, and `src/Infrastructure/Persistence/{Entities,Connections,Sessions,Emails,Synchronization}` of applying it. Reasons to look: an agent reads a listing before it reads a file, so a forty-name listing costs the same context on every task that touches the project, and it is the point at which a new type gets dropped next to the nearest-sounding name instead of into the group it belongs to.
- The rule above is about a directory that holds a structure. `specs/`, `docs/operations/`, `scripts/`, and `.github/workflows/` are flat by design and stay flat at any size, because each of their entries is an independent document or tool that nothing else in the directory composes with. A test directory instead follows the structure of the code it covers, so `tests/Host.UnitTests/Configuration/Endpoints/` mirrors `src/Host/Configuration/Endpoints/`; the doubles a project's tests share are not covering anything and belong in its own `TestDoubles/`.
- Keep email retrieval read-only. Synchronization and content retrieval must never set the remote IMAP `\Seen` flag.

## Enterprise governance, privacy, and GDPR readiness

- Design every feature with GDPR-aligned privacy by design and by default: data minimization, purpose limitation, storage limitation, confidentiality, integrity, availability, and accountable processing must be visible in the architecture, tests, and documentation.
- Email content, metadata, embeddings, retrieval snippets, audit events, and model/tool traces can contain personal data. Classify them as sensitive by default and avoid broad access, unnecessary copying, long retention, or unredacted logging.
- Keep explicit seams for future data-subject workflows such as access, export, rectification support, erasure, restriction of processing, retention holds, and audit evidence, even when those workflows are not implemented in the first release.
- Do not treat embeddings or derived indexes as anonymous. They inherit the classification, retention, access-control, deletion, and export constraints of the source mail content unless a reviewed privacy design proves otherwise.
- Future AGT adoption must remain an adapter-level governance concern. AGT policy decisions, audit records, and tool-call controls must not leak provider-specific or governance-framework types into `Domain` or `Application`.
- Before adding AGT or any governance/compliance package, verify the current official documentation, .NET 10 compatibility, license, service terms, telemetry behavior, and data-processing implications; update `THIRD_PARTY_LICENSES.md` for any dependency or externally sourced component.

## Reliability, security, and performance

- Apply timeouts to external I/O and distinguish caller cancellation, service shutdown, timeout, authentication failure, and transient transport failure.
- Retry only operations known to be transient and safe to repeat. Use bounded attempts with jittered backoff; never create nested retry storms.
- Make worker leases and state transitions durable where a process crash could otherwise duplicate or lose work.
- Prefer bounded channels or queues for background pipelines. Record queue depth, processing duration, failures, and retry counts without recording message content.
- Use cryptographically secure random generation for tokens and security-sensitive identifiers. Compare secrets using appropriate constant-time APIs where relevant.
- Encode and validate data for its destination context. Treat email HTML, headers, filenames, URLs, tool arguments, and model output as untrusted input.
- Use least privilege for PostgreSQL, OAuth scopes, filesystem access, certificates, and operating-system service accounts.
- Optimize only after measurement. Prefer appropriate algorithms, bounded allocations, streaming, and database projections before low-level micro-optimizations.
- Stream large MIME content and attachments rather than buffering them repeatedly. Set explicit size and count limits at every public or remote boundary.

## Cross-boundary email invariants

- Treat `(account, folder, UIDVALIDITY, UID)` as the stable remote occurrence identity.
- Keep MCP reads local; an MCP request must not trigger a synchronous IMAP fetch.
- Make synchronization, object writes, indexing, and SMTP outbox processing idempotent.

## Dependency and implementation discipline

- Keep third-party types inside their owning adapter wherever practical.
- Prefer platform capabilities before adding packages. Every new package must have a clear owner and purpose.
- Take every package from the sources the repository's own `NuGet.config` declares. It clears the inherited source list so a feed configured on a developer machine cannot supply a dependency the license register never reviewed, and its package source mapping means a second source restores nothing until its packages are named explicitly. Adding a source is a licensing and supply-chain decision, so review it as one and record it in `THIRD_PARTY_LICENSES.md`.
- Do not expose EF Core entities, MailKit objects, MCP SDK types, or provider-specific AI types across application boundaries.
- Access raw RFC 822 content only through the application-owned `IEmailContentStore` port. Its initial implementation uses a dedicated PostgreSQL table, separate from email metadata.
- Keep PostgreSQL, Npgsql, and `bytea` details inside the initial content-store adapter so a future MinIO/S3 implementation does not change application use cases or domain types.
- Do not load raw MIME in ordinary mailbox queries or track large `bytea` values in EF Core unnecessarily.
- Apply database migrations explicitly. Do not run destructive or automatic production migrations during ordinary host startup.
- Use keyset pagination for email timelines and bounded result sizes for all public queries.
- Treat email content, OAuth tokens, credentials, certificate material, and embeddings as sensitive data.

## Agent workflow and verification

`docs/operations/agent-workflow.md` describes every script and skill named here at length, including what each one asserts and why. These are the entry points and the rules that hold before one is reached.

- For file-changing tasks, start with `$start-task`. Before final verification, use `$review-change`. To finish, use `$finish-change`; it requires `$check-docs-licenses`, full verification, focused staging, and a pull request.
- Use `scripts/inspect-workspace.sh` for a read-only workspace preflight, `scripts/verify-fast.sh` during implementation, and `scripts/verify-full.sh` before committing. Stage the task files first: the full gate rejects remaining untracked files, so a newly added file cannot bypass diff validation.
- Both verification scripts refuse to run on `main` or `master`. Check out the branch that carries the change instead of working around the refusal.
- The full gate fetches the base branch and rejects a branch that does not contain it. Rebase when that check fails, and treat an unreachable remote as a blocked gate: verification against a stale base proves nothing about the branch that will actually merge.
- Never invoke `dotnet format` by hand. Both of its modes already run where they belong — the fast loop repairs the changed files and names by file and line whatever has no code fix, and the full gate verifies the whole solution without touching it. Fix what the loop reported, then run `scripts/verify-fast.sh` again.
- Use `scripts/review-obligations.sh` to see what a change obliges elsewhere: the tests naming each changed type, the pages whose `describes:` marker covers each changed path, and the registers whose trigger moved. It is the same index `Fathom review` runs on a pull request, reached through an adapter rather than a second implementation, so a rule cannot hold in review and lapse in the pipeline. It reports and never gates, and nothing it prints is a finding until it is confirmed in the file it points at.
- Read the version with `scripts/read-declared-version.sh` rather than retyping it. `<VersionPrefix>` in `Directory.Build.props` is the only application version number in the repository, and everything that has to put that number somewhere gets it there at build or package time. ADR 0004 and `docs/operations/release-procedure.md` record the whole scheme.
- Never cut a release as part of a task. `$prepare-release` is manual-invocation only — its frontmatter sets `disable-model-invocation`, so an agent cannot reach it — because when a version becomes real is the owner's decision rather than something to infer from work that looks release-shaped. It pushes no tag either: tagging stays the owner's act as well.
- Never start `Fathom review`. It reviews a published pull request on its own and spends Claude subscription usage doing it; `$review-change` is the review an agent performs, and asking for another is the owner's act through a label or a comment. A comment line beginning with `fathom-review` or `@fathom-review` triggers a run only when the phrase leads the line, which is what makes writing it mid-sentence in prose about the workflow safe.
- Use `scripts/run-integration-tests.sh` only when the owner asks for it. The suite starts a PostgreSQL container, so it is deliberately absent from both verification scripts and from every pull-request workflow, and its GitHub workflow is manual dispatch only. `tests/AGENTS.md` states what belongs in it.
- Review a change under `deploy/` by reading it. No script here deploys anything, and `helm lint` and `helm template` against `deploy/helm/mailfathom/ci/*-values.yaml` are the useful local reading; nothing gates on them.
- Inspect the final diff for accidental secrets, unrelated edits, generated files, and dependency-boundary violations.
