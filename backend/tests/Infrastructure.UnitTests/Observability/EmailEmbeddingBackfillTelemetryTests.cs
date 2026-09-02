// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

public sealed class EmailEmbeddingBackfillTelemetryTests
{
    private const string RunCountInstrument = "mailfathom.embedding.backfill.runs";

    private const string ChunkedCountInstrument = "mailfathom.embedding.backfill.chunked";

    private const string MessageCountInstrument = "mailfathom.embedding.backfill.messages";

    private const string PassageCountInstrument = "mailfathom.embedding.backfill.passages";

    private const string ExhaustedCountInstrument = "mailfathom.embedding.backfill.exhausted";
    private const string OwnerCeilingCountInstrument = "mailfathom.embedding.backfill.owner_ceiling";

    private const string OutstandingGauge = "mailfathom.embedding.backfill.outstanding";

    private const string SwitchCountInstrument = "mailfathom.embedding.generation.switches";

    private const string RemovedVectorInstrument = "mailfathom.embedding.generation.removed";

    /// <summary>
    /// The tag strings are what an operator splits the series on to tell a sweep that finished from one a provider
    /// refused, so a swapped or mistyped value would mislabel that split rather than fail. Every value of both closed
    /// sets is driven through the mapping here.
    /// </summary>
    [Fact]
    public void RecordPass_EveryOutcomeAndFailure_TagsTheSeriesWithTheNameThatOutcomeIsReadBy()
    {
        // Arrange
        var telemetry = new EmailEmbeddingBackfillTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(RunCountInstrument);

        // Act
        RecordEveryOutcome(telemetry);

        // Assert
        Assert.Equal(
            [
                "batch_budget_spent/none",
                "sweep_completed/none",
                "no_active_profile/none",
                "generator_disagrees_with_profile/none",
                "provider_failed/credential_rejected",
                "provider_failed/rate_limited",
                "provider_failed/request_timed_out",
                "provider_failed/transport_faulted",
                "provider_failed/request_refused",
                "provider_failed/vector_shape_unexpected",
            ],
            measurements.TagsOf(RunCountInstrument));
    }

    /// <summary>Every run counts as exactly one, whatever it produced, or the series would count work instead of runs.</summary>
    [Fact]
    public void RecordPass_AnyOutcome_CountsTheRunOnce()
    {
        // Arrange
        var telemetry = new EmailEmbeddingBackfillTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(RunCountInstrument);

        // Act
        RecordEveryOutcome(telemetry);

        // Assert
        Assert.All(measurements.ValuesOf(RunCountInstrument), value => Assert.Equal(1, value));
    }

    /// <summary>
    /// The three progress counters are what an operator reads as a backfill making headway, and each is added only when
    /// it moved: a stream of zeroes on every interval would make an instance with nothing to backfill look like one
    /// that is working through a mailbox.
    /// </summary>
    [Fact]
    public void RecordPass_ProgressCounters_RecordWhatMovedAndNothingForARunThatProducedNone()
    {
        // Arrange
        var telemetry = new EmailEmbeddingBackfillTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(
            ChunkedCountInstrument,
            MessageCountInstrument,
            PassageCountInstrument,
            ExhaustedCountInstrument);

        // Act
        RecordSweep(telemetry, CreateResult(
            StoredEmailEmbeddingBackfillOutcome.BatchBudgetSpent,
            chunkedEmailCount: 2,
            embeddedEmailCount: 5,
            embeddedChunkCount: 31,
            callBudgetExhaustedEmailCount: 1));
        RecordSweep(telemetry, CreateResult(StoredEmailEmbeddingBackfillOutcome.SweepCompleted));

        // Assert
        Assert.Equal([2], measurements.ValuesOf(ChunkedCountInstrument));
        Assert.Equal([5], measurements.ValuesOf(MessageCountInstrument));
        Assert.Equal([31], measurements.ValuesOf(PassageCountInstrument));

        // The signal that a message needs several sweeps to finish, which no other number here would show.
        Assert.Equal([1], measurements.ValuesOf(ExhaustedCountInstrument));
    }

    /// <summary>The meter carries every pass an owner was over their share, because the log deliberately does not.</summary>
    /// <remarks>
    /// The worker writes its warning once per period so it does not bury the rest of the log, which leaves the meter as
    /// the only place the size of the refusal is readable after that first line. A sweep that stepped past nobody adds
    /// nothing, so an instance with no owner over their share stays distinguishable from one that has.
    /// </remarks>
    [Fact]
    public void RecordPass_ASweepSteppedPastAnOwnerOverTheirShare_CountsEveryPass()
    {
        // Arrange
        var telemetry = new EmailEmbeddingBackfillTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(OwnerCeilingCountInstrument);
        var periodEndsAt = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        // Act
        RecordSweep(telemetry, CreateResult(
            StoredEmailEmbeddingBackfillOutcome.BatchBudgetSpent,
            ownerSpendCeilingEmailCount: 3,
            ownerSpendPeriodEndsAt: periodEndsAt));
        RecordSweep(telemetry, CreateResult(
            StoredEmailEmbeddingBackfillOutcome.BatchBudgetSpent,
            ownerSpendCeilingEmailCount: 2,
            ownerSpendPeriodEndsAt: periodEndsAt));
        RecordSweep(telemetry, CreateResult(StoredEmailEmbeddingBackfillOutcome.SweepCompleted));

        // Assert
        Assert.Equal([3, 2], measurements.ValuesOf(OwnerCeilingCountInstrument));
    }

