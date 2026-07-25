# Explicit Enum Values Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every enum member an explicit contiguous numeric value starting at zero and make the convention and persistence-test restrictions unambiguous in repository guidance.

**Architecture:** Keep the change source-level and dependency-free. Update repository guidance, then make the only noncompliant private enum explicit without changing persistence mappings or runtime behavior.

**Tech Stack:** C# 14, .NET 10, Markdown, Git.

## Global Constraints

- Every enum member declares an explicit integral value.
- Values start at `0` and remain unique and contiguous in declaration order.
- Existing numeric assignments are never reordered, renumbered, or reused.
- New members are appended with the next available value.
- The convention applies to all enums, including private and currently non-persisted types.
- Do not change the existing EF Core string conversion or database schema.
- Do not add dependencies.

---

### Task 1: Repository guidance

**Files:**
- Modify: `AGENTS.md`
- Modify: `docs/superpowers/specs/2026-07-25-explicit-enum-values-design.md`

**Interfaces:**
- Consumes: the accepted enum convention and ADR 0001 persistence-test boundary
- Produces: durable instructions for future contributors and agents

- [x] **Step 1: Add the enum convention**

Add this rule under `.NET and C# conventions`:

```markdown
- Give every enum member an explicit, unique integral value starting at `0` and increasing contiguously in declaration order. Never reorder, renumber, or reuse an existing value; append new members with the next value. Apply this to every enum, including private and currently non-persisted types, so a future numeric persistence representation cannot silently change meaning after refactoring.
```

- [x] **Step 2: Add the persistence-testing prohibition**

Add this rule under `Unit testing policy`:

```markdown
- Never use the EF Core InMemory provider, SQLite in-memory, any other in-memory SQL database, or mocked `DbSet` query behavior as a substitute for PostgreSQL persistence semantics. Unit-test application behavior through application-owned ports and hand-written state fakes; verify provider-specific persistence behavior only against real PostgreSQL integration tests when that phase is enabled.
```

- [x] **Step 3: Verify the guidance is explicit**

Run:

```bash
rg -n "every enum member|EF Core InMemory|SQLite in-memory|in-memory SQL|mocked `DbSet`|real PostgreSQL" AGENTS.md
```

Expected: both new rules are returned and all prohibited persistence-test substitutes are named.

### Task 2: Explicit occurrence outcome values

**Files:**
- Modify: `src/Application/Synchronization/MailboxSynchronizer.cs`

**Interfaces:**
- Consumes: existing private `OccurrenceOutcome` enum
- Produces: `Stored = 0` and `SkippedOversized = 1`

- [x] **Step 1: Run the source-level regression check before implementation**

Run:

```bash
if rg -n '^\s+(Stored|SkippedOversized),$' src/Application/Synchronization/MailboxSynchronizer.cs; then
  exit 1
fi
```

Expected: exit code `1` with both implicit members listed.

- [x] **Step 2: Add the explicit values**

Change the enum to:

```csharp
private enum OccurrenceOutcome
{
    Stored = 0,
    SkippedOversized = 1,
}
```

- [x] **Step 3: Re-run the source-level regression check**

Run:

```bash
if rg -n '^\s+(Stored|SkippedOversized),$' src/Application/Synchronization/MailboxSynchronizer.cs; then
  exit 1
fi
```

Expected: exit code `0` with no matches.

- [x] **Step 4: Inspect every production enum**

Run:

```bash
rg -n --glob '*.cs' '^\s*(public|internal|private|protected)?\s*enum\s+|^\s+[A-Za-z][A-Za-z0-9_]*\s*(=\s*[0-9]+)?,\s*$' src
```

Expected: `StoredEmailContentAvailability` and `OccurrenceOutcome` are the only enum declarations, and every listed member has an explicit contiguous value starting at zero.

### Task 3: Repository verification and publication

**Files:**
- Verify: all changed files

**Interfaces:**
- Consumes: completed guidance and enum changes
- Produces: verified commit and draft pull request

- [x] **Step 1: Run repository verification**

Run:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format --verify-no-changes
dotnet build --configuration Release --no-restore
dotnet msbuild .config/CodeCoverage.proj -t:Collect
```

Expected: every command exits `0`, all tests pass, formatting is unchanged, and aggregate line coverage is at least 85%.

- [x] **Step 2: Inspect the final diff**

Run:

```bash
git diff --check
git diff origin/main...HEAD
git status -sb
```

Expected: only the accepted enum convention, persistence-testing prohibition, design/plan documentation, and explicit enum assignments are present; no secrets or unrelated files appear.

- [ ] **Step 3: Commit and publish**

Stage only the four intended files, commit without co-author trailers, push `agent/explicit-enum-values`, and open a draft pull request targeting `main`.
