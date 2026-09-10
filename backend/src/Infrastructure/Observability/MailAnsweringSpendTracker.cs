// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Common.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Admits a question against what answering has cost the deployment this period, and publishes both as instruments.</summary>
/// <remarks>
/// <para>
/// The period is the fixed window <see cref="MailAnsweringPeriodBounds.PeriodStartAt" /> places, and it is the key the
/// ledger's row is held under. Because the window is a function of the clock rather than of when a process started,
/// every replica derives the same key, nothing has to be stored to say where a period begins, and an instance that
/// answered nothing for a day is not owed the windows that passed while it was idle. Nothing here schedules a
/// roll-over: a later window is simply a different key.
/// </para>
/// <para>
/// Deployment-wide and durable, through <see cref="IMailAnsweringSpendPeriodStore" />. It is where this diverges from
/// what it used to be, and the reason is the one
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// records: a ceiling worded as the deployment's and counted in each process is multiplied by the replica count, and
/// the two ceilings here are the two that cost money. A write per admitted run is what that costs, beside a run that
/// is already about to spend a provider's tokens.
/// </para>
/// <para>
/// A refusal is counted as well as measured, because the two questions an operator asks are opposite: the counter says
/// how often the ceiling was reached, and the gauges say how close the deployment is to reaching it now. A ceiling that
/// is met constantly is a ceiling to raise or a client to look at, and neither is visible from a single number.
/// </para>
/// <para>
/// The gauges publish what this instance last read of the deployment's figures rather than a reading taken when they
/// are collected, because a gauge's callback is synchronous and the figures are a row. Every admission and every spend
/// refreshes them, so an instance answering questions publishes current numbers and one answering none publishes what
/// it last saw — which is the honest reading and is what a deployment aggregating several replicas' instruments has to
/// know about them.
/// </para>
/// <para>
/// Only the first refusal of a period is written to the log, and that bound is the point rather than tidiness: a client
/// that keeps asking is exactly what spends a period's allowance, so a line per refusal would put the log's volume on
/// how enthusiastic that client is. The counter carries how often it happened; the line says that it started. What the
/// line names is the ceiling rather than the counts, because the replica writing it need not be the one that spent the
/// period.
/// </para>
/// <para>
/// Nothing recorded here is mail or derived from it. The values are a run count and a token count, and the one tag is
/// an outcome from a closed set of two — which is a cardinality rule as much as a privacy one, since anything per
/// caller or per question would open a time series that grows with use.
/// </para>
/// </remarks>
public sealed partial class MailAnsweringSpendTracker : IMailAnsweringSpendLedger
{
    private const string OutcomeTagName = "mailfathom.answering.outcome";

    private readonly MailAnsweringPeriodBounds bounds;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<MailAnsweringSpendTracker> logger;
    private readonly Counter<long> runCount;
    private readonly Counter<long> tokenCount;
    private readonly Lock gate = new();
    private DateTimeOffset observedPeriodStartedAt;
    private int observedRuns;
    private long observedTokens;
    private bool refusalReported;

    /// <summary>Initializes a tracker over the deployment's ledger, and the instruments it publishes through.</summary>
    /// <param name="bounds">What the runs of one period may add up to.</param>
    /// <param name="scopeFactory">Opens the scope each durable admission and spend is written through.</param>
    /// <param name="timeProvider">Decides which period an admission or a spend falls in.</param>
    /// <param name="logger">Records that a period was spent, in counts alone.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The scope factory rather than the store itself, because this is one instance for the process — the instruments
    /// below are created once and a second registration of them would publish the same names twice — while the store
    /// reaches the database through the scoped session that owns a connection.
    /// </remarks>
    public MailAnsweringSpendTracker(
        MailAnsweringPeriodBounds bounds,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<MailAnsweringSpendTracker> logger)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.bounds = bounds;
        this.scopeFactory = scopeFactory;
        this.timeProvider = timeProvider;
        this.logger = logger;
        this.observedPeriodStartedAt = bounds.PeriodStartAt(timeProvider.GetUtcNow());

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
    public async Task<bool> TryAdmitRunAsync(CancellationToken cancellationToken)
    {
        var periodStart = this.bounds.PeriodStartAt(this.timeProvider.GetUtcNow());

        await using var scope = this.scopeFactory.CreateAsyncScope();

        var admittedRuns = await scope.ServiceProvider
            .GetRequiredService<IMailAnsweringSpendPeriodStore>()
            .TryAdmitRunAsync(periodStart, this.bounds.MaximumRuns, this.bounds.MaximumTokens, cancellationToken);

        var admitted = admittedRuns > 0;
        var worthReporting = this.ObserveAdmission(periodStart, admittedRuns);

        this.runCount.Add(1, new KeyValuePair<string, object?>(OutcomeTagName, admitted ? "admitted" : "refused"));

        if (worthReporting)
        {
            this.LogPeriodSpent(this.bounds.MaximumRuns, this.bounds.MaximumTokens, this.bounds.Period);
        }

        return admitted;
    }

