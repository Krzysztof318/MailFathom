# IMAP Synchronization Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first production-shaped vertical slice for read-only IMAP synchronization, local PostgreSQL persistence, and periodic background execution.

**Architecture:** Domain provides pure mail identifiers and synchronization invariants. Application coordinates synchronization through its own IMAP and persistence ports. Infrastructure implements EF Core persistence and MailKit adapters; Host wires options and the background worker.

**Tech Stack:** .NET 10, C# preview, xUnit.net v3, NSubstitute, EF Core 10, Npgsql EF Core 10, MailKit 4.17.0, ASP.NET Core hosted services.

## Global Constraints

- Keep the slice small enough for one reviewable PR.
- Do not introduce new third-party packages; use versions already pinned in `Directory.Packages.props`.
- Synchronization and content retrieval must never set the remote IMAP `\\Seen` flag.
- `Domain` and `Application` must not reference EF Core, Npgsql, MailKit, ASP.NET Core, MCP SDKs, or provider-specific AI types.
- Unit tests must not use network, filesystem, databases, containers, sleeps, or real clocks.
- Documentation is updated after verified code and notes implemented vs pending draft scope.

---

### Task 1: Domain identifiers and synchronization value objects

**Files:** create focused value objects under `src/Domain/Accounts`, `Folders`, `Messages`, and `Synchronization`; add tests under `tests/Domain.UnitTests`.

**Deliverable:** Validated account/folder/UID identity objects and `SynchronizationCheckpoint`.

### Task 2: Application IMAP and persistence ports plus synchronization use case

**Files:** create application contracts under `src/Application/Accounts`, `MessageContent`, and `Synchronization`; add tests under `tests/Application.UnitTests`.

**Deliverable:** `MailboxSynchronizer` opens folders read-only, fetches only safe message batches, stores metadata/content idempotently, and advances checkpoints after successful storage.

### Task 3: Infrastructure EF Core persistence and MailKit adapter

**Files:** modify `Infrastructure.csproj`; create `MailFathomDbContext`, EF entities/configuration, repositories, content store, MailKit session factory/session; add infrastructure unit tests.

**Deliverable:** Database model expresses uniqueness for stable remote occurrence identity and account/folder names; MailKit adapter exposes only seen-preserving content fetch operations.

### Task 4: Host background worker and configuration

**Files:** create host options, hosted service, DI registration; update `Program.cs` and appsettings examples; add host-relevant unit coverage where possible in application/infrastructure tests.

**Deliverable:** Periodic scoped background synchronization honors cancellation and validates unsafe account configuration at startup.

### Task 5: Documentation and draft status update

**Files:** update `docs/features/initial-scope.md`, create `docs/features/imap-synchronization.md`, update `docs/README.md`, and mark implemented/pending status in `specs/2026-07-22-mail-fathom-architecture-draft.md`.

**Deliverable:** Durable documentation describes implemented behavior, safety assumptions, configuration, and pending draft items.
