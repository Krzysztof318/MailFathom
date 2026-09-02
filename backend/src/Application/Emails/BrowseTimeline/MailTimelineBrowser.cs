// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Observability;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseTimeline;

/// <summary>Reads one page of a message list, in either direction, from the local mailbox copy.</summary>
/// <remarks>
/// <para>
/// It is the timeline read a screen is drawn from, beside <see cref="ListEmails.MailboxTimelineReader" />, which is the
/// one a tool calls. The two share the scope, the filters, the ordering, the page bound and the cursor codec — a list
/// and a listing are the same walk over the same rows — and differ in the two things a screen needs and a tool does
/// not: a row carries the opening of the message so a list can be drawn from what one request returned, and the walk
/// runs in both directions so somebody who scrolled down can scroll back.
/// </para>
/// <para>
/// Scrolling back is a second walk over the same list rather than a second list. The order the rows are sorted in is
/// part of what a cursor was issued for, so it never changes with the direction a page is asked in: a backward page is
/// read away from the cursor with the walk reversed and handed back in the sorted order, and its cursors carry the
/// sorted list's fingerprint. That is what lets a client page in both directions from cursors it collected in either.
/// </para>
/// <para>
/// It reaches no mail server. A page answers from what synchronization has already stored, so no request from a browser
/// can wait on IMAP and none can set the remote <c>\Seen</c> flag.
/// </para>
/// <para>
/// A page is one of the points mail content leaves this deployment, and it publishes more of a message than a tool
/// listing does, so where a sensitive-content scanner is switched on the subject, the sender's display name and the
/// preview of every row are scanned before the page is returned; a scanner that cannot answer refuses the page rather
/// than serving it unscanned.
/// </para>
/// </remarks>
public sealed class MailTimelineBrowser
{
    /// <summary>How this use case names the value that says which way a page continues, when it refuses one.</summary>
    private const string PageDirectionFilterName = "page direction";

    private readonly IStoredEmailTimelineReader timelineReader;
    private readonly IStoredEmailPreviewReader previewReader;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IMailboxReadTelemetry readTelemetry;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the use case.</summary>
    /// <param name="timelineReader">Reads bounded pages of stored email summaries.</param>
    /// <param name="previewReader">Reads the bounded opening of the text of the emails a page returned.</param>
    /// <param name="scopeResolver">Decides which accounts and folders the list runs against.</param>
    /// <param name="egressGuard">Scans what the page is about to publish, where this deployment scans anything.</param>
    /// <param name="readTelemetry">Publishes the read as the operation it is, beside the call it happened inside.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailTimelineBrowser(
        IStoredEmailTimelineReader timelineReader,
        IStoredEmailPreviewReader previewReader,
        MailboxScopeResolver scopeResolver,
        SensitiveContentEgressGuard egressGuard,
        IMailboxReadTelemetry readTelemetry,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(timelineReader);
        ArgumentNullException.ThrowIfNull(previewReader);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(readTelemetry);
        ArgumentNullException.ThrowIfNull(authorization);

