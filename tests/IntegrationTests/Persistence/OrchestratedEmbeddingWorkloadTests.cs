// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Administration;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>
/// Proves the counting an operator is shown before agreeing to a provider bill, against a real schema.
/// </summary>
/// <remarks>
/// It is here rather than in the unit suite because none of it is decidable without PostgreSQL. The counts are a
/// correlated <c>NOT EXISTS</c> over the vector table keyed by a profile resolved from an identity fingerprint, a join
/// from messages to their passages, and a <c>SUM</c> over a character length — and a substitute for the database would
/// report the arranged figure whatever that translation produced, which is the one thing worth establishing.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmbeddingWorkloadTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "embedding-workload";

    /// <summary>Bounds the sweep loop. A run that has not finished by then is a defect rather than a slow database.</summary>
    private const int MaximumRunsPerSweep = 200;

    /// <summary>
    /// One test covering the whole reading, because the two answers are only meaningful against each other: a geometry
    /// nothing has registered owes every passage in the mailbox, and the geometry that has just embedded them owes not
    /// one. Asking them separately would leave either answer explainable by a query that ignored the fingerprint.
    /// </summary>
    [Fact]
    public async Task ReadWorkloadAsync_AMailboxEmbeddedUnderOneGeometry_OwesNothingUnderItAndEverythingUnderAnother()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await StoreOneMessageAsync(services, uid: 9401, cancellationToken);
        await StoreOneMessageAsync(services, uid: 9402, cancellationToken);
        await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);
        await SweepAsync(services, cancellationToken);

        var embedded = await ActiveGeometryAsync(services, cancellationToken);
        var neverActivated = AGeometryNothingHasRegistered();
        var searchable = await CountSearchableEmailsAsync(services, cancellationToken);
        var passages = await CountPassagesAsync(services, cancellationToken);
        var uncut = await CountUncutSearchableEmailsAsync(services, cancellationToken);

        // Act
        var underEmbedded = await ReadWorkloadAsync(services, embedded, cancellationToken);
        var underNeverActivated = await ReadWorkloadAsync(services, neverActivated, cancellationToken);

        // Assert
        Assert.Equal(searchable, underEmbedded.SearchableEmailCount);

        // Every passage carries a vector of this geometry, and what the space still owes is the mail nothing has cut.
        // That group is not zero here and a deployment's is: cutting is a step of an account run, so a suite whose
        // fixtures seed mail directly always leaves some behind, and asserting a plain zero would be asserting that no
        // other class in this collection stored a message — which is a claim about the suite rather than about the count.
        Assert.Equal(0, underEmbedded.OutstandingPassageCount);
        Assert.Equal(0, underEmbedded.OutstandingCharacterCount);
        Assert.Equal(uncut, underEmbedded.OutstandingEmailCount);
        Assert.Equal(searchable - uncut, underEmbedded.EmbeddedEmailCount);

        Assert.Equal(searchable, underNeverActivated.OutstandingEmailCount);
        Assert.Equal(passages, underNeverActivated.OutstandingPassageCount);
        Assert.True(underNeverActivated.OutstandingCharacterCount > 0);
        Assert.Equal(0, underNeverActivated.EmbeddedEmailCount);
    }

    private static Task<EmbeddingWorkload> ReadWorkloadAsync(
        OrchestratedMailFathomServices services,
        EmbeddingProfileIdentity geometry,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmbeddingWorkloadReader>()
                .ReadWorkloadAsync(EmbeddingProfileFingerprint.Compute(geometry), token),
            cancellationToken);

    private static Task<EmbeddingProfileIdentity> ActiveGeometryAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, _) => Task.FromResult(scope.GetRequiredService<ITextEmbeddingGenerator>().Identity),
            cancellationToken);

    /// <summary>A geometry no activation could have produced, which is what a declaration nobody took up looks like.</summary>
    private static EmbeddingProfileIdentity AGeometryNothingHasRegistered() => EmbeddingProfileIdentity.Create(
        "a-provider-nobody-declared",
        "a-model-nobody-declared",
        modelVersion: null,
        dimension: 8,
        EmbeddingDistanceMetric.Cosine,
        EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    /// <summary>Counts the messages a search may reach, which is what the reader measures coverage against.</summary>
    private static Task<int> CountSearchableEmailsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                .AsNoTracking()
                .Where(StoredEmailTombstone.IsNotTombstoned)
                .CountAsync(
                    email => email.Chunks.Any()
                        || (email.SearchDocument != null && email.SearchDocument.BodyText != null),
                    token),
            cancellationToken);

    /// <summary>Counts the searchable messages nothing has cut, which is what a vector space owes with no passage.</summary>
    /// <remarks>
    /// Mail every cutting path is still ahead of: a suite seeding messages directly runs no account run, so this group
    /// holds whatever the other classes in this collection stored and is what the reader reports outstanding after a
    /// sweep has embedded every passage that exists.
    /// </remarks>
    private static Task<int> CountUncutSearchableEmailsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                .AsNoTracking()
                .Where(StoredEmailTombstone.IsNotTombstoned)
                .CountAsync(
                    email => !email.Chunks.Any()
                        && email.SearchDocument != null
                        && email.SearchDocument.BodyText != null,
                    token),
            cancellationToken);

    private static Task<int> CountPassagesAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                .AsNoTracking()
                .Where(StoredEmailTombstone.IsNotTombstoned)
                .SelectMany(email => email.Chunks)
                .CountAsync(token),
            cancellationToken);

    /// <summary>Runs the backfill until the sweep ends, so every passage carries a vector of the active geometry.</summary>
    private static async Task SweepAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        StoredEmailEmbeddingBackfillResult run;
        var attempt = 0;

        do
        {
            run = await RunOnceAsync(services, cancellationToken);
            attempt++;
        }
        while (run.MoreWorkIsWorthTryingSoon && attempt < MaximumRunsPerSweep);

        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, run.Outcome);
    }

    private static Task<StoredEmailEmbeddingBackfillResult> RunOnceAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
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
                        BatchSize = 20,
                        MaxBatchesPerRun = 25,
                    })
                    .RunAsync(Assert.IsType<RegisteredEmbeddingProfile>(generations.Target), token);
            },
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
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var subject = $"embedding-workload-{uid}";
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

        await OrchestratedRuleEvaluationStamp.ApplyAsync(services, storedEmailId, SyntheticEmail.SentAt, cancellationToken);

        return storedEmailId;
    }
}
