// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.BrowseTimeline;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves one page of the owner's message list, keyset-paged in both directions.</summary>
/// <remarks>
/// <para>
/// It is the route a mail screen spends its time in, and the one most easily made slow. A page is an indexed read of a
/// bounded window rather than an offset into a count, so message forty thousand of a folder costs what message one
/// costs, and the cursor that continues the walk is a value the client holds rather than a position this deployment
/// remembers — which is what makes leaving a screen and coming back a continuation instead of a fresh start.
/// </para>
/// <para>
/// A row carries what a list draws and stops there: who wrote it, what it is about, when it arrived, whether it has
/// been read, flagged or answered, whether anything is attached, and the opening of the message's own text. It carries
/// no body and no raw MIME, because a page of fifty rows carrying bodies is a megabyte to draw a list.
/// </para>
/// <para>
/// Both the order the list is sorted in and the direction a page continues in are stated by the request, and a value
/// neither of them accepts is refused rather than ignored: a screen that asked to be sorted by something this
/// deployment cannot sort by would otherwise be handed the default order and no way to tell.
/// </para>
/// <para>
/// It speaks to no mail server, so a request from a browser cannot wait on IMAP and cannot set the remote <c>\Seen</c>
/// flag. What it answers is the local copy, whose currency the folders route is where a screen reads.
/// </para>
/// </remarks>
internal static class ClientMailTimelineEndpoint
{
    /// <summary>The route reporting one page of the owner's mail, relative to the client prefix.</summary>
    internal const string MailTimelineRoute = "/emails";

    /// <summary>The one value the <c>sort</c> parameter accepts, which is the column the timeline indexes are ordered by.</summary>
    internal const string ReceivedAtSort = "receivedAt";

    /// <summary>The <c>order</c> value that reads the newest mail first, which is what a request naming none takes.</summary>
    internal const string NewestFirstOrder = "newestFirst";

    /// <summary>The <c>order</c> value that reads the oldest mail first.</summary>
    internal const string OldestFirstOrder = "oldestFirst";

    /// <summary>The <c>direction</c> value that reads the page after the cursor, which is what a request naming none takes.</summary>
    internal const string ForwardDirection = "forward";

