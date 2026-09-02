// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
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

        // The two rows the sweep's two groups are: the first is what storing leaves behind — text, a search document,
        // and no passages — and the second is a message an account run already cut, outstanding only for its vectors.
        await OrchestratedPassages.CutAsync(services, storedBeforeTheProfile, cancellationToken);

        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        // Act
        var sweep = await SweepAsync(services, cancellationToken);
        var repeat = await RunOnceAsync(services, cancellationToken);

        // Assert
        Assert.True(sweep.ChunkedEmailCount > 0);

        // Counted rather than compared alone: the message the sweep had to cut ends with passages and a vector for each
        // of them, and two equal zeroes would report a sweep that never reached it as one that finished it.
        var cutPassageCount = await CountPassagesAsync(services, storedBeforeChunking, cancellationToken);
        Assert.True(cutPassageCount > 0);
        Assert.Equal(
            cutPassageCount,
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
        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        // Act
        await SweepAsync(services, cancellationToken);

        // Assert
        Assert.Equal(0, await CountPassagesAsync(services, unmapped, cancellationToken));
        Assert.Equal(0, await CountVectorsAsync(services, unmapped, profileId, cancellationToken));
        Assert.True(await CountPassagesAsync(services, mapped, cancellationToken) > 0);
        Assert.True(await CountVectorsAsync(services, mapped, profileId, cancellationToken) > 0);
    }

    /// <summary>
    /// The sweep obeys the arrival pipeline's order rather than a version of it: a message the owner's rules have not
    /// reached is not cut here, however long it has been sitting uncut.
    /// </summary>
    /// <remarks>
    /// This is the ordering that is easiest to lose, because the sweep runs on its own interval while an account run is
    /// still fetching a mailbox — so without it a first synchronization would have its mail cut by whichever of the two
    /// got there first, before a rule had read any of it. The stamped message beside it is the control: without one,
    /// zero passages would report a sweep that found nothing at all rather than one that passed this message over.
    /// </remarks>
    [Fact]
    public async Task Sweeping_MailTheRulesHaveNotReachedYet_CutsNothingAndEmbedsNothingOfIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var unevaluated = await StoreOneMessageAsync(services, uid: 9331, cancellationToken);
        var evaluated = await StoreOneMessageAsync(services, uid: 9332, cancellationToken);
        await OrchestratedRuleEvaluationStamp.ClearAsync(services, unevaluated, cancellationToken);
        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        // Act
        await SweepAsync(services, cancellationToken);

        // Assert
        Assert.Equal(0, await CountPassagesAsync(services, unevaluated, cancellationToken));
        Assert.Equal(0, await CountVectorsAsync(services, unevaluated, profileId, cancellationToken));
        Assert.True(await CountPassagesAsync(services, evaluated, cancellationToken) > 0);
        Assert.True(await CountVectorsAsync(services, evaluated, profileId, cancellationToken) > 0);
    }

    /// <summary>
    /// A message a rule has filed elsewhere is passed over too, until the move has actually landed: the stamp says the
    /// rules finished, and the record beside it says where the message is going.
    /// </summary>
    /// <remarks>
    /// A rule declares a move rather than performing one, and the account's next run carries it to the server, so
    /// between the two the message sits in a folder it is leaving. This sweep runs on its own interval inside that
    /// window, and passages are not undone by the message moving afterwards — so cutting it here would describe it under
    /// the mapping it is about to leave, permanently. The settled message beside it is the control: without one, zero
    /// passages would report a sweep that found nothing rather than one that passed this message over.
    /// </remarks>
    [Fact]
    public async Task Sweeping_MailARuleIsStillMoving_CutsNothingAndEmbedsNothingOfIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var relocating = await StoreOneMessageAsync(services, uid: 9341, cancellationToken);
        var settled = await StoreOneMessageAsync(services, uid: 9342, cancellationToken);
        await RecordConvergingRelocationAsync(services, relocating, uid: 9341, cancellationToken);
        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        // Act
        await SweepAsync(services, cancellationToken);

        // Assert
        Assert.Equal(0, await CountPassagesAsync(services, relocating, cancellationToken));
        Assert.Equal(0, await CountVectorsAsync(services, relocating, profileId, cancellationToken));
        Assert.True(await CountPassagesAsync(services, settled, cancellationToken) > 0);
        Assert.True(await CountVectorsAsync(services, settled, profileId, cancellationToken) > 0);
    }

    /// <summary>Writes down a relocation nothing has carried to the mail server yet.</summary>
    /// <remarks>
    /// Opened through the port the rule pass writes through rather than by inserting a row, so the record carries the
    /// stage a freshly authored move actually has — <see cref="MailboxMutationStage.Recorded" />, which is neither
    /// completed nor abandoned and is therefore exactly what the sweep has to pass over.
    /// </remarks>
    private static async Task RecordConvergingRelocationAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        uint uid,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);

        var record = default(MailboxMutationRecord);
        var commitResult = await services.CommitAsync(
            async (scope, session, token) => record = await scope
                .GetRequiredService<IMailboxMutationRecordStore>()
                .OpenAsync(
                    session,
                    MailboxMutationRequest.Relocate(
                        storedEmailId, SyntheticMailAccount.Owner,
                        occurrenceId,
                        MailboxMutationRequester.Rule("file-the-newsletters", "1"),
                        RemoteFolderPath.Create("Archive")),
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
        Assert.Equal(MailboxMutationStage.Recorded, record?.Stage);
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

    /// <summary>Stores one synthetic message with no passages, as an instance that predates chunking holds it.</summary>
    /// <remarks>
    /// The metadata write cuts nothing — the cut is a step of the account run rather than something a store does on its
    /// way past — so the message is stamped as one the rules have finished with, which is the state every cutting path
    /// waits for and the state a run would have left it in. What this seeds is therefore exactly the sweep's first
    /// group: text, no passages, and nothing still to happen to the message before it may be cut.
    /// </remarks>
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
                    session, SyntheticMailAccount.Owner,
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

        await OrchestratedRuleEvaluationStamp.ApplyAsync(services, storedEmailId, SyntheticEmail.SentAt, cancellationToken);

        return storedEmailId;
    }

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
