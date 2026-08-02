# Documentation Instructions

These instructions apply under `docs/` in addition to the repository root instructions.

- Write repository documentation in English and keep durable documentation under `docs/`.
- Documentation describes verified implemented behavior, not intended implementation. Keep future intent in specifications.
- Document architecture, feature behavior, configuration, security assumptions, operational procedures, failure modes, and important implementation trade-offs when introduced or changed.
- Keep documentation discoverable under `architecture/`, `features/`, `operations/`, and `decisions/`; add an index when more than a few pages exist.
- `users/` is the audience-facing guide layer for people who install, configure, and use MailFathom. Its pages guide and link into the sections above for every contract; a limit, default, or rule stated in full belongs on the owning reference page, never duplicated in a guide where it would go stale silently.
- Create or modify ADRs under `decisions/` only with explicit owner approval. `docs/decisions/` is a protected path in `.github/workflows/protected-paths.yml`, so a pull request from any other author that touches one — a record, a template, or the index — is refused within seconds of the push.
- Update examples, configuration snippets, command names, and diagrams with their corresponding behavior.
- Check whether `AGENTS.md` files need updates when workflows, structure, tooling, or documentation rules change.
- Explain purpose, contracts, invariants, data flow, operational impact, and reasons for decisions. Do not merely repeat type names or folder structure.
