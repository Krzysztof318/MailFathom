// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Observability;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ListEmails;

/// <summary>Lists emails from the local mailbox copy as one bounded page of a keyset walk.</summary>
/// <remarks>
/// <para>
/// The use case owns everything between an unvalidated request and a page: it normalizes and bounds the filters, refuses
/// an account this deployment does not serve, decides the effective page size, decodes and authenticates the
/// continuation cursor against the filters it was issued for, and issues the cursor that continues the walk. Storage
/// does none of that, and no protocol adapter repeats it.
/// </para>
/// <para>
/// It reaches no mail server. A listing answers from what synchronization has already stored, which is what keeps an
/// MCP read independent of IMAP availability, and it reports how current that copy is instead of pretending it is live.
/// </para>
/// <para>
/// A page is one of the points mail content leaves this deployment, so where a sensitive-content scanner is switched on
/// the subject of every summary is scanned before the page is returned, and a scanner that cannot answer refuses the
/// listing rather than serving it unscanned.
/// </para>
/// </remarks>
public sealed class MailboxTimelineReader
{
    private readonly IStoredEmailTimelineReader timelineReader;
    private readonly ISynchronizationFreshnessReader freshnessReader;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IMailboxReadTelemetry readTelemetry;

    /// <summary>Initializes the use case.</summary>
    /// <param name="timelineReader">Reads bounded pages of stored email summaries.</param>
    /// <param name="freshnessReader">Reads how current the local copy of each folder is.</param>
    /// <param name="scopeResolver">Decides which accounts and folders the listing runs against.</param>
    /// <param name="egressGuard">Scans what the page is about to publish, where this deployment scans anything.</param>
    /// <param name="readTelemetry">Publishes the read as the operation it is, beside the call it happened inside.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailboxTimelineReader(
        IStoredEmailTimelineReader timelineReader,
        ISynchronizationFreshnessReader freshnessReader,
        MailboxScopeResolver scopeResolver,
        SensitiveContentEgressGuard egressGuard,
        IMailboxReadTelemetry readTelemetry)
    {
        ArgumentNullException.ThrowIfNull(timelineReader);
        ArgumentNullException.ThrowIfNull(freshnessReader);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(readTelemetry);

        this.timelineReader = timelineReader;
        this.freshnessReader = freshnessReader;
        this.scopeResolver = scopeResolver;
        this.egressGuard = egressGuard;
        this.readTelemetry = readTelemetry;
    }

    /// <summary>Lists one page of emails.</summary>
    /// <param name="request">What the caller asked for.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The page, the cursor that continues the walk when one exists, and the scope's synchronization freshness.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when a filter carries a value, a count, or a length the query does not accept.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account this deployment does not serve.</exception>
    /// <exception cref="MailboxQueryPageSizeOutOfRangeException">Thrown when the request names a page size outside the accepted range.</exception>
    /// <exception cref="MailboxQueryCursorMalformedException">Thrown when the request carries a cursor this system did not issue.</exception>
    /// <exception cref="MailboxQueryCursorFilterMismatchException">Thrown when the cursor was issued for different filters than the request carries.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the page carries, which refuses the listing rather than serving it unscanned.</exception>
    /// <remarks>
    /// Nothing here writes, and the operation is therefore safe to repeat. It also never sets the remote <c>\Seen</c>
    /// flag or any other remote state, because it speaks to no mail server at all.
    /// </remarks>
    public async Task<ListEmailsResult> ListEmailsAsync(ListEmailsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var read = this.readTelemetry.BeginRead(MailboxReadOperation.ListMailboxTimeline, cancellationToken);

        var filter = this.ReadableFilter(request);
        var pageSize = MailboxQueryPageSize.FromRequested(request.PageSize);
        var continueAfter = ContinuationPosition(request.Cursor, filter);

        // Every filter has been validated by this point, so a deployment that serves no account answers the same
        // refusals a deployment that serves several does, and only then reports that it holds nothing to read.
        if (filter.Selection.Scope.AccountIds.Count is 0)
        {
            read.Completed(0);

            return new ListEmailsResult([], NextCursor: null, [], filter.Selection.Scope.IncludesJunkMail);
        }

        // One row beyond the page is what establishes whether another page exists. A count over the same filter would
        // cost a second scan and could still disagree with the page it describes, because mail arrives between queries.
        var rows = await this.timelineReader.ReadPageAsync(
            filter,
            continueAfter,
            pageSize.Value + 1,
            cancellationToken);

        IReadOnlyList<EmailSummary> page = [.. rows.Take(pageSize.Value)];
        var nextCursor = rows.Count > pageSize.Value
            ? EmailTimelineCursor.After(page[^1].Position, filter.Fingerprint).Encode()
            : null;

        // Read after the page rather than beside it: both reads reach the same scoped EF Core context, which serves one
        // operation at a time, so starting them together would fault instead of overlapping.
        var folderFreshness = await this.freshnessReader.ReadAsync(filter.Selection.Scope, cancellationToken);

        // Guarded after the cursor is issued rather than before it. The cursor names a position in the timeline, which
        // is the received instant and the stored identity, and redaction touches neither — issuing it from the guarded
        // page would be the same value arrived at through more work.
        var guardedPage = await this.GuardedAsync(page, cancellationToken);

        read.Completed(guardedPage.Count);

        return new ListEmailsResult(
            guardedPage,
            nextCursor,
            folderFreshness,
            filter.Selection.Scope.IncludesJunkMail);
    }

    /// <summary>Scans the two things a summary carries that a message's author wrote.</summary>
    /// <remarks>
    /// <para>
    /// A listing publishes no body, so the subject and the sender's display name are the whole of its mail content and
    /// the whole of what is scanned. The display name is beside the subject rather than beside the address it
    /// accompanies: an address is a routing identity a server issued, while the name in front of it is free text the
    /// sending side wrote, and a header reading <c>"&lt;a credential&gt; &lt;someone@example.test&gt;"</c> would
    /// otherwise be served whole while the subject beside it was redacted.
    /// </para>
    /// <para>
    /// Everything else on a summary is what a caller acts on — the identity a later request names, the folder alias,
    /// the addresses a reply goes to, the sizes and the flags — and those are protected by who may reach this
    /// deployment rather than by redaction, which would leave a listing nobody could act on.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<EmailSummary>> GuardedAsync(
        IReadOnlyList<EmailSummary> page,
        CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive)
        {
            return page;
        }

        var guarded = new List<EmailSummary>(page.Count);

        foreach (var summary in page)
        {
            guarded.Add(summary with
            {
                Subject = await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.McpSnippet,
                    summary.Subject,
                    cancellationToken),
                SenderDisplayName = await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.McpSnippet,
                    summary.SenderDisplayName,
                    cancellationToken),
            });
        }

        return guarded;
    }

    /// <summary>Validates the request's filters and restricts the query to the accounts this deployment serves.</summary>
    private EmailTimelineFilter ReadableFilter(ListEmailsRequest request) => EmailTimelineFilter.Create(
        this.scopeResolver.ReadableScope(
            request.Accounts,
            request.Folders,
            request.IncludeJunkMail ? JunkMailInclusion.Included : JunkMailInclusion.Excluded),
        request.SenderAddress,
        request.RecipientAddress,
        request.SubjectFragment,
        request.ReceivedOnOrAfter,
        request.ReceivedBefore,
        request.IsRemotelySeen,
        request.IsRemotelyFlagged,
        request.Keyword,
        request.HasAttachments,
        request.Direction);

    /// <summary>Reads the position a cursor names, after establishing that the cursor belongs to this request.</summary>
    /// <remarks>
    /// A blank cursor is the first page rather than a malformed one: a client that carries the field but has nothing to
    /// put in it yet has asked for the beginning of the walk, and refusing that would make the absent and the empty
    /// value mean different things for no reason a caller could act on.
    /// </remarks>
    private static EmailTimelinePosition? ContinuationPosition(string? cursor, EmailTimelineFilter filter)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        if (!EmailTimelineCursor.TryDecode(cursor, out var decodedCursor))
        {
            throw new MailboxQueryCursorMalformedException();
        }

        if (!string.Equals(decodedCursor.FilterFingerprint, filter.Fingerprint, StringComparison.Ordinal))
        {
            throw new MailboxQueryCursorFilterMismatchException();
        }

        return decodedCursor.Position;
    }
}
