// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Common.Observability;

namespace MailFathom.Host.Observability.ClientTelemetry;

/// <summary>Reports what the proxy accepted, refused, forwarded, and failed to forward.</summary>
/// <remarks>
/// <para>
/// The proxy is silent while it works, and that is a constraint rather than a preference. A signed-in client exports
/// every few seconds for as long as it is open, so a record per accepted batch would be the largest thing in a
/// deployment's own logs within a day while saying nothing that was not already true — an operator reading their logs
/// would be reading the bookkeeping rather than the deployment. So accepting and forwarding writes nothing at all, at
/// any level, and the numbers live here instead.
/// </para>
/// <para>
/// A failure does write a line, and it is written <b>per condition rather than per batch</b>. A collector that has been
/// unreachable for an hour is one condition, not two thousand incidents, so the first batch to meet it says so and the
/// next line comes when the condition has held for long enough to be worth repeating — carrying how many batches went
/// with it, which is the part a rate cannot tell an operator reading a log. The line carries the signal, the condition,
/// and a count, and no part of any payload: what was in a batch is somebody's telemetry, and none of it is this
/// process's to write down.
/// </para>
/// <para>
/// Every instrument's dimensions are closed sets of this process's own words — three signal names, six refusals, six
/// conditions — so nothing here opens a series per person, per batch, or per collector. The owner a batch was
/// attributed to is deliberately on no instrument, for the reason
/// <see cref="Infrastructure.Observability.SensitiveContentEgressTelemetry" /> states about the same identifier: a
/// counter incremented once per export would be a time series per person.
/// </para>
/// </remarks>
internal sealed partial class ClientTelemetryProxyTelemetry
{
    /// <summary>Which of the three signals a batch belonged to.</summary>
    internal const string SignalTagName = "mailfathom.client_telemetry.signal";

    /// <summary>Why this endpoint refused a batch before anything was forwarded.</summary>
    internal const string RefusalTagName = "mailfathom.client_telemetry.refusal";

    /// <summary>What stopped a batch from reaching the destination.</summary>
    internal const string ConditionTagName = "mailfathom.client_telemetry.condition";

    /// <summary>How long one condition holds before a second line is written about it.</summary>
    /// <remarks>
    /// Long enough that an outage is a handful of lines an hour rather than a stream, and short enough that an operator
    /// watching a deployment sees the condition is still true. It is a constant rather than a setting because it
    /// decides nothing an operator configures around: what they act on is the condition, and the counter beside it
    /// already reports the rate at whatever resolution their collector keeps.
    /// </remarks>
    private static readonly TimeSpan QuietPeriodPerCondition = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, ReportedCondition> reportedConditions = new(StringComparer.Ordinal);
    private readonly ILogger<ClientTelemetryProxyTelemetry> logger;
    private readonly TimeProvider clock;
    private readonly Counter<long> acceptedBatchCount;
    private readonly Counter<long> acceptedRecordCount;
    private readonly Counter<long> refusedBatchCount;
    private readonly Counter<long> forwardedBatchCount;
    private readonly Counter<long> failedBatchCount;

    /// <summary>Initializes the instruments every forwarded batch reports through.</summary>
    /// <param name="logger">Where a forwarding condition is written, and nothing else is.</param>
    /// <param name="clock">Decides when a condition has held long enough to be written about again.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger" /> or <paramref name="clock" /> is <see langword="null" />.</exception>
    public ClientTelemetryProxyTelemetry(ILogger<ClientTelemetryProxyTelemetry> logger, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);

        this.logger = logger;
        this.clock = clock;

