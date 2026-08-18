// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Observability;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes one segment of a re-derivation as a span, its bounded passes beneath it, and what they re-read.</summary>
/// <remarks>
/// <para>
/// The spans answer where a segment's time went, which is the question a walk over tens of thousands of stored messages
/// raises and which one duration for the whole attempt cannot: a pass reads local bytes, parses them, and commits, and
/// the three slow down for entirely different reasons.
/// </para>
/// <para>
/// The counters answer the question spans cannot, because it is asked across segments rather than within one — how much
/// of the mailbox has been re-read, and how much of it nobody could parse or no longer has MIME to parse. They carry the
/// same two dimensions the segment's span does, so a deployment refreshing two accounts can tell them apart.
/// </para>
/// <para>
/// Nothing here is derived from a message. The values are counts and durations, and the dimensions are the deployment's
/// own configured aliases for a mailbox and a folder.
/// </para>
/// </remarks>
public sealed class StoredMailRederivationTelemetry : IStoredMailRederivationTelemetry
{
    /// <summary>The span one segment of a run is published as.</summary>
    internal const string RunSpanName = "rederive_stored_mail";

    /// <summary>The span one bounded pass of a segment is published as.</summary>
    internal const string PassSpanName = "rederive_stored_mail_pass";

    /// <summary>The dimension naming the account whose stored mail is being re-read.</summary>
    internal const string AccountTagName = "mailfathom.mail.account";

    /// <summary>The dimension naming the folder, which a whole-account run reports its own word for.</summary>
    internal const string FolderTagName = "mailfathom.mail.folder";

    /// <summary>The event a segment that handed the rest of the walk on publishes on its own span.</summary>
    internal const string HandedOnEventName = "handed_on";

    /// <summary>The dimension saying whether the segment carrying the remainder is waiting in the queue.</summary>
    internal const string QueuedTagName = "mailfathom.mail.rederivation.queued";

    /// <summary>What the folder dimension carries when the run covers every folder the account holds mail in.</summary>
    /// <remarks>
    /// A word rather than an absent tag, because a series with the dimension missing and one carrying a folder are two
    /// shapes a dashboard has to sum differently. It is not an alias: an alias is validated non-blank and this is not
    /// one an operator can configure, so no folder can collide with it.
    /// </remarks>
    internal const string WholeAccountFolderTagValue = "(every folder)";

    private readonly TimeProvider timeProvider;
    private readonly Counter<long> rederivedEmails;
    private readonly Counter<long> unreadableEmails;
    private readonly Counter<long> missingContentEmails;
    private readonly Histogram<double> passDuration;

    /// <summary>Initializes the instruments every pass reports through.</summary>
    /// <param name="timeProvider">Times each bounded pass.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public StoredMailRederivationTelemetry(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
        this.rederivedEmails = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.rederivation.rederived",
            unit: "{message}",
            description: "Messages a re-derivation re-read from stored MIME and wrote metadata for.");
        this.unreadableEmails = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.rederivation.unreadable",
            unit: "{message}",
            description: "Messages a re-derivation stepped over because no reader could parse their stored MIME.");
        this.missingContentEmails = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.rederivation.missing_content",
            unit: "{message}",
            description: "Messages a re-derivation stepped over because their raw MIME is no longer stored.");
        this.passDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.mail.rederivation.pass.duration",
            unit: "s",
            description: "How long one bounded pass of a re-derivation took.");
    }

    /// <inheritdoc />
    public IStoredMailRederivationRunScope BeginRun(MailAccountId accountId, MailFolderAlias? folderAlias)
    {
        var tags = new TagList
        {
            { AccountTagName, accountId.Value },
            { FolderTagName, folderAlias?.Value ?? WholeAccountFolderTagValue },
        };

        var activity = Telemetry.ActivitySource.StartActivity(RunSpanName);
        activity?.SetTag(AccountTagName, accountId.Value);
        activity?.SetTag(FolderTagName, folderAlias?.Value ?? WholeAccountFolderTagValue);

        return new RunScope(this, activity, tags);
    }

    /// <summary>Records what one pass committed, and how long it took.</summary>
    /// <remarks>
    /// Each counter is added to only when it moved. A stream of zeroes on the two rejection counters would make a
    /// mailbox whose MIME reads cleanly indistinguishable from one nobody is walking.
    /// </remarks>
    private void RecordPass(StoredMailRederivationPass pass, TimeSpan duration, TagList tags)
    {
        if (pass.RederivedEmailCount > 0)
        {
            this.rederivedEmails.Add(pass.RederivedEmailCount, tags);
        }

        if (pass.UnreadableEmailCount > 0)
        {
            this.unreadableEmails.Add(pass.UnreadableEmailCount, tags);
        }

        if (pass.MissingContentEmailCount > 0)
        {
            this.missingContentEmails.Add(pass.MissingContentEmailCount, tags);
        }

        this.passDuration.Record(duration.TotalSeconds, tags);
    }

    /// <summary>Holds one segment's span open, and opens each pass beneath it.</summary>
    private sealed class RunScope(StoredMailRederivationTelemetry telemetry, Activity? activity, TagList tags)
        : IStoredMailRederivationRunScope
    {
        private bool reachedEndOfScope;
        private bool stalled;
        private bool reported;

        public IStoredMailRederivationPassScope BeginPass() => new PassScope(
            telemetry,
            Telemetry.ActivitySource.StartActivity(PassSpanName),
            tags,
            telemetry.timeProvider.GetTimestamp());

        public void ReachedEndOfScope() => this.reachedEndOfScope = true;

        public void HandedOn(bool queued)
        {
            this.stalled = !queued;

            ActivityTagsCollection handedOn = new() { { QueuedTagName, queued } };

            activity?.AddEvent(new ActivityEvent(
                HandedOnEventName,
                telemetry.timeProvider.GetUtcNow(),
                handedOn));
        }

        public void Dispose()
        {
            if (this.reported)
            {
                return;
            }

            this.reported = true;

            // A segment that handed the rest of the walk on is not an error and not a success either: the work it was
            // given is done and the run is not. Only the segment that ended the run reports Ok, and only one whose
            // hand-on the queue refused reports an error, because that is the run nothing is carrying.
            activity?.SetStatus(Status(this.reachedEndOfScope, this.stalled));
            activity?.Dispose();
        }

        private static ActivityStatusCode Status(bool reachedEndOfScope, bool stalled) => (reachedEndOfScope, stalled) switch
        {
            (true, _) => ActivityStatusCode.Ok,
            (false, true) => ActivityStatusCode.Error,
            _ => ActivityStatusCode.Unset,
        };
    }

    /// <summary>Holds one pass's span open, and publishes what it committed when it is disposed.</summary>
    /// <remarks>
    /// The duration is measured here rather than taken from the span, because a pass that was cancelled part way
    /// through publishes no measurement at all: what it committed is durable but is not a pass anybody can compare
    /// against another, and a truncated duration in the histogram would read as a mailbox that had got faster.
    /// </remarks>
    private sealed class PassScope(
        StoredMailRederivationTelemetry telemetry,
        Activity? activity,
        TagList tags,
        long startingTimestamp)
        : IStoredMailRederivationPassScope
    {
        private StoredMailRederivationPass? committed;
        private bool reported;

        public void Completed(StoredMailRederivationPass pass) => this.committed = pass;

        public void Dispose()
        {
            if (this.reported)
            {
                return;
            }

            this.reported = true;

            if (this.committed is { } pass)
            {
                telemetry.RecordPass(pass, telemetry.timeProvider.GetElapsedTime(startingTimestamp), tags);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }

            activity?.Dispose();
        }
    }
}
