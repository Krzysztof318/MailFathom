// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports what embedding one message cost and how it ended.</summary>
/// <remarks>
/// <para>
/// The outcome tag is the whole point of the instrument. "The worker ran" says nothing an operator can act on, while
/// the difference between no profile being active, a declaration disagreeing with the one that was activated, and a
/// provider refusing is three different things to go and do.
/// </para>
/// <para>
/// Nothing recorded here is mail or derived from it. The tags are an outcome name and a provider failure
/// classification, both of them MailFathom's own closed sets, and the values are a count and a duration — never a
/// message identity, a passage, or a vector, each of which would open a time series per message.
/// </para>
/// </remarks>
public sealed class EmailEmbeddingTelemetry
{
    /// <summary>The name one message's turn at being embedded opens its span under.</summary>
    /// <remarks>
    /// A turn is caused by a backlog rather than by a request, and it is the one place this deployment reaches a paid
    /// provider outside a tool call. Without a span of its own the provider call the AI decorators publish arrives
    /// parentless, so the most expensive thing the process does would be attributable to nothing — and the database
    /// commands that store the vectors would sit beside it unexplained.
    /// <para>
    /// Published rather than kept inside this assembly, because the span is opened around a boundary the worker draws
    /// — one message, one scope — so what asserts that the provider call really is inside it lives with the worker. A
    /// second copy of the word would be a second place for it to drift.
    /// </para>
    /// </remarks>
    public const string MessageSpanName = "embed_stored_email";

    internal const string OutcomeTagName = "mailfathom.embedding.outcome";
    internal const string FailureTagName = "mailfathom.embedding.failure";

    /// <summary>How many passages of the message the turn gave a vector, which is the same quantity the counter sums.</summary>
    internal const string PassageCountTagName = "mailfathom.embedding.passages";

    private readonly Counter<long> messageCount;
    private readonly Counter<long> passageCount;
    private readonly Histogram<double> messageDuration;
    private readonly Counter<long> consumedInputCharacterCount;
    private readonly Counter<long> truncatedMessageCount;
    private readonly Counter<long> omittedInputCharacterCount;

