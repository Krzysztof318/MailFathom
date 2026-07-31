// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Accounts;
using MailFathom.Application.Synchronization;
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
/// </remarks>
public sealed class MailboxTimelineReader
{
    private readonly IStoredEmailTimelineReader timelineReader;
    private readonly ISynchronizationFreshnessReader freshnessReader;
    private readonly MailboxScopeResolver scopeResolver;

    /// <summary>Initializes the use case.</summary>
    /// <param name="timelineReader">Reads bounded pages of stored email summaries.</param>
    /// <param name="freshnessReader">Reads how current the local copy of each folder is.</param>
    /// <param name="scopeResolver">Decides which accounts and folders the listing runs against.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailboxTimelineReader(
        IStoredEmailTimelineReader timelineReader,
        ISynchronizationFreshnessReader freshnessReader,
        MailboxScopeResolver scopeResolver)
    {
        ArgumentNullException.ThrowIfNull(timelineReader);
        ArgumentNullException.ThrowIfNull(freshnessReader);
        ArgumentNullException.ThrowIfNull(scopeResolver);

        this.timelineReader = timelineReader;
        this.freshnessReader = freshnessReader;
        this.scopeResolver = scopeResolver;
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
    /// <remarks>
    /// Nothing here writes, and the operation is therefore safe to repeat. It also never sets the remote <c>\Seen</c>
    /// flag or any other remote state, because it speaks to no mail server at all.
    /// </remarks>
    public async Task<ListEmailsResult> ListEmailsAsync(ListEmailsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var filter = this.ReadableFilter(request);
        var pageSize = MailboxQueryPageSize.FromRequested(request.PageSize);
        var continueAfter = ContinuationPosition(request.Cursor, filter);

        // Every filter has been validated by this point, so a deployment that serves no account answers the same
        // refusals a deployment that serves several does, and only then reports that it holds nothing to read.
        if (filter.Selection.Scope.AccountIds.Count is 0)
        {
            return new ListEmailsResult([], NextCursor: null, []);
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

        return new ListEmailsResult(page, nextCursor, folderFreshness);
    }

    /// <summary>Validates the request's filters and restricts the query to the accounts this deployment serves.</summary>
    private EmailTimelineFilter ReadableFilter(ListEmailsRequest request) => EmailTimelineFilter.Create(
        this.scopeResolver.ReadableScope(request.AccountIds, request.FolderAliases),
        request.SenderAddress,
        request.RecipientAddress,
        request.SubjectFragment,
        request.ReceivedOnOrAfter,
        request.ReceivedBefore,
        request.IsRemotelySeen,
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
