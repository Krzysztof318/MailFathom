// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Application.Observability;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes one read of the local mailbox copy as a span between the protocol call and the queries it issued.</summary>
/// <remarks>
/// <para>
/// The span is named after the use case, so a trace reads as the work that was done rather than as the tool that asked
/// for it — the tool's own name is already on the MCP SDK's span above. Nesting is what the span is for: it becomes a
/// child of that protocol span because it is started inside it, and the database and content-store spans the read
/// causes become children of this, which is what attributes a slow tool call to a use case, to a query, or to neither.
/// </para>
/// <para>
/// A read publishes a count and an ending and nothing else. Not the query text, not a filter value, not a cursor, not a
/// subject, an address, or a stored identity — every one of those is either mail or a value per message, and a span
/// store is the last place either belongs.
/// </para>
/// </remarks>
internal sealed class MailboxReadTelemetry : IMailboxReadTelemetry
{
    internal const string AccountDirectorySpanName = "read_account_directory";
    internal const string MailboxTimelineSpanName = "list_mailbox_timeline";
    internal const string MailboxSearchSpanName = "search_mailbox";
    internal const string EmailContentSpanName = "read_email_content";
    internal const string EmailThreadSpanName = "read_email_thread";

    /// <summary>The name the ranking inside one search opens its span under, beneath the search's own.</summary>
    /// <remarks>
    /// The ends of a ranking are spanned by the libraries they run through and the middle is not: a fusion of two
    /// rankings, and the depth each of them was asked for, are MailFathom's own work between a provider call and a
    /// query. Without this the two are separable only by subtraction.
    /// </remarks>
    internal const string SearchRankingSpanName = "rank_mailbox_search";

    internal const string ResultCountTagName = "mailfathom.mailbox.read.results";
    internal const string OutcomeTagName = "mailfathom.mailbox.read.outcome";

    internal const string SucceededOutcomeName = "succeeded";

    /// <summary>Names a read the caller stopped waiting for, which is a disconnect rather than a defect.</summary>
    internal const string CancelledOutcomeName = "cancelled";

    internal const string FailedOutcomeName = "failed";

    /// <inheritdoc />
    public IMailboxReadScope BeginRead(MailboxReadOperation operation, CancellationToken cancellationToken) =>
        new ReadSpan(Telemetry.ActivitySource.StartActivity(SpanNameOf(operation)), cancellationToken);

    /// <inheritdoc />
    public IMailboxReadScope BeginSearchRanking(CancellationToken cancellationToken) =>
        new ReadSpan(Telemetry.ActivitySource.StartActivity(SearchRankingSpanName), cancellationToken);

    /// <summary>Reads the name one operation is published under.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the operation is not one this adapter publishes.</exception>
    private static string SpanNameOf(MailboxReadOperation operation) => operation switch
    {
        MailboxReadOperation.ReadAccountDirectory => AccountDirectorySpanName,
        MailboxReadOperation.ListMailboxTimeline => MailboxTimelineSpanName,
        MailboxReadOperation.SearchMailbox => MailboxSearchSpanName,
        MailboxReadOperation.ReadEmailContent => EmailContentSpanName,
        MailboxReadOperation.ReadEmailThread => EmailThreadSpanName,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "The read operation has no published span name."),
    };

    /// <summary>Carries one read from the span that opens it to the ending that closes it.</summary>
    /// <remarks>
    /// A read that reported no result is published as cancelled where the caller's token was cancelled and as failed
    /// otherwise. Telling the two apart matters here more than it does for background work: a client disconnecting
    /// mid-read is ordinary traffic, and counting it as a failure would make an impatient assistant look like a broken
    /// deployment. The activity is null on an instance nothing is listening to, which is the ordinary case for a
    /// deployment exporting nothing, and the scope then costs one allocation and no work.
    /// </remarks>
    private sealed class ReadSpan(Activity? activity, CancellationToken cancellationToken) : IMailboxReadScope
    {
        private int? returnedResults;
        private bool reported;

        public void Completed(int resultCount) => this.returnedResults = resultCount;

        public void Dispose()
        {
            if (this.reported)
            {
                return;
            }

            this.reported = true;

            if (activity is null)
            {
                return;
            }

            if (this.returnedResults is { } returned)
            {
                activity.SetTag(ResultCountTagName, returned);
                activity.SetTag(OutcomeTagName, SucceededOutcomeName);
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                activity.SetTag(OutcomeTagName, CancelledOutcomeName);
                activity.SetStatus(ActivityStatusCode.Unset);
            }
            else
            {
                activity.SetTag(OutcomeTagName, FailedOutcomeName);
                activity.SetStatus(ActivityStatusCode.Error);
            }

            activity.Dispose();
        }
    }
}
