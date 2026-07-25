# Explicit Enum Values Design

## Goal

Keep every C# enum safe for a future numeric persistence representation by assigning stable, explicit integral values.

## Convention

- Every enum member declares an explicit integral value.
- Values start at `0` and remain unique and contiguous in declaration order.
- Existing numeric assignments are never reordered, renumbered, or reused.
- New members are appended with the next available value.
- The convention applies to all enums, including private and currently non-persisted types.

## Code Change

`StoredEmailContentAvailability` already complies. `OccurrenceOutcome` will declare `Stored = 0` and `SkippedOversized = 1`.

The existing EF Core string conversion for `StoredEmailContentAvailability` remains unchanged. This change does not modify the database schema or runtime behavior.

## Persistence Testing Rule

`AGENTS.md` will explicitly prohibit using the EF Core InMemory provider, SQLite in-memory, any other in-memory SQL database, or mocked `DbSet` query behavior as a substitute for PostgreSQL persistence semantics. Application behavior remains unit-tested through application-owned ports and hand-written state fakes. Provider-specific persistence behavior must be verified against real PostgreSQL when the integration-test phase is enabled.

## Verification

Search all production C# sources for enum declarations and confirm each member has the expected explicit value. Confirm that `AGENTS.md` names every prohibited in-memory persistence-testing mechanism. Run the repository restore, build, unit-test, formatting, and aggregate coverage checks.
