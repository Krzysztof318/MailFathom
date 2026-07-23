---
status: accepted
contact: repository owner
date: 2026-07-23
deciders: repository owner
consulted: architecture draft in specs/2026-07-22-mail-mcp-architecture-draft.md
informed: future MailMcp contributors
---

# Establish the initial MailMcp solution baseline

## Context and Problem Statement

MailMcp needs a reproducible .NET 10 starting point that matches the architecture draft and makes future feature slices reviewable. The repository needs project boundaries, central build and package configuration, test-project wiring, minimal runnable hosts, and durable documentation before behavior-specific implementation begins.

## Decision Drivers

- The solution must follow the clean-architecture modular monolith boundaries described in the architecture draft.
- Package versions, analyzer settings, and test-runner behavior must be centralized and deterministic.
- The first scaffold must avoid production behavior beyond minimal ASP.NET Core and Aspire host entry points.
- Future contributors need discoverable documentation, decision records, and license-review guidance.

## Considered Options

- Create the full MailMcp scaffold now.
- Keep only the architecture draft and defer project creation.
- Create a single project and split boundaries later.

## Decision Outcome

Chosen option: "Create the full MailMcp scaffold now", because it establishes enforceable project references, centralized configuration, and a documented baseline without implementing mail, persistence, retrieval, or MCP behavior prematurely.

### Consequences

- Good, because later feature work starts from explicit boundaries instead of inventing project structure ad hoc.
- Good, because package versions, analyzer settings, and Microsoft Testing Platform v2 configuration are visible at the repository root.
- Neutral, because scaffold-only unit-test projects temporarily contain no behavioral tests until the first implementation slice.
- Bad, because maintaining empty directory placeholders and bootstrap documentation adds small upfront repository noise.

## Validation

Compliance is validated by solution restore/build/test/format commands, review of project references, review of package pins and license-register entries, and future checks that behavior-specific changes add tests before production code.

## Pros and Cons of the Options

### Create the full MailMcp scaffold now

This option creates runtime projects, test projects, central configuration, minimal hosts, documentation, and license records before behavior implementation.

- Good, because it turns the architecture draft into concrete project boundaries.
- Good, because it gives analyzers, formatting, and package management one shared baseline.
- Neutral, because placeholder folders are represented with `.gitkeep` until real code is added.
- Bad, because some bootstrap-only settings, such as allowing empty test assemblies, must be revisited once real tests exist.

### Keep only the architecture draft and defer project creation

This option would leave the repository mostly as documentation until the first feature is implemented.

- Good, because it avoids scaffold files before they are used.
- Neutral, because it keeps decisions conceptual rather than enforceable.
- Bad, because the first feature would also need to solve solution structure, project references, package management, test wiring, and documentation layout.

### Create a single project and split boundaries later

This option would create one runnable project first and extract clean-architecture boundaries later.

- Good, because it minimizes the initial file count.
- Neutral, because it could still support a simple proof of concept.
- Bad, because it encourages boundary leakage and makes later refactoring more expensive.

## More Information

The baseline follows `specs/2026-07-22-mail-mcp-architecture-draft.md`. Adding or modifying ADRs after this scaffold requires explicit owner approval, as recorded in `AGENTS.md`.
