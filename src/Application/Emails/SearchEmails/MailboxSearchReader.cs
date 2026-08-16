// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Observability;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Synchronization.Checkpoints;

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
/// It answers lexically or hybridly according to what the instance can do at the moment of the call, and reports which
/// in the result. Nothing about the request selects between them: retrieval quality is a deployment's decision, and a
/// caller able to ask for the lexical ranking of a hybrid instance would be asking for worse results with no way to
/// know it.
/// </para>
/// <para>
/// Beside the mode it reports what semantic retrieval can do at all, so a lexical answer says whether this instance
/// never embeds or is currently unable to. A search never fails because an embedding provider did, and never returns an
/// empty window in place of one it could not rank semantically.
/// </para>
/// <para>
/// Because the index covers body text only, a word that appears solely inside an attachment payload matches nothing
/// here. That is a deliberate limit of text extraction rather than something this use case works
/// around, and the feature documentation states it so the behavior is not surprising.
/// </para>
/// <para>
/// A window is one of the points mail content leaves this deployment, so where a sensitive-content scanner is switched
/// on the content of a result is scanned before the result is returned, and a scanner that cannot answer refuses the
/// search rather than serving it unscanned. The guard is here rather than at the protocol boundary for the reason the
/// authorization above it is: a second entrypoint over this use case inherits it instead of repeating it.
/// </para>
/// </remarks>
public sealed class MailboxSearchReader
{
    /// <summary>How many candidates each ranking contributes to a fusion, as a multiple of the window being returned.</summary>
    /// <remarks>
    /// <para>
    /// Fusion has nothing to work with beyond what each ranking hands it, so asking both for exactly the window would
    /// make the whole method decorative: a message ranked first semantically and twenty-first lexically would arrive
    /// from one side only, scoring as though the other side had never seen it. Reaching past the window is what lets
    /// agreement between the two rankings be observed at all.
    /// </para>
    /// <para>
    /// Four is deep enough for that and shallow enough to stay a bounded cost: the deepest window a search serves is
    /// fifty, so no query ranks more than two hundred candidates per side, and neither ranking cuts a snippet — that
    /// happens once, for the fused window. A deployment setting would be a third number an operator has to reason
    /// about to predict what a search returns.
    /// </para>
    /// </remarks>
    private const int FusionCandidateDepthMultiplier = 4;

    private readonly IEmailSearchIndexReader searchIndexReader;
    private readonly SemanticEmailSearch semanticSearch;
    private readonly ISynchronizationFreshnessReader freshnessReader;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly EmailSearchSnippetBounds snippetBounds;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IMailboxReadTelemetry readTelemetry;

