// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.EmailContent;
using MailMcp.Application.Folders;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Infrastructure.Persistence;
using MailMcp.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailMcp.IntegrationTests.Persistence;

/// <summary>Proves what the transaction a persistence session opens is worth, and which failures it reports as conflicts.</summary>
/// <remarks>
/// <para>
/// The session's contract is three claims a substitute can only restate: work that was never committed leaves nothing
/// behind even when the provider already sent it to the server, a losing writer on a recognized unique constraint gets a
/// result rather than an exception, and a stale <c>xmin</c> token is recognized as an optimistic conflict. All three are
/// properties of PostgreSQL's behavior under two overlapping transactions.
/// </para>
/// <para>
/// Every one of them therefore uses two sessions whose lifetimes overlap, arranged so neither can see the other's
/// uncommitted work — the shape two synchronization runs over one folder produce, and the only arrangement in which the
/// database rather than the application decides the outcome.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedPersistenceSessionTests(MailMcpOrchestrationFixture orchestration)
{
    private const string FolderAlias = "persistence-session";

    /// <summary>The alias the binding race binds for the first time, which no other test writes.</summary>
    private const string ContestedFolderAlias = "persistence-session-race";

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
        await using var services = await OrchestratedMailMcpServices.StartAsync(orchestration, cancellationToken);
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
    /// decides the winner, so nothing short of two real overlapping transactions can establish the behavior.
    /// </remarks>
    [Fact]
    public async Task CommitAsync_WhenAnotherWriterBoundTheSameAliasFirst_ReportsAConcurrencyConflict()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailMcpServices.StartAsync(orchestration, cancellationToken);
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

    /// <summary>Proves the <c>xmin</c> token detects a revision another transaction committed first.</summary>
    /// <remarks>
    /// The stored email's concurrency token is PostgreSQL's own <c>xmin</c> system column rather than a column MailMcp
    /// writes, so nothing in the process updates it and no substitute can make it go stale. Reading a row in one
    /// transaction, letting another revise and commit it, and only then writing is the arrangement that makes the token
    /// the loser holds no longer the row's.
    /// </remarks>
    [Fact]
    public async Task CommitAsync_WhenAnotherTransactionRevisedTheStoredEmailFirst_ReportsAConcurrencyConflict()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailMcpServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, ConflictingUid);

        var storedEmailId = await StoreMetadataAsync(services, occurrenceId, "concurrency-original", cancellationToken);

        // Act
        var losingCommit = await services.InScopeAsync(
            async (losingScope, token) =>
            {
                // The scoped context is the one the session enlists, so a row tracked here carries the token this
                // session will write against.
                var losingContext = losingScope.GetRequiredService<MailMcpDbContext>();

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

    private static async Task<StoredEmailId> StoreMetadataAsync(
        OrchestratedMailMcpServices services,
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
        OrchestratedMailMcpServices services,
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
        OrchestratedMailMcpServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        var alias = binding.Alias.Value;
        var generation = binding.Generation.Value;

        return services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailMcpDbContext>()
                .MailFolders
                .AsNoTracking()
                .CountAsync(
                    folder => folder.MailboxAccountId == SyntheticMailAccount.AccountId.Value
                        && folder.Alias == alias
                        && folder.ResolutionGeneration == generation,
                    token),
            cancellationToken);
    }

    private static Task<string?> ReadSubjectAsync(
        OrchestratedMailMcpServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailMcpDbContext>()
                .StoredEmails
                .AsNoTracking()
                .Where(storedEmail => storedEmail.Id == storedEmailId.Value)
                .Select(storedEmail => storedEmail.Subject)
                .SingleAsync(token),
            cancellationToken);
}