    /// <inheritdoc />
    public async Task RecordSpendAsync(ChatTokenUsage usage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usage);

        // Charged to the window the call finished in, which is the same rule the admission uses. A run that spans a
        // roll-over therefore pays part of itself into each, and that is the honest reading of a fixed window: the
        // alternative is holding a second row for a period that has ended so a slow call can still reach it.
        var periodStart = this.bounds.PeriodStartAt(this.timeProvider.GetUtcNow());
        var spent = usage.InputTokens + usage.OutputTokens;

        await using var scope = this.scopeFactory.CreateAsyncScope();

        var consumedTokens = await scope.ServiceProvider
            .GetRequiredService<IMailAnsweringSpendPeriodStore>()
            .RecordSpendAsync(periodStart, spent, cancellationToken);

        this.ObserveSpend(periodStart, consumedTokens);

        this.tokenCount.Add(spent);
    }

    /// <summary>Reads what the current period has cost, as this instance last observed the deployment's figures.</summary>
    /// <returns>The period's start, the runs it has admitted, and the tokens they consumed.</returns>
    /// <remarks>
    /// This tracker's own member rather than one of the ledger port's, because nothing above that boundary acts on the
    /// figure: a use case is told whether a question may run and never how close the period is to its ceiling. What
    /// reads it is the pair of gauges above and a test asserting what they would publish. A window this instance has
    /// observed nothing in reads as unspent, which is what it knows rather than what the deployment holds.
    /// </remarks>
    public MailAnsweringSpend Read()
    {
        var currentPeriodStart = this.bounds.PeriodStartAt(this.timeProvider.GetUtcNow());

        lock (this.gate)
        {
            return currentPeriodStart == this.observedPeriodStartedAt
                ? new MailAnsweringSpend(this.observedPeriodStartedAt, this.observedRuns, this.observedTokens)
                : new MailAnsweringSpend(currentPeriodStart, Runs: 0, Tokens: 0);
        }
    }

    /// <summary>Adopts what an admission reported, and decides whether this refusal is the period's first here.</summary>
    /// <remarks>
    /// A refused admission reports no run count, so the observed figure is left where it was: what the deployment has
    /// admitted is at least what this instance last saw, and overwriting it with nothing would publish a gauge that
    /// falls to zero exactly when the period is fullest.
    /// </remarks>
    private bool ObserveAdmission(DateTimeOffset periodStart, int admittedRuns)
    {
        lock (this.gate)
        {
            this.AdoptPeriod(periodStart);

            if (admittedRuns > 0)
            {
                this.observedRuns = admittedRuns;

                return false;
            }

            var worthReporting = !this.refusalReported;
            this.refusalReported = true;

            return worthReporting;
        }
    }

    private void ObserveSpend(DateTimeOffset periodStart, long consumedTokens)
    {
        lock (this.gate)
        {
            this.AdoptPeriod(periodStart);

            this.observedTokens = consumedTokens;
        }
    }

    /// <summary>Drops what was observed whenever the write that reported it belongs to a later window.</summary>
    private void AdoptPeriod(DateTimeOffset periodStart)
    {
        if (periodStart == this.observedPeriodStartedAt)
        {
            return;
        }

        this.observedPeriodStartedAt = periodStart;
        this.observedRuns = 0;
        this.observedTokens = 0;
        this.refusalReported = false;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Answering was refused: this deployment's current period has spent what it allows, which is at most {MaximumRuns} runs costing at most {MaximumTokens} tokens every {Period}. Questions are answered again when the period turns over. Later refusals in this period are counted rather than written.")]
    private partial void LogPeriodSpent(int maximumRuns, long maximumTokens, TimeSpan period);
}
