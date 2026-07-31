# IMAP Synchronization Review Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct the merge-blocking resource, memory, asynchronous I/O, ownership-validation, and coverage-documentation issues in pull request 13 without removing its PostgreSQL persistence or hosted-worker scope.

**Architecture:** Process one MIME payload at a time, commit metadata and content in one short local session, and commit the checkpoint only after the inspected window finishes. Align the pre-migration EF Core model with the architecture draft: local UUIDv7 stored-email identity, a PK/FK one-to-one MIME row with integrity metadata, and a separate folder checkpoint. Keep MailKit setup and cleanup fully asynchronous, preserve primary failures, validate occurrence ownership, and retain only explicitly temporary provider-bound coverage exclusions.

**Tech Stack:** .NET 10, C# preview, MailKit 4.17.0, EF Core 10.0.10, Npgsql EF Core 10.0.3, xUnit.net v3, NSubstitute, Microsoft Testing Platform v2.

## Global Constraints

- Preserve read-only IMAP selection and never set the remote `\Seen` flag.
- Do not hold EF Core transactions across IMAP calls.
- Do not add integration tests, Testcontainers, fake EF Core providers, or new dependencies.
- Keep the complete persistence and worker scope of pull request 13.
- Preserve unrelated changes and never add co-author trailers.

---

### Task 1: Bound synchronization memory to one message

**Files:**
- Modify: `tests/Application.UnitTests/MailboxSynchronizerTests.cs`
- Modify: `src/Application/Synchronization/MailboxSynchronizer.cs`

**Interfaces:**
- Consumes: `IMailboxSession.FetchMessageContentWithoutSettingSeenAsync`, `ISessionFactory.BeginSessionAsync`, `IMessageContentStore.SaveContentAsync`, `IMessageMetadataRepository.UpsertMetadataAsync`
- Produces: unchanged `MailboxSynchronizer.SynchronizeAsync` behavior with per-message persistence sessions and a final checkpoint session

- [x] **Step 1: Write a failing ordering test**

Add a two-message test whose second content-fetch callback asserts that the first persistence session has already committed and disposed. Configure a distinct final checkpoint session and assert it starts only after both message sessions commit.

- [x] **Step 2: Verify the test fails for batch buffering**

Run:

```bash
/home/krzysztof/.dotnet/dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --no-restore --filter-method "MailFathom.Application.UnitTests.MailboxSynchronizerTests.SynchronizeAsync_MultipleMessages_CommitsAndDisposesEachMessageBeforeFetchingTheNext"
```

Expected: failure because the current implementation fetches both contents before opening the first persistence session.

- [x] **Step 3: Implement one-message-at-a-time persistence**

Replace the `fetchedMessages` batch list with this operation order:

```csharp
var content = await session.FetchMessageContentWithoutSettingSeenAsync(
    metadata.OccurrenceId,
    this.options.MaxRawMimeBytes,
    cancellationToken);
await using var messageSession = await this.sessionScopeFactory.BeginSessionAsync(cancellationToken);
await this.contentStore.SaveContentAsync(messageSession, content, cancellationToken);
await this.metadataRepository.UpsertMetadataAsync(messageSession, metadata, cancellationToken);
await messageSession.CommitAsync(cancellationToken);
storedCount++;
```

After the message loop, start a separate persistence session only when `InspectedThroughUid` is present, save the advanced checkpoint, and commit.

- [x] **Step 4: Verify application tests**

Run:

```bash
/home/krzysztof/.dotnet/dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --no-restore
```

Expected: all application tests pass.

### Task 2: Make MailKit setup and cleanup ownership-safe

**Files:**
- Modify: `tests/Infrastructure.UnitTests/MailKitImapMailboxSessionTests.cs`
- Modify: `src/Infrastructure/Mail/MailKit/MailKitImapMailboxSession.cs`

**Interfaces:**
- Consumes: MailKit 4.17.0 `ImapClient.GetFolderAsync`
- Produces: asynchronous `IMailKitImapClient.GetFolderAsync`, primary-exception-preserving cleanup, and occurrence ownership validation

- [x] **Step 1: Write failing MailKit regression tests**

Add focused tests that prove:

