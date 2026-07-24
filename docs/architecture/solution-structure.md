# Solution structure

MailMcp uses a clean-architecture modular monolith. Dependencies point inward from adapters and hosts toward application and domain contracts.

## Runtime projects

- `Domain` contains pure business concepts and invariants.
- `Application` contains use-case contracts and ports and references only `Domain`.
- `Infrastructure` implements persistence, MailKit, security, and observability adapters.
- `AI` owns chunking, embeddings, retrieval, and agent-framework composition.
- `Mcp` maps MCP protocol requests and responses to application use cases.
- `Host` is the ASP.NET Core composition root.
- `AppHost` is the Aspire local-development orchestration host.

## Test projects

Unit tests live under `tests/` and are split by production boundary. They use xUnit.net v3 on Microsoft Testing Platform v2 and NSubstitute for architectural-boundary doubles.
