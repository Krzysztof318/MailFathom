// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports how fast the extraction backfill is working and how much of the mailbox is still ahead of it.</summary>
/// <remarks>
/// <para>
/// The pass already opens a span, which says what one run did and how long it took. What a span cannot answer is the
/// question an operator actually has about a backfill — <em>will this finish</em> — because that needs a rate and a
/// remaining amount side by side, over runs rather than within one. Without both, the only way to ask is a query
/// against the database by hand.
/// </para>
/// <para>
/// The backlog is a gauge fed once per run rather than measured when a collector asks. It is a count over every message
/// the walk still owes work on, so answering it inside the meter's callback would put that scan on whatever interval a
/// collector happened to be configured with; a figure that is one run old is what somebody watching a backfill needs,
/// and the counters beside it are what move in between. A backlog that stops falling while the extracted counter keeps
/// rising is a walk finding new work as fast as it does old — which is a mailbox still synchronizing rather than a
/// backfill that has stalled, and the two are only separable because both figures are published.
/// </para>
/// <para>
/// Nothing here is derived from a message. The values are counts and a duration, and the one dimension is the outcome,
/// which is a closed set of MailFathom's own words.
/// </para>
/// </remarks>
public sealed class MailExtractionBackfillTelemetry
{
    /// <summary>The dimension every pass is broken down by, which the worker's span carries under the same name.</summary>
    /// <remarks>
    /// This constant and the four below it are public because the worker that drives the pass lives in the composition
    /// root and tags its span with the same words. One declaration is what stops a span saying <c>deferred</c> while the
    /// series beside it says something else about the same pass.
    /// </remarks>
    public const string OutcomeTagName = "mailfathom.mail.extraction.backfill.outcome";

    /// <summary>Names a pass that ran to the end of its batch budget without anything stopping it.</summary>
    public const string SucceededOutcomeName = "succeeded";

    /// <summary>Names a pass a competing writer deferred, which the next interval resumes from.</summary>
    public const string DeferredOutcomeName = "deferred";

    /// <summary>Names a pass that failed, which the next interval also resumes from the committed position.</summary>
    public const string FailedOutcomeName = "failed";

    /// <summary>Names a pass the host stopped, which is shutdown rather than a failure.</summary>
    public const string InterruptedOutcomeName = "interrupted";

    private readonly Counter<long> extractedEmails;
    private readonly Counter<long> unreadableEmails;
    private readonly Counter<long> missingContentEmails;
    private readonly Histogram<double> runDuration;

    private int outstandingEmailCount;

    /// <summary>Initializes the instruments every bounded pass reports through.</summary>
    public MailExtractionBackfillTelemetry()
    {
        this.extractedEmails = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.extraction.backfill.extracted",
            unit: "{message}",
            description: "Messages the backfill re-read and gave normalized metadata and searchable text.");
        this.unreadableEmails = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.extraction.backfill.unreadable",
            unit: "{message}",
            description: "Messages the backfill stepped over because no reader could parse their stored MIME.");
        this.missingContentEmails = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.extraction.backfill.missing_content",
            unit: "{message}",
            description: "Messages the backfill stepped over because their raw MIME is no longer stored.");
        this.runDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.mail.extraction.backfill.run.duration",
            unit: "s",
            description: "How long one bounded pass of the extraction backfill took, by how it ended.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.mail.extraction.backfill.outstanding",
            () => Volatile.Read(ref this.outstandingEmailCount),
            unit: "{message}",
            description: "Messages still awaiting extraction when the most recent pass ended.");
    }

    /// <summary>Records a pass that completed, what it moved, and the backlog it found ahead of it.</summary>
    /// <param name="result">What the pass produced.</param>
    /// <param name="duration">How long it took.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Each counter is added to only when it moved. A stream of zeroes every interval would make an instance with
    /// nothing left to extract indistinguishable from one working through a mailbox.
    /// </remarks>
    public void RecordCompleted(StoredEmailExtractionBackfillResult result, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(result);

        Volatile.Write(ref this.outstandingEmailCount, result.OutstandingEmailCount);

        if (result.ExtractedEmailCount > 0)
        {
            this.extractedEmails.Add(result.ExtractedEmailCount);
        }

        if (result.UnreadableEmailCount > 0)
        {
            this.unreadableEmails.Add(result.UnreadableEmailCount);
        }

        if (result.MissingContentEmailCount > 0)
        {
            this.missingContentEmails.Add(result.MissingContentEmailCount);
        }

        this.RecordRun(SucceededOutcomeName, duration);
    }

    /// <summary>Records a pass a competing writer deferred, which moved nothing this interval.</summary>
    /// <param name="duration">How long it ran before the conflict ended it.</param>
    public void RecordDeferred(TimeSpan duration) => this.RecordRun(DeferredOutcomeName, duration);

    /// <summary>Records a pass that failed, which the next interval resumes from the committed position.</summary>
    /// <param name="duration">How long it ran before it failed.</param>
    public void RecordFailed(TimeSpan duration) => this.RecordRun(FailedOutcomeName, duration);

    /// <summary>Records a pass the host stopped, which is shutdown rather than a failure.</summary>
    /// <param name="duration">How long it ran before it was stopped.</param>
    public void RecordInterrupted(TimeSpan duration) => this.RecordRun(InterruptedOutcomeName, duration);

    /// <summary>Records one pass's duration under the outcome that ended it, which is also how passes are counted.</summary>
    /// <remarks>
    /// The histogram's own count is the number of passes, so a separate run counter would publish the same figure under
    /// a second name and give a dashboard two places to disagree about how many passes there were.
    /// </remarks>
    private void RecordRun(string outcome, TimeSpan duration) =>
        this.runDuration.Record(duration.TotalSeconds, new TagList { { OutcomeTagName, outcome } });
}
