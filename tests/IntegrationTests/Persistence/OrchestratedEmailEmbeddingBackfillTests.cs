// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>
/// Proves the sweep over pre-existing mail against a real schema: which messages it selects, that it cuts one with no
/// passages before embedding it, and that the position it commits is what a later run resumes past.
/// </summary>
/// <remarks>
/// None of it is reachable without a real server. What the walk selects is a disjunction of two correlated
/// <c>NOT EXISTS</c> forms over the passage and vector tables plus a keyset comparison PostgreSQL evaluates under its
/// own <c>uuid</c> ordering, and a substitute for the database would report whatever the test told it to whatever the
/// translation actually produced. The vectors come from the deterministic generator, so the whole path costs no
/// provider call.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailEmbeddingBackfillTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "embedding-backfill";

    /// <summary>Bounds the sweep loop. A run that has not finished by then is a defect rather than a slow database.</summary>
    private const int MaximumRunsPerSweep = 200;

    /// <summary>
    /// The whole walk over a real schema. A message stored before chunking existed carries extracted text and no
    /// passages, and this is what gives it both — in that order, because the vectors it ends up with can only have come
    /// from passages this run cut. A message that was already current is not selected again, which is what makes a
    /// repeated sweep cost queries rather than a provider bill.
    /// </summary>
    [Fact]
    public async Task Sweeping_MailStoredBeforeTheProfileExisted_CutsWhatHasNoPassagesAndEmbedsEverythingOutstanding()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var storedBeforeChunking = await StoreOneMessageAsync(services, uid: 9301, cancellationToken);
        var storedBeforeTheProfile = await StoreOneMessageAsync(services, uid: 9302, cancellationToken);

        // What a row written before chunking existed looks like: the extraction and its search document are there and
        // the passages are not, which no synchronization run can produce today.
        var removedPassageCount = await RemovePassagesAsync(services, storedBeforeChunking, cancellationToken);
        Assert.True(removedPassageCount > 0);

        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        // Act
        var sweep = await SweepAsync(services, cancellationToken);
        var repeat = await RunOnceAsync(services, cancellationToken);

        // Assert
        Assert.True(sweep.ChunkedEmailCount > 0);
        Assert.Equal(
            await CountPassagesAsync(services, storedBeforeChunking, cancellationToken),
            await CountVectorsAsync(services, storedBeforeChunking, profileId, cancellationToken));
        Assert.Equal(
            await CountPassagesAsync(services, storedBeforeTheProfile, cancellationToken),
            await CountVectorsAsync(services, storedBeforeTheProfile, profileId, cancellationToken));

        // A sweep that has just finished has nothing in front of it, and the message it cut is not cut again.
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, repeat.Outcome);
        Assert.Equal(0, repeat.ChunkedEmailCount);
        Assert.Equal(0, repeat.EmbeddedChunkCount);
    }

    /// <summary>
    /// A run that spends its batch budget leaves a position behind, and the next run continues past it rather than
    /// starting again — and reaching the end removes the row, so the sweep after that starts from the beginning and
    /// picks up whatever a refused call left without a vector.
    /// </summary>
    [Fact]
    public async Task ARunThatSpendsItsBatchBudget_LeavesAPositionTheNextRunResumesPastAndClearsItAtTheEnd()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);
        await SweepAsync(services, cancellationToken);

        // Three messages behind everything already embedded, so the walk has exactly this much left to find.
        var messages = new List<StoredEmailId>();
        foreach (var uid in (uint[])[9311, 9312, 9313])
        {
            messages.Add(await StoreOneMessageAsync(services, uid, cancellationToken));
        }

        // Act
        var first = await RunOnceAsync(services, cancellationToken, batchSize: 1, maxBatchesPerRun: 1);
        var positionAfterFirst = await ReadResumePositionAsync(services, cancellationToken);
        var rest = await SweepAsync(services, cancellationToken, batchSize: 1, maxBatchesPerRun: 1);
        var positionAfterSweep = await ReadResumePositionAsync(services, cancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.BatchBudgetSpent, first.Outcome);
        Assert.Equal(messages[0], positionAfterFirst);
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, rest.Outcome);
        Assert.Null(positionAfterSweep);
    }

    /// <summary>
    /// Mail stored under an alias no mapping names is outside the walk, so a folder an operator withdrew never becomes
    /// a bill: nothing of it is cut and nothing of it is embedded, however long the deployment runs.
    /// </summary>
    /// <remarks>
    /// The walk is where this has to be proved rather than at the cut, because the two answer different halves. A
    /// message with a body and no passages is exactly what the sweep's second group selects, so a folder left out only
    /// at the cut would be found outstanding on every sweep for the rest of the deployment's life. The mapped message
    /// beside it is the control: without it, zero passages would report a query that selected nothing at all.
    /// </remarks>
    [Fact]
    public async Task Sweeping_MailInAFolderNoMappingNames_CutsNothingAndEmbedsNothingOfIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var unmapped = await StoreOneMessageAsync(
            services,
            uid: 9321,
            cancellationToken,
            SyntheticMailAccount.UnmappedFolderAlias);
        var mapped = await StoreOneMessageAsync(services, uid: 9322, cancellationToken);
        await RemovePassagesAsync(services, mapped, cancellationToken);
        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        // Act
        await SweepAsync(services, cancellationToken);

        // Assert
        Assert.Equal(0, await CountPassagesAsync(services, unmapped, cancellationToken));
        Assert.Equal(0, await CountVectorsAsync(services, unmapped, profileId, cancellationToken));
        Assert.True(await CountPassagesAsync(services, mapped, cancellationToken) > 0);
        Assert.True(await CountVectorsAsync(services, mapped, profileId, cancellationToken) > 0);
    }

    /// <summary>Runs the backfill until the sweep ends, and answers with the last run that did any work.</summary>
    private static async Task<StoredEmailEmbeddingBackfillResult> SweepAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken,
        int batchSize = 20,
        int maxBatchesPerRun = 25)
    {
        var chunkedEmailCount = 0;
        var embeddedEmailCount = 0;
        var embeddedChunkCount = 0;
        StoredEmailEmbeddingBackfillResult run;
        var attempt = 0;

        do
        {
            run = await RunOnceAsync(services, cancellationToken, batchSize, maxBatchesPerRun);
            chunkedEmailCount += run.ChunkedEmailCount;
            embeddedEmailCount += run.EmbeddedEmailCount;
            embeddedChunkCount += run.EmbeddedChunkCount;
            attempt++;
        }
        while (run.MoreWorkIsWorthTryingSoon && attempt < MaximumRunsPerSweep);

        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, run.Outcome);

        return run with
        {
            ChunkedEmailCount = chunkedEmailCount,
            EmbeddedEmailCount = embeddedEmailCount,
            EmbeddedChunkCount = embeddedChunkCount,
        };
    }

    /// <summary>
    /// Builds the backfill by hand so a test chooses its own bounds, over the real store, generator, and retry policy
    /// the container resolves.
    /// </summary>
    private static Task<StoredEmailEmbeddingBackfillResult> RunOnceAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken,
        int batchSize = 20,
        int maxBatchesPerRun = 25) => services.InScopeAsync(
            async (scope, token) =>
            {
                var generations = await scope.GetRequiredService<IEmbeddingGenerationStore>()
                    .ReadGenerationsAsync(token);

                return await new StoredEmailEmbeddingBackfill(
                    scope.GetRequiredService<IStoredEmailEmbeddingBackfillStore>(),
                    scope.GetRequiredService<StoredEmailEmbeddingGenerator>(),
                    scope.GetRequiredService<OptimisticConcurrencyRetryPolicy>(),
                    scope.GetRequiredService<IDerivedWorkGateTelemetry>(),
                    new StoredEmailEmbeddingBackfillOptions
                    {
                        BatchSize = batchSize,
                        MaxBatchesPerRun = maxBatchesPerRun,
                    })
                    .RunAsync(Assert.IsType<RegisteredEmbeddingProfile>(generations.Target), token);
            },
            cancellationToken);

    private static Task<StoredEmailId?> ReadResumePositionAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailEmbeddingBackfillStore>()
                .FindResumePositionAsync(token),
            cancellationToken);

    /// <summary>Stores one synthetic message, whose passages the chunk writer derives in the same session.</summary>
    private static async Task<StoredEmailId> StoreOneMessageAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        CancellationToken cancellationToken,
        string folderAlias = FolderAlias)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, folderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var subject = $"embedding-backfill-{uid}";
        var storedEmailId = default(StoredEmailId);

        var commitResult = await services.CommitAsync(
            async (scope, session, token) => storedEmailId = await scope
                .GetRequiredService<IEmailMetadataRepository>()
                .UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                    SyntheticEmail.ExtractionOf(
                        occurrenceId,
                        subject,
                        SyntheticEmail.BodyTextContaining(subject, wordCount: 400),
                        "recipient@mailfathom.test"),
                    StoredEmailContentAvailability.Available,
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailId;
    }

    /// <summary>Removes one message's passages, leaving the row shape an instance that predates chunking has.</summary>
    private static Task<int> RemovePassagesAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailChunks
                .Where(chunk => chunk.StoredEmailId == storedEmailId.Value)
                .ExecuteDeleteAsync(token),
            cancellationToken);

    private static Task<int> CountPassagesAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailChunks
                .AsNoTracking()
                .CountAsync(chunk => chunk.StoredEmailId == storedEmailId.Value, token),
            cancellationToken);

    private static Task<int> CountVectorsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailEmbeddings
                .AsNoTracking()
                .CountAsync(
                    embedding => embedding.EmbeddingProfileId == profileId.Value
                        && embedding.EmailChunk!.StoredEmailId == storedEmailId.Value,
                    token),
            cancellationToken);
}
