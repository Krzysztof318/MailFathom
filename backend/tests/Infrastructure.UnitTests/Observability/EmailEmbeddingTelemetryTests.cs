// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

public sealed class EmailEmbeddingTelemetryTests
{
    private const string MessageCountInstrument = "mailfathom.embedding.messages";

    private const string MessageDurationInstrument = "mailfathom.embedding.message.duration";

    private const string PassageCountInstrument = "mailfathom.embedding.passages";

    private const string ConsumedBudgetInstrument = "mailfathom.embedding.budget.consumed";

    private const string TruncatedMessageInstrument = "mailfathom.embedding.input.truncated";

    private const string OmittedCharacterInstrument = "mailfathom.embedding.input.omitted";

    /// <summary>
    /// The tag strings are the whole value of the instrument: an operator diagnoses a falling-behind instance by
    /// splitting these series on the outcome and, when the provider refused, on the classification. A swapped or
    /// mistyped string would mislabel that split rather than fail, so every value of both closed sets is driven through
    /// the mapping here.
    /// </summary>
    [Fact]
    public void RecordEmbeddedMessage_EveryOutcomeAndFailure_TagsTheSeriesWithTheNameThatOutcomeIsReadBy()
    {
        // Arrange
        var telemetry = new EmailEmbeddingTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(MessageCountInstrument);

        // Act
        RecordEveryOutcome(telemetry);

        // Assert
        Assert.Equal(
            [
                "embedded/none",
                "no_active_profile/none",
                "generator_disagrees_with_profile/none",
                "call_budget_exhausted/none",
                "spend_ceiling_reached/none",
                "provider_failed/credential_rejected",
                "provider_failed/rate_limited",
                "provider_failed/request_timed_out",
                "provider_failed/transport_faulted",
                "provider_failed/request_refused",
                "provider_failed/vector_shape_unexpected",
            ],
            measurements.TagsOf(MessageCountInstrument));
    }

    /// <summary>Every turn counts as exactly one message, whatever it produced, or the series would count work instead of messages.</summary>
    [Fact]
    public void RecordEmbeddedMessage_AnyOutcome_CountsTheMessageOnce()
    {
        // Arrange
        var telemetry = new EmailEmbeddingTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(MessageCountInstrument);

        // Act
        RecordEveryOutcome(telemetry);

        // Assert
        Assert.All(measurements.ValuesOf(MessageCountInstrument), value => Assert.Equal(1, value));
    }

    /// <summary>
    /// The duration carries the same tags as the count, because a dashboard splits both on one dimension and a series
    /// present on one instrument and absent from the other reads as a gap rather than as an absence.
    /// </summary>
    [Fact]
    public void RecordEmbeddedMessage_AProviderFailure_TagsTheDurationAsItTagsTheCountAndRecordsTheElapsedSeconds()
    {
        // Arrange
        var telemetry = new EmailEmbeddingTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(
            MessageCountInstrument,
            MessageDurationInstrument);

        // Act
        telemetry.RecordEmbeddedMessage(
            StoredEmailEmbeddingRun.ProviderFailed(0, EmbeddingGenerationFailure.RateLimited),
            TimeSpan.FromMilliseconds(2500));

        // Assert
        Assert.Equal(["provider_failed/rate_limited"], measurements.TagsOf(MessageCountInstrument));
        Assert.Equal(
            measurements.TagsOf(MessageCountInstrument),
            measurements.TagsOf(MessageDurationInstrument));
        Assert.Equal([2.5], measurements.ValuesOf(MessageDurationInstrument));
    }

