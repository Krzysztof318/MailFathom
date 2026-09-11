// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that two replicas sweeping one database embed each passage once, because a pass runs only under the sweep's hold.</summary>
/// <remarks>
/// <para>
/// Each replica is a service graph of its own over the orchestrated database, and each runs passes the way the embedding
/// backfill worker does: it asks for the sweep under the scope the worker names it by, runs one bounded pass under the
/// hold when that is granted, and asks again shortly when it is not. A running worker is not composed here, for the
/// reason the supervision test beside this one gives: what decides whether a second replica embeds is that hold being
/// granted, and the worker's own pacing would turn how many passes each replica got into a matter of timing.
/// </para>
/// <para>
/// The claim is counted rather than inferred. Every pass reports how many passages it gave a vector, and the vector
/// table grows by exactly the sum of those counts only when no passage was embedded by both replicas: a passage both
/// embedded is counted by two passes and stored once. The vectors come from the deterministic generator, so the
/// comparison costs no provider call.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmbeddingSweepHoldTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "embedding-sweep-hold";

    private const uint SeededMessageCount = 6;

    /// <summary>Bounds the passes one replica runs. A sweep that has not ended by then is a defect rather than a slow database.</summary>
    /// <remarks>Only granted passes count: how often a replica is refused depends on how long the other one's passes take.</remarks>
    private const int MaximumPassesPerReplica = 1_000;

    /// <summary>A lease long enough that nothing in the test expires underneath it, so only a pass ending moves the sweep.</summary>
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromMinutes(5);

    /// <summary>How long a replica refused the sweep waits before asking again: short, so the two contend for nearly every pass.</summary>
    private static readonly TimeSpan RefusedRetryDelay = TimeSpan.FromMilliseconds(10);

    /// <summary>Stops both loops should the sweep never be given back, which a refused replica would otherwise ask for forever.</summary>
    private static readonly TimeSpan SweepDeadline = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Sweeping_TwoReplicasContendForTheSweep_EmbedEachPassageOnce()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var firstReplica = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var secondReplica = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        List<StoredEmailId> storedEmails = [];
        for (uint uid = 1; uid <= SeededMessageCount; uid++)
        {
            storedEmails.Add(await StoreOneMessageAsync(firstReplica, uid, cancellationToken));
        }

        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(firstReplica, cancellationToken);
        var vectorsBefore = await CountVectorsAsync(firstReplica, profileId, cancellationToken);

        // Act
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(SweepDeadline);

        var embeddedByEachReplica = await Task.WhenAll(
            SweepUnderTheHoldAsync(firstReplica, deadline.Token),
            SweepUnderTheHoldAsync(secondReplica, deadline.Token));
        var vectorsAfter = await CountVectorsAsync(firstReplica, profileId, cancellationToken);

        // Assert
        Assert.Equal(vectorsAfter - vectorsBefore, embeddedByEachReplica.Sum());

        // Every seeded message ends with a vector per passage, so the equality above is not two zeroes: the sweep reached
        // this class's mail, whichever replica's passes it was.
        List<(int Passages, int Vectors)> coverage = [];
        foreach (var storedEmail in storedEmails)
        {
            coverage.Add((
                await CountPassagesAsync(firstReplica, storedEmail, cancellationToken),
                await CountVectorsOfAsync(firstReplica, profileId, storedEmail, cancellationToken)));
        }

        Assert.All(coverage, message =>
        {
            Assert.True(message.Passages > 0);
            Assert.Equal(message.Passages, message.Vectors);
        });
    }

    /// <summary>Asks for the sweep until one of this replica's own passes ends it, and answers with how many passages its passes embedded.</summary>
    /// <remarks>
    /// Ending on a pass of its own rather than on the other replica's is what lets both loops finish: once the sweep has
    /// ended, the next pass either replica takes starts a fresh one over mail that is already current and ends at once.
    /// </remarks>
    private static async Task<int> SweepUnderTheHoldAsync(
        OrchestratedMailFathomServices replica,
        CancellationToken cancellationToken)
    {
        var embeddedChunkCount = 0;
        var passCount = 0;

        while (passCount < MaximumPassesPerReplica)
        {
            using var hold = await TakeAsync(replica, cancellationToken);

            if (hold is null)
            {
                await Task.Delay(RefusedRetryDelay, cancellationToken);

                continue;
            }

            passCount++;

            var pass = await hold.RunWhileHeldAsync(token => RunOnePassAsync(replica, token), cancellationToken);
            embeddedChunkCount += pass.EmbeddedChunkCount;

            if (pass.Outcome == StoredEmailEmbeddingBackfillOutcome.SweepCompleted)
            {
                return embeddedChunkCount;
            }
        }

        Assert.Fail($"The sweep did not end within {MaximumPassesPerReplica} passes of one replica.");

        return embeddedChunkCount;
    }

    private static Task<WorkLeaseHold?> TakeAsync(
        OrchestratedMailFathomServices replica,
        CancellationToken cancellationToken) =>
        replica.InScopeAsync(
            (serviceScope, token) => WorkLeaseHold.TryTakeAsync(
                MailEmbeddingBackfillWorker.SweepScope,
                LeaseDuration,
                LeaseRenewalInterval,
                serviceScope.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<WorkLeaseHold>.Instance,
                TimeProvider.System,
                token),
            cancellationToken);

    /// <summary>
    /// Runs one pass over the real store, generator, and retry policy, bounded small enough that a sweep takes many passes
    /// and the two replicas contend for each of them.
    /// </summary>
    private static Task<StoredEmailEmbeddingBackfillResult> RunOnePassAsync(
        OrchestratedMailFathomServices replica,
        CancellationToken cancellationToken) => replica.InScopeAsync(
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
                        BatchSize = 2,
                        MaxBatchesPerRun = 1,
                    })
                    .RunAsync(Assert.IsType<RegisteredEmbeddingProfile>(generations.Target), token);
            },
            cancellationToken);

    /// <summary>Stores one synthetic message with text and no passages, stamped as one the rules have finished with, so the sweep cuts and embeds it.</summary>
    private static async Task<StoredEmailId> StoreOneMessageAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var subject = $"embedding-sweep-hold-{uid}";
        var storedEmailId = default(StoredEmailId);

        var commitResult = await services.CommitAsync(
            async (scope, session, token) => storedEmailId = await scope
                .GetRequiredService<IEmailMetadataRepository>()
                .UpsertMetadataAsync(
                    session,
                    SyntheticMailAccount.User,
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

    /// <summary>Counts every vector of one generation in the database, which is what both replicas' passes wrote into.</summary>
    private static Task<int> CountVectorsAsync(
        OrchestratedMailFathomServices services,
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailEmbeddings
                .AsNoTracking()
                .CountAsync(embedding => embedding.EmbeddingProfileId == profileId.Value, token),
            cancellationToken);

    private static Task<int> CountVectorsOfAsync(
        OrchestratedMailFathomServices services,
        EmbeddingProfileId profileId,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailEmbeddings
                .AsNoTracking()
                .CountAsync(
                    embedding => embedding.EmbeddingProfileId == profileId.Value
                        && embedding.EmailChunk!.StoredEmailId == storedEmailId.Value,
                    token),
            cancellationToken);
}
