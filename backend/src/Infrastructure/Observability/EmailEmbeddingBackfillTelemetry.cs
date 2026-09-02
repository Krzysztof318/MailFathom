// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
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
    /// <summary>The name one bounded upkeep pass opens its span under.</summary>
    /// <remarks>
    /// The counterpart of <c>backfill_email_extraction</c>, and it exists for the same reason: a pass is caused by an
    /// interval rather than by a request, so without a span of its own its provider calls and its database commands are
    /// parentless work competing with the requests around them. Named after what the pass does rather than after the
    /// worker that drives it.
    /// <para>
    /// Published for the same reason <see cref="EmailEmbeddingTelemetry.MessageSpanName" /> is: the boundary the span
    /// is opened around is the worker's, so what asserts that the pass really runs inside it lives with the worker.
    /// </para>
    /// </remarks>
    public const string PassSpanName = "backfill_email_embeddings";

    // The same two tag keys the live worker publishes, because the instruments are already named apart and a dashboard
    // that splits embedding work by outcome should split both families on one dimension rather than on two.
    internal const string OutcomeTagName = "mailfathom.embedding.outcome";
    internal const string FailureTagName = "mailfathom.embedding.failure";

    internal const string ChunkedEmailCountTagName = "mailfathom.embedding.backfill.chunked";
    internal const string EmbeddedEmailCountTagName = "mailfathom.embedding.backfill.messages";
    internal const string PassageCountTagName = "mailfathom.embedding.backfill.passages";

    /// <summary>Whether a generation being built became the one searches are answered from during this pass.</summary>
    internal const string GenerationSwitchedTagName = "mailfathom.embedding.generation.switched";

    private readonly Counter<long> runCount;
    private readonly Counter<long> chunkedEmailCount;
    private readonly Counter<long> embeddedEmailCount;
    private readonly Counter<long> passageCount;
    private readonly Counter<long> callBudgetExhaustedEmailCount;
    private readonly Counter<long> ownerSpendCeilingEmailCount;
    private readonly Counter<long> generationSwitchCount;
    private readonly Counter<long> removedSupersededVectorCount;
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
        this.callBudgetExhaustedEmailCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.backfill.exhausted",
            unit: "{message}",
            description: "Messages the backfill left part-way through because one turn spent every provider call it is allowed.");
        this.ownerSpendCeilingEmailCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.backfill.owner_ceiling",
            unit: "{message}",
            description: "Messages the backfill stepped past because the owner they belong to had spent what one period admits for them.");
        this.generationSwitchCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.generation.switches",
            unit: "{switch}",
            description: "Generations that finished being built and became the one searches are answered from.");
        this.removedSupersededVectorCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.generation.removed",
            unit: "{vector}",
            description: "Vectors of a superseded generation removed after a switch.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.embedding.backfill.outstanding",
            () => Volatile.Read(ref this.outstandingEmailCount),
            unit: "{message}",
            description: "Messages awaiting embedding when the current sweep began.");
    }

    /// <summary>Opens the span one bounded upkeep pass is reported as, and returns the scope that ends it.</summary>
    /// <returns>The scope, which the caller must dispose; a scope disposed without <see cref="PassScope.Ended" /> reports a pass that produced no result.</returns>
    public PassScope BeginPass() => new(Telemetry.ActivitySource.StartActivity(PassSpanName));

    /// <summary>Records one bounded upkeep pass: its sweep, its switch, and what it removed.</summary>
    /// <param name="result">What the pass produced.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The switch is a counter rather than a state, because what an operator asks of it afterwards is when it happened
    /// and how many have — and a gauge of the current generation would publish an identifier, which is a dimension of
    /// unbounded cardinality for a value one log line already carries.
    /// </remarks>
    public void RecordPass(EmbeddingGenerationUpkeepResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        this.RecordSweep(result.Sweep);

        if (result.Transition == EmbeddingGenerationTransition.Switched)
        {
            this.generationSwitchCount.Add(1);
        }

        if (result.RemovedSupersededVectorCount > 0)
        {
            this.removedSupersededVectorCount.Add(result.RemovedSupersededVectorCount);
        }
    }

    private void RecordSweep(StoredEmailEmbeddingBackfillResult result)
    {
        var tags = new TagList
        {
            { OutcomeTagName, OutcomeTagOf(result.Outcome) },
            { FailureTagName, EmailEmbeddingTelemetry.FailureTagOf(result.Failure) },
        };

        this.runCount.Add(1, tags);

        // Each count is added only when it moved. A stream of zeroes on every interval would make an instance with
        // nothing to backfill indistinguishable from one that is working through a mailbox.
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

        if (result.CallBudgetExhaustedEmailCount > 0)
        {
            this.callBudgetExhaustedEmailCount.Add(result.CallBudgetExhaustedEmailCount);
        }

        // The one number here that says a bound was reached without the run stopping. The worker's own line is written
        // once per period so it does not bury the log, which is exactly why the meter has to carry every pass: without
        // it there is nothing an operator can read the size of the refusal from after the first line.
        if (result.OwnerSpendCeilingEmailCount > 0)
        {
            this.ownerSpendCeilingEmailCount.Add(result.OwnerSpendCeilingEmailCount);
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
        StoredEmailEmbeddingBackfillOutcome.SpendCeilingReached => "spend_ceiling_reached",
        _ => "unknown",
    };

    /// <summary>Carries one bounded upkeep pass from the span that opens it to the result that ends it.</summary>
    /// <remarks>
    /// A pass that reached no result is published with an error status and no outcome, which is what an unresolved
    /// concurrency conflict and an unexpected failure both produce. Neither has a word among the outcomes, because
    /// every one of those is a state the sweep itself reached.
    /// </remarks>
    public sealed class PassScope : IDisposable
    {
        private readonly Activity? activity;

        private bool reported;

        internal PassScope(Activity? activity) => this.activity = activity;

        /// <summary>Records how the pass ended and what it moved.</summary>
        /// <param name="result">What the pass produced.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
        public void Ended(EmbeddingGenerationUpkeepResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            this.reported = true;

            var sweep = result.Sweep;

            this.activity?.SetTag(OutcomeTagName, OutcomeTagOf(sweep.Outcome));
            this.activity?.SetTag(FailureTagName, EmailEmbeddingTelemetry.FailureTagOf(sweep.Failure));
            this.activity?.SetTag(ChunkedEmailCountTagName, sweep.ChunkedEmailCount);
            this.activity?.SetTag(EmbeddedEmailCountTagName, sweep.EmbeddedEmailCount);
            this.activity?.SetTag(PassageCountTagName, sweep.EmbeddedChunkCount);
            this.activity?.SetTag(
                GenerationSwitchedTagName,
                result.Transition == EmbeddingGenerationTransition.Switched);
            this.activity?.SetStatus(
                sweep.Outcome is StoredEmailEmbeddingBackfillOutcome.ProviderFailed
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
