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
    private const string OutcomeTagName = "mailfathom.embedding.outcome";
    private const string FailureTagName = "mailfathom.embedding.failure";

    private readonly Counter<long> messageCount;
    private readonly Counter<long> passageCount;
    private readonly Histogram<double> messageDuration;

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
    }

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
    }

    private static string OutcomeTagOf(StoredEmailEmbeddingOutcome outcome) => outcome switch
    {
        StoredEmailEmbeddingOutcome.Embedded => "embedded",
        StoredEmailEmbeddingOutcome.NoActiveProfile => "no_active_profile",
        StoredEmailEmbeddingOutcome.GeneratorDisagreesWithProfile => "generator_disagrees_with_profile",
        StoredEmailEmbeddingOutcome.ProviderFailed => "provider_failed",
        StoredEmailEmbeddingOutcome.CallBudgetExhausted => "call_budget_exhausted",
        _ => "unknown",
    };

    /// <summary>Names the failure, or says there was none, so the tag stays present on every series.</summary>
    /// <remarks>
    /// A tag left off some of the measurements and set on others produces two time series for one instrument, which a
    /// dashboard reads as a gap rather than as an absence.
    /// </remarks>
    private static string FailureTagOf(EmbeddingGenerationFailure? failure) => failure switch
    {
        EmbeddingGenerationFailure.CredentialRejected => "credential_rejected",
        EmbeddingGenerationFailure.RateLimited => "rate_limited",
        EmbeddingGenerationFailure.RequestTimedOut => "request_timed_out",
        EmbeddingGenerationFailure.TransportFaulted => "transport_faulted",
        EmbeddingGenerationFailure.RequestRefused => "request_refused",
        EmbeddingGenerationFailure.VectorShapeUnexpected => "vector_shape_unexpected",
        _ => "none",
    };
}
