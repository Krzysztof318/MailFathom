// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports what a deployment's outbox is doing, as the questions an operator actually asks of it.</summary>
/// <remarks>
/// <para>
/// The first is whether mail is leaving, which the attempt counter answers by account and by outcome. The second is
/// whether one particular submission is the slow part, which the span answers by covering the exchange with the server
/// and nothing else — the claim, the record movements, and the backoff are local work and would blur what the span is
/// for.
/// </para>
/// <para>
/// The third is whether the owner can see what they sent in their own mail client, which the filing counter answers by
/// account, by the place a copy was meant for, and by outcome. It is a counter of its own rather than a dimension of
/// the attempts, because the two say different things about the same message: a copy that could not be filed never
/// means the message failed to reach anybody, and summing them would produce a failure rate nobody could act on.
/// </para>
/// <para>
/// The one outcome worth alerting on is the unknown one, and it is a dimension of the same counter rather than an
/// instrument of its own: a send whose server never answered is neither a success nor a failure, and separating it
/// would let a dashboard summing successes and failures report a total that quietly omits it.
/// </para>
/// <para>
/// Two instruments beside those answer questions no rate can. The retries say how much of the attempt count is work
/// being repeated, which is what separates an instance that is sending a lot from one that is failing and trying again;
/// the depth is the level all of them are a rate against, so a stalled outbox is visible while it is still small and
/// long before anybody is told about a message that never arrived. The depth is a gauge over the last figure a pass
/// measured rather than a live count, for the reason the queue's own is: an exact live count is a query, and making it
/// a gauge would put that query on whatever interval a collector happened to be configured with.
/// </para>
/// <para>
/// Nothing recorded here is mail. The account alias is MailFathom's own configured name, the outcome and the stage are
/// closed sets of words this system chose, and the one identifier a span carries is the record's own, which is what an
/// operator types into <c>mfctl</c> to read the send a slow trace belongs to. No address, subject, reply text, or
/// recipient count reaches a span, a log, or an exporter.
/// </para>
/// </remarks>
public sealed class MailDeliveryTelemetry
{
    private const string AccountTagName = "mailfathom.mail.account";
    private const string OutcomeTagName = "mailfathom.mail.delivery.outcome";
    private const string FilingTagName = "mailfathom.mail.filing.place";
    private const string FilingOutcomeTagName = "mailfathom.mail.filing.outcome";
    private const string StageTagName = "mailfathom.mail.delivery.stage";

    /// <summary>The span one submission to a provider is reported under.</summary>
    internal const string SubmissionSpanName = "submit_outgoing_email";

    /// <summary>The record the submission belongs to, which is what joins a span to the send an operator can read.</summary>
    /// <remarks>
    /// The one identifier on the span, and it is MailFathom's own rather than the message's: a <c>Message-ID</c> is
    /// written into every recipient's mailbox and would follow the correspondence out of this deployment, while this
    /// value means nothing anywhere but here. It is what makes a slow or failed exchange actionable — the operator
    /// reads the send it belongs to with <c>mfctl outbox show</c> and decides about that one record.
    /// </remarks>
    internal const string RecordTagName = "mailfathom.mail.delivery.record";

    private readonly ConcurrentDictionary<(string Account, string Stage), int> outstandingByAccountAndStage =
        new();