    /// <summary>The <c>direction</c> value that reads the page before the cursor.</summary>
    internal const string BackwardDirection = "backward";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailTimeline(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailTimelineRoute, ReadTimelineAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Serves one page of the acting owner's mail, or reports what was wrong with the request.</summary>
    /// <param name="account">The account to draw from, by its identifier or its display name, or <see langword="null" /> for every account the owner owns.</param>
    /// <param name="folder">The folder to draw from, by its alias or as <c>role:Inbox</c>, or <see langword="null" /> for every folder.</param>
    /// <param name="includeJunk">Whether the junk folder takes part, which it does not unless the request asks.</param>
    /// <param name="unread">Whether to keep only unread mail, only read mail, or <see langword="null" /> for both.</param>
    /// <param name="flagged">Whether to keep only flagged mail, only unflagged mail, or <see langword="null" /> for both.</param>
    /// <param name="hasAttachments">Whether to keep only mail with attachments, only mail without, or <see langword="null" /> for both.</param>
    /// <param name="receivedOnOrAfter">The inclusive start of the received range, or <see langword="null" /> for no start.</param>
    /// <param name="receivedBefore">The exclusive end of the received range, or <see langword="null" /> for no end.</param>
    /// <param name="sort">What the list is ordered by, which is <c>receivedAt</c> or nothing else.</param>
    /// <param name="order">Which end of that order leads, <c>newestFirst</c> or <c>oldestFirst</c>.</param>
    /// <param name="direction">Whether the page lies <c>forward</c> of the cursor or <c>backward</c> of it.</param>
    /// <param name="pageSize">How many rows the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor a previous page returned, or <see langword="null" /> for the leading end of the list.</param>
    /// <param name="timeline">Reads the page, for a caller the read's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, <c>400</c> naming what was wrong with the request, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>
    /// Every refusal is <c>400</c> and each one says which of them it is, because a cursor a deployment never issued and
    /// a cursor issued for a different list are two different mistakes with two different repairs — and answering
    /// either with the leading page would be a screen silently jumping back to the top.
    /// </remarks>
    internal static async Task<Results<Ok<ClientMailTimelineResponse>, ProblemHttpResult>> ReadTimelineAsync(
        [FromQuery] string? account,
        [FromQuery] string? folder,
        [FromQuery] bool? includeJunk,
        [FromQuery] bool? unread,
        [FromQuery] bool? flagged,
        [FromQuery] bool? hasAttachments,
        [FromQuery] DateTimeOffset? receivedOnOrAfter,
        [FromQuery] DateTimeOffset? receivedBefore,
        [FromQuery] string? sort,
        [FromQuery] string? order,
        [FromQuery] string? direction,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] MailTimelineBrowser timeline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        if (Named(sort) is { } namedSort && !string.Equals(namedSort, ReceivedAtSort, StringComparison.OrdinalIgnoreCase))
        {
            return Refuse($"A mail list is ordered by '{ReceivedAtSort}', and by nothing else this deployment can index.");
        }

        if (SortedOrder(Named(order)) is not { } sortedOrder)
        {
            return Refuse($"The order names neither '{NewestFirstOrder}' nor '{OldestFirstOrder}'.");
        }

        if (PageDirection(Named(direction)) is not { } pageDirection)
        {
            return Refuse($"The direction names neither '{ForwardDirection}' nor '{BackwardDirection}'.");
        }

        if (!TryReadScope(Named(account), Named(folder), out var accounts, out var folders))
        {
            return Refuse("The account or the folder names a value this deployment does not issue.");
        }

        var request = new BrowseTimelineRequest
        {
            Accounts = accounts,
            Folders = folders,
            IncludeJunkMail = includeJunk ?? false,
            IsRemotelySeen = unread is { } wanted ? !wanted : null,
            IsRemotelyFlagged = flagged,
            HasAttachments = hasAttachments,
            ReceivedOnOrAfter = receivedOnOrAfter,
            ReceivedBefore = receivedBefore,
            Order = sortedOrder,
            PageDirection = pageDirection,
            PageSize = pageSize,
            Cursor = cursor,
        };

        try
        {
            var page = await timeline.BrowsePageAsync(request, cancellationToken);

            return TypedResults.Ok(ClientMailTimelineResponse.For(page));
        }
        catch (MailboxQueryCursorMalformedException)
        {
            return Refuse("The cursor is not one this deployment issued.");
        }
        catch (MailboxQueryCursorFilterMismatchException)
        {
            return Refuse("The cursor was issued for a different list, so the filters or the order have to be the ones it was taken under.");
        }
        catch (MailboxQueryPageSizeOutOfRangeException)
        {
            return Refuse($"A page holds between 1 and {MailboxQueryPageSize.MaximumValue} rows.");
        }
        catch (MailAccountNotAccessibleException)
        {
            return Refuse("The account is not one this owner owns.");
        }
        catch (MailboxQueryFilterInvalidException refusal)
        {
            return Refuse(refusal.Message);
        }
    }

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Reads a parameter a caller may have sent empty as the parameter they did not send.</summary>
    /// <remarks>
    /// A query string is composed by a page rather than typed, so a field the screen has nothing to put in yet arrives
    /// as <c>?folder=</c> rather than absent. Refusing that would make the two spellings of "no folder" mean different
    /// things for no reason a client could act on, which is how the cursor is already read.
    /// </remarks>
    private static string? Named(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Reads which end of the timeline leads, taking the newest first where the request named nothing.</summary>
    /// <remarks>
    /// A closed mapping rather than <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)" />, which would also
    /// accept a number and a comma-separated list — neither of which any client wrote and both of which name a member.
    /// Case is not part of the name, the way it is not part of a folder role.
    /// </remarks>
    private static EmailTimelineDirection? SortedOrder(string? order) => order switch
    {
        null => EmailTimelineDirection.NewestFirst,
        _ when NamesTheSame(order, NewestFirstOrder) => EmailTimelineDirection.NewestFirst,
        _ when NamesTheSame(order, OldestFirstOrder) => EmailTimelineDirection.OldestFirst,
        _ => null,
    };

    /// <summary>Reads which way the page continues from its cursor, continuing forward where the request named nothing.</summary>
    private static TimelinePageDirection? PageDirection(string? direction) => direction switch
    {
        null => TimelinePageDirection.Forward,
        _ when NamesTheSame(direction, ForwardDirection) => TimelinePageDirection.Forward,
        _ when NamesTheSame(direction, BackwardDirection) => TimelinePageDirection.Backward,
        _ => null,
    };

    /// <summary>Reports whether a caller wrote one of this surface's published names.</summary>
    private static bool NamesTheSame(string written, string published) =>
        string.Equals(written, published, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the two names a request narrows the list with, refusing text no name of this system is spelled with.</summary>
    /// <remarks>
    /// One account and one folder rather than lists of them, because this route draws a folder somebody is looking at.
    /// A request that names neither draws every folder of every account the owner owns, which is the unified view.
    /// </remarks>
    private static bool TryReadScope(
        string? account,
        string? folder,
        out IReadOnlyList<MailAccountSelector> accounts,
        out IReadOnlyList<MailFolderReference> folders)
    {
        accounts = [];
        folders = [];

        try
        {
            accounts = account is null ? [] : [MailAccountSelector.Create(account)];
            folders = folder is null ? [] : [MailFolderReference.Create(folder)];

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>One page of the owner's mail, as the client endpoint serves it.</summary>
/// <param name="Emails">The rows, in the order the request asked the list to be sorted in.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the list.</param>
/// <param name="PreviousCursor">The cursor the preceding page is asked with, or <see langword="null" /> at the beginning of the list.</param>
/// <param name="PageSize">How many rows the read ran under, which is what the request asked for or the default it took.</param>
/// <remarks>
/// The two cursors are what makes a list scrollable in both directions from one request. Each is opaque and holdable —
/// it names a row of this page together with the list it was read under, and nothing on the server remembers it — so a
/// client may keep one while the screen is closed and continue from it afterwards. An absent one is that end of the
/// list having been reached, never a hint to try again.
/// </remarks>
internal sealed record ClientMailTimelineResponse(
    IReadOnlyList<ClientMailTimelineEntryResponse> Emails,
    string? NextCursor,
    string? PreviousCursor,
    int PageSize)
{
    /// <summary>Describes one page for the wire.</summary>
    /// <param name="page">The page the use case read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static ClientMailTimelineResponse For(BrowsedTimelinePage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ClientMailTimelineResponse(
            [.. page.Emails.Select(ClientMailTimelineEntryResponse.For)],
            page.NextCursor,
            page.PreviousCursor,
            page.PageSize);
    }
}

/// <summary>One row of the list, carrying what a screen draws and nothing it does not.</summary>
/// <param name="Id">The stable local identity of the email, which every later request names it by.</param>
/// <param name="Account">The account the email was read from, as the accounts route names it.</param>
/// <param name="Folder">The folder alias the email was read from, as the folders route names it.</param>
/// <param name="ThreadId">The conversation the email belongs to, or <see langword="null" /> where nothing has placed it in one.</param>
/// <param name="Subject">The subject, or <see langword="null" /> where the message carried none.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded the message, which is what the list is ordered by, or <see langword="null" /> where no header carried a usable date.</param>
/// <param name="SentAt">When the message says it was sent, or <see langword="null" /> where no header carried a usable date.</param>
/// <param name="SenderAddress">The sender's address as the message wrote it, or <see langword="null" /> where no usable sender was found.</param>
/// <param name="SenderDisplayName">The display name the sender wrote, or <see langword="null" /> where the header carried none.</param>
/// <param name="ToAddresses">The comparison forms of the <c>To</c> addresses, in header order, which is what a sent-mail row draws instead of a sender.</param>
/// <param name="Unread">Whether the mail server last reported the message without <c>\Seen</c>.</param>
/// <param name="Flagged">Whether the mail server last reported it with <c>\Flagged</c>.</param>
/// <param name="Answered">Whether the mail server last reported it with <c>\Answered</c>.</param>
/// <param name="HasAttachments">Whether the message carries anything besides its body and its inline resources.</param>
/// <param name="AttachmentCount">How many of those there are.</param>
/// <param name="SizeOctets">The size the mail server reported for the message.</param>
/// <param name="Preview">The opening of the message's own text, bounded, or <see langword="null" /> where nothing has extracted the message yet.</param>
/// <remarks>
/// <para>
/// The three flags are published as the states a row draws rather than as the snapshot they came from, because a screen
/// draws an unread badge and not an observation instant. A message no run has read flags for reads as read, unflagged
/// and unanswered, which is what a folder still being backfilled shows and what the folders route's freshness is where
/// a screen tells the two apart.
/// </para>
/// <para>
/// <c>preview</c> is the message's own text and nothing else: no quoted history, no signature block, and never a body.
/// It is absent rather than empty for a message this deployment has stored but not yet extracted.
/// </para>
/// </remarks>
internal sealed record ClientMailTimelineEntryResponse(
    Guid Id,
    string Account,
    string Folder,
    Guid? ThreadId,
    string? Subject,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? SentAt,
    string? SenderAddress,
    string? SenderDisplayName,
    IReadOnlyList<string> ToAddresses,
    bool Unread,
    bool Flagged,
    bool Answered,
    bool HasAttachments,
    int AttachmentCount,
    long SizeOctets,
    string? Preview)
{
    /// <summary>Describes one row for the wire.</summary>
    /// <param name="row">The row the use case read.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailTimelineEntryResponse For(BrowsedEmail row) => For(row.Email, row.Preview);

    /// <summary>Describes one message for the wire, wherever on this surface a message is drawn.</summary>
    /// <param name="email">The message the use case read.</param>
    /// <param name="preview">The opening of the message's own text, or <see langword="null" /> where nothing has extracted it.</param>
    /// <returns>The response body.</returns>
    /// <remarks>
    /// The pair rather than a reading's own row type, because a message is one shape on this surface: a list row and a
    /// message inside a conversation are drawn from the same fields, and a second mapping is how the two would come to
    /// publish one message two ways.
    /// </remarks>
    internal static ClientMailTimelineEntryResponse For(EmailSummary email, string? preview) => new(
        email.StoredEmailId.Value,
        email.AccountId.Value,
        email.FolderAlias.Value,
        email.ThreadId?.Value,
        email.Subject,
        email.ReceivedAt,
        email.SentAt,
        email.SenderAddress,
        email.SenderDisplayName,
        email.ToAddresses,
        !email.RemoteFlags.IsSeen,
        email.RemoteFlags.IsFlagged,
        email.RemoteFlags.IsAnswered,
        email.Attachments.HasAttachments,
        email.Attachments.AttachmentCount,
        email.SizeOctets,
        preview);
}