```csharp
await Assert.ThrowsAsync<InvalidOperationException>(
    () => factory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None));
Assert.Equal(1, client.DisconnectCount);
Assert.Equal(1, client.DisposeCount);
```

The setup-failure test configures both disconnect and disposal failures and asserts that the folder-open exception remains the observed exception. Add a session-disposal test that asserts disposal still occurs after disconnect fails. Add a theory for foreign account, folder, and UIDVALIDITY identities and assert `GetStreamAsync` is not called.

- [x] **Step 2: Verify the tests fail**

Run:

```bash
/home/krzysztof/.dotnet/dotnet test tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj --no-restore
```

Expected: failures for synchronous folder lookup, cleanup exception replacement, skipped disposal, and missing occurrence validation.

- [x] **Step 3: Implement asynchronous folder lookup and deterministic cleanup**

Change the client seam to:

```csharp
Task<IMailFolder> GetFolderAsync(
    string path,
    CancellationToken cancellationToken);
```

Await it in `OpenReadOnlyAsync`. Centralize disconnect and disposal so disposal always runs, the first cleanup failure is retained for normal disposal, and the setup failure path suppresses cleanup failures before rethrowing the original exception.

- [x] **Step 4: Validate occurrence ownership**

Before `GetStreamAsync`, compare the occurrence account, folder, and UIDVALIDITY with the session values and current folder UIDVALIDITY. Throw a safe `ArgumentException` before remote I/O when they differ.

- [x] **Step 5: Verify infrastructure tests**

Run:

```bash
/home/krzysztof/.dotnet/dotnet test tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj --no-restore
```

Expected: all infrastructure tests pass.

### Task 3: Document temporary provider-bound coverage exclusions

