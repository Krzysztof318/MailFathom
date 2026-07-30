// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Accounts;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;

namespace MailMcp.Application.Emails.ListEmails;

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
    private readonly IMailAccountCatalog accountCatalog;

    /// <summary>Initializes the use case.</summary>
    /// <param name="timelineReader">Reads bounded pages of stored email summaries.</param>
    /// <param name="freshnessReader">Reads how current the local copy of each folder is.</param>
    /// <param name="accountCatalog">Answers which accounts this deployment serves.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailboxTimelineReader(
        IStoredEmailTimelineReader timelineReader,
        ISynchronizationFreshnessReader freshnessReader,
        IMailAccountCatalog accountCatalog)
    {
        ArgumentNullException.ThrowIfNull(timelineReader);
        ArgumentNullException.ThrowIfNull(freshnessReader);
        ArgumentNullException.ThrowIfNull(accountCatalog);

        this.timelineReader = timelineReader;
        this.freshnessReader = freshnessReader;
        this.accountCatalog = accountCatalog;
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
        if (filter.Scope.AccountIds.Count is 0)
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
        var folderFreshness = await this.freshnessReader.ReadAsync(filter.Scope, cancellationToken);

        return new ListEmailsResult(page, nextCursor, folderFreshness);
    }

    /// <summary>Validates the request's filters and restricts the query to the accounts this deployment serves.</summary>
    /// <remarks>
    /// <para>
    /// An account the deployment does not serve is refused before anything is read, rather than narrowed away by a
    /// predicate: a narrowed predicate would answer with an empty page, and an empty page tells a caller that the
    /// identifier they named exists. The check runs against the normalized scope, so the same request cannot be written
    /// two ways to reach two answers.
    /// </para>
    /// <para>
    /// A request that names no account is restricted to the served accounts rather than left unrestricted. Removing an
    /// account from configuration leaves its stored rows in place, so an absent account predicate would keep publishing
    /// mail from an account this deployment no longer serves — which is also why the resolved accounts, not the
    /// requested ones, take part in the cursor's fingerprint.
    /// </para>
    /// </remarks>
    private EmailTimelineFilter ReadableFilter(ListEmailsRequest request)
    {
        var requestedScope = MailboxScope.Create(request.AccountIds, request.FolderAliases);
        var servedAccountIds = this.accountCatalog.ServedAccountIds;

        if (FirstAccountNotServed(requestedScope, servedAccountIds) is { } inaccessibleAccountId)
        {
            throw new MailAccountNotAccessibleException(inaccessibleAccountId);
        }

        var readableScope = requestedScope.AccountIds.Count is 0
            ? MailboxScope.RestrictedToServedAccounts(servedAccountIds, requestedScope.FolderAliases)
            : requestedScope;

        return EmailTimelineFilter.Create(
            readableScope,
            request.SenderAddress,
            request.RecipientAddress,
            request.SubjectFragment,
            request.ReceivedOnOrAfter,
            request.ReceivedBefore,
            request.IsRemotelySeen,
            request.HasAttachments,
            request.Direction);
    }

    private static MailAccountId? FirstAccountNotServed(
        MailboxScope requestedScope,
        IReadOnlyList<MailAccountId> servedAccountIds) => requestedScope.AccountIds
        .Select(static accountId => (MailAccountId?)accountId)
        .FirstOrDefault(accountId => !servedAccountIds.Contains(accountId!.Value));

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
