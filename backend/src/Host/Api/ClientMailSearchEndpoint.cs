// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.BrowseSearch;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves one page of the owner's mail ranked against what they are looking for, by words and by meaning at once.</summary>
/// <remarks>
/// <para>
/// It is one route rather than two because finding a message is one question. A person looking for mail does not know
/// whether the words they remember are the words the message used, so asking them to choose between a word search and a
/// meaning search is asking them to guess which one would have worked. This route ranks both ways wherever the
/// deployment can, and says in the answer which of them happened.
/// </para>
/// <para>
/// The filters beside the query are constraints. A person who narrowed to one sender, one folder, or last year has said
/// which mail may come back, so those values decide what is eligible before anything is ranked and the query decides
/// only the order of what remains — a search cannot return mail the filters excluded however well it matches.
/// </para>
/// <para>
/// A result carries what a list row draws and, beside it, why it is in the list: the highlighted extracts around what
/// matched, and which ranking found it. A message ranked by meaning carries no extract, because there is no part of it
/// that shows the query's words, and saying so is the honest answer where inventing one would not be.
/// </para>
/// <para>
/// A query that matched nothing is answered with nothing. The page comes back empty rather than filled with the nearest
/// mail, which is the difference between a person knowing to search again and a person acting on a message that never
/// matched.
/// </para>
/// <para>
/// It speaks to no mail server, so a request from a browser cannot wait on IMAP and cannot set the remote <c>\Seen</c>
/// flag. What it searches is the local copy, whose currency the folders route is where a screen reads.
/// </para>
/// </remarks>
internal static class ClientMailSearchEndpoint
{
    /// <summary>The route serving one page of ranked search results, relative to the client prefix.</summary>
    internal const string MailSearchRoute = "/emails/search";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailSearch(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailSearchRoute, SearchAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Searches the acting owner's mail, or reports what was wrong with the request.</summary>
    /// <param name="query">The text to search for, which every search carries.</param>
    /// <param name="account">The account to search, by its identifier or its display name, or <see langword="null" /> for every account the owner owns.</param>
    /// <param name="folder">The folder to search, by its alias or as <c>role:Inbox</c>, or <see langword="null" /> for every folder.</param>
    /// <param name="includeJunk">Whether the junk folder takes part, which it does not unless the request asks.</param>
    /// <param name="sender">The address the sender must carry, or <see langword="null" /> for any sender.</param>
    /// <param name="recipient">The address a <c>To</c> or <c>Cc</c> recipient must carry, or <see langword="null" /> for any recipient.</param>
    /// <param name="unread">Whether to keep only unread mail, only read mail, or <see langword="null" /> for both.</param>
    /// <param name="flagged">Whether to keep only flagged mail, only unflagged mail, or <see langword="null" /> for both.</param>
    /// <param name="hasAttachments">Whether to keep only mail with attachments, only mail without, or <see langword="null" /> for both.</param>
    /// <param name="receivedOnOrAfter">The inclusive start of the received range, or <see langword="null" /> for no start.</param>
    /// <param name="receivedBefore">The exclusive end of the received range, or <see langword="null" /> for no end.</param>
    /// <param name="pageSize">How many results the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor a previous page returned, or <see langword="null" /> for the best-ranked results.</param>
    /// <param name="search">Ranks and pages the results, for a caller the read's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, <c>400</c> naming what was wrong with the request, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>
    /// Every refusal is <c>400</c> and each one says which of them it is. A blank query, a page size out of range, a
    /// cursor this deployment never issued and a cursor issued for a different search are four different mistakes with
    /// four different repairs, and answering any of them with the best-ranked page would be a screen silently showing
    /// somebody the results of a search they did not ask for.
    /// </remarks>
    internal static async Task<Results<Ok<ClientMailSearchResponse>, ProblemHttpResult>> SearchAsync(
        [FromQuery] string? query,
        [FromQuery] string? account,
        [FromQuery] string? folder,
        [FromQuery] bool? includeJunk,
        [FromQuery] string? sender,
        [FromQuery] string? recipient,
        [FromQuery] bool? unread,
        [FromQuery] bool? flagged,
        [FromQuery] bool? hasAttachments,
        [FromQuery] DateTimeOffset? receivedOnOrAfter,
        [FromQuery] DateTimeOffset? receivedBefore,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] MailSearchBrowser search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        if (!TryReadScope(Named(account), Named(folder), out var accounts, out var folders))
        {
            return Refuse("The account or the folder names a value this deployment does not issue.");
        }

        var request = new BrowseSearchRequest
        {
            QueryText = query,
            Accounts = accounts,
            Folders = folders,
            IncludeJunkMail = includeJunk ?? false,
            SenderAddress = Named(sender),
            RecipientAddress = Named(recipient),
            ReceivedOnOrAfter = receivedOnOrAfter,
            ReceivedBefore = receivedBefore,
            IsRemotelySeen = unread is { } wanted ? !wanted : null,
            IsRemotelyFlagged = flagged,
            HasAttachments = hasAttachments,
            PageSize = pageSize,
            Cursor = cursor,
        };

        try
        {
            var page = await search.SearchPageAsync(request, cancellationToken);

            return TypedResults.Ok(ClientMailSearchResponse.For(page));
        }
        catch (MailboxQueryCursorMalformedException)
        {
            return Refuse("The cursor is not one this deployment issued.");
        }
        catch (MailboxQueryCursorFilterMismatchException)
        {
            return Refuse("The cursor was issued for a different search, so the query and the filters have to be the ones it was taken under.");
        }
        catch (EmailSearchResultLimitOutOfRangeException)
        {
            return Refuse($"A page holds between 1 and {EmailSearchResultLimit.MaximumValue} results.");
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
    /// <remarks>Without echoing it because a query is the most revealing value this surface carries, and a problem detail is the one part of a response that reaches a log by default.</remarks>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Reads a parameter a caller may have sent empty as the parameter they did not send.</summary>
    /// <remarks>
    /// A query string is composed by a page rather than typed, so a filter the screen has nothing to put in yet arrives
    /// as <c>?sender=</c> rather than absent. The search text is deliberately not read this way: a blank query is
    /// refused by the use case as the one filter that cannot be absent.
    /// </remarks>
    private static string? Named(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Reads the two names a request narrows the search with, refusing text no name of this system is spelled with.</summary>
    /// <remarks>
    /// One account and one folder rather than lists of them, because this route serves a screen searching where somebody
    /// is looking. A request that names neither searches every folder of every account the owner owns, which is what a
    /// search box with no scope chosen means.
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

/// <summary>One page of ranked search results, as the client endpoint serves it.</summary>
/// <param name="Results">The results, best first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> where the ranked list ends here.</param>
/// <param name="PageSize">How many results the read ran under, which is what the request asked for or the default it took.</param>
/// <param name="RetrievalMode">How this page was ranked: <c>Lexical</c> for words alone, <c>Hybrid</c> for words and meaning together.</param>
/// <param name="SemanticSearch">What semantic retrieval can do on this deployment: <c>Inactive</c>, <c>Available</c>, or <c>Degraded</c>.</param>
/// <param name="IncludedJunkMail">Whether the junk folder took part.</param>
/// <remarks>
/// <para>
/// The two fields describing retrieval are what keeps a narrower answer from being a quieter one. A lexical page on a
/// deployment that has activated no embedding profile and a lexical page on one whose provider is refusing look
/// identical from the results alone, and only the second is something to fix — so a screen showing "words only" reads
/// them rather than inferring it, and an operator reading <c>Degraded</c> has been told.
/// </para>
/// <para>
/// An absent cursor is the end of the ranked list rather than a hint to retry. What continues it is opaque and holdable:
/// it names a place in this search's own ranking, and nothing on the server remembers it.
/// </para>
/// </remarks>
internal sealed record ClientMailSearchResponse(
    IReadOnlyList<ClientMailSearchResultResponse> Results,
    string? NextCursor,
    int PageSize,
    string RetrievalMode,
    string SemanticSearch,
    bool IncludedJunkMail)
{
    /// <summary>Describes one page for the wire.</summary>
    /// <param name="page">The page the use case read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static ClientMailSearchResponse For(BrowsedSearchPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ClientMailSearchResponse(
            [.. page.Results.Select(ClientMailSearchResultResponse.For)],
            page.NextCursor,
            page.PageSize,
            page.RetrievalMode.ToString(),
            page.SemanticSearch.ToString(),
            page.IncludedJunkMail);
    }
}

/// <summary>One search result: the row a list draws, and what says why the row is there.</summary>
/// <param name="Id">The stable local identity of the email, which every later request names it by.</param>
/// <param name="Account">The account the email was read from, as the accounts route names it.</param>
/// <param name="Folder">The folder alias the email was read from, as the folders route names it.</param>
/// <param name="ThreadId">The conversation the email belongs to, or <see langword="null" /> where nothing has placed it in one.</param>
/// <param name="Subject">The subject, or <see langword="null" /> where the message carried none.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded the message, or <see langword="null" /> where no header carried a usable date.</param>
/// <param name="SentAt">When the message says it was sent, or <see langword="null" /> where no header carried a usable date.</param>
/// <param name="SenderAddress">The sender's address as the message wrote it, or <see langword="null" /> where no usable sender was found.</param>
/// <param name="SenderDisplayName">The display name the sender wrote, or <see langword="null" /> where the header carried none.</param>
/// <param name="ToAddresses">The comparison forms of the <c>To</c> addresses, in header order.</param>
/// <param name="Unread">Whether the mail server last reported the message without <c>\Seen</c>.</param>
/// <param name="Flagged">Whether the mail server last reported it with <c>\Flagged</c>.</param>
/// <param name="Answered">Whether the mail server last reported it with <c>\Answered</c>.</param>
/// <param name="HasAttachments">Whether the message carries anything besides its body and its inline resources.</param>
/// <param name="AttachmentCount">How many of those there are.</param>
/// <param name="SizeOctets">The size the mail server reported for the message.</param>
/// <param name="Preview">The opening of the message's own text, bounded, or <see langword="null" /> where nothing has extracted the message yet.</param>
/// <param name="Snippets">The extracts around what matched, each marking the matched words with <c>**</c>, and empty where nothing in the body matched.</param>
/// <param name="MatchedBy">Which ranking found this result: <c>LexicalRanking</c>, <c>SemanticRanking</c>, or <c>BothRankings</c>.</param>
/// <remarks>
/// <para>
/// The row is the message list's row, field for field, so one layout draws both and a search result can be opened,
/// filtered, and acted on without a second request.
/// </para>
/// <para>
/// The extracts are text cut from untrusted mail and are marked rather than marked up: the emphasis is <c>**</c> around
/// the matched words and nothing in them is markup a client should render as such. An empty list is a message that
/// matched on its headers or by meaning, which <c>matchedBy</c> separates.
/// </para>
/// <para>
/// No relevance score is published. A rank means something only inside the ordering that produced it, so a number here
/// would invite a comparison between two searches that no ranking supports; the order of the results is what the
/// ranking has to say.
/// </para>
/// </remarks>
internal sealed record ClientMailSearchResultResponse(
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
    string? Preview,
    IReadOnlyList<string> Snippets,
    string MatchedBy)
{
    /// <summary>Describes one result for the wire.</summary>
    /// <param name="result">The result the use case read.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailSearchResultResponse For(BrowsedSearchResult result) => new(
        result.Email.StoredEmailId.Value,
        result.Email.AccountId.Value,
        result.Email.FolderAlias.Value,
        result.Email.ThreadId?.Value,
        result.Email.Subject,
        result.Email.ReceivedAt,
        result.Email.SentAt,
        result.Email.SenderAddress,
        result.Email.SenderDisplayName,
        result.Email.ToAddresses,
        !result.Email.RemoteFlags.IsSeen,
        result.Email.RemoteFlags.IsFlagged,
        result.Email.RemoteFlags.IsAnswered,
        result.Email.Attachments.HasAttachments,
        result.Email.Attachments.AttachmentCount,
        result.Email.SizeOctets,
        result.Preview,
        result.Snippets,
        result.MatchedBy.ToString());
}
