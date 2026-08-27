// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Observability;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseSearch;

/// <summary>Searches the owner's mail by words and by meaning at once, and pages through what the ranking produced.</summary>
/// <remarks>
/// <para>
/// It is the search a screen is drawn from, beside <see cref="SearchEmails.MailboxSearchReader" />, which is the one a
/// tool calls. The two share the scope, the filters, both rankings, the fusion, and the extracts — a search and a search
/// are the same question over the same mail — and differ in the two things a screen needs and a tool does not: the
/// results continue past the first window, and each one says why it is in the list.
/// </para>
/// <para>
/// The filters constrain and never rank. <see cref="RankedSearchList" /> carries them into both rankings as the
/// predicate each one selects under, so a message outside the sender, the range, the folder, the flags, or the
/// attachment state a person asked for is absent from the ranked list rather than pushed down it — and because the
/// predicate is inside the bounded query rather than applied to what came back, a message the filters admit is never
/// lost to a limit spent on messages they exclude.
/// </para>
/// <para>
/// Paging is a keyset walk of a list of fixed depth. Every page ranks the same two hundred candidates and hands back the
/// results ordered after the cursor's place, so a page costs the same wherever in the list it is asked for, and the
/// sequence a client walks is one sequence rather than a series of differently-deep re-rankings.
/// <see cref="RankedSearchCursor" /> states what a ranked boundary can and cannot promise.
/// </para>
/// <para>
/// A query that matched nothing returns an empty page. Nothing here widens a search that found nothing, because the
/// nearest unmatched messages are exactly what somebody must not be handed while believing they matched.
/// </para>
/// <para>
/// It reaches no mail server. A page answers from what synchronization has already stored and what extraction has
/// already indexed, so no request from a browser can wait on IMAP and none can set the remote <c>\Seen</c> flag.
/// </para>
/// <para>
/// A page is one of the points mail content leaves this deployment, and it publishes more of a message than a tool
/// window does, so where a sensitive-content scanner is switched on the subject, the sender's display name, the preview
/// and every extract are scanned before the page is returned; a scanner that cannot answer refuses the page rather than
/// serving it unscanned.
/// </para>
/// </remarks>
public sealed class MailSearchBrowser
{
    private readonly IEmailSearchIndexReader searchIndexReader;
    private readonly SemanticEmailSearch semanticSearch;
    private readonly IStoredEmailPreviewReader previewReader;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly EmailSearchSnippetBounds snippetBounds;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IMailboxReadTelemetry readTelemetry;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the use case.</summary>
    /// <param name="searchIndexReader">Ranks mail against the query text and reads the window a ranking selected.</param>
    /// <param name="semanticSearch">Ranks mail by meaning, or reports that this instance cannot.</param>
    /// <param name="previewReader">Reads the bounded opening of the text of the emails a page returned.</param>
    /// <param name="scopeResolver">Decides which accounts and folders the search runs against.</param>
    /// <param name="snippetBounds">How much of a message's body one result may show.</param>
    /// <param name="egressGuard">Scans what the page is about to publish, where this deployment scans anything.</param>
    /// <param name="readTelemetry">Publishes the search as the operation it is, beside the call it happened inside.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailSearchBrowser(
        IEmailSearchIndexReader searchIndexReader,
        SemanticEmailSearch semanticSearch,
        IStoredEmailPreviewReader previewReader,
        MailboxScopeResolver scopeResolver,
        EmailSearchSnippetBounds snippetBounds,
        SensitiveContentEgressGuard egressGuard,
        IMailboxReadTelemetry readTelemetry,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(searchIndexReader);
        ArgumentNullException.ThrowIfNull(semanticSearch);
        ArgumentNullException.ThrowIfNull(previewReader);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(snippetBounds);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(readTelemetry);
        ArgumentNullException.ThrowIfNull(authorization);

