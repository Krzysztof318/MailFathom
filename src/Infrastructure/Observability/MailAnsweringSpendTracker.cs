// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Common.Observability;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Holds what answering has cost in the period currently running, admits a question against it, and publishes both as instruments.</summary>
/// <remarks>
/// <para>
/// The period is the fixed window <see cref="MailAnsweringPeriodBounds.PeriodStartAt" /> places, held as three numbers
/// beside the start it currently counts under. The counts are dropped whenever a call notices that the clock has moved
/// into a later window, rather than by a timer, so an idle deployment holds no callback and nothing here schedules
/// work. Because the window is a function of the clock rather than of when the last reset happened, an instance that
/// answered nothing for a day is not owed the windows that passed while it was idle.
/// </para>
/// <para>
/// Process-local, and this is where it diverges from
/// <see cref="Application.Emails.Embeddings.Limits.IEmbeddingSpendLedger" />, which keeps its equivalent in a table so a
/// crash-restart loop cannot begin every period again from zero. That reasoning applies here in kind and not in degree:
/// an embedding sweep charges inside a transaction that was committing vectors anyway, while a question opens no write
/// of its own, so making this durable would add a database write to the path of every provider call in every run. A
/// restart therefore begins a new window with nothing spent, and an operator who needs the stronger guarantee needs the
/// table rather than a longer period.
/// </para>
/// <para>
/// A refusal is counted as well as measured, because the two questions an operator asks are opposite: the counter says
/// how often the ceiling was reached, and the gauges say how close the deployment is to reaching it now. A ceiling that
/// is met constantly is a ceiling to raise or a client to look at, and neither is visible from a single number.
/// </para>
/// <para>
/// Only the first refusal of a period is written to the log, and that bound is the point rather than tidiness: a client
/// that keeps asking is exactly what spends a period's allowance, so a line per refusal would put the log's volume on
/// how enthusiastic that client is. The counter carries how often it happened; the line says that it started.
/// </para>
/// <para>
/// Nothing recorded here is mail or derived from it. The values are a run count and two token counts, and the one tag
/// is an outcome from a closed set of two — which is a cardinality rule as much as a privacy one, since anything per
/// caller or per question would open a time series that grows with use.
/// </para>
/// </remarks>
public sealed partial class MailAnsweringSpendTracker : IMailAnsweringSpendLedger
{
    private const string OutcomeTagName = "mailfathom.answering.outcome";

    private readonly MailAnsweringPeriodBounds bounds;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<MailAnsweringSpendTracker> logger;
    private readonly Counter<long> runCount;
    private readonly Counter<long> tokenCount;
    private readonly Lock gate = new();
    private DateTimeOffset periodStartedAt;
    private int runs;
    private long inputTokens;
    private long outputTokens;
    private bool refusalReported;

    /// <summary>Initializes a ledger whose first period begins now, and the instruments it publishes through.</summary>
    /// <param name="bounds">What the runs of one period may add up to.</param>
    /// <param name="timeProvider">Decides when a period has elapsed.</param>
    /// <param name="logger">Records that a period was spent, in counts alone.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailAnsweringSpendTracker(
        MailAnsweringPeriodBounds bounds,
        TimeProvider timeProvider,
        ILogger<MailAnsweringSpendTracker> logger)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.bounds = bounds;
        this.timeProvider = timeProvider;
        this.logger = logger;
        this.periodStartedAt = bounds.PeriodStartAt(timeProvider.GetUtcNow());

        this.runCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.answering.runs",
            unit: "{run}",
            description: "Questions this deployment was asked to answer, by whether the period's allowance admitted them.");
        this.tokenCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.answering.tokens",
            unit: "{token}",
            description: "Tokens answering has consumed, as the provider reported them.");

        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.answering.period.runs",
            () => this.Read().Runs,
            unit: "{run}",
            description: "Runs the current period has admitted, against the ceiling configured for it.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.answering.period.tokens",
            () => this.Read().Tokens,
            unit: "{token}",
            description: "Tokens the current period has consumed, against the ceiling configured for it.");
    }

    /// <inheritdoc />
    public bool TryAdmitRun()
    {
        bool admitted;
        bool worthReporting;
        int runsInPeriod;
        long tokensInPeriod;

        lock (this.gate)
        {
            this.RollOverToTheCurrentPeriod();

            admitted = this.runs < this.bounds.MaximumRuns
                && this.inputTokens + this.outputTokens < this.bounds.MaximumTokens;

            if (admitted)
            {
                this.runs++;
            }

            worthReporting = !admitted && !this.refusalReported;
            this.refusalReported |= !admitted;
            runsInPeriod = this.runs;
            tokensInPeriod = this.inputTokens + this.outputTokens;
        }

        this.runCount.Add(1, new KeyValuePair<string, object?>(OutcomeTagName, admitted ? "admitted" : "refused"));

        if (worthReporting)
        {
            this.LogPeriodSpent(runsInPeriod, tokensInPeriod, this.bounds.Period);
        }

        return admitted;
    }

    /// <inheritdoc />
    public void RecordSpend(ChatTokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        lock (this.gate)
        {
            // Charged to the window the call finished in, which is the same rule the admission uses. A run that spans a
            // roll-over therefore pays part of itself into each, and that is the honest reading of a fixed window: the
            // alternative is holding a second set of counters for a period that has ended so a slow call can still
            // reach it.
            this.RollOverToTheCurrentPeriod();

            this.inputTokens += usage.InputTokens;
            this.outputTokens += usage.OutputTokens;
        }

        this.tokenCount.Add(usage.InputTokens + usage.OutputTokens);
    }

    /// <summary>Reads what the current period has cost so far, rolling the window over first when the clock has left it.</summary>
    /// <returns>The period's start, the runs it has admitted, and the tokens they consumed.</returns>
    /// <remarks>
    /// This tracker's own member rather than one of the ledger port's, because nothing above that boundary acts on the
    /// figure: a use case is told whether a question may run and never how close the period is to its ceiling. What
    /// reads it is the pair of gauges above and a test asserting what they would publish.
    /// </remarks>
    public MailAnsweringSpend Read()
    {
        lock (this.gate)
        {
            this.RollOverToTheCurrentPeriod();

            return new MailAnsweringSpend(this.periodStartedAt, this.runs, this.inputTokens, this.outputTokens);
        }
    }

    /// <summary>Drops the counts whenever the clock has moved into a window later than the one being counted.</summary>
    /// <remarks>
    /// The new start is where the bounds place the current instant rather than the instant itself, so the windows an
    /// idle instance skipped are skipped rather than owed to it, and two processes of one deployment count against the
    /// same boundaries.
    /// </remarks>
    private void RollOverToTheCurrentPeriod()
    {
        var currentPeriodStart = this.bounds.PeriodStartAt(this.timeProvider.GetUtcNow());

        if (currentPeriodStart == this.periodStartedAt)
        {
            return;
        }

        this.periodStartedAt = currentPeriodStart;
        this.runs = 0;
        this.inputTokens = 0;
        this.outputTokens = 0;
        this.refusalReported = false;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Answering was refused: the current period has admitted {Runs} runs costing {Tokens} tokens, which is what this deployment allows every {Period}. Questions are answered again when the period turns over. Later refusals in this period are counted rather than written.")]
    private partial void LogPeriodSpent(int runs, long tokens, TimeSpan period);
}