**Files:**
- Modify: `src/Infrastructure/Mail/MailKit/MailKitImapMailboxSession.cs`
- Modify: `src/Infrastructure/Persistence/MailboxAccountEntity.cs`
- Modify: `src/Infrastructure/Persistence/MailFolderEntity.cs`
- Modify: `src/Infrastructure/Persistence/MailFathomDbContext.cs`
- Modify: `src/Infrastructure/Persistence/EmailMessageContentEntity.cs`
- Modify: `src/Infrastructure/Persistence/MessageContentStore.cs`
- Modify: `src/Infrastructure/Persistence/StoredEmailEntity.cs`
- Modify: `src/Infrastructure/Persistence/StoredEmailMetadataRepository.cs`
- Modify: `src/Infrastructure/Persistence/SynchronizationCheckpointStore.cs`
- Modify: `src/Infrastructure/Persistence/UnitOfWork.cs`
- Modify: `src/Infrastructure/ServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: current aggregate coverage policy and deferred PostgreSQL integration-test policy
- Produces: temporary exclusions with an explicit removal condition

- [x] **Step 1: Replace ambiguous justifications**

Use the exact provider-bound justification:

```csharp
[ExcludeFromCodeCoverage(Justification = "Will be covered by the planned PostgreSQL integration test suite.")]
// TODO: Remove this exclusion when PostgreSQL integration tests are enabled.
```

For the thin MailKit delegation wrapper, use the corresponding planned MailKit integration-suite wording. Do not add exclusions to the tested session factory or session logic.

- [x] **Step 2: Verify exclusion scope**

Run:

```bash
git grep -n -B1 -A1 "ExcludeFromCodeCoverage" -- src
```

Expected: every temporary exclusion has a concrete future integration-suite justification and adjacent removal TODO.

### Task 4: Align the pre-migration persistence model

**Files:**
- Add: `src/Domain/Messages/StoredEmailId.cs`
- Modify: `tests/Domain.UnitTests/MailIdentityTests.cs`
- Modify: `tests/Application.UnitTests/MailboxSynchronizerTests.cs`
- Modify: `src/Application/Synchronization/IMessageMetadataRepository.cs`
- Modify: `src/Application/MessageContent/IMessageContentStore.cs`
- Modify: `src/Application/Synchronization/MailboxSynchronizer.cs`
- Rename and modify persistence entities and repositories under `src/Infrastructure/Persistence/`

**Interfaces:**
- Produces: `StoredEmailId` from metadata upsert
- Consumes: `StoredEmailId` when persisting raw MIME
- Maps: `mailbox_accounts`, `mail_folders`, `stored_emails`, `email_message_contents`, and `synchronization_checkpoints`

- [x] **Step 1: Write failing domain and application tests**

Add domain tests for non-empty stored-email UUID identity. Update the application synchronization test to require the metadata upsert result to be passed to the content store.

- [x] **Step 2: Implement application identity flow**

Add `StoredEmailId`, return it from `IMessageMetadataRepository.UpsertMetadataAsync`, and pass it to `IMessageContentStore.SaveContentAsync` after metadata is upserted in the same session.

- [x] **Step 3: Align EF Core entities and mappings**

Use UUIDv7 for new stored-email rows. Model raw MIME as a required one-to-one PK/FK dependent with byte length, SHA-256, and storage time. Separate checkpoints from folder identity and add explicit account-folder, folder-email, folder-checkpoint, and email-content relationships. Do not add a migration before schema review.

- [x] **Step 4: Clear tracked state after each persistence session**

Ensure session disposal clears the shared scoped context after transaction cleanup so raw MIME and tracked entities do not accumulate between short per-message sessions.

- [x] **Step 5: Verify domain and application tests**

Run:

```bash
/home/krzysztof/.dotnet/dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --no-restore
/home/krzysztof/.dotnet/dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --no-restore
```

Expected: all domain and application tests pass.

### Task 5: Resolve final full-scope review findings

**Files:**
- Modify: `src/Host/Configuration/MailSynchronizationOptions.cs`
- Modify: `src/Infrastructure/Mail/MailKit/MailKitImapAccountSettings.cs`
- Modify: `src/Infrastructure/Mail/MailKit/MailKitImapMailboxSession.cs`
- Modify: `src/Infrastructure/Persistence/StoredEmailMetadataRepository.cs`
- Modify: `src/Infrastructure/Persistence/MessageContentStore.cs`
- Modify: `src/Host/Host.csproj`
- Modify: `src/Infrastructure/Infrastructure.csproj`
- Modify: `tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj`

- [x] **Step 1: Harden configuration identity, nested validation, and TLS naming**

Normalize account lookup consistently, reject duplicate normalized account/folder identities and invalid nested port values at startup, and rename `UseTls` to `UseSslOnConnect` so false unambiguously means mandatory STARTTLS rather than clear text.

- [x] **Step 2: Remove hidden clock and MIME-copy costs**

Generate UUIDv7 from the injected `TimeProvider`. Reuse a full array-backed MIME buffer when safe and copy only when the read-only memory is a slice or has a non-array backing store.

- [x] **Step 3: Move relational dependency ownership**

Reference `Microsoft.EntityFrameworkCore.Relational` directly from `Infrastructure` and remove the unnecessary direct Host and test-project references.

### Task 6: Update durable behavior documentation and verify

**Files:**
- Modify: `docs/features/imap-synchronization.md`

**Interfaces:**
- Consumes: implemented transaction, memory, cleanup, and validation behavior
- Produces: accurate durable operational documentation

- [x] **Step 1: Update the feature documentation**

Document that only one MIME payload is retained at a time, content and metadata commit together per occurrence, checkpoint commit happens after the inspected window, retries may re-fetch already persisted occurrences idempotently, and session cleanup preserves the primary setup failure.

- [x] **Step 2: Run full verification**

Run:

```bash
/home/krzysztof/.dotnet/dotnet restore
/home/krzysztof/.dotnet/dotnet build --no-restore
/home/krzysztof/.dotnet/dotnet test --no-build
/home/krzysztof/.dotnet/dotnet format --verify-no-changes
/home/krzysztof/.dotnet/dotnet build --configuration Release --no-restore
/home/krzysztof/.dotnet/dotnet msbuild .config/CodeCoverage.proj -t:Collect
git diff --check
```

Expected: all commands exit successfully, all tests pass, and aggregate coverage meets the configured gate.

- [ ] **Step 3: Inspect, commit, push, and restore draft state**

Review `git diff origin/main...HEAD` for secrets, unrelated edits, generated files, and dependency-boundary violations. Commit only review-fix files without co-author trailers, push the existing PR branch, and convert pull request 13 back to draft.