    private readonly TimeProvider timeProvider;
    private readonly Counter<long> attemptCount;
    private readonly Counter<long> retryCount;
    private readonly Counter<long> filingCount;
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
        this.retryCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.delivery.retries",
            unit: "{attempt}",
            description: "Delivery attempts that were not the message's first, by account and outcome.");
        this.filingCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.filing.attempts",
            unit: "{attempt}",
            description: "Attempts to put a copy of an outgoing message into a folder, by account, place, and outcome.");
        this.submissionDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.mail.delivery.submission.duration",
            unit: "s",
            description: "How long one submission to a mail provider took, by account.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.mail.outbox.depth",
            this.ObserveOutstanding,
            unit: "{message}",
            description: "Queued outgoing messages standing at each stage nothing has finished with, by account, as the last delivery pass measured it.");
    }

    /// <summary>Begins reporting one submission, and returns the scope that finishes the report.</summary>
    /// <param name="accountId">The account whose message is being submitted.</param>
    /// <param name="outgoingEmailId">The record the submission belongs to, which the span names so an operator can read it.</param>
    /// <returns>The scope, which the caller must dispose; a scope disposed without <see cref="MailDeliveryScope.Completed" /> reports a failure.</returns>
    internal MailDeliveryScope BeginSubmission(MailAccountId accountId, OutgoingEmailId outgoingEmailId)
    {
        var activity = Telemetry.ActivitySource.StartActivity(SubmissionSpanName, ActivityKind.Client);
        activity?.SetTag(AccountTagName, accountId.Value);
        activity?.SetTag(RecordTagName, outgoingEmailId.Value.ToString());

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
            TagList attemptTags = new()
            {
                { AccountTagName, accountId.Value },
                { OutcomeTagName, NameOf(result.Outcome) },
            };

            this.attemptCount.Add(1, attemptTags);

            // An attempt past the first is the same measurement read as repetition, so it carries the outcome that
            // failed rather than a dimension of its own: what a rising retry rate says is which ending keeps coming
            // back, and a counter without that would only say that something does.
            if (result.AttemptCount > 1)
            {
                this.retryCount.Add(1, attemptTags);
            }
        }

        foreach (var filing in report.FilingResults)
        {
            this.filingCount.Add(
                1,
                new TagList
                {
                    { AccountTagName, accountId.Value },
                    { FilingTagName, filing.FilingName },
                    { FilingOutcomeTagName, NameOf(filing.Outcome) },
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

        // A pass that measured nothing leaves the account's last known level standing. An account with no submission
        // endpoint and a pass that failed before it counted both produce an empty measurement, and publishing zero for
        // either would clear a backlog on a dashboard that nothing had drained.
        foreach (var counted in report.OutstandingByStage)
        {
            this.outstandingByAccountAndStage[(accountId.Value, NameOf(counted.Stage))] = counted.Count;
        }
    }

    /// <summary>Records how long one submission took and whether the server took the message.</summary>
    internal void RecordSubmission(MailAccountId accountId, TimeSpan elapsed) =>
        this.submissionDuration.Record(elapsed.TotalSeconds, new TagList { { AccountTagName, accountId.Value } });

    internal TimeSpan ElapsedSince(long startingTimestamp) => this.timeProvider.GetElapsedTime(startingTimestamp);

    /// <summary>Publishes the level each account was last measured at, one measurement per account and stage.</summary>
    private IEnumerable<Measurement<int>> ObserveOutstanding() =>
    [
        .. this.outstandingByAccountAndStage.Select(level => new Measurement<int>(
            level.Value,
            new TagList
            {
                { AccountTagName, level.Key.Account },
                { StageTagName, level.Key.Stage },
            })),
    ];

    /// <summary>Names a stage as the dimension a dashboard groups by, under the same rule the outcomes follow.</summary>
    /// <remarks>
    /// Only the stages a send can still move from are named. A terminal stage is history rather than depth, and nothing
    /// counts one into this gauge — a member reaching here would therefore be a defect in what was counted rather than a
    /// dimension to invent a word for.
    /// </remarks>
    private static string NameOf(OutgoingEmailStage stage) => stage switch
    {
        OutgoingEmailStage.Recorded => "recorded",
        OutgoingEmailStage.TransmissionBegun => "transmission-begun",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "No outbox depth dimension is defined for this stage."),
    };

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

    /// <summary>Names a filing outcome as the dimension a dashboard groups by, under the same rule as the one above.</summary>
    private static string NameOf(OutgoingMailFilingOutcome outcome) => outcome switch
    {
        OutgoingMailFilingOutcome.Filed => "filed",
        OutgoingMailFilingOutcome.AlreadyFiled => "already-filed",
        OutgoingMailFilingOutcome.NotRequested => "not-requested",
        OutgoingMailFilingOutcome.DestinationUnavailable => "destination-unavailable",
        OutgoingMailFilingOutcome.OutcomeUnknown => "outcome-unknown",
        OutgoingMailFilingOutcome.Failed => "failed",
        OutgoingMailFilingOutcome.Withdrawn => "withdrawn",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "No metric dimension is defined for this filing outcome."),
    };
}
