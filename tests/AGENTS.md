# Unit Test Instructions

These instructions apply under `tests/` in addition to the repository root instructions.

## Unit testing policy

- Write or update the failing test before production code when practical.
- Use xUnit.net v3 on Microsoft Testing Platform v2 and NSubstitute for test doubles.
- Keep unit tests in projects named after the production boundary: `Domain.UnitTests`, `Application.UnitTests`, `Infrastructure.UnitTests`, `AI.UnitTests`, and `Mcp.UnitTests`.
- Follow Arrange, Act, Assert. Add explicit `// Arrange`, `// Act`, and `// Assert` comments.
- Name tests `Member_Scenario_ExpectedBehavior`. Use `[Fact]` for one scenario and `[Theory]` for the same behavior over multiple inputs.
- Test observable behavior and domain invariants, not private implementation details. One test describes one behavior even if several assertions prove it.
- Tests must be fast, isolated, repeatable, order-independent, and safe to run in parallel. Do not use real clocks, nondeterministic random values, shared mutable fixtures, sleeps, network calls, databases, containers, or the filesystem.
- Prefer real domain values and simple in-memory fakes. Use NSubstitute at external or architectural boundaries where interaction is part of the contract.
- Never use the EF Core InMemory provider, SQLite in-memory, another in-memory SQL database, or mocked `DbSet` query behavior as a substitute for PostgreSQL semantics. Unit-test through application-owned ports and hand-written state fakes.
- Do not substitute concrete MailKit clients. Define narrow application-facing ports and use NSubstitute to model server capabilities, responses, disconnects, and failures.
- Use `Received()` and `DidNotReceive()` only when the interaction is a required side effect or safety invariant. Use argument matchers only while configuring substitutes or verifying calls.
- Every IMAP content-fetch path must prove that no operation capable of setting `\Seen` was requested.
- Cover cancellation, retry boundaries, idempotency, duplicate events, UIDVALIDITY changes, partial failures, and unsafe TLS or authentication configuration where relevant.
- Run the complete unit test suite before committing.

## Code coverage

- Maintain at least 85% aggregate line coverage across `Domain`, `Application`, `Infrastructure`, `AI`, and `Mcp`.
- Calculate the threshold across the complete configured codebase, never from patch, changed-line, or per-project coverage.
- Keep `Host` and `AppHost` excluded as thin composition roots. Do not add other exclusions merely to pass the threshold.
- Add `using System.Diagnostics.CodeAnalysis;` and apply `[ExcludeFromCodeCoverage]` only to a class with no executable application, domain, mapping, validation, policy, or infrastructure logic. Do not fully qualify the attribute.
- Never exclude meaningfully testable behavior. Remove the exclusion and add coverage when logic enters an excluded class.
- Run `dotnet msbuild .config/CodeCoverage.proj -t:Collect` before committing production or test changes.

## Integration tests

- Integration tests are planned for a later phase and currently exist only in the architecture draft.
- Do not add integration-test projects, Testcontainers, Docker fixtures, real PostgreSQL dependencies, or real or mock network mail servers yet.
- MailKit wire behavior against an IMAP or SMTP server belongs to the future integration suite; do not mislabel it as a unit test.