    /// <summary>
    /// How much remains is measured once at the start of a sweep, so the gauge answers with the last figure a sweep
    /// established and a run resuming one leaves it alone rather than publishing nothing.
    /// </summary>
    [Fact]
    public void RecordPass_ASweepStarts_HoldsItsOutstandingCountUntilTheNextSweepMeasuresAgain()
    {
        // Arrange
        var telemetry = new EmailEmbeddingBackfillTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(OutstandingGauge);

        // Act
        RecordSweep(telemetry, CreateResult(
            StoredEmailEmbeddingBackfillOutcome.BatchBudgetSpent,
            outstandingEmailCountAtSweepStart: 412));
        measurements.ObserveGauges();

        RecordSweep(telemetry, CreateResult(StoredEmailEmbeddingBackfillOutcome.BatchBudgetSpent));
        measurements.ObserveGauges();

        // Assert
        // Counted rather than compared as a sequence: every backfill telemetry an earlier test built published a gauge
        // of this name that is still alive on the process-wide meter and still answers for its own sweep, so one
        // observation records several numbers and only this one's is the figure under test.
        Assert.Equal(2, measurements.ValuesOf(OutstandingGauge).Count(outstanding => outstanding == 412));
    }

    /// <summary>
    /// The switch and the removal are what an operator watching a model change reads, and they are counted rather than
    /// published as a state: a gauge naming the current generation would be a dimension of unbounded cardinality for a
    /// value one log line already carries.
    /// </summary>
    [Fact]
    public void RecordPass_APassThatSwitchedAndRemoved_CountsBothAgainstTheirOwnInstruments()
    {
        // Arrange
        var telemetry = new EmailEmbeddingBackfillTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(SwitchCountInstrument, RemovedVectorInstrument);

        // Act
        telemetry.RecordPass(new EmbeddingGenerationUpkeepResult(
            CreateResult(StoredEmailEmbeddingBackfillOutcome.SweepCompleted),
            EmbeddingGenerationTransition.Switched,
            RemovedSupersededVectorCount: 4_000));
        telemetry.RecordPass(new EmbeddingGenerationUpkeepResult(
            CreateResult(StoredEmailEmbeddingBackfillOutcome.SweepCompleted),
            EmbeddingGenerationTransition.None,
            RemovedSupersededVectorCount: 0));

        // Assert
        Assert.Equal([1], measurements.ValuesOf(SwitchCountInstrument));
        Assert.Equal([4_000], measurements.ValuesOf(RemovedVectorInstrument));
    }

    /// <summary>Drives one run of every shape the backfill can end in, in the order the outcomes are declared.</summary>
    private static void RecordEveryOutcome(EmailEmbeddingBackfillTelemetry telemetry)
    {
        StoredEmailEmbeddingBackfillResult[] results =
        [
            CreateResult(StoredEmailEmbeddingBackfillOutcome.BatchBudgetSpent),
            CreateResult(StoredEmailEmbeddingBackfillOutcome.SweepCompleted),
            CreateResult(StoredEmailEmbeddingBackfillOutcome.NoActiveProfile),
            CreateResult(StoredEmailEmbeddingBackfillOutcome.GeneratorDisagreesWithProfile),
            .. Enum.GetValues<EmbeddingGenerationFailure>()
                .Select(failure => CreateResult(
                    StoredEmailEmbeddingBackfillOutcome.ProviderFailed,
                    failure: failure)),
        ];

        foreach (var result in results)
        {
            RecordSweep(telemetry, result);
        }
    }

    /// <summary>Records a pass whose sweep is the one under test and which changed no generation.</summary>
    /// <remarks>
    /// The sweep's own instruments are what most of these tests are about, and wrapping the result here keeps each of
    /// them stating the sweep it drives rather than the two fields it does not care about.
    /// </remarks>
    private static void RecordSweep(
        EmailEmbeddingBackfillTelemetry telemetry,
        StoredEmailEmbeddingBackfillResult sweep) =>
        telemetry.RecordPass(new EmbeddingGenerationUpkeepResult(
            sweep,
            EmbeddingGenerationTransition.None,
            RemovedSupersededVectorCount: 0));

    private static StoredEmailEmbeddingBackfillResult CreateResult(
        StoredEmailEmbeddingBackfillOutcome outcome,
        int chunkedEmailCount = 0,
        int embeddedEmailCount = 0,
        int embeddedChunkCount = 0,
        int callBudgetExhaustedEmailCount = 0,
        int ownerSpendCeilingEmailCount = 0,
        DateTimeOffset? ownerSpendPeriodEndsAt = null,
        int? outstandingEmailCountAtSweepStart = null,
        EmbeddingGenerationFailure? failure = null,
        DateTimeOffset? spendPeriodEndsAt = null,
        EmbeddingSpendBound reachedSpendBound = EmbeddingSpendBound.None) =>
        new(
            outcome,
            chunkedEmailCount,
            embeddedEmailCount,
            embeddedChunkCount,
            callBudgetExhaustedEmailCount,
            ownerSpendCeilingEmailCount,
            ownerSpendPeriodEndsAt,
            outstandingEmailCountAtSweepStart,
            failure,
            spendPeriodEndsAt,
            reachedSpendBound);
}
