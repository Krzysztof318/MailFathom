// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Emails;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the extraction backfill's walk shrinks as it commits, and resumes from what it recorded.</summary>
/// <remarks>
/// <para>
/// The walk is a keyset scan ordered by the stored email's <c>uuid</c> primary key, and both the ordering and the
/// resume comparison are evaluated by PostgreSQL. That is the point of the design — the walk never has to agree with how
/// the CLR compares two identifiers — and it is also why only a real server can establish that consecutive batches meet
/// exactly, with no email visited twice and none skipped.
/// </para>
/// <para>
/// The rows are inserted through the context rather than through the metadata repository, because what the backfill
/// selects is an email stored before extraction existed: the repository always writes a search document now, even for a
/// message whose body nothing read, so nothing it writes would ever appear in this walk.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedExtractionBackfillTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "extraction-backfill";

    private const int AwaitingEmailCount = 10;

    private const int BatchSize = 4;

    [Fact]
    public async Task GetEmailsAwaitingExtractionAsync_OverEmailsStoredBeforeExtractionExisted_WalksEachOnceAndResumesFromTheCommittedPosition()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var awaitingEmailIds = await InsertEmailsWithoutSearchDocumentsAsync(services, binding, cancellationToken);

        var resumePositionBeforeTheWalk = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IStoredEmailExtractionBackfillStore>()
                .FindResumePositionAsync(token),
            cancellationToken);

        // The walk is only this class's set while nothing else in the suite leaves an email without a document, so that
        // is checked rather than assumed: every other write path indexes what it stores.
        var initialBatch = await ReadBatchAsync(services, resumeAfter: null, cancellationToken);
        Assert.Equal(BatchSize, initialBatch.Count);

        // Act
        var walkedBatches = await WalkAsync(services, binding, cancellationToken);

        // Assert
        Assert.Null(resumePositionBeforeTheWalk);

        var walkedIds = (IReadOnlyList<StoredEmailId>)[.. walkedBatches.SelectMany(batch => batch)];
        Assert.Equal(awaitingEmailIds, walkedIds);
        Assert.Equal([BatchSize, BatchSize, AwaitingEmailCount - (2 * BatchSize)], walkedBatches.Select(batch => batch.Count));

        var committedResumePosition = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IStoredEmailExtractionBackfillStore>()
                .FindResumePositionAsync(token),
            cancellationToken);
        Assert.Equal(awaitingEmailIds[^1], committedResumePosition);

        // The walk is empty once every email it visited carries a document, and stays empty from the beginning of the
        // table rather than only from the recorded position: the predicate, not the cursor, is what makes it shrink.
        Assert.Empty(await ReadBatchAsync(services, resumeAfter: null, cancellationToken));
    }

    /// <summary>Runs the walk to exhaustion, committing each batch's extraction and its resume position together.</summary>
    private static async Task<IReadOnlyList<IReadOnlyList<StoredEmailId>>> WalkAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        var batches = new List<IReadOnlyList<StoredEmailId>>();
        StoredEmailId? resumeAfter = null;

        while (true)
        {
            var batch = await ReadBatchAsync(services, resumeAfter, cancellationToken);
            if (batch.Count == 0)
            {
                return batches;
            }

            var commitResult = await services.CommitAsync(
                async (scope, session, token) =>
                {
                    var backfillStore = scope.GetRequiredService<IStoredEmailExtractionBackfillStore>();

                    foreach (var storedEmailId in batch)
                    {
                        await backfillStore.ApplyExtractionAsync(
                            session,
                            storedEmailId,
                            SyntheticEmail.ExtractionOf(
                                SyntheticEmail.OccurrenceIn(binding, uid: 1),
                                "backfilled",
                                SyntheticEmail.BodyTextContaining("backfilled", wordCount: 8)),
                            token);
                    }

                    await backfillStore.SaveResumePositionAsync(session, batch[^1], token);
                },
                cancellationToken);

            Assert.Equal(PersistenceCommitResult.Committed, commitResult);

            batches.Add(batch);
            resumeAfter = batch[^1];
        }
    }

    private static Task<IReadOnlyList<StoredEmailId>> ReadBatchAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId? resumeAfter,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var awaitingEmails = await scope
                    .GetRequiredService<IStoredEmailExtractionBackfillStore>()
                    .GetEmailsAwaitingExtractionAsync(resumeAfter, BatchSize, token);

                return (IReadOnlyList<StoredEmailId>)[.. awaitingEmails.Select(email => email.StoredEmailId)];
            },
            cancellationToken);

    /// <summary>Inserts stored emails whose content is available and whose text nothing has derived yet.</summary>
    /// <remarks>
    /// The identifiers are minted from increasing timestamps, so the order the walk must visit them in is the order they
    /// were created and a failure names a position rather than an unordered set.
    /// </remarks>
    private static async Task<IReadOnlyList<StoredEmailId>> InsertEmailsWithoutSearchDocumentsAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        var alias = binding.Alias.Value;
        var generation = binding.Generation.Value;
        var insertedIds = new List<Guid>();

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var dbContext = scope.GetRequiredService<MailFathomDbContext>();
                var folder = await dbContext.MailFolders.SingleAsync(
                    candidate => candidate.MailboxAccountId == SyntheticMailAccount.AccountId.Value
                        && candidate.Alias == alias
                        && candidate.ResolutionGeneration == generation,
                    token);

                foreach (var index in Enumerable.Range(0, AwaitingEmailCount))
                {
                    var storedEmail = new StoredEmailEntity
                    {
                        Id = Guid.CreateVersion7(SyntheticEmail.SentAt.AddSeconds(index)),
                        MailboxAccountId = folder.MailboxAccountId,
                        MailFolder = folder,
                        UidValidity = SyntheticEmail.UidValidity,
                        Uid = (uint)(2000 + index),
                        Subject = $"awaiting-extraction-{index:D2}",
                        SizeOctets = 2048,
                        ContentAvailability = StoredEmailContentAvailability.Available,
                    };

                    dbContext.StoredEmails.Add(storedEmail);
                    insertedIds.Add(storedEmail.Id);
                }
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return [.. insertedIds.Order().Select(StoredEmailId.Create)];
    }
}
