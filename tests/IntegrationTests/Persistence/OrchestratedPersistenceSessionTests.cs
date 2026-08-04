// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves what the transaction a persistence session opens is worth, and which failures it reports as conflicts.</summary>
/// <remarks>
/// <para>
/// The session's contract is three claims a substitute can only restate: work that was never committed leaves nothing
/// behind even when the provider already sent it to the server, a losing writer on a recognized unique constraint gets a
/// result rather than an exception, and a stale <c>xmin</c> token is recognized as an optimistic conflict. All three are
/// properties of PostgreSQL's behavior under two overlapping transactions. The middle one is proven twice, because one
/// first binding violates a different constraint depending on whether the account it hangs from is already stored, and
/// a single test would prove whichever of the two the suite's order happened to produce.
/// </para>
/// <para>
/// Every one of them therefore uses two sessions whose lifetimes overlap, arranged so neither can see the other's
/// uncommitted work — the shape two synchronization runs over one folder produce, and the only arrangement in which the
/// database rather than the application decides the outcome.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedPersistenceSessionTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "persistence-session";

    /// <summary>The alias the binding race binds for the first time, which no other test writes.</summary>
    private const string ContestedFolderAlias = "persistence-session-race";

    /// <summary>The account the race over an account row runs under, which nothing else in the suite stores anything for.</summary>
    private const string UnstoredAccountId = "persistence-session-unstored-account";

    /// <summary>The alias the retried race binds, which no other test writes and no other attempt may find bound.</summary>
    private const string RetriedFolderAlias = "persistence-session-retry";

    private const uint RolledBackUid = 21;

    private const uint ConflictingUid = 22;

    /// <summary>Proves the transaction covers SQL the provider had already executed, not only staged changes.</summary>
    /// <remarks>
    /// The content store overwrites an existing payload with a set-based <c>UPDATE</c>, which the provider sends the
    /// moment it is called rather than at commit. That makes it the write worth rolling back: staged changes are still
    /// in memory when a session ends and would be lost whether or not a transaction existed, while this one has already
    /// reached the server and is undone only because the session opened a transaction around it.
    /// </remarks>
    [Fact]
    public async Task DisposeAsync_ForASessionThatIssuedASetBasedUpdateWithoutCommitting_RollsThatUpdateBack()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, RolledBackUid);
        var committedRawMime = SyntheticEmail.RawMimeOf("session-rollback-committed", 4096);
        var abandonedRawMime = SyntheticEmail.RawMimeOf("session-rollback-abandoned", 6144);

        var storedEmailId = await StoreMetadataAndContentAsync(
            services,
            occurrenceId,
            "session-rollback",
            committedRawMime,
            cancellationToken);

        // Act
        await services.InScopeAsync(
            async (scope, token) =>
            {
                var abandonedSession = await scope
                    .GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                // Left without a commit deliberately, so disposal is what ends the transaction.
                await using (abandonedSession)
                {
                    await scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                        abandonedSession,
                        storedEmailId,
                        new RemoteEmailContent(occurrenceId, abandonedRawMime),
                        token);
                }

                return true;
            },
            cancellationToken);

        // Assert
        var readBack = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailContentStore>().FindStoredContentAsync(storedEmailId, token),
            cancellationToken);

        Assert.NotNull(readBack);
        Assert.True(
            committedRawMime.AsSpan().SequenceEqual(readBack.RawMime.Span),
            "The payload of an uncommitted session's set-based update survived, so the transaction did not cover it.");
    }

    /// <summary>Proves a losing writer on the alias binding is reported as a conflict rather than as a failure.</summary>
    /// <remarks>
    /// Two runs binding the same alias for the first time is a race to resolve, not bad data, which is why the session
    /// recognizes this one constraint by name and reports it as a result the caller loops on. The unique index is what
    /// decides the winner, so nothing short of two real overlapping transactions can establish the behavior. The
    /// account row is committed first, deliberately: the binding insert creates it when it is missing, and the two
    /// writers would then collide on the account before either reached the alias — which is the race the test below
    /// this one owns.
    /// </remarks>
    [Fact]
    public async Task CommitAsync_WhenAnotherWriterBoundTheSameAliasFirst_ReportsAConcurrencyConflict()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var contestedBinding = MailFolderResolution.FirstBindingOf(
            MailFolderAlias.Create(ContestedFolderAlias),
            RemoteFolderPath.Create(ContestedFolderAlias, hierarchyDelimiter: '.'));

        // Act
        var losingCommit = await services.InScopeAsync(
            async (losingScope, token) =>
            {
                await using var losingSession = await losingScope
                    .GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                await losingScope.GetRequiredService<IMailFolderResolutionStore>().SaveResolutionAsync(
                    losingSession,
                    SyntheticMailAccount.AccountId,
                    contestedBinding,
                    token);

                var winningCommit = await services.CommitAsync(
                    (winningScope, winningSession, winningToken) => winningScope
                        .GetRequiredService<IMailFolderResolutionStore>()
                        .SaveResolutionAsync(
                            winningSession,
                            SyntheticMailAccount.AccountId,
                            contestedBinding,
                            winningToken),
                    token);
                Assert.Equal(PersistenceCommitResult.Committed, winningCommit);

                return await losingSession.CommitAsync(token);
            },
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.ConcurrencyConflict, losingCommit);
        Assert.Equal(1, await CountBindingsOfAsync(services, contestedBinding, cancellationToken));
    }

    /// <summary>Proves the same race under an account nothing has stored yet is reported as a conflict as well.</summary>
    /// <remarks>
    /// The first binding of an alias creates the account row it hangs from, so on an empty database the two runs
    /// collide on the account's primary key rather than on the alias index: the account is inserted first, because the
    /// binding references it. The account is one nothing else in the suite writes, which is what keeps this the first
    /// binding under it however the suite is ordered.
    /// </remarks>
    [Fact]
    public async Task CommitAsync_WhenAnotherWriterCreatedTheAccountRowFirst_ReportsAConcurrencyConflict()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var unstoredAccountId = MailAccountId.Create(UnstoredAccountId);
        var firstBinding = MailFolderResolution.FirstBindingOf(
            MailFolderAlias.Create(FolderAlias),
            RemoteFolderPath.Create(FolderAlias, hierarchyDelimiter: '.'));

        // Act
        var losingCommit = await services.InScopeAsync(
            async (losingScope, token) =>
            {
                await using var losingSession = await losingScope
                    .GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                await losingScope.GetRequiredService<IMailFolderResolutionStore>().SaveResolutionAsync(
                    losingSession,
                    unstoredAccountId,
                    firstBinding,
                    token);

                var winningCommit = await services.CommitAsync(
                    (winningScope, winningSession, winningToken) => winningScope
                        .GetRequiredService<IMailFolderResolutionStore>()
                        .SaveResolutionAsync(
                            winningSession,
                            unstoredAccountId,
                            firstBinding,
                            winningToken),
                    token);
                Assert.Equal(PersistenceCommitResult.Committed, winningCommit);

                return await losingSession.CommitAsync(token);
            },
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.ConcurrencyConflict, losingCommit);
        Assert.Equal(1, await CountAccountRowsOfAsync(services, unstoredAccountId, cancellationToken));
    }

    /// <summary>Proves the <c>xmin</c> token detects a revision another transaction committed first.</summary>
    /// <remarks>
    /// The stored email's concurrency token is PostgreSQL's own <c>xmin</c> system column rather than a column MailFathom
    /// writes, so nothing in the process updates it and no substitute can make it go stale. Reading a row in one
    /// transaction, letting another revise and commit it, and only then writing is the arrangement that makes the token
    /// the loser holds no longer the row's.
    /// </remarks>
    [Fact]
    public async Task CommitAsync_WhenAnotherTransactionRevisedTheStoredEmailFirst_ReportsAConcurrencyConflict()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, ConflictingUid);

        var storedEmailId = await StoreMetadataAsync(services, occurrenceId, "concurrency-original", cancellationToken);

        // Act
        var losingCommit = await services.InScopeAsync(
            async (losingScope, token) =>
            {
                // The scoped context is the one the session enlists, so a row tracked here carries the token this
                // session will write against.
                var losingContext = losingScope.GetRequiredService<MailFathomDbContext>();

                await using var losingSession = await losingScope
                    .GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                var trackedRow = await losingContext.StoredEmails.SingleAsync(
                    storedEmail => storedEmail.Id == storedEmailId.Value,
                    token);
                trackedRow.Subject = "concurrency-losing-writer";

                var revisedStoredEmailId = await StoreMetadataAsync(
                    services,
                    occurrenceId,
                    "concurrency-winning-writer",
                    token);

                // The same row, revised: a second identifier would mean the winning writer inserted rather than
                // updated, and this test would then prove nothing about the token.
                Assert.Equal(storedEmailId, revisedStoredEmailId);

                return await losingSession.CommitAsync(token);
            },
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.ConcurrencyConflict, losingCommit);
        Assert.Equal(
            "concurrency-winning-writer",
            await ReadSubjectAsync(services, storedEmailId, cancellationToken));
    }

    /// <summary>Proves a losing writer that retries in the same scope commits, rather than losing to itself.</summary>
    /// <remarks>
    /// <para>
    /// The three conflict tests above end where a conflict is reported; this one is what a caller does next. Every attempt runs
    /// in a fresh session but through the same scoped context, so the entities the losing attempt staged are still
    /// tracked when the next one begins — and the next one would insert them again, collide on the same constraint, and
    /// exhaust the policy. That the retry commits instead is what proves session disposal cleared the tracked state,
    /// which is observable only after a conflict a real database produced.
    /// </para>
    /// <para>
    /// Which of the two constraints the losing attempt violates depends on whether the account row is already stored
    /// when this test runs, exactly as it does for the two binding races above. Both are recognized, both leave the winner's
    /// binding as the only one, and the retry re-resolves from whichever the loser found — so this test asserts the
    /// outcome rather than the constraint and stays independent of the order the suite ran in.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CommitAsync_AfterLosingTheBindingRace_CommitsOnTheRetryThroughTheSameScopedContext()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var retriedBinding = MailFolderResolution.FirstBindingOf(
            MailFolderAlias.Create(RetriedFolderAlias),
            RemoteFolderPath.Create(RetriedFolderAlias, hierarchyDelimiter: '.'));

        // Act
        var attemptCount = await services.InScopeAsync(
            async (retryingScope, token) =>
            {
                var attempts = 0;

                await retryingScope.GetRequiredService<OptimisticConcurrencyRetryPolicy>().CommitAsync(
                    async (session, attemptToken) =>
                    {
                        attempts++;

                        await retryingScope.GetRequiredService<IMailFolderResolutionStore>().SaveResolutionAsync(
                            session,
                            SyntheticMailAccount.AccountId,
                            retriedBinding,
                            attemptToken);

                        if (attempts > 1)
                        {
                            return;
                        }

                        // The competing writer commits between this attempt's staging and its commit, which is the one
                        // ordering in which the unique index rather than either writer decides the outcome.
                        var winningCommit = await services.CommitAsync(
                            (winningScope, winningSession, winningToken) => winningScope
                                .GetRequiredService<IMailFolderResolutionStore>()
                                .SaveResolutionAsync(
                                    winningSession,
                                    SyntheticMailAccount.AccountId,
                                    retriedBinding,
                                    winningToken),
                            attemptToken);
                        Assert.Equal(PersistenceCommitResult.Committed, winningCommit);
                    },
                    token);

                return attempts;
            },
            cancellationToken);

        // Assert
        // Two rather than one, because a single attempt would mean the race never happened and the retry was never
        // exercised; the policy allows three, so anything above two would mean the retry conflicted as well.
        Assert.Equal(2, attemptCount);
        Assert.Equal(1, await CountBindingsOfAsync(services, retriedBinding, cancellationToken));
    }

    private static async Task<StoredEmailId> StoreMetadataAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        string subject,
        CancellationToken cancellationToken)
    {
        StoredEmailId? storedEmailId = null;

        var commitResult = await services.CommitAsync(
            async (scope, session, token) => storedEmailId = await scope
                .GetRequiredService<IEmailMetadataRepository>()
                .UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                    extractedMetadata: null,
                    StoredEmailContentAvailability.Available,
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailId!.Value;
    }

    private static async Task<StoredEmailId> StoreMetadataAndContentAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        string subject,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        var storedEmailId = await StoreMetadataAsync(services, occurrenceId, subject, cancellationToken);

        var contentCommit = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                session,
                storedEmailId,
                new RemoteEmailContent(occurrenceId, rawMime),
                token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, contentCommit);

        return storedEmailId;
    }

    private static Task<int> CountBindingsOfAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        var alias = binding.Alias.Value;
        var generation = binding.Generation.Value;

        return services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .MailFolders
                .AsNoTracking()
                .CountAsync(
                    folder => folder.MailboxAccountId == SyntheticMailAccount.AccountId.Value
                        && folder.Alias == alias
                        && folder.ResolutionGeneration == generation,
                    token),
            cancellationToken);
    }

    private static Task<int> CountAccountRowsOfAsync(
        OrchestratedMailFathomServices services,
        MailAccountId accountId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .MailboxAccounts
                .AsNoTracking()
                .CountAsync(account => account.Id == accountId.Value, token),
            cancellationToken);

    private static Task<string?> ReadSubjectAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .Where(storedEmail => storedEmail.Id == storedEmailId.Value)
                .Select(storedEmail => storedEmail.Subject)
                .SingleAsync(token),
            cancellationToken);
}
