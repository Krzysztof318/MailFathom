// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports what a deployment's outbox is doing, as the two questions an operator actually asks of it.</summary>
/// <remarks>
/// <para>
/// The first is whether mail is leaving, which the attempt counter answers by account and by outcome. The second is
/// whether one particular submission is the slow part, which the span answers by covering the exchange with the server
/// and nothing else — the claim, the record movements, and the backoff are local work and would blur what the span is
/// for.
/// </para>
/// <para>
/// The one outcome worth alerting on is the unknown one, and it is a dimension of the same counter rather than an
/// instrument of its own: a send whose server never answered is neither a success nor a failure, and separating it
/// would let a dashboard summing successes and failures report a total that quietly omits it.
/// </para>
/// <para>
/// Nothing recorded here is mail. The account alias is MailFathom's own configured name, and the outcome is one of a
/// closed set of words this system chose; no address, subject, reply text, message identifier, or recipient count
/// reaches a span, a log, or an exporter.
/// </para>
/// </remarks>
public sealed class MailDeliveryTelemetry
{
    private const string AccountTagName = "mailfathom.mail.account";
    private const string OutcomeTagName = "mailfathom.mail.delivery.outcome";
    /// <summary>The span one submission to a provider is reported under.</summary>
    internal const string SubmissionSpanName = "submit_outgoing_email";

    private readonly TimeProvider timeProvider;
    private readonly Counter<long> attemptCount;
    private readonly Histogram<double> submissionDuration;

    /// <summary>Initializes the instruments every delivery attempt reports through.</summary>
    /// <param name="timeProvider">Measures how long one submission took.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public MailDeliveryTelemetry(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
        this.attemptCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.delivery.attempts",
            unit: "{attempt}",
            description: "Attempts to deliver a queued outgoing message, by account and outcome.");
        this.submissionDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.mail.delivery.submission.duration",
            unit: "s",
            description: "How long one submission to a mail provider took, by account.");
    }

    /// <summary>Begins reporting one submission, and returns the scope that finishes the report.</summary>
    /// <param name="accountId">The account whose message is being submitted.</param>
    /// <returns>The scope, which the caller must dispose; a scope disposed without <see cref="MailDeliveryScope.Completed" /> reports a failure.</returns>
    internal MailDeliveryScope BeginSubmission(MailAccountId accountId)
    {
        var activity = Telemetry.ActivitySource.StartActivity(SubmissionSpanName, ActivityKind.Client);
        activity?.SetTag(AccountTagName, accountId.Value);

        return new MailDeliveryScope(this, accountId, activity, this.timeProvider.GetTimestamp());
    }

    /// <summary>Publishes what one pass over an account's outbox did.</summary>
    /// <param name="accountId">The account the pass ran over.</param>
    /// <param name="report">What each claimed send ended in.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="report" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// One measurement per send rather than one per pass, because the outcome is what the counter is broken down by and
    /// a pass routinely ends in more than one of them.
    /// </remarks>
    public void Report(MailAccountId accountId, MailOutboxPassReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        foreach (var result in report.Results)
        {
            this.attemptCount.Add(
                1,
                new TagList
                {
                    { AccountTagName, accountId.Value },
                    { OutcomeTagName, NameOf(result.Outcome) },
                });
        }

        // What the sweep found is an attempt too — one an earlier process made and never lived to report. It is
        // counted where it becomes knowable rather than not at all, because a stranded send is exactly the measurement
        // this counter exists to surface, and it is stamped once, so counting it here cannot count it twice.
        if (report.MarkedUnknownCount > 0)
        {
            this.attemptCount.Add(
                report.MarkedUnknownCount,
                new TagList
                {
                    { AccountTagName, accountId.Value },
                    { OutcomeTagName, NameOf(MailOutboxDeliveryOutcome.OutcomeUnknown) },
                });
        }
    }

    /// <summary>Records how long one submission took and whether the server took the message.</summary>
    internal void RecordSubmission(MailAccountId accountId, TimeSpan elapsed) =>
        this.submissionDuration.Record(elapsed.TotalSeconds, new TagList { { AccountTagName, accountId.Value } });

    internal TimeSpan ElapsedSince(long startingTimestamp) => this.timeProvider.GetElapsedTime(startingTimestamp);

    /// <summary>Names an outcome as the dimension a dashboard groups by.</summary>
    /// <remarks>
    /// Written out rather than derived from the member name, so a rename of the enum cannot silently split a metric
    /// series that a dashboard or an alert was already grouping by. Every member is named and nothing falls through,
    /// because a default arm answering for a member nobody listed would publish one outcome under another's word —
    /// which is the same failure read from the other end.
    /// </remarks>
    private static string NameOf(MailOutboxDeliveryOutcome outcome) => outcome switch
    {
        MailOutboxDeliveryOutcome.Sent => "sent",
        MailOutboxDeliveryOutcome.Refused => "refused",
        MailOutboxDeliveryOutcome.Deferred => "deferred",
        MailOutboxDeliveryOutcome.OutcomeUnknown => "outcome-unknown",
        MailOutboxDeliveryOutcome.ReleasedForShutdown => "released",
        MailOutboxDeliveryOutcome.LeaseLost => "lease-lost",
        MailOutboxDeliveryOutcome.NotRecorded => "not-recorded",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "No metric dimension is defined for this delivery outcome."),
    };
}
