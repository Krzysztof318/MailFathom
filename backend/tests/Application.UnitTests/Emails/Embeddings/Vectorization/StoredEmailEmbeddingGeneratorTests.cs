// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Vectorization;

public sealed class StoredEmailEmbeddingGeneratorTests
{
    private static readonly StoredEmailId Message = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly EmbeddingProfileId ProfileId = EmbeddingProfileId.Create(Guid.CreateVersion7());

    /// <summary>A moment the daily period places on a whole day, so a test's expected roll-over is arithmetic rather than a guess.</summary>
    private static readonly DateTimeOffset PeriodStart = new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EmbedAsync_ActiveProfileAndOutstandingPassages_EmbedsEveryPassage()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        var passages = CreatePassages(3);
        store.AddPassages(Message, passages);
        var generator = CreateGenerator(store, maximumPassagesPerCall: 8);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, run.Outcome);
        Assert.Equal(3, run.EmbeddedChunkCount);
        Assert.Null(run.Failure);
        Assert.Equal(passages.Select(passage => passage.Id).ToArray(), store.EmbeddedPassages);
    }

    [Fact]
    public async Task EmbedAsync_MorePassagesThanOneCallAccepts_SendsBoundedBatchesAndCommitsEachOne()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(5));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 2);
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, run.EmbeddedChunkCount);
        Assert.Equal([2, 2, 1], textEmbeddingGenerator.RequestedBatches.Select(batch => batch.Count).ToArray());
        Assert.Equal(3, store.WriteCount);
    }

    [Fact]
    public async Task EmbedAsync_MessageAlreadyEmbedded_ProducesNothingAndCallsNoProvider()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(2));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 8);
        var generator = CreateGenerator(store, textEmbeddingGenerator);
        await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Act
        var repeat = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, repeat.Outcome);
        Assert.Equal(0, repeat.EmbeddedChunkCount);
        Assert.Single(textEmbeddingGenerator.RequestedBatches);
        Assert.Equal(2, store.StoredVectors.Count);
    }

    /// <summary>
    /// The generation is the caller's decision, which is what lets a reindex fill a new one while the live path goes on
    /// embedding arriving mail into the one still answering searches. A generator that resolved it itself could serve
    /// only one of the two, and the vectors of the other would silently land under the wrong attribution.
    /// </summary>
    [Fact]
    public async Task EmbedAsync_GenerationBeingBuilt_AttributesEveryVectorToThatGenerationAlone()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(2));
        var generator = CreateGenerator(store, maximumPassagesPerCall: 8);
        var building = new RegisteredEmbeddingProfile(
            EmbeddingProfileId.Create(Guid.CreateVersion7()),
            CreateIdentity());

        // Act
        var run = await generator.EmbedAsync(Message, building, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, run.Outcome);
        Assert.Equal([building.Id, building.Id], store.StoredVectors.Keys.Select(key => key.ProfileId).ToArray());
        Assert.DoesNotContain(ProfileId, store.StoredVectors.Keys.Select(key => key.ProfileId));
    }

    [Fact]
    public async Task EmbedAsync_ConfiguredModelIsNotTheActivatedOne_RefusesToWriteIntoTheActiveProfile()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(2));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(
            CreateIdentity(modelIdentifier: "a-newer-model"),
            maximumPassagesPerCall: 8);
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.GeneratorDisagreesWithProfile, run.Outcome);
        Assert.Empty(textEmbeddingGenerator.RequestedBatches);
        Assert.Empty(store.StoredVectors);
    }

    [Theory]
    [InlineData(EmbeddingGenerationFailure.CredentialRejected)]
    [InlineData(EmbeddingGenerationFailure.RateLimited)]
    [InlineData(EmbeddingGenerationFailure.RequestTimedOut)]
    [InlineData(EmbeddingGenerationFailure.TransportFaulted)]
    [InlineData(EmbeddingGenerationFailure.RequestRefused)]
    [InlineData(EmbeddingGenerationFailure.VectorShapeUnexpected)]
    public async Task EmbedAsync_ProviderUnavailable_ReportsTheClassificationWithoutRepeatingTheCall(
        EmbeddingGenerationFailure failure)
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(2));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 8)
        {
            Failure = failure,
        };
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.ProviderFailed, run.Outcome);
        Assert.Equal(failure, run.Failure);
        Assert.Single(textEmbeddingGenerator.RequestedBatches);
        Assert.Empty(store.StoredVectors);
    }

    [Fact]
    public async Task EmbedAsync_ProviderFailsPartWayThrough_KeepsWhatWasCommittedAndLeavesTheRestOutstanding()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(4));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 2)
        {
            Failure = EmbeddingGenerationFailure.RateLimited,
            FailingCallNumber = 2,
        };
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.ProviderFailed, run.Outcome);
        Assert.Equal(2, run.EmbeddedChunkCount);
        Assert.Equal(2, store.StoredVectors.Count);
    }

    /// <summary>
    /// A batch size far below what one message carries is a supported configuration, so a message can need more calls
    /// than a turn is allowed. Reporting that as <see cref="StoredEmailEmbeddingOutcome.Embedded" /> would say the
    /// message is whole when it is not — a truncated message stays retrievable and simply answers worse, so nothing
    /// later would notice.
    /// </summary>
    [Fact]
    public async Task EmbedAsync_MoreCallsThanOneTurnAllows_ReportsTheMessageAsUnfinishedRatherThanEmbedded()
    {
        // Arrange
        const int callBudget = 512;
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(callBudget + 5));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 1);
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.CallBudgetExhausted, run.Outcome);
        Assert.Equal(callBudget, run.EmbeddedChunkCount);
        Assert.Equal(callBudget, textEmbeddingGenerator.RequestedBatches.Count);

        // What it did embed stays durable, which is what leaves the rest outstanding for the backfill rather than lost.
        Assert.Equal(callBudget, store.StoredVectors.Count);
    }

    /// <summary>
    /// The last call taking the final passages leaves the loop with nowhere to go rather than with work left, and
    /// reporting that message as truncated would be a false warning exactly as the opposite is a false success.
    /// </summary>
    [Fact]
    public async Task EmbedAsync_TheLastAllowedCallTakesTheFinalPassages_ReportsTheMessageAsEmbedded()
    {
        // Arrange
        const int callBudget = 512;
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(callBudget));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 1);
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, run.Outcome);
        Assert.Equal(callBudget, run.EmbeddedChunkCount);
        Assert.Equal(callBudget, textEmbeddingGenerator.RequestedBatches.Count);
    }

    [Fact]
    public async Task EmbedAsync_CancelledWhileTheProviderIsAnswering_StopsWithoutStartingAnotherCall()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(4));
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 2)
        {
            CancelOnCall = cancellation,
        };
        var generator = CreateGenerator(store, textEmbeddingGenerator);

        // Act
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => generator.EmbedAsync(Message, CreateProfile(), cancellation.Token));

        // Assert
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.Single(textEmbeddingGenerator.RequestedBatches);
    }

    /// <summary>A turn asks the ceiling before every call, so an exhausted period buys no provider request at all.</summary>
    [Fact]
    public async Task EmbedAsync_ThePeriodIsAlreadySpent_SendsNothingAndSaysWhenTheCeilingLifts()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(3));
        var ledger = new InMemoryEmbeddingSpendLedger();
        ledger.Seed(PeriodStart, SyntheticMailOwner.Deployment, inputCharacterCount: 500);
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 8);
        var generator = CreateGenerator(
            store,
            textEmbeddingGenerator,
            CreateSpendGate(ledger, EmbeddingSpendBudget.Create(500, 0, TimeSpan.FromDays(1))));

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.SpendCeilingReached, run.Outcome);
        Assert.Equal(0, run.EmbeddedChunkCount);
        Assert.Equal(PeriodStart.AddDays(1), run.SpendPeriodEndsAt);
        Assert.Empty(textEmbeddingGenerator.RequestedBatches);
        Assert.Empty(store.EmbeddedPassages);
    }

    /// <summary>
    /// A batch is admitted whenever anything at all is left and is then paid for whole, so the ceiling binds on the
    /// call after the one that crossed it. Weighing a batch against what remains instead would stall a deployment whose
    /// ceiling is smaller than one batch for ever.
    /// </summary>
    [Fact]
    public async Task EmbedAsync_TheCeilingIsCrossedMidTurn_PaysForThatBatchAndStopsBeforeTheNext()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(4));
        var ledger = new InMemoryEmbeddingSpendLedger();
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 2);
        var generator = CreateGenerator(
            store,
            textEmbeddingGenerator,
            CreateSpendGate(ledger, EmbeddingSpendBudget.Create(1, 0, TimeSpan.FromDays(1))));

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.SpendCeilingReached, run.Outcome);
        Assert.Equal(2, run.EmbeddedChunkCount);
        Assert.Single(textEmbeddingGenerator.RequestedBatches);
        Assert.Equal(run.InputCharacterCount, ledger.ConsumedByPeriod[PeriodStart]);
    }

    /// <summary>What a turn sent is charged to the period beside the vectors it produced, in the characters sent.</summary>
    [Fact]
    public async Task EmbedAsync_APassageIsEmbedded_ChargesThePeriodWhatThePreparationWouldHaveSent()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        var passages = CreatePassages(3);
        store.AddPassages(Message, passages);
        var ledger = new InMemoryEmbeddingSpendLedger();
        var profile = CreateProfile();
        var generator = CreateGenerator(
            store,
            new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 8),
            CreateSpendGate(ledger, EmbeddingSpendBudget.Unbounded));

        // Act
        var run = await generator.EmbedAsync(Message, profile, TestContext.Current.CancellationToken);

        // Assert
        var expected = passages.Sum(passage =>
            profile.Identity.InputPreparation.CountBilledCharacters(passage.Text));
        Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, run.Outcome);
        Assert.Equal(expected, run.InputCharacterCount);
        Assert.Equal(expected, ledger.ConsumedByPeriod[PeriodStart]);
    }

    /// <summary>A refused call produced no vectors, so charging a period for it would let an outage spend the ceiling.</summary>
    [Fact]
    public async Task EmbedAsync_TheProviderRefuses_ChargesThePeriodNothingForTheFailedCall()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(2));
        var ledger = new InMemoryEmbeddingSpendLedger();
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 8)
        {
            Failure = EmbeddingGenerationFailure.RateLimited,
        };
        var generator = CreateGenerator(
            store,
            textEmbeddingGenerator,
            CreateSpendGate(ledger, EmbeddingSpendBudget.Unbounded));

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.ProviderFailed, run.Outcome);
        Assert.Equal(0, run.InputCharacterCount);
        Assert.Empty(ledger.ConsumedByPeriod);
    }

    /// <summary>A refusal names which ceiling it met, because the two need different actions from an operator.</summary>
    [Fact]
    public async Task EmbedAsync_ThisOwnerHasSpentTheirShare_SaysTheOwnerReachedTheirCeilingRatherThanTheDeployment()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(3));
        var ledger = new InMemoryEmbeddingSpendLedger();
        ledger.Seed(PeriodStart, SyntheticMailOwner.Another, inputCharacterCount: 500);
        var textEmbeddingGenerator = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 8);
        var generator = CreateGenerator(
            store,
            textEmbeddingGenerator,
            CreateSpendGate(ledger, EmbeddingSpendBudget.Create(10_000, 500, TimeSpan.FromDays(1))),
            new StubMailOwnership().Owns(Message, SyntheticMailOwner.Another));

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.SpendCeilingReached, run.Outcome);
        Assert.Equal(EmbeddingSpendBound.Owner, run.ReachedSpendBound);
        Assert.Empty(textEmbeddingGenerator.RequestedBatches);
    }

    /// <summary>An owner at their share stops that owner's mail, and somebody else's message is embedded whole.</summary>
    /// <remarks>
    /// The two runs share one ledger and one budget, which is what makes the claim about the ceiling rather than about
    /// two generators that happened to be configured differently.
    /// </remarks>
    [Fact]
    public async Task EmbedAsync_OneOwnerIsAtTheirShare_StillEmbedsAnotherOwnersMessage()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        var otherMessage = StoredEmailId.Create(Guid.CreateVersion7());
        store.AddPassages(Message, CreatePassages(2));
        store.AddPassages(otherMessage, CreatePassages(2));
        var ledger = new InMemoryEmbeddingSpendLedger();
        ledger.Seed(PeriodStart, SyntheticMailOwner.Another, inputCharacterCount: 500);
        var generator = CreateGenerator(
            store,
            new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 8),
            CreateSpendGate(ledger, EmbeddingSpendBudget.Create(10_000, 500, TimeSpan.FromDays(1))),
            new StubMailOwnership().Owns(Message, SyntheticMailOwner.Another));

        // Act
        var refused = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);
        var embedded = await generator.EmbedAsync(otherMessage, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingOutcome.SpendCeilingReached, refused.Outcome);
        Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, embedded.Outcome);
        Assert.Equal(2, embedded.EmbeddedChunkCount);
    }

    /// <summary>What a turn spent is charged to whoever the message belongs to, not to whoever ran the worker.</summary>
    [Fact]
    public async Task EmbedAsync_AMessageOfAnotherOwner_ChargesThatOwnersRowRatherThanTheDefault()
    {
        // Arrange
        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, CreatePassages(3));
        var ledger = new InMemoryEmbeddingSpendLedger();
        var generator = CreateGenerator(
            store,
            new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 8),
            CreateSpendGate(ledger, EmbeddingSpendBudget.Unbounded),
            new StubMailOwnership().Owns(Message, SyntheticMailOwner.Another));

        // Act
        var run = await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(run.InputCharacterCount, ledger.ConsumedByPeriodAndOwner[(PeriodStart, SyntheticMailOwner.Another)]);
        Assert.False(ledger.ConsumedByPeriodAndOwner.ContainsKey((PeriodStart, SyntheticMailOwner.Deployment)));
    }

    /// <summary>
    /// The passages leaving for a hosted provider are scanned under the posture of the owner whose message they were
    /// cut from rather than the deployment's. Nothing else says so: the generator opens its scope from the ownership it
    /// read, and one naming the wrong owner would publish one person's body text judged by another person's answer,
    /// while one naming nobody would fail only on a deployment that scans somebody.
    /// </summary>
    [Fact]
    public async Task EmbedAsync_TwoOwnersScannedDifferently_SendsEachOwnersPassagesUnderTheirOwnPosture()
    {
        // Arrange
        const string marker = "AKIAEXAMPLEKEY";

        var scannedOwnersMessage = StoredEmailId.Create(Guid.CreateVersion7());
        var scanner = new MarkerSensitiveContentScanner(
            marker,
            SensitiveContentScannerKind.Secrets,
            TimeProvider.System);
        var plan = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [
                SensitiveContentScannerPlan.Create(
                    scanner.Scanner,
                    [MarkerSensitiveContentScanner.Category],
                    []),
            ]);
        using var permits = new SensitiveContentScanConcurrency(plan.Bounds.MaximumConcurrentScans);

        var postures = FixedSensitiveContentPostures.Of(
            SensitiveContentPosture.ScanningNothing,
            (SyntheticMailOwner.Another, SensitiveContentPosture.Scanning(
                [scanner.Scanner],
                new SensitiveContentRedactor(plan, [scanner], TimeProvider.System, permits),
                SensitiveContentScreeningPolicy.ScreeningNothing(),
                SensitiveContentDerivationStamp.Compute(plan, [scanner]))));

        var store = new InMemoryEmailEmbeddingStore();
        store.AddPassages(Message, PassageCarrying(marker));
        store.AddPassages(scannedOwnersMessage, PassageCarrying(marker));

        var egressGuard = new SensitiveContentEgressGuard(
            postures,
            new RecordingSensitiveContentEgressTelemetry(),
            TimeProvider.System);
        var provider = new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall: 8)
        {
            EgressGuard = egressGuard,
        };
        var generator = CreateGenerator(
            store,
            provider,
            ownership: new StubMailOwnership().Owns(scannedOwnersMessage, SyntheticMailOwner.Another),
            egressGuard: egressGuard);

        // Act
        await generator.EmbedAsync(Message, CreateProfile(), TestContext.Current.CancellationToken);
        await generator.EmbedAsync(scannedOwnersMessage, CreateProfile(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(marker, Assert.Single(provider.RequestedBatches[0]), StringComparison.Ordinal);
        Assert.DoesNotContain(marker, Assert.Single(provider.RequestedBatches[1]), StringComparison.Ordinal);
    }

    private static IReadOnlyList<EmailChunkAwaitingEmbedding> PassageCarrying(string marker) =>
    [
        new EmailChunkAwaitingEmbedding(
            EmailChunkId.Create(Guid.CreateVersion7()),
            $"the key is {marker}, in a message somebody sent"),
    ];

    private static IReadOnlyList<EmailChunkAwaitingEmbedding> CreatePassages(int count) =>
        [.. Enumerable.Range(0, count).Select(ordinal => new EmailChunkAwaitingEmbedding(
            EmailChunkId.Create(Guid.CreateVersion7()),
            $"passage {ordinal}"))];

    private static EmbeddingProfileIdentity CreateIdentity(string modelIdentifier = "a-model") =>
        EmbeddingProfileIdentity.Create(
            "a-provider",
            modelIdentifier,
            modelVersion: null,
            dimension: 8,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    private static RegisteredEmbeddingProfile CreateProfile() => new(ProfileId, CreateIdentity());

    private static StoredEmailEmbeddingGenerator CreateGenerator(
        IEmailEmbeddingStore store,
        int maximumPassagesPerCall) =>
        CreateGenerator(store, new ScriptedTextEmbeddingGenerator(CreateIdentity(), maximumPassagesPerCall));

    private static StoredEmailEmbeddingGenerator CreateGenerator(
        IEmailEmbeddingStore store,
        ITextEmbeddingGenerator textEmbeddingGenerator,
        EmbeddingSpendGate? spendGate = null,
        IMailOwnership? ownership = null,
        SensitiveContentEgressGuard? egressGuard = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        return new StoredEmailEmbeddingGenerator(
            store,
            textEmbeddingGenerator,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider()),
            spendGate ?? CreateSpendGate(new InMemoryEmbeddingSpendLedger(), EmbeddingSpendBudget.Unbounded),
            EmbeddingRequestPacer.Create(maxRequestsPerMinute: 0, new FakeTimeProvider()),
            ownership ?? new StubMailOwnership(),
            egressGuard ?? SensitiveContentEgressGuards.Inactive());
    }

    private static EmbeddingSpendGate CreateSpendGate(
        IEmbeddingSpendLedger ledger,
        EmbeddingSpendBudget budget) =>
        new(ledger, budget, new FakeTimeProvider(PeriodStart));
}