    /// <summary>Initializes the instruments every embedded message reports through.</summary>
    public EmailEmbeddingTelemetry()
    {
        this.messageCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.messages",
            unit: "{message}",
            description: "Messages the embedding worker took from the queue, by outcome.");
        this.passageCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.passages",
            unit: "{passage}",
            description: "Passages given a vector under the active profile.");
        this.messageDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.embedding.message.duration",
            unit: "s",
            description: "How long embedding one message took, by outcome.");

        // A counter rather than a gauge of what is left, because the ledger's remaining figure would have to be read
        // from the database inside a callback the meter invokes on its own schedule. Summed over a period this answers
        // the same question, and summed over any other window it answers one a ceiling cannot.
        this.consumedInputCharacterCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.budget.consumed",
            unit: "{character}",
            description: "Characters sent to an embedding provider and charged against the spend ceiling.");
        this.truncatedMessageCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.input.truncated",
            unit: "{message}",
            description: "Messages whose text the per-message embedding ceiling cut short.");
        this.omittedInputCharacterCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.input.omitted",
            unit: "{character}",
            description: "Characters the per-message embedding ceiling left out of the passages it cut.");
    }

    /// <summary>Opens the span one message's turn at being embedded is reported as, and returns the scope that ends it.</summary>
    /// <returns>The scope, which the caller must dispose; a scope disposed without <see cref="MessageScope.Ended" /> reports a turn that produced no result.</returns>
    /// <remarks>
    /// Nothing about the message reaches the span. Which message was embedded is a stored identity and would open a
    /// series per message, so the span carries what the turn did — how it ended, and how many passages it gave a vector
    /// to — and the message itself stays in the queue the worker took it from.
    /// </remarks>
    public MessageScope BeginMessage() => new(Telemetry.ActivitySource.StartActivity(MessageSpanName));

    /// <summary>Records one message's turn at being embedded.</summary>
    /// <param name="run">How the turn ended and how much it produced.</param>
    /// <param name="elapsed">How long the turn took, including the provider calls inside it.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="run" /> is <see langword="null" />.</exception>
    public void RecordEmbeddedMessage(StoredEmailEmbeddingRun run, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(run);

        var tags = new TagList
        {
            { OutcomeTagName, OutcomeTagOf(run.Outcome) },
            { FailureTagName, FailureTagOf(run.Failure) },
        };

        this.messageCount.Add(1, tags);
        this.messageDuration.Record(elapsed.TotalSeconds, tags);

        if (run.EmbeddedChunkCount > 0)
        {
            this.passageCount.Add(run.EmbeddedChunkCount);
        }

        // Recorded for every outcome that sent anything, including the two that stopped part-way: what a turn spent is
        // spent whether or not the message it was for is now whole.
        if (run.InputCharacterCount > 0)
        {
            this.consumedInputCharacterCount.Add(run.InputCharacterCount);
        }
    }

    /// <summary>Records one message the per-message input ceiling cut short.</summary>
    /// <param name="omittedCharacterCount">The characters the ceiling left out of the passages.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
    /// <remarks>
    /// Two instruments rather than one, because the two questions differ: how many messages are being cut says whether
    /// the ceiling is set where an operator meant it, and how much text is being left out says what raising it would
    /// cost. A count alone would make one enormous message and a thousand slightly oversized ones look the same.
    /// </remarks>
    public void RecordTruncatedEmbeddingInput(int omittedCharacterCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(omittedCharacterCount);

        this.truncatedMessageCount.Add(1);
        this.omittedInputCharacterCount.Add(omittedCharacterCount);
    }

    private static string OutcomeTagOf(StoredEmailEmbeddingOutcome outcome) => outcome switch
    {
        StoredEmailEmbeddingOutcome.Embedded => "embedded",
        StoredEmailEmbeddingOutcome.NoActiveProfile => "no_active_profile",
        StoredEmailEmbeddingOutcome.GeneratorDisagreesWithProfile => "generator_disagrees_with_profile",
        StoredEmailEmbeddingOutcome.ProviderFailed => "provider_failed",
        StoredEmailEmbeddingOutcome.CallBudgetExhausted => "call_budget_exhausted",
        StoredEmailEmbeddingOutcome.SpendCeilingReached => "spend_ceiling_reached",
        _ => "unknown",
    };

    /// <summary>Names the failure, or says there was none, so the tag stays present on every series.</summary>
    /// <remarks>
    /// A tag left off some of the measurements and set on others produces two time series for one instrument, which a
    /// dashboard reads as a gap rather than as an absence. <see cref="EmailEmbeddingBackfillTelemetry" /> publishes the
    /// same dimension and calls this rather than restating it, so one failure has one word in both families.
    /// </remarks>
    internal static string FailureTagOf(EmbeddingGenerationFailure? failure) => failure switch
    {
        EmbeddingGenerationFailure.CredentialRejected => "credential_rejected",
        EmbeddingGenerationFailure.RateLimited => "rate_limited",
        EmbeddingGenerationFailure.RequestTimedOut => "request_timed_out",
        EmbeddingGenerationFailure.TransportFaulted => "transport_faulted",
        EmbeddingGenerationFailure.RequestRefused => "request_refused",
        EmbeddingGenerationFailure.VectorShapeUnexpected => "vector_shape_unexpected",
        _ => "none",
    };

    /// <summary>Carries one message's turn at being embedded from the span that opens it to the run that ends it.</summary>
    /// <remarks>
    /// A turn that reached no run is published with an error status and no outcome. Every outcome this subsystem
    /// publishes is one the generator decided, so there is no word for a turn that ended before it did — which is what
    /// an unresolved concurrency conflict and an unexpected failure both produce, and both are already logged by the
    /// worker that isolates them.
    /// </remarks>
    public sealed class MessageScope : IDisposable
    {
        private readonly Activity? activity;

        private bool reported;

        internal MessageScope(Activity? activity) => this.activity = activity;

        /// <summary>Records how the turn ended and what it produced.</summary>
        /// <param name="run">The run the generator reported.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="run" /> is <see langword="null" />.</exception>
        public void Ended(StoredEmailEmbeddingRun run)
        {
            ArgumentNullException.ThrowIfNull(run);

            this.reported = true;

            this.activity?.SetTag(OutcomeTagName, OutcomeTagOf(run.Outcome));
            this.activity?.SetTag(FailureTagName, FailureTagOf(run.Failure));
            this.activity?.SetTag(PassageCountTagName, run.EmbeddedChunkCount);
            this.activity?.SetStatus(
                run.Outcome is StoredEmailEmbeddingOutcome.ProviderFailed
                    ? ActivityStatusCode.Error
                    : ActivityStatusCode.Ok);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!this.reported)
            {
                this.activity?.SetStatus(ActivityStatusCode.Error);
            }

            this.activity?.Dispose();
        }
    }
}
