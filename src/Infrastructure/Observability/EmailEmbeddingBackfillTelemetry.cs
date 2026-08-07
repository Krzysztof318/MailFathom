// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports how far the embedding backfill has come and what it is spending to get there.</summary>
/// <remarks>
/// <para>
/// The backfill's numbers are published apart from the live worker's rather than merged into them, because they answer
/// different questions about the same provider bill: mail arriving is a rate an instance settles at, and a backfill is
/// a finite amount of work an operator started and can stop.
/// </para>
/// <para>
/// How much remains is measured once per sweep rather than per observation, and the gauge answers with the last figure
/// the backfill measured. An exact live count is an unbounded scan of every passage, so making it a gauge would put
/// that scan on whatever interval a collector happened to be configured with; a figure that is a sweep old is what an
/// operator watching a backfill needs, and the counters beside it are what move in between.
/// </para>
/// <para>
/// Nothing recorded here is mail or derived from it. The tags are MailFathom's own closed sets — an outcome name and a
/// provider failure classification — and the values are counts, never a message identity, a passage, or a vector.
/// </para>
/// </remarks>
public sealed class EmailEmbeddingBackfillTelemetry
{
    // The same two tag keys the live worker publishes, because the instruments are already named apart and a dashboard
    // that splits embedding work by outcome should split both families on one dimension rather than on two.
    private const string OutcomeTagName = "mailfathom.embedding.outcome";
    private const string FailureTagName = "mailfathom.embedding.failure";

    private readonly Counter<long> runCount;
    private readonly Counter<long> chunkedEmailCount;
    private readonly Counter<long> embeddedEmailCount;
    private readonly Counter<long> passageCount;
    private int outstandingEmailCount;

    /// <summary>Initializes the instruments every backfill run reports through.</summary>
    public EmailEmbeddingBackfillTelemetry()
    {
        this.runCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.backfill.runs",
            unit: "{run}",
            description: "Bounded passes of the embedding backfill, by how each one ended.");
        this.chunkedEmailCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.backfill.chunked",
            unit: "{message}",
            description: "Messages the backfill had to cut into passages before anything could be embedded.");
        this.embeddedEmailCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.backfill.messages",
            unit: "{message}",
            description: "Messages the backfill brought up to date with the active profile.");
        this.passageCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.backfill.passages",
            unit: "{passage}",
            description: "Passages the backfill gave a vector under the active profile.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.embedding.backfill.outstanding",
            () => Volatile.Read(ref this.outstandingEmailCount),
            unit: "{message}",
            description: "Messages awaiting embedding when the current sweep began.");
    }

    /// <summary>Records one bounded pass of the backfill.</summary>
    /// <param name="result">What the pass produced and why it ended.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    public void RecordRun(StoredEmailEmbeddingBackfillResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var tags = new TagList
        {
            { OutcomeTagName, OutcomeTagOf(result.Outcome) },
            { FailureTagName, FailureTagOf(result.Failure) },
        };

        this.runCount.Add(1, tags);

        // Each of the three is added only when it moved. A stream of zeroes on every interval would make an instance
        // with nothing to backfill indistinguishable from one that is working through a mailbox.
        if (result.ChunkedEmailCount > 0)
        {
            this.chunkedEmailCount.Add(result.ChunkedEmailCount);
        }

        if (result.EmbeddedEmailCount > 0)
        {
            this.embeddedEmailCount.Add(result.EmbeddedEmailCount);
        }

        if (result.EmbeddedChunkCount > 0)
        {
            this.passageCount.Add(result.EmbeddedChunkCount);
        }

        if (result.OutstandingEmailCountAtSweepStart is { } outstanding)
        {
            Volatile.Write(ref this.outstandingEmailCount, outstanding);
        }
    }

    private static string OutcomeTagOf(StoredEmailEmbeddingBackfillOutcome outcome) => outcome switch
    {
        StoredEmailEmbeddingBackfillOutcome.BatchBudgetSpent => "batch_budget_spent",
        StoredEmailEmbeddingBackfillOutcome.SweepCompleted => "sweep_completed",
        StoredEmailEmbeddingBackfillOutcome.NoActiveProfile => "no_active_profile",
        StoredEmailEmbeddingBackfillOutcome.GeneratorDisagreesWithProfile => "generator_disagrees_with_profile",
        StoredEmailEmbeddingBackfillOutcome.ProviderFailed => "provider_failed",
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
