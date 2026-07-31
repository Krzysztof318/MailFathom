# Optimistic Concurrency Handling Implementation Plan

> **Completed and partly superseded.** This plan is kept as the record of how the slice was executed. Its conflict-signaling and configuration decisions were later revised: the per-boundary conflict enums (`MailboxSynchronizationOutcome`, `SynchronizationCheckpointSaveResult`, and the private occurrence outcome) were replaced by `PersistenceConcurrencyConflictException` above the retry, and `MailSynchronization:MaxPersistenceConcurrencyAttempts` was replaced by the deployment-wide `Persistence:MaximumConcurrencyCommitAttempts`, default `2`. ADR 0001 and `docs/features/imap-synchronization.md` describe the current behavior.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:test-driven-development and execute the tasks inline in order. Subagent execution is disabled for this workspace.

**Goal:** Implement PostgreSQL `xmin` optimistic concurrency for mutable tracked persistence records, translate EF Core conflicts into an application-owned commit result, and retry safe synchronization writes with a fresh persistence session.

**Architecture:** `Infrastructure` maps `ConcurrencyVersion` to `xmin` and converts `DbUpdateConcurrencyException` at the persistence-session commit boundary. `Application` owns the commit result and a focused retry policy that recreates the complete local write attempt for each bounded retry. `MailboxSynchronizer` uses that policy only for idempotent email writes after remote IMAP reads have completed, so retries do not repeat external side effects. Checkpoints use one attempt because retrying a previously calculated value could overwrite newer progress.

**Tech Stack:** .NET 10, C# 14, EF Core 10, Npgsql EF Core provider 10.0.3, xUnit.net v3, NSubstitute.

## Global Constraints

- Keep EF Core, Npgsql, `xmin`, persistence entities, and provider exceptions inside `Infrastructure`.
- Use `ConcurrencyVersion` only on mutable tracked entities; keep the atomic set-based raw MIME update exempt.
- Retry the complete local write with a new `IPersistenceSession`; never retry `SaveChangesAsync` on the failed context.
- Retry only `ConcurrencyConflict`, propagate cancellation and unrelated failures immediately, and bound attempts through validated synchronization options.
- Preserve the later PostgreSQL integration-test boundary for provider mapping and real conflict detection.
- Update actual feature documentation after production behavior passes.

---

### Task 1: Application-owned commit result and retry policy

**Files:**
- Create: `src/Application/Persistence/PersistenceCommitResult.cs`
- Create: `src/Application/Persistence/OptimisticConcurrencyRetryPolicy.cs`
- Create: `tests/Application.UnitTests/OptimisticConcurrencyRetryPolicyTests.cs`
- Modify: `src/Application/Persistence/IPersistenceSession.cs`
- Modify: `src/Application/Application.csproj`

**Interfaces:**
- Produces: `PersistenceCommitResult` with `Committed` and `ConcurrencyConflict`.
- Produces: `OptimisticConcurrencyRetryPolicy.CommitAsync(Func<IPersistenceSession, CancellationToken, Task>, CancellationToken)`.
- Changes: `IPersistenceSession.CommitAsync` returns `Task<PersistenceCommitResult>`.

- [x] **Step 1: Write failing retry-policy tests**

Cover conflict followed by success with two distinct sessions, exhaustion after the configured maximum, cancellation between attempts, immediate propagation of unrelated failures, and rejection of a non-positive attempt count.

- [x] **Step 2: Run the focused tests and verify RED**

Run:

```bash
/home/krzysiek/.dotnet/dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --no-restore --filter-class "MailFathom.Application.UnitTests.OptimisticConcurrencyRetryPolicyTests"
```

Expected: compilation fails because the commit result and retry policy do not exist.

- [x] **Step 3: Implement the minimal result and policy**

The policy opens and asynchronously disposes a new session inside every attempt, invokes the supplied staging delegate, commits once, returns immediately on `Committed`, and retries only `ConcurrencyConflict`.

- [x] **Step 4: Run the focused tests and verify GREEN**

Run the command from Step 2. Expected: all retry-policy tests pass.

### Task 2: PostgreSQL token mapping and commit translation

**Files:**
- Modify: `src/Infrastructure/Persistence/StoredEmailEntity.cs`
- Modify: `src/Infrastructure/Persistence/SynchronizationCheckpointEntity.cs`
- Modify: `src/Infrastructure/Persistence/MailFathomDbContext.cs`
- Modify: `src/Infrastructure/Persistence/PersistenceSessionFactory.cs`

**Interfaces:**
- Consumes: `PersistenceCommitResult`.
- Produces: `uint ConcurrencyVersion` mapped through `IsRowVersion()` on the two mutable tracked entities.
- Produces: `PersistenceCommitResult.ConcurrencyConflict` after `DbUpdateConcurrencyException`, with rollback before the failed session becomes invalid.