        this.acceptedBatchCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.client_telemetry.accepted",
            unit: "{batch}",
            description: "Client telemetry batches this endpoint read, bounded, and attributed, by signal.");
        this.acceptedRecordCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.client_telemetry.records",
            unit: "{record}",
            description: "Records carried by the accepted batches, by signal, which is what their volume is read against.");
        this.refusedBatchCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.client_telemetry.refused",
            unit: "{batch}",
            description: "Client telemetry batches this endpoint refused before forwarding anything, by signal and refusal.");
        this.forwardedBatchCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.client_telemetry.forwarded",
            unit: "{batch}",
            description: "Client telemetry batches the configured destination accepted, by signal.");
        this.failedBatchCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.client_telemetry.failed",
            unit: "{batch}",
            description: "Client telemetry batches that did not reach the destination, by signal and condition.");
    }

    /// <summary>Records a batch this endpoint read and is about to forward.</summary>
    /// <param name="signal">The signal the batch belongs to.</param>
    /// <param name="records">How many records it carries.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signal" /> is <see langword="null" />.</exception>
    internal void RecordAccepted(ClientTelemetrySignal signal, int records)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var tags = new TagList { { SignalTagName, signal.Name } };

        this.acceptedBatchCount.Add(1, tags);
        this.acceptedRecordCount.Add(records, tags);
    }

    /// <summary>Records a batch this endpoint refused, which reached no destination and was never counted as accepted.</summary>
    /// <param name="signal">The signal the batch claimed to belong to.</param>
    /// <param name="refusal">Why it was refused, in this endpoint's own closed vocabulary.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signal" /> or <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// No line is written for one. A refusal is the endpoint working — a client sending what this deployment will not
    /// take is answered, and an answer is not an incident — and the counter is what makes a client suddenly being
    /// refused visible without a log entry per attempt.
    /// </remarks>
    internal void RecordRefused(ClientTelemetrySignal signal, string refusal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(refusal);

        this.refusedBatchCount.Add(
            1,
            new TagList { { SignalTagName, signal.Name }, { RefusalTagName, refusal } });
    }

    /// <summary>Records what became of a batch that was sent on.</summary>
    /// <param name="signal">The signal the batch belongs to.</param>
    /// <param name="forwarding">What the destination did with it.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signal" /> is <see langword="null" />.</exception>
    internal void RecordForwarding(ClientTelemetrySignal signal, ClientTelemetryForwarding forwarding)
    {
        ArgumentNullException.ThrowIfNull(signal);

        if (forwarding.Arrived)
        {
            this.forwardedBatchCount.Add(1, new TagList { { SignalTagName, signal.Name } });

            return;
        }

        var condition = ConditionOf(forwarding.Failure);

        this.failedBatchCount.Add(
            1,
            new TagList { { SignalTagName, signal.Name }, { ConditionTagName, condition } });

        this.ReportCondition(signal, condition);
    }

    /// <summary>Names one forwarding condition in the vocabulary the log and the counter share.</summary>
    /// <param name="failure">The condition being named.</param>
    /// <returns>The word the dimension carries.</returns>
    /// <remarks>Past participles, as every other outcome in this repository is written, so a panel splitting on this reads the same way as one splitting on any other family's.</remarks>
    internal static string ConditionOf(ClientTelemetryFailure failure) => failure switch
    {
        ClientTelemetryFailure.None => "forwarded",
        ClientTelemetryFailure.Refused => "refused",
        ClientTelemetryFailure.Throttled => "throttled",
        ClientTelemetryFailure.Unavailable => "unavailable",
        ClientTelemetryFailure.TimedOut => "timed_out",
        ClientTelemetryFailure.Unreachable => "unreachable",
        _ => "cancelled",
    };

    /// <summary>Writes a line about one condition, or counts this batch against the line already written for it.</summary>
    /// <remarks>
    /// The count reported is the batches that met the condition since the previous line, so a reader sees both that the
    /// condition holds and how much traffic it is costing. The dictionary is keyed by the condition rather than by the
    /// signal beside it, because a collector that is unreachable is unreachable for all three and three identical lines
    /// would be the noise this exists to avoid.
    /// </remarks>
    private void ReportCondition(ClientTelemetrySignal signal, string condition)
    {
        var now = this.clock.GetUtcNow();
        var reported = this.reportedConditions.AddOrUpdate(
            condition,
            _ => new ReportedCondition(now, Unreported: 0, Due: 1),
            (_, previous) => previous.WithAnother(now, QuietPeriodPerCondition));

        if (reported.Due > 0)
        {
            this.LogForwardingCondition(condition, signal.Name, reported.Due);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Client telemetry could not be forwarded to the configured OTLP destination [{Condition}], most "
            + "recently a {Signal} batch, and {Batches} batch(es) have been dropped under this condition since it was "
            + "last reported. The clients hold what they have not exported; nothing is queued here.")]
    private partial void LogForwardingCondition(string condition, string signal, long batches);

    /// <summary>When one condition was last written about, and how many batches have met it silently since.</summary>
    /// <param name="ReportedAt">When the last line about this condition was written.</param>
    /// <param name="Unreported">How many batches have met it since that line, none of which produced one.</param>
    /// <param name="Due">How many batches this line reports, or zero where no line is owed.</param>
    private readonly record struct ReportedCondition(DateTimeOffset ReportedAt, long Unreported, long Due)
    {
        /// <summary>Counts one more batch against this condition, writing it off as a line once the quiet period has passed.</summary>
        /// <param name="now">The instant the batch failed at.</param>
        /// <param name="quietPeriod">How long a condition holds before it is worth writing about again.</param>
        /// <returns>The condition as it now stands, whose <see cref="Due" /> is what the line owed reports.</returns>
        internal ReportedCondition WithAnother(DateTimeOffset now, TimeSpan quietPeriod) =>
            now - this.ReportedAt >= quietPeriod
                ? new ReportedCondition(now, Unreported: 0, Due: this.Unreported + 1)
                : new ReportedCondition(this.ReportedAt, this.Unreported + 1, Due: 0);
    }
}
