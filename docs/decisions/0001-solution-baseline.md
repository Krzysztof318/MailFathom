# Decision 0001: Solution baseline

## Status

Accepted

## Context

The architecture draft requires a .NET 10 solution named `MailMcp`, clean runtime boundaries, central package management, xUnit.net v3 on Microsoft Testing Platform v2, and Aspire local orchestration.

## Decision

Create the solution scaffold with runtime projects for Domain, Application, Infrastructure, AI, MCP, Host, and AppHost. Create boundary-specific unit-test projects under `tests/`, pin package versions centrally, and configure `global.json` to use the Microsoft Testing Platform runner.

## Consequences

Future feature work can add use cases and adapters inside established boundaries. Package additions and upgrades must update `Directory.Packages.props` and the third-party license register in the same change set.