        this.searchIndexReader = searchIndexReader;
        this.semanticSearch = semanticSearch;
        this.previewReader = previewReader;
        this.scopeResolver = scopeResolver;
        this.snippetBounds = snippetBounds;
        this.egressGuard = egressGuard;
        this.readTelemetry = readTelemetry;
        this.authorization = authorization;
    }

    /// <summary>Reads one page of the ranked results.</summary>
    /// <param name="request">What the screen asked for.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The page, how it was ranked, and the cursor that continues it where the list goes on.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when the query text is blank or unusable, or a structured filter carries a value or a length the query does not accept.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account its owner does not own.</exception>
    /// <exception cref="EmailSearchResultLimitOutOfRangeException">Thrown when the request names a page size outside the accepted range.</exception>
    /// <exception cref="MailboxQueryCursorMalformedException">Thrown when the request carries a cursor this system did not issue.</exception>
    /// <exception cref="MailboxQueryCursorFilterMismatchException">Thrown when the cursor was issued for a different search than the request describes.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the page carries, which refuses the page rather than serving it unscanned.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// Nothing here writes, and the operation is therefore safe to repeat. The grant is asked for before the request is
    /// validated, so a caller that may not read learns nothing about which filters this deployment accepts.
    /// </remarks>
    public async Task<BrowsedSearchPage> SearchPageAsync(
        BrowseSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        using var read = this.readTelemetry.BeginRead(MailboxReadOperation.SearchMailbox, cancellationToken);

        var rankedList = this.RankedList(request);
        var pageSize = EmailSearchResultLimit.FromRequested(request.PageSize);
        var boundary = ContinuationBoundary(request.Cursor, rankedList);

        // Every value has been validated by this point, so a deployment serving this owner no account answers the same
        // refusals a deployment serving several does, and only then reports that it holds nothing to search.
        if (rankedList.Selection.Scope.AccountIds.Count is 0)
        {
            read.Completed(0);

            return EmptyPage(
                pageSize,
                await this.semanticSearch.ReadCapabilityAsync(cancellationToken),
                rankedList.Selection.Scope.IncludesJunkMail);
        }

        var ranking = await this.RankAsync(rankedList, cancellationToken);

        // One candidate past the page is what says whether the list goes on, and it is taken from the ranking rather
        // than from what the projection returned: a candidate the projection drops was still walked past, so a cursor
        // minted from a surviving row would send the next page back over the ground this one already covered.
        var walked = ranking.After(boundary, pageSize.Value + 1);
        var page = walked.Take(pageSize.Value).ToArray();

        var matches = await this.searchIndexReader.ReadMatchesAsync(
            rankedList.Selection,
            rankedList.QueryText,
            this.snippetBounds,
            page,
            cancellationToken);

        var previews = await this.previewReader.ReadPreviewsAsync(
            [.. matches.Select(static match => match.Summary.StoredEmailId)],
            cancellationToken);

        var results = await this.GuardedAsync(matches, previews, ranking, cancellationToken);

        read.Completed(results.Count);

        return new BrowsedSearchPage(
            results,
            CursorAfterThePage(page, rankedList, beyondThePage: walked.Count > pageSize.Value),
            pageSize.Value,
            ranking.RetrievalMode,
            ranking.SemanticSearch,
            rankedList.Selection.Scope.IncludesJunkMail);
    }

    /// <summary>Answers a search whose owner owns no account this deployment serves.</summary>
    /// <remarks>
    /// The capability is still read and still reported. It describes the instance rather than the page, so an empty
    /// answer that claimed semantic retrieval was inactive would be wrong about a hybrid deployment for the one request
    /// least able to tell.
    /// </remarks>
    private static BrowsedSearchPage EmptyPage(
        EmailSearchResultLimit pageSize,
        SemanticSearchCapability capability,
        bool includedJunkMail) => new(
        [],
        NextCursor: null,
        pageSize.Value,
        EmailSearchRetrievalMode.Lexical,
        capability,
        includedJunkMail);

    /// <summary>Issues the cursor that reads the page after this one, or nothing where the ranked list ends here.</summary>
    /// <remarks>
    /// Two things end a walk and both end it the same way: the ranking ran out of candidates, and the walk reached
    /// <see cref="RankedSearchList.MaximumRankedDepth" />. Neither is reported as a state of its own, because what a
    /// client does about either is identical — narrow the filters or write a different query.
    /// </remarks>
    private static string? CursorAfterThePage(
        RankedEmailCandidate[] page,
        RankedSearchList rankedList,
        bool beyondThePage) => page.Length is not 0 && beyondThePage
        ? RankedSearchCursor.After(page[^1], rankedList.Fingerprint).Encode()
        : null;

    /// <summary>Reads the boundary a cursor names, after establishing that the cursor belongs to this list.</summary>
    /// <remarks>
    /// A blank cursor is the best-ranked end of the list rather than a malformed one, for the reason the timeline reads
    /// it that way: a client carrying the field with nothing in it yet has asked for the beginning of the walk.
    /// </remarks>
    private static RankedEmailCandidate? ContinuationBoundary(string? cursor, RankedSearchList rankedList)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        if (!RankedSearchCursor.TryDecode(cursor, out var decodedCursor))
        {
            throw new MailboxQueryCursorMalformedException();
        }

        if (!string.Equals(decodedCursor.FilterFingerprint, rankedList.Fingerprint, StringComparison.Ordinal))
        {
            throw new MailboxQueryCursorFilterMismatchException();
        }

        return decodedCursor.Boundary;
    }

    /// <summary>Ranks the eligible mail to the list's whole depth, by whichever method this instance can apply to this query.</summary>
    /// <remarks>
    /// The semantic ranking is asked for first, because its answer decides whether a lexical ranking is the list itself
    /// or one half of a fusion. Both sides reach the same depth, which is what keeps agreement between them observable
    /// as far down the list as paging can go.
    /// </remarks>
    private async Task<RankedSearchRanking> RankAsync(
        RankedSearchList rankedList,
        CancellationToken cancellationToken)
    {
        using var ranking = this.readTelemetry.BeginSearchRanking(cancellationToken);

        var depth = RankedSearchList.MaximumRankedDepth;

        var semantic = await this.semanticSearch.FindNearestCandidatesAsync(
            rankedList.Selection,
            rankedList.QueryText,
            depth,
            cancellationToken);

        var lexicalCandidates = await this.searchIndexReader.ReadRankedCandidatesAsync(
            rankedList.Selection,
            rankedList.QueryText,
            depth,
            cancellationToken);

        if (semantic.Candidates is not { } semanticCandidates)
        {
            ranking.Completed(lexicalCandidates.Count);

            return new RankedSearchRanking(
                lexicalCandidates,
                lexicalCandidates,
                [],
                EmailSearchRetrievalMode.Lexical,
                semantic.Capability);
        }

        ranking.Completed(lexicalCandidates.Count + semanticCandidates.Count);

        return new RankedSearchRanking(
            ReciprocalRankFusion.Fuse(lexicalCandidates, semanticCandidates, depth),
            lexicalCandidates,
            semanticCandidates,
            EmailSearchRetrievalMode.Hybrid,
            semantic.Capability);
    }

    /// <summary>Validates what the request asked for and restricts the search to the accounts its owner owns.</summary>
    private RankedSearchList RankedList(BrowseSearchRequest request) => RankedSearchList.Create(
        this.scopeResolver.ReadableScope(
            request.Accounts,
            request.Folders,
            request.IncludeJunkMail ? JunkMailInclusion.Included : JunkMailInclusion.Excluded),
        request.QueryText,
        request.SenderAddress,
        request.RecipientAddress,
        request.ReceivedOnOrAfter,
        request.ReceivedBefore,
        request.IsRemotelySeen,
        request.IsRemotelyFlagged,
        request.HasAttachments);

    /// <summary>Scans the four things a result carries that a message's author wrote.</summary>
    /// <remarks>
    /// The subject and the sender's display name are what every listing scans, and for the same reasons. The extracts
    /// and the preview are the message's own text, and a result carrying either unscanned would be the leak the subject
    /// beside it was redacted to prevent. Everything else is what a screen acts on: the identity a later request names,
    /// the folder alias, the addresses, the sizes, the flags, and which ranking found the result.
    /// </remarks>
    private async Task<IReadOnlyList<BrowsedSearchResult>> GuardedAsync(
        IReadOnlyList<EmailSearchMatch> matches,
        IReadOnlyDictionary<StoredEmailId, string> previews,
        RankedSearchRanking ranking,
        CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive)
        {
            return
            [
                .. matches.Select(match => new BrowsedSearchResult(
                    match.Summary,
                    PreviewOf(match.Summary, previews),
                    match.Snippets,
                    ranking.OriginOf(match.Summary.StoredEmailId))),
            ];
        }

        // One report for the page rather than one per result, because the page is what a screen waits for.
        using var scan = this.egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.ClientMailSearch,
            cancellationToken);

        var guarded = new List<BrowsedSearchResult>(matches.Count);

        foreach (var match in matches)
        {
            var summary = match.Summary with
            {
                Subject = await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.ClientMailSearch,
                    match.Summary.Subject,
                    cancellationToken),
                SenderDisplayName = await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.ClientMailSearch,
                    match.Summary.SenderDisplayName,
                    cancellationToken),
            };

            guarded.Add(new BrowsedSearchResult(
                summary,
                await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.ClientMailSearch,
                    PreviewOf(match.Summary, previews),
                    cancellationToken),
                await this.egressGuard.GuardAllAsync(
                    SensitiveContentEgressPoint.ClientMailSearch,
                    match.Snippets,
                    cancellationToken),
                ranking.OriginOf(match.Summary.StoredEmailId)));
        }

        scan.Completed();

        return guarded;
    }

    /// <summary>Reads the preview of one result, which is absent for a message nothing has extracted yet.</summary>
    private static string? PreviewOf(EmailSummary email, IReadOnlyDictionary<StoredEmailId, string> previews) =>
        previews.TryGetValue(email.StoredEmailId, out var preview) ? EmailPreview.Bounded(preview) : null;

    /// <summary>One query's ranked list, together with which ranking placed each message in it.</summary>
    /// <param name="Ordered">The list a page is cut from, best first.</param>
    /// <param name="LexicalCandidates">What the full-text ranking returned, which is the list itself on a lexical page.</param>
    /// <param name="SemanticCandidates">What the semantic ranking returned, empty where this query was not ranked that way.</param>
    /// <param name="RetrievalMode">How the list was produced.</param>
    /// <param name="SemanticSearch">What semantic retrieval can do on this instance, read after the query rather than before it.</param>
    /// <remarks>
    /// The two rankings are kept beside the fused list rather than discarded into it, because which of them found a
    /// message is what a result publishes as why it matched — and the fusion deliberately forgets it, scoring by place
    /// alone so that no ranking's units can influence the other's.
    /// </remarks>
    private sealed record RankedSearchRanking(
        IReadOnlyList<RankedEmailCandidate> Ordered,
        IReadOnlyList<RankedEmailCandidate> LexicalCandidates,
        IReadOnlyList<RankedEmailCandidate> SemanticCandidates,
        EmailSearchRetrievalMode RetrievalMode,
        SemanticSearchCapability SemanticSearch)
    {
        private readonly HashSet<StoredEmailId> lexicalIds =
            [.. LexicalCandidates.Select(static candidate => candidate.StoredEmailId)];

        private readonly HashSet<StoredEmailId> semanticIds =
            [.. SemanticCandidates.Select(static candidate => candidate.StoredEmailId)];

        /// <summary>Names which ranking found one message.</summary>
        /// <param name="storedEmailId">The message's stable local identity.</param>
        /// <returns>The origin a result publishes.</returns>
        public SearchMatchOrigin OriginOf(StoredEmailId storedEmailId) =>
            (this.lexicalIds.Contains(storedEmailId), this.semanticIds.Contains(storedEmailId)) switch
            {
                (true, true) => SearchMatchOrigin.BothRankings,
                (false, true) => SearchMatchOrigin.SemanticRanking,
                _ => SearchMatchOrigin.LexicalRanking,
            };

        /// <summary>Cuts the candidates ordered strictly after a boundary, at most as many as asked for.</summary>
        /// <param name="boundary">The place the previous page ended on, or <see langword="null" /> for the best-ranked end of the list.</param>
        /// <param name="count">The greatest number of candidates to return.</param>
        /// <returns>The candidates, in the order the list publishes them.</returns>
        /// <remarks>
        /// The list is already in that order, so the boundary is skipped past rather than searched for. A boundary whose
        /// own message has left the list still names a place between two that remain, which is what keeps a page
        /// continuable after mail was expunged.
        /// </remarks>
        public IReadOnlyList<RankedEmailCandidate> After(RankedEmailCandidate? boundary, int count) => boundary is null
            ? [.. this.Ordered.Take(count)]
            :
            [
                .. this.Ordered
                    .SkipWhile(candidate => RankedEmailCandidate.BestFirst.Compare(candidate, boundary) <= 0)
                    .Take(count),
            ];
    }
}
