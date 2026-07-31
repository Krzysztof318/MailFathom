// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Accounts;
using MailFathom.Application.Synchronization;

namespace MailFathom.Application.Emails.SearchEmails;

/// <summary>Searches the local mailbox copy for text and returns one bounded, ranked window of results.</summary>
/// <remarks>
/// <para>
/// The use case owns everything between an unvalidated request and a window: it normalizes and bounds the structured
/// filters, validates the free text, refuses an account this deployment does not serve, and decides the effective
/// result count and the snippet bounds. Storage does none of that, and no protocol adapter repeats it.
/// </para>
/// <para>
/// It reaches no mail server. A search answers from what synchronization has already stored and what extraction has
/// already indexed, which is what keeps an MCP read independent of IMAP availability, and it reports how current that
/// copy is instead of pretending it is live.
/// </para>
/// <para>
/// Because the index covers body text only, a word that appears solely inside an attachment payload matches nothing
/// here. That is the deliberate limit the extraction specification records rather than something this use case works
/// around, and the feature documentation states it so the behavior is not surprising.
/// </para>
/// </remarks>
public sealed class MailboxSearchReader
{
    private readonly IEmailSearchIndexReader searchIndexReader;
    private readonly ISynchronizationFreshnessReader freshnessReader;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly EmailSearchSnippetBounds snippetBounds;

    /// <summary>Initializes the use case.</summary>
    /// <param name="searchIndexReader">Reads bounded ranked windows out of the lexical index.</param>
    /// <param name="freshnessReader">Reads how current the local copy of each folder is.</param>
    /// <param name="scopeResolver">Decides which accounts and folders the search runs against.</param>
    /// <param name="snippetBounds">How much of a message's body one result may show.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailboxSearchReader(
        IEmailSearchIndexReader searchIndexReader,
        ISynchronizationFreshnessReader freshnessReader,
        MailboxScopeResolver scopeResolver,
        EmailSearchSnippetBounds snippetBounds)
    {
        ArgumentNullException.ThrowIfNull(searchIndexReader);
        ArgumentNullException.ThrowIfNull(freshnessReader);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(snippetBounds);

        this.searchIndexReader = searchIndexReader;
        this.freshnessReader = freshnessReader;
        this.scopeResolver = scopeResolver;
        this.snippetBounds = snippetBounds;
    }

    /// <summary>Searches for one window of ranked emails.</summary>
    /// <param name="request">What the caller asked for.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The ranked window and the scope's synchronization freshness.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when the query text is blank or unusable, or a structured filter carries a value, a count, or a length the query does not accept.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account this deployment does not serve.</exception>
    /// <exception cref="EmailSearchResultLimitOutOfRangeException">Thrown when the request names a result count outside the accepted range.</exception>
    /// <remarks>
    /// Nothing here writes, and the operation is therefore safe to repeat. It also never sets the remote <c>\Seen</c>
    /// flag or any other remote state, because it speaks to no mail server at all. A query that matches nothing returns
    /// an empty window rather than a failure, so a search cannot be used to establish that a folder or an account holds
    /// mail the caller was not already entitled to see.
    /// </remarks>
    public async Task<SearchEmailsResult> SearchEmailsAsync(
        SearchEmailsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var queryText = EmailSearchQueryText.Create(request.QueryText);
        var selection = this.ReadableSelection(request);
        var resultLimit = EmailSearchResultLimit.FromRequested(request.ResultLimit);

        // Every filter has been validated by this point, so a deployment that serves no account answers the same
        // refusals a deployment that serves several does, and only then reports that it holds nothing to search.
        if (selection.Scope.AccountIds.Count is 0)
        {
            return new SearchEmailsResult([], []);
        }

        var matches = await this.searchIndexReader.ReadRankedMatchesAsync(
            selection,
            queryText,
            this.snippetBounds,
            resultLimit.Value,
            cancellationToken);

        // Read after the window rather than beside it: both reads reach the same scoped EF Core context, which serves
        // one operation at a time, so starting them together would fault instead of overlapping.
        var folderFreshness = await this.freshnessReader.ReadAsync(selection.Scope, cancellationToken);

        return new SearchEmailsResult(matches, folderFreshness);
    }

    /// <summary>Validates the request's structured filters and restricts the search to the accounts this deployment serves.</summary>
    private MailboxEmailSelection ReadableSelection(SearchEmailsRequest request) => MailboxEmailSelection.Create(
        this.scopeResolver.ReadableScope(request.AccountIds, request.FolderAliases),
        request.SenderAddress,
        request.RecipientAddress,
        request.SubjectFragment,
        request.ReceivedOnOrAfter,
        request.ReceivedBefore,
        request.IsRemotelySeen,
        request.HasAttachments);
}