        this.timelineReader = timelineReader;
        this.previewReader = previewReader;
        this.scopeResolver = scopeResolver;
        this.egressGuard = egressGuard;
        this.readTelemetry = readTelemetry;
        this.authorization = authorization;
    }

    /// <summary>Reads one page of the list.</summary>
    /// <param name="request">What the screen asked for.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The page, and the cursor that continues it at each end where one exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the request names an order or a page direction that is not a defined member.</exception>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when a filter carries a value or a count the query does not accept, and when a backward page is asked for without a cursor.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account its owner does not own.</exception>
    /// <exception cref="MailboxQueryPageSizeOutOfRangeException">Thrown when the request names a page size outside the accepted range.</exception>
    /// <exception cref="MailboxQueryCursorMalformedException">Thrown when the request carries a cursor this system did not issue.</exception>
    /// <exception cref="MailboxQueryCursorFilterMismatchException">Thrown when the cursor was issued for a different list than the request describes.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the page carries, which refuses the page rather than serving it unscanned.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// Nothing here writes, and the operation is therefore safe to repeat. The grant is asked for before the request is
    /// validated, so a caller that may not read learns nothing about which filters this deployment accepts.
    /// </remarks>
    public async Task<BrowsedTimelinePage> BrowsePageAsync(
        BrowseTimelineRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        using var read = this.readTelemetry.BeginRead(MailboxReadOperation.ListMailboxTimeline, cancellationToken);

        using var actingFor = this.egressGuard.ActingFor(this.scopeResolver.Owner);

        var sortedList = this.SortedList(request);
        var pageSize = MailboxQueryPageSize.FromRequested(request.PageSize);
        var pageDirection = DefinedPageDirection(request.PageDirection);
        var boundary = ContinuationBoundary(request.Cursor, sortedList, pageDirection);

        // Every value has been validated by this point, so a deployment serving this owner no account answers the same
        // refusals a deployment serving several does, and only then reports that it holds nothing to draw.
        if (sortedList.Selection.Scope.AccountIds.Count is 0)
        {
            read.Completed(0);

            return new BrowsedTimelinePage([], NextCursor: null, PreviousCursor: null, pageSize.Value);
        }

        // One row beyond the page is what establishes whether another page exists at the end being walked towards. The
        // other end is known without asking: a page reached from a cursor has whatever the cursor came from behind it.
        var walked = await this.timelineReader.ReadPageAsync(
            WalkedIn(sortedList, pageDirection),
            boundary,
            pageSize.Value + 1,
            cancellationToken);

        var page = SortedPage(walked, pageSize, pageDirection);
        var beyondThePage = walked.Count > pageSize.Value;

        var previews = await this.previewReader.ReadPreviewsAsync(
            [.. page.Select(static email => email.StoredEmailId)],
            cancellationToken);

        // Guarded after the boundaries are taken rather than before. A cursor names a received instant and a stored
        // identity, and redaction touches neither, so issuing one from the guarded page would be the same value
        // arrived at through more work.
        var rows = await this.GuardedAsync(page, previews, cancellationToken);

        read.Completed(rows.Count);

        return new BrowsedTimelinePage(
            rows,
            CursorAfterThePage(page, sortedList, pageDirection, beyondThePage),
            CursorBeforeThePage(page, sortedList, pageDirection, beyondThePage, boundary),
            pageSize.Value);
    }

    /// <summary>Names the walk that produces the page, which is the sorted list itself or that list read backwards.</summary>
    private static EmailTimelineFilter WalkedIn(EmailTimelineFilter sortedList, TimelinePageDirection pageDirection) =>
        pageDirection is TimelinePageDirection.Forward
            ? sortedList
            : EmailTimelineFilter.ReadIn(sortedList.Selection, Opposite(sortedList.Direction));

    /// <summary>Names the end of the timeline a backward walk reads towards.</summary>
    private static EmailTimelineDirection Opposite(EmailTimelineDirection direction) =>
        direction is EmailTimelineDirection.NewestFirst
            ? EmailTimelineDirection.OldestFirst
            : EmailTimelineDirection.NewestFirst;

    /// <summary>Cuts the walk to one page and puts it back into the order the list is sorted in.</summary>
    /// <remarks>
    /// The probe row is dropped first, so reversing a backward walk turns the page the caller keeps rather than a page
    /// with somebody else's boundary row still on the end of it.
    /// </remarks>
    private static IReadOnlyList<EmailSummary> SortedPage(
        IReadOnlyList<EmailSummary> walked,
        MailboxQueryPageSize pageSize,
        TimelinePageDirection pageDirection)
    {
        var withoutTheProbe = walked.Take(pageSize.Value);

        return pageDirection is TimelinePageDirection.Forward
            ? [.. withoutTheProbe]
            : [.. withoutTheProbe.Reverse()];
    }

    /// <summary>Issues the cursor that reads the page after this one, or nothing where the list ends here.</summary>
    /// <remarks>
    /// Walking forward, another page exists exactly when the probe row came back. Walking backward there is always
    /// something after the page, because the cursor the walk started from names a row that is still there.
    /// </remarks>
    private static string? CursorAfterThePage(
        IReadOnlyList<EmailSummary> page,
        EmailTimelineFilter sortedList,
        TimelinePageDirection pageDirection,
        bool beyondThePage)
    {
        if (page.Count is 0)
        {
            return null;
        }

        var more = pageDirection is not TimelinePageDirection.Forward || beyondThePage;

        return more ? EmailTimelineCursor.After(page[^1].Position, sortedList.Fingerprint).Encode() : null;
    }

    /// <summary>Issues the cursor that reads the page before this one, or nothing where the list begins here.</summary>
    /// <remarks>
    /// Walking backward, a further page exists exactly when the probe row came back. Walking forward, what lies before
    /// the page is whatever the presented cursor was taken from — so a page read from the leading end of the list has
    /// nothing before it and every other forward page has.
    /// </remarks>
    private static string? CursorBeforeThePage(
        IReadOnlyList<EmailSummary> page,
        EmailTimelineFilter sortedList,
        TimelinePageDirection pageDirection,
        bool beyondThePage,
        EmailTimelinePosition? boundary)
    {
        if (page.Count is 0)
        {
            return null;
        }

        var more = pageDirection is TimelinePageDirection.Forward
            ? boundary is not null
            : beyondThePage;

        return more ? EmailTimelineCursor.After(page[0].Position, sortedList.Fingerprint).Encode() : null;
    }

    /// <summary>Reads the position a cursor names, after establishing that the cursor belongs to this list.</summary>
    /// <remarks>
    /// A blank cursor is the leading end of the list rather than a malformed one, for the reason the tool listing reads
    /// it that way: a client carrying the field with nothing in it yet has asked for the beginning of the walk. That
    /// makes it no cursor at all, which is why a backward page is refused against it as well as against an absent one.
    /// </remarks>
    private static EmailTimelinePosition? ContinuationBoundary(
        string? cursor,
        EmailTimelineFilter sortedList,
        TimelinePageDirection pageDirection)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            if (pageDirection is TimelinePageDirection.Backward)
            {
                throw MailboxQueryFilterInvalidException.NeedsACursor(PageDirectionFilterName);
            }

            return null;
        }

        if (!EmailTimelineCursor.TryDecode(cursor, out var decodedCursor))
        {
            throw new MailboxQueryCursorMalformedException();
        }

        if (!string.Equals(decodedCursor.FilterFingerprint, sortedList.Fingerprint, StringComparison.Ordinal))
        {
            throw new MailboxQueryCursorFilterMismatchException();
        }

        return decodedCursor.Position;
    }

    /// <summary>Establishes that the page direction names one of the two answers rather than an unmapped value.</summary>
    private static TimelinePageDirection DefinedPageDirection(TimelinePageDirection pageDirection) =>
        Enum.IsDefined(pageDirection)
            ? pageDirection
            : throw new ArgumentOutOfRangeException(
                nameof(pageDirection),
                pageDirection,
                "A page continues from its cursor in one of two directions, and no other value names one.");

    /// <summary>Scans the three things a row carries that a message's author wrote.</summary>
    /// <remarks>
    /// The subject and the sender's display name are what a tool listing scans, and for the same reasons. The preview is
    /// the third because it is the message's own text, which is the one thing on this surface a tool listing never
    /// publishes — a row whose preview went out unscanned would be the leak the subject beside it was redacted to
    /// prevent. Everything else on a row is what a screen acts on: the identity a later request names, the folder alias,
    /// the addresses, the sizes and the flags.
    /// </remarks>
    private async Task<IReadOnlyList<BrowsedEmail>> GuardedAsync(
        IReadOnlyList<EmailSummary> page,
        IReadOnlyDictionary<StoredEmailId, string> previews,
        CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive)
        {
            return [.. page.Select(email => new BrowsedEmail(email, PreviewOf(email, previews)))];
        }

        // One report for the page rather than one per row, because the page is what a screen waits for.
        using var scan = this.egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.ClientMailListing,
            cancellationToken);

        var guarded = new List<BrowsedEmail>(page.Count);

        foreach (var email in page)
        {
            var guardedEmail = email with
            {
                Subject = await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.ClientMailListing,
                    email.Subject,
                    cancellationToken),
                SenderDisplayName = await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.ClientMailListing,
                    email.SenderDisplayName,
                    cancellationToken),
            };

            guarded.Add(new BrowsedEmail(
                guardedEmail,
                await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.ClientMailListing,
                    PreviewOf(email, previews),
                    cancellationToken)));
        }

        scan.Completed();

        return guarded;
    }

    /// <summary>Reads the preview of one row, which is absent for a message nothing has extracted yet.</summary>
    private static string? PreviewOf(EmailSummary email, IReadOnlyDictionary<StoredEmailId, string> previews) =>
        previews.TryGetValue(email.StoredEmailId, out var preview) ? EmailPreview.Bounded(preview) : null;

    /// <summary>Validates what the request asked for and restricts the list to the accounts its owner owns.</summary>
    private EmailTimelineFilter SortedList(BrowseTimelineRequest request) => EmailTimelineFilter.Create(
        this.scopeResolver.ReadableScope(
            request.Accounts,
            request.Folders,
            request.IncludeJunkMail ? JunkMailInclusion.Included : JunkMailInclusion.Excluded),
        senderAddress: null,
        recipientAddress: null,
        subjectFragment: null,
        request.ReceivedOnOrAfter,
        request.ReceivedBefore,
        request.IsRemotelySeen,
        request.IsRemotelyFlagged,
        keyword: null,
        request.HasAttachments,
        request.Order);
}
