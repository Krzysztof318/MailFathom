// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;

namespace MailFathom.Application.Emails.BrowseSearch;

/// <summary>One page of a ranked search, and what a caller needs to read it correctly.</summary>
/// <param name="Results">The results, best first, holding no more than the effective page size.</param>
/// <param name="NextCursor">The cursor that reads the page after this one, or <see langword="null" /> when the ranked list ends here.</param>
/// <param name="PageSize">The page size the read actually ran under, which is the request's own or the default it took.</param>
/// <param name="RetrievalMode">How this page was ranked, which can differ between two searches of one instance.</param>
/// <param name="SemanticSearch">What semantic retrieval can do on this instance, which is what says why a lexical answer was lexical.</param>
/// <param name="IncludedJunkMail">Whether the account's junk folder took part in the search.</param>
/// <remarks>
/// <para>
/// An empty page is an answer rather than a shortfall. A query nothing matched returns no results and no cursor, which
/// is the difference between this and a search that fills a page with whatever ranked nearest: a person told a search
/// found nothing knows to write a different one, and a person handed three loosely related messages does not.
/// </para>
/// <para>
/// The page walks forward and only forward. A relevance order is recomputed per query, so the page somebody already
/// read is the page they are still holding on screen rather than something to fetch again — and a backward cursor would
/// promise a re-read of a list that no longer exists in the form it was read in. What a client keeps is the pages it has
/// drawn.
/// </para>
/// <para>
/// The mode and the capability travel with the page for the reason they travel with an MCP window, and they are how
/// this surface refuses to degrade quietly: an instance that has activated no embedding profile serves a lexical page
/// and says so, and one whose provider is refusing serves the same page and says something an operator can act on. A
/// screen that wants to tell a person their search was words-only reads them rather than guessing from the deployment.
/// </para>
/// <para>
/// Whether junk took part is reported whichever answer it is, for the reason a listing reports it: a page that left a
/// whole folder out looks exactly like one whose query matched nothing in it.
/// </para>
/// </remarks>
public sealed record BrowsedSearchPage(
    IReadOnlyList<BrowsedSearchResult> Results,
    string? NextCursor,
    int PageSize,
    EmailSearchRetrievalMode RetrievalMode,
    SemanticSearchCapability SemanticSearch,
    bool IncludedJunkMail);