    /// <summary>
    /// The passage count is what an operator reads as how much of a mailbox has become searchable, so it records the
    /// passages a turn produced rather than the messages it took — and a turn that produced none adds nothing, because
    /// a stream of zeroes would make an idle instance look like a working one.
    /// </summary>
    [Fact]
    public void RecordEmbeddedMessage_PassagesEmbedded_RecordsTheirCountAndNothingForATurnThatProducedNone()
    {
        // Arrange
        var telemetry = new EmailEmbeddingTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(PassageCountInstrument);

        // Act
        telemetry.RecordEmbeddedMessage(StoredEmailEmbeddingRun.Embedded(4), TimeSpan.FromSeconds(1));
        telemetry.RecordEmbeddedMessage(StoredEmailEmbeddingRun.Embedded(0), TimeSpan.FromSeconds(1));
        telemetry.RecordEmbeddedMessage(StoredEmailEmbeddingRun.NoActiveProfile(), TimeSpan.FromSeconds(1));
        telemetry.RecordEmbeddedMessage(StoredEmailEmbeddingRun.CallBudgetExhausted(9), TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal([4, 9], measurements.ValuesOf(PassageCountInstrument));
    }

    /// <summary>
    /// What a turn spent is charged whether or not the message it was for is now whole, because the characters were
    /// sent either way. A counter that skipped the two endings that stop part-way would under-report exactly the
    /// periods an operator watching a ceiling is looking at.
    /// </summary>
    [Fact]
    public void RecordEmbeddedMessage_ATurnThatSentSomething_CountsItAgainstTheBudgetWhateverTheOutcome()
    {
        // Arrange
        var telemetry = new EmailEmbeddingTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(ConsumedBudgetInstrument);

        // Act
        telemetry.RecordEmbeddedMessage(
            StoredEmailEmbeddingRun.Embedded(3, inputCharacterCount: 30),
            TimeSpan.FromSeconds(1));
        telemetry.RecordEmbeddedMessage(
            StoredEmailEmbeddingRun.SpendCeilingReached(1, inputCharacterCount: 12, DateTimeOffset.UnixEpoch),
            TimeSpan.FromSeconds(1));
        telemetry.RecordEmbeddedMessage(
            StoredEmailEmbeddingRun.CallBudgetExhausted(2, inputCharacterCount: 20),
            TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal(62, measurements.ValuesOf(ConsumedBudgetInstrument).Sum());
    }

    /// <summary>A turn that sent nothing charges nothing, so a period with no work opens no series of zeroes.</summary>
    [Fact]
    public void RecordEmbeddedMessage_ATurnThatSentNothing_ChargesTheBudgetNothing()
    {
        // Arrange
        var telemetry = new EmailEmbeddingTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(ConsumedBudgetInstrument);

        // Act
        telemetry.RecordEmbeddedMessage(StoredEmailEmbeddingRun.NoActiveProfile(), TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal(0, measurements.ValuesOf(ConsumedBudgetInstrument).Sum());
    }

    /// <summary>
    /// Two instruments rather than one, because one enormous message and a thousand slightly oversized ones need
    /// different answers from an operator: how often the ceiling binds, and what raising it would cost.
    /// </summary>
    [Fact]
    public void RecordTruncatedEmbeddingInput_AMessageTheCeilingCut_CountsTheMessageAndTheTextItLeftOut()
    {
        // Arrange
        var telemetry = new EmailEmbeddingTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(
            TruncatedMessageInstrument,
            OmittedCharacterInstrument);

        // Act
        telemetry.RecordTruncatedEmbeddingInput(omittedCharacterCount: 4_000);
        telemetry.RecordTruncatedEmbeddingInput(omittedCharacterCount: 1_000);

        // Assert
        Assert.Equal(2, measurements.ValuesOf(TruncatedMessageInstrument).Sum());
        Assert.Equal(5_000, measurements.ValuesOf(OmittedCharacterInstrument).Sum());
    }

    [Fact]
    public void RecordTruncatedEmbeddingInput_ANegativeCount_IsRefused()
    {
        // Arrange
        var telemetry = new EmailEmbeddingTelemetry();

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => telemetry.RecordTruncatedEmbeddingInput(-1));
    }

    /// <summary>Drives one turn of every shape a run can end in, in the order the outcomes are declared.</summary>
    private static void RecordEveryOutcome(EmailEmbeddingTelemetry telemetry)
    {
        StoredEmailEmbeddingRun[] runs =
        [
            StoredEmailEmbeddingRun.Embedded(3),
            StoredEmailEmbeddingRun.NoActiveProfile(),
            StoredEmailEmbeddingRun.GeneratorDisagreesWithProfile(),
            StoredEmailEmbeddingRun.CallBudgetExhausted(7),
            StoredEmailEmbeddingRun.SpendCeilingReached(2, inputCharacterCount: 40, DateTimeOffset.UnixEpoch),
            .. Enum.GetValues<EmbeddingGenerationFailure>()
                .Select(failure => StoredEmailEmbeddingRun.ProviderFailed(1, failure)),
        ];

        foreach (var run in runs)
        {
            telemetry.RecordEmbeddedMessage(run, TimeSpan.FromSeconds(2));
        }
    }
}