- [x] **Step 1: Add `ConcurrencyVersion` and configure `IsRowVersion()`**

Do not add a user-defined timestamp column or token to append-only/reference entities or to `EmailMessageContentEntity`, whose persisted overwrite is already one atomic `ExecuteUpdate`.

- [x] **Step 2: Translate commit conflicts**

Catch only `DbUpdateConcurrencyException`, roll back the active transaction, clear failed tracked state during cleanup, and return `ConcurrencyConflict`. Let cancellation, rollback failures, provider failures, and all non-concurrency failures propagate.

- [x] **Step 3: Build the production projects**

Run:

```bash
/home/krzysiek/.dotnet/dotnet build --no-restore
```

Expected: build succeeds with zero warnings. Provider mapping behavior remains assigned to the planned PostgreSQL integration suite by repository policy.

### Task 3: Synchronization conflict retry and safe exhaustion

**Files:**
- Modify: `src/Application/Synchronization/MailboxSynchronizationOptions.cs`
- Modify: `src/Application/Synchronization/MailboxSynchronizer.cs`
- Modify: `tests/Application.UnitTests/MailboxSynchronizerTests.cs`
- Modify: `src/Host/Configuration/MailSynchronizationOptions.cs`
- Modify: `src/Host/Program.cs`
- Modify: `src/Host/appsettings.json`
- Modify: `src/Host/Hosting/MailSynchronizationWorker.cs`

**Interfaces:**
- Produces: validated `MaxPersistenceConcurrencyAttempts`, default `3`.
- Produces: `MailboxSynchronizationOutcome` with `Completed` and `ConcurrencyConflict`.
- Changes: `MailboxSynchronizationResult` reports the outcome so exhaustion is an explicit application result.

- [x] **Step 1: Write failing synchronizer tests**

Cover one conflict followed by success using distinct sessions, confirm the IMAP content fetch occurs only once, cover exhaustion returning `ConcurrencyConflict` without advancing the checkpoint, and prove a checkpoint conflict is not retried with stale progress.

- [x] **Step 2: Run the focused tests and verify RED**

Run:

```bash
/home/krzysiek/.dotnet/dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --no-restore --filter-class "MailFathom.Application.UnitTests.MailboxSynchronizerTests"
```

Expected: compilation or assertion failure because the new commit result and synchronization outcome are not handled yet.

- [x] **Step 3: Retry idempotent email writes and attempt checkpoints once**

Metadata plus content and oversized metadata each supply the complete staging delegate. A checkpoint update uses one fresh session and is not retried with its previously calculated progress. On exhaustion or a checkpoint conflict, return a conflict outcome with the last persisted checkpoint and stop the current run.

- [x] **Step 4: Bind and validate the attempt limit**

Expose `MailSynchronization:MaxPersistenceConcurrencyAttempts` with range `1..10`, map it into application options, and log conflict exhaustion as a deferred folder synchronization rather than success.

- [x] **Step 5: Run the focused tests and verify GREEN**

Run the command from Step 2. Expected: all mailbox synchronizer tests pass.

### Task 4: Review feedback, documentation, and full verification

**Files:**
- Modify: `docs/decisions/0001-application-owned-repositories-for-persistence-ports.md`
- Modify: `docs/features/imap-synchronization.md`
- Modify: pull request title/body and two review threads through GitHub

**Interfaces:**
- Corrects: `xmin` changes when another transaction creates the current row version, not necessarily between multiple writes in the same transaction.
- Documents: implemented token scope, commit translation, bounded retry configuration, exhaustion behavior, and deferred integration coverage.

- [x] **Step 1: Correct ADR token semantics and implementation wording**

Remove the per-successful-write token assertion and require a real conflicting transaction to prove detection in the future PostgreSQL integration suite.

- [x] **Step 2: Document implemented synchronization behavior**

Add the new configuration value, explain fresh-session retries, and record that exhaustion stops the run without advancing its checkpoint.

- [x] **Step 3: Run full verification**

Run:

```bash
/home/krzysiek/.dotnet/dotnet restore
/home/krzysiek/.dotnet/dotnet build --no-restore
/home/krzysiek/.dotnet/dotnet test --no-build
/home/krzysiek/.dotnet/dotnet format --verify-no-changes --no-restore
/home/krzysiek/.dotnet/dotnet msbuild .config/CodeCoverage.proj -t:Collect
git diff --check
```

Expected: every command succeeds, all unit tests pass, and aggregate line coverage remains at least 85%.

- [ ] **Step 4: Commit, push, reply, and resolve**

Stage only task files, commit without co-author trailers, push the existing branch, reply to both review comments with the implementation evidence, resolve both thread IDs, and re-fetch thread state to confirm no selected thread remains unresolved.