    /// <summary>Initializes the use case.</summary>
    /// <param name="searchIndexReader">Ranks mail against the query text and reads the window a ranking selected.</param>
    /// <param name="semanticSearch">Ranks mail by meaning, or reports that this instance cannot.</param>
    /// <param name="freshnessReader">Reads how current the local copy of each folder is.</param>
    /// <param name="scopeResolver">Decides which accounts and folders the search runs against.</param>
    /// <param name="snippetBounds">How much of a message's body one result may show.</param>
    /// <param name="egressGuard">Scans what the window is about to publish, where this deployment scans anything.</param>
    /// <param name="readTelemetry">Publishes the search as the operation it is, beside the call it happened inside.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailboxSearchReader(
        IEmailSearchIndexReader searchIndexReader,
        SemanticEmailSearch semanticSearch,
        ISynchronizationFreshnessReader freshnessReader,
        MailboxScopeResolver scopeResolver,
        EmailSearchSnippetBounds snippetBounds,
        SensitiveContentEgressGuard egressGuard,
        IMailboxReadTelemetry readTelemetry)
    {
        ArgumentNullException.ThrowIfNull(searchIndexReader);
        ArgumentNullException.ThrowIfNull(semanticSearch);
        ArgumentNullException.ThrowIfNull(freshnessReader);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(snippetBounds);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(readTelemetry);

        this.searchIndexReader = searchIndexReader;
        this.semanticSearch = semanticSearch;
        this.freshnessReader = freshnessReader;
        this.scopeResolver = scopeResolver;
        this.snippetBounds = snippetBounds;
        this.egressGuard = egressGuard;
        this.readTelemetry = readTelemetry;
    }

    /// <summary>Searches for one window of ranked emails and publishes it to a caller outside this process.</summary>
    /// <param name="request">What the caller asked for.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The ranked window, how it was ranked, and the scope's synchronization freshness.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when the query text is blank or unusable, or a structured filter carries a value, a count, or a length the query does not accept.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account this deployment does not serve.</exception>
    /// <exception cref="EmailSearchResultLimitOutOfRangeException">Thrown when the request names a result count outside the accepted range.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the window carries, which refuses the search rather than serving it unscanned.</exception>
    /// <remarks>
    /// <para>
    /// The guard belongs to publishing rather than to searching, which is why it is here and not in
    /// <see cref="SearchWindowAsync" />: this window becomes an MCP tool's answer, while the one that method returns is
    /// read inside the process by something that guards it at its own egress point.
    /// </para>
    /// <para>
    /// The span is reported around the same boundary and for the same reason. What it has to measure is what an MCP
    /// caller waited for, which includes the scan of everything the window publishes; a span around the ranking alone
    /// would report a fast search on a deployment whose reads are slow because they are being scanned. The retrieval an
    /// answering run makes is reported by that run's own span instead, so the two do not report the same work twice.
    /// </para>
    /// </remarks>
    public async Task<SearchEmailsResult> SearchEmailsAsync(
        SearchEmailsRequest request,
        CancellationToken cancellationToken)
    {
        using var read = this.readTelemetry.BeginRead(MailboxReadOperation.SearchMailbox, cancellationToken);

        var window = await this.SearchWindowAsync(request, cancellationToken);
        var matches = await this.GuardedAsync(window.Matches, cancellationToken);

        read.Completed(matches.Count);

        return window with { Matches = matches };
    }

    /// <summary>Searches for one window of ranked emails, for a reader inside this process.</summary>
    /// <param name="request">What the caller asked for.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The ranked window, how it was ranked, and the scope's synchronization freshness.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when the query text is blank or unusable, or a structured filter carries a value, a count, or a length the query does not accept.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account this deployment does not serve.</exception>
    /// <exception cref="EmailSearchResultLimitOutOfRangeException">Thrown when the request names a result count outside the accepted range.</exception>
    /// <remarks>
    /// <para>
    /// The window comes back as it was found, with no sensitive-content guard on it, because nothing has left this
    /// deployment yet. Its one caller is the retrieval an answering run makes, which sends the extracts to a model and
    /// guards them there under the egress point they actually cross. Guarding here as well would scan every extract
    /// twice — a remote round trip apiece under the personal-data scanner — and would count text against an MCP series
    /// no MCP caller ever sees.
    /// </para>
    /// <para>
    /// Nothing here writes, and the operation is therefore safe to repeat. It also never sets the remote <c>\Seen</c>
    /// flag or any other remote state, because it speaks to no mail server at all. A query that matches nothing returns
    /// an empty window rather than a failure, so a search cannot be used to establish that a folder or an account holds
    /// mail the caller was not already entitled to see.
    /// </para>
    /// </remarks>
    internal async Task<SearchEmailsResult> SearchWindowAsync(
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
            // The capability is still read and still reported. It describes the instance rather than the window, so an
            // empty answer that claimed semantic retrieval was inactive would be wrong about a hybrid deployment for
            // the one request least able to tell.
            var capability = await this.semanticSearch.ReadCapabilityAsync(cancellationToken);

            return new SearchEmailsResult(
                [],
                EmailSearchRetrievalMode.Lexical,
                capability,
                [],
                selection.Scope.IncludesJunkMail);
        }

        var (rankedCandidates, retrievalMode, semanticSearchCapability) =
            await this.RankAsync(selection, queryText, resultLimit.Value, cancellationToken);

        var matches = await this.searchIndexReader.ReadMatchesAsync(
            selection,
            queryText,
            this.snippetBounds,
            rankedCandidates,
            cancellationToken);

        // Read after the window rather than beside it: both reads reach the same scoped EF Core context, which serves
        // one operation at a time, so starting them together would fault instead of overlapping.
        var folderFreshness = await this.freshnessReader.ReadAsync(selection.Scope, cancellationToken);

        return new SearchEmailsResult(
            matches,
            retrievalMode,
            semanticSearchCapability,
            folderFreshness,
            selection.Scope.IncludesJunkMail);
    }

    /// <summary>Scans the mail content of a window before the window becomes somebody else's.</summary>
    /// <remarks>
    /// <para>
    /// A snippet, a subject, and the sender's display name are what a result carries that a message's author wrote, so
    /// they are what is scanned. The display name is scanned rather than treated as part of the address it accompanies:
    /// an address is a routing identity a server issued, while the name in front of it is free text the sending side
    /// wrote. The identifiers beside them — the account, the folder alias, the addresses, the stored identity — are
    /// what a caller acts on rather than text to read, and redacting the address a reply has to go to would remove the
    /// result's whole use while protecting nothing the message body did not already carry.
    /// </para>
    /// <para>
    /// Each value is scanned on its own and the window is composed afterwards. Scanning the composed result instead
    /// would let one detection cover the end of a snippet and the beginning of the next field, and replacing that
    /// region would take the boundary between them with it.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<EmailSearchMatch>> GuardedAsync(
        IReadOnlyList<EmailSearchMatch> matches,
        CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive)
        {
            return matches;
        }

        var guarded = new List<EmailSearchMatch>(matches.Count);

        foreach (var match in matches)
        {
            var subject = await this.egressGuard.GuardOptionalAsync(
                SensitiveContentEgressPoint.McpSnippet,
                match.Summary.Subject,
                cancellationToken);
            var senderDisplayName = await this.egressGuard.GuardOptionalAsync(
                SensitiveContentEgressPoint.McpSnippet,
                match.Summary.SenderDisplayName,
                cancellationToken);
            var snippets = await this.egressGuard.GuardAllAsync(
                SensitiveContentEgressPoint.McpSnippet,
                match.Snippets,
                cancellationToken);

            guarded.Add(match with
            {
                Summary = match.Summary with { Subject = subject, SenderDisplayName = senderDisplayName },
                Snippets = snippets,
            });
        }

        return guarded;
    }

    /// <summary>Ranks the eligible mail by whichever method this instance can apply to this query.</summary>
    /// <remarks>
    /// The semantic ranking is asked for first, because its answer decides how deep the lexical one has to reach: a
    /// lexical-only search ranks exactly the window it returns, while a fusion needs both sides deeper than the window
    /// for the fusion to mean anything. Asking the other way round would either rank four times too much mail on every
    /// lexical-only instance or discover the fused window was drawn from a truncated ranking.
    /// </remarks>
    private async Task<(
        IReadOnlyList<RankedEmailCandidate> Candidates,
        EmailSearchRetrievalMode RetrievalMode,
        SemanticSearchCapability SemanticSearch)>
        RankAsync(
            MailboxEmailSelection selection,
            EmailSearchQueryText queryText,
            int resultLimit,
            CancellationToken cancellationToken)
    {
        var candidateDepth = resultLimit * FusionCandidateDepthMultiplier;

        var semantic = await this.semanticSearch.FindNearestCandidatesAsync(
            selection,
            queryText,
            candidateDepth,
            cancellationToken);

        if (semantic.Candidates is not { } semanticCandidates)
        {
            var lexicalWindow = await this.searchIndexReader.ReadRankedCandidatesAsync(
                selection,
                queryText,
                resultLimit,
                cancellationToken);

            return (lexicalWindow, EmailSearchRetrievalMode.Lexical, semantic.Capability);
        }

        var lexicalCandidates = await this.searchIndexReader.ReadRankedCandidatesAsync(
            selection,
            queryText,
            candidateDepth,
            cancellationToken);

        var fused = ReciprocalRankFusion.Fuse(lexicalCandidates, semanticCandidates, resultLimit);

        return (fused, EmailSearchRetrievalMode.Hybrid, semantic.Capability);
    }

    /// <summary>Validates the request's structured filters and restricts the search to the accounts this deployment serves.</summary>
    /// <remarks>
    /// A request carrying a scope its caller already resolved is searched with that one rather than resolved a second
    /// time. <see cref="SearchEmailsRequest.ResolvedScope" /> says why that is the narrower answer as well as the
    /// cheaper one, and why nothing outside this assembly can set it.
    /// </remarks>
    private MailboxEmailSelection ReadableSelection(SearchEmailsRequest request) => MailboxEmailSelection.Create(
        request.ResolvedScope ?? this.scopeResolver.ReadableScope(
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
        request.HasAttachments);
}
