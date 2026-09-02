// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.BrowseSearch;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.BrowseSearch;

/// <summary>Covers the paged search: what it validates, how it constrains, how it pages, and what each result says about itself.</summary>
/// <remarks>
/// The rankings themselves belong to PostgreSQL and to an embedding model, so what is arranged here is where each of
/// them placed a message and what is asserted is everything downstream of that: that a filter removes mail rather than
/// demoting it, that two pages are contiguous, that an empty answer stays empty, and that a result says which ranking
/// found it.
/// </remarks>
public sealed class MailSearchBrowserTests
{
    /// <summary>The literal the scanner in the guarded-egress tests reports, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private const string Query = "invoice";

    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset SearchedAt = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly EmbeddingProfileId ProfileId =
        EmbeddingProfileId.Create(new Guid("6f2f1f0e-6a1a-4a2a-9d1f-0f5a1b2c3d4e"));

    /// <summary>A page holds what was asked for and hands back the boundary the next page continues from.</summary>
    [Fact]
    public async Task SearchPageAsync_MoreMatchesThanOnePageHolds_ReturnsThePageAndACursorThatContinuesIt()
    {
        // Arrange
        var ranked = RankedCorpus(4);
        var browser = BrowserOver(IndexOver(ranked));

        // Act
        var page = await browser.SearchPageAsync(RequestFor(Query, pageSize: 2), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [ranked[0].StoredEmailId, ranked[1].StoredEmailId],
            page.Results.Select(result => result.Email.StoredEmailId));
        Assert.NotNull(page.NextCursor);
        Assert.Equal(2, page.PageSize);
    }

    /// <summary>Two pages are contiguous: what the second returns is what the first left, in the order the ranking put them in.</summary>
    [Fact]
    public async Task SearchPageAsync_ThePageAfterACursor_ContinuesTheRankingWithoutRepeatingOrSkipping()
    {
        // Arrange
        var ranked = RankedCorpus(4);
        var browser = BrowserOver(IndexOver(ranked));
        var first = await browser.SearchPageAsync(
            RequestFor(Query, pageSize: 2),
            TestContext.Current.CancellationToken);

        // Act
        var second = await browser.SearchPageAsync(
            RequestFor(Query, pageSize: 2, cursor: first.NextCursor),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [ranked[2].StoredEmailId, ranked[3].StoredEmailId],
            second.Results.Select(result => result.Email.StoredEmailId));
        Assert.Null(second.NextCursor);
    }

    /// <summary>An absent cursor is the end of the list rather than a hint to ask again.</summary>
    [Fact]
    public async Task SearchPageAsync_APageHoldingTheLastMatch_CarriesNoCursor()
    {
        // Arrange
        var browser = BrowserOver(IndexOver(RankedCorpus(2)));

        // Act
        var page = await browser.SearchPageAsync(RequestFor(Query, pageSize: 5), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, page.Results.Count);
        Assert.Null(page.NextCursor);
    }

    /// <summary>The regression this whole route exists to avoid: a query nothing matched is answered with nothing at all.</summary>
    [Fact]
    public async Task SearchPageAsync_AQueryNothingMatched_ReturnsAnEmptyPageRatherThanTheNearestMail()
    {
        // Arrange
        var browser = BrowserOver(IndexOver(RankedCorpus(3)));

        // Act
        var page = await browser.SearchPageAsync(
            RequestFor("something nothing carries"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(page.Results);
        Assert.Null(page.NextCursor);
    }

    /// <summary>A filter constrains: the best-ranked mail it excludes is absent from the results rather than lower down them.</summary>
    [Fact]
    public async Task SearchPageAsync_AFilterExcludingTheBestRankedMail_LeavesItOutRatherThanRankingItLower()
    {
        // Arrange
        var excluded = SyntheticEmailSummaries.Create(FirstJuly, senderAddress: "loud@example.test");
        var kept = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1), senderAddress: "quiet@example.test");
        var index = new InMemoryEmailSearchIndex()
            .With(excluded, relevanceRank: 0.9f, matchedText: Query)
            .With(kept, relevanceRank: 0.1f, matchedText: Query);
        var browser = BrowserOver(index);

        // Act
        var page = await browser.SearchPageAsync(
            RequestFor(Query) with { SenderAddress = "quiet@example.test" },
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(page.Results);
        Assert.Equal(kept.StoredEmailId, result.Email.StoredEmailId);
    }

    /// <summary>The filters reach the ranking rather than what came back from it, so a limit is never spent on mail they exclude.</summary>
    [Fact]
    public async Task SearchPageAsync_AnyFilteredSearch_RanksUnderTheRequestsOwnFiltersRatherThanApplyingThemAfterwards()
    {
        // Arrange
        var index = IndexOver(RankedCorpus(2));
        var browser = BrowserOver(index);

        // Act
        await browser.SearchPageAsync(
            RequestFor(Query) with { SenderAddress = "quiet@example.test", HasAttachments = true },
            TestContext.Current.CancellationToken);

        // Assert
        var ranking = Assert.Single(index.RankedCandidatesCalls);

        // The comparison form the persistence layer indexes, which is what says the filter was normalized on the way in
        // rather than compared as the caller happened to spell it.
        Assert.Equal("QUIET@EXAMPLE.TEST", ranking.Selection.SenderNormalizedAddress);
        Assert.True(ranking.Selection.HasAttachments);
    }

    /// <summary>Every page ranks the same list to the same depth, which is what makes the sequence a client walks one sequence.</summary>
    [Fact]
    public async Task SearchPageAsync_AnyPageOfARankedList_RanksToTheListsWholeDepth()
    {
        // Arrange
        var index = IndexOver(RankedCorpus(3));
        var browser = BrowserOver(index);

        // Act
        await browser.SearchPageAsync(RequestFor(Query, pageSize: 1), TestContext.Current.CancellationToken);

        // Assert
        var ranking = Assert.Single(index.RankedCandidatesCalls);
        Assert.Equal(RankedSearchList.MaximumRankedDepth, ranking.Limit);
    }

    /// <summary>A boundary means something only inside the list it was taken from, so one from another search is refused rather than followed.</summary>
    [Fact]
    public async Task SearchPageAsync_ACursorIssuedForAnotherSearch_IsRefused()
    {
        // Arrange
        var browser = BrowserOver(IndexOver(RankedCorpus(4)));
        var first = await browser.SearchPageAsync(
            RequestFor(Query, pageSize: 2),
            TestContext.Current.CancellationToken);

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryCursorFilterMismatchException>(
            () => browser.SearchPageAsync(
                RequestFor(Query, pageSize: 2, cursor: first.NextCursor) with { HasAttachments = true },
                TestContext.Current.CancellationToken));
    }

    /// <summary>A cursor this system never issued and one issued for a different list are two different mistakes with two different repairs.</summary>
    [Fact]
    public async Task SearchPageAsync_ACursorThisSystemNeverIssued_IsRefusedAsUnreadableRatherThanAsAMismatch()
    {
        // Arrange
        var browser = BrowserOver(IndexOver(RankedCorpus(2)));

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryCursorMalformedException>(
            () => browser.SearchPageAsync(
                RequestFor(Query, cursor: "a cursor nobody issued"),
                TestContext.Current.CancellationToken));
    }

    /// <summary>A client carrying the field with nothing in it yet has asked for the beginning of the walk.</summary>
    [Fact]
    public async Task SearchPageAsync_ABlankCursor_ReadsTheBestRankedResults()
    {
        // Arrange
        var ranked = RankedCorpus(2);
        var browser = BrowserOver(IndexOver(ranked));

        // Act
        var page = await browser.SearchPageAsync(
            RequestFor(Query, cursor: "   "),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ranked[0].StoredEmailId, page.Results[0].Email.StoredEmailId);
    }

    /// <summary>A search with no text is a list, which the timeline answers in a stable order and with a cursor in both directions.</summary>
    [Fact]
    public async Task SearchPageAsync_ABlankQuery_IsRefusedRatherThanTreatedAsMatchingEverything()
    {
        // Arrange
        var browser = BrowserOver(IndexOver(RankedCorpus(2)));

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => browser.SearchPageAsync(RequestFor("   "), TestContext.Current.CancellationToken));
    }

    /// <summary>A page size outside the accepted range is refused rather than clamped, so a client is never quietly served a different page.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(EmailSearchResultLimit.MaximumValue + 1)]
    public async Task SearchPageAsync_APageSizeOutsideTheAcceptedRange_IsRefused(int pageSize)
    {
        // Arrange
        var browser = BrowserOver(IndexOver(RankedCorpus(2)));

        // Act, Assert
        await Assert.ThrowsAsync<EmailSearchResultLimitOutOfRangeException>(
            () => browser.SearchPageAsync(
                RequestFor(Query, pageSize: pageSize),
                TestContext.Current.CancellationToken));
    }

    /// <summary>The grant is asked for before the request is validated, so a caller that may not read learns nothing about what this deployment accepts.</summary>
    [Fact]
    public async Task SearchPageAsync_ACallerWithoutTheMailReadGrant_IsRefusedBeforeAnythingIsValidated()
    {
        // Arrange
        var index = IndexOver(RankedCorpus(2));
        var browser = BrowserOver(index, authorization: AccessAuthorizations.ForCallerGranted());

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => browser.SearchPageAsync(RequestFor("   "), TestContext.Current.CancellationToken));
        Assert.Empty(index.RankedCandidatesCalls);
    }

    /// <summary>An instance that has activated no embedding profile answers lexically and says so, rather than answering more quietly.</summary>
    [Fact]
    public async Task SearchPageAsync_AnInstanceThatEmbedsNothing_ReportsALexicalPageAndAnInactiveCapability()
    {
        // Arrange
        var browser = BrowserOver(IndexOver(RankedCorpus(2)));

        // Act
        var page = await browser.SearchPageAsync(RequestFor(Query), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Lexical, page.RetrievalMode);
        Assert.Equal(SemanticSearchCapability.Inactive, page.SemanticSearch);
        Assert.All(page.Results, result => Assert.Equal(SearchMatchOrigin.LexicalRanking, result.MatchedBy));
    }

    /// <summary>The one thing a person cannot work out from a result list: whether the message is there for its words or for its meaning.</summary>
    [Fact]
    public async Task SearchPageAsync_AHybridInstance_ReportsWhichRankingFoundEachResult()
    {
        // Arrange
        var words = SyntheticEmailSummaries.Create(FirstJuly);
        var both = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1));
        var meaning = SyntheticEmailSummaries.Create(FirstJuly.AddDays(2));
        var index = new InMemoryEmailSearchIndex()
            .With(words, relevanceRank: 0.9f, matchedText: Query)
            .With(both, relevanceRank: 0.5f, matchedText: Query)
            .With(meaning, relevanceRank: 0.5f, matchedText: "nothing this query carries");
        var vectors = new InMemoryEmailVectorSearchIndex()
            .With(both, distance: 0.1f)
            .With(meaning, distance: 0.2f);
        var browser = BrowserOver(index, semanticSearch: SemanticSearchOver(vectors));

        // Act
        var page = await browser.SearchPageAsync(RequestFor(Query), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Hybrid, page.RetrievalMode);
        Assert.Equal(
            [SearchMatchOrigin.LexicalRanking, SearchMatchOrigin.BothRankings, SearchMatchOrigin.SemanticRanking],
            page.Results
                .OrderBy(result => result.Email.ReceivedAt)
                .Select(result => result.MatchedBy));
    }

    /// <summary>A fused score is a sum of reciprocals rather than a rank, so a boundary taken over one has to name the same place on the next page.</summary>
    [Fact]
    public async Task SearchPageAsync_ThePageAfterACursorOnAHybridInstance_ContinuesTheFusedOrderWithoutRepeatingOrSkipping()
    {
        // Arrange
        var ranked = RankedCorpus(4);
        var index = IndexOver(ranked);
        var vectors = new InMemoryEmailVectorSearchIndex();

        // A substitute's arrangement is a side effect, so this stays a loop rather than becoming a projection. The
        // semantic order is the reverse of the lexical one, which is what makes the fused order neither of them.
        for (var place = 0; place < ranked.Length; place++)
        {
            vectors.With(ranked[^(place + 1)], distance: 0.1f + (place * 0.1f));
        }

        var browser = BrowserOver(index, semanticSearch: SemanticSearchOver(vectors));
        var first = await browser.SearchPageAsync(
            RequestFor(Query, pageSize: 2),
            TestContext.Current.CancellationToken);

        // Act
        var second = await browser.SearchPageAsync(
            RequestFor(Query, pageSize: 2, cursor: first.NextCursor),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Hybrid, first.RetrievalMode);
        Assert.Equal(2, second.Results.Count);
        Assert.Empty(first.Results
            .Select(result => result.Email.StoredEmailId)
            .Intersect(second.Results.Select(result => result.Email.StoredEmailId)));
        Assert.Equal(
            ranked.Select(email => email.StoredEmailId).Order(Comparer<StoredEmailId>.Create(ByIdentity)),
            first.Results
                .Concat(second.Results)
                .Select(result => result.Email.StoredEmailId)
                .Order(Comparer<StoredEmailId>.Create(ByIdentity)));
    }

    /// <summary>Both rankings reach the list's whole depth, which is what keeps their agreement observable as far down as paging can go.</summary>
    [Fact]
    public async Task SearchPageAsync_AHybridInstance_RanksBothSidesToTheListsWholeDepth()
    {
        // Arrange
        var index = IndexOver(RankedCorpus(2));
        var vectors = new InMemoryEmailVectorSearchIndex();
        var browser = BrowserOver(index, semanticSearch: SemanticSearchOver(vectors));

        // Act
        await browser.SearchPageAsync(RequestFor(Query, pageSize: 1), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(RankedSearchList.MaximumRankedDepth, Assert.Single(index.RankedCandidatesCalls).Limit);
        Assert.Equal(RankedSearchList.MaximumRankedDepth, Assert.Single(vectors.Calls).Limit);
    }

    /// <summary>A result is drawn from one request, so it carries the opening of the message beside the extracts.</summary>
    [Fact]
    public async Task SearchPageAsync_AMessageThisDeploymentHasExtracted_CarriesItsPreviewBesideTheExtracts()
    {
        // Arrange
        var matched = SyntheticEmailSummaries.Create(FirstJuly);
        var index = new InMemoryEmailSearchIndex()
            .With(matched, relevanceRank: 0.9f, matchedText: Query, "the **invoice** is attached");
        var previews = new InMemoryStoredEmailPreviews()
            .With(matched.StoredEmailId, "the invoice is attached and due on Friday");
        var browser = BrowserOver(index, previewReader: previews);

        // Act
        var page = await browser.SearchPageAsync(RequestFor(Query), TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(page.Results);
        Assert.Equal("the invoice is attached and due on Friday", result.Preview);
        Assert.Equal(["the **invoice** is attached"], result.Snippets);
    }

    /// <summary>A message nothing has extracted yet has no opening to show, which is absent rather than empty.</summary>
    [Fact]
    public async Task SearchPageAsync_AMessageNothingHasExtractedYet_CarriesNoPreview()
    {
        // Arrange
        var browser = BrowserOver(IndexOver(RankedCorpus(1)));

        // Act
        var page = await browser.SearchPageAsync(RequestFor(Query), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(Assert.Single(page.Results).Preview);
    }

    /// <summary>A page is one of the points mail content leaves the deployment, so everything of the message it carries is scanned first.</summary>
    [Fact]
    public async Task SearchPageAsync_ASwitchedOnScanner_RedactsTheSubjectThePreviewAndTheExtracts()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var matched = SyntheticEmailSummaries.Create(FirstJuly, subject: $"the key is {Marker}");
        var index = new InMemoryEmailSearchIndex()
            .With(matched, relevanceRank: 0.9f, matchedText: Query, $"use {Marker} to sign in");
        var previews = new InMemoryStoredEmailPreviews().With(matched.StoredEmailId, $"sign in with {Marker} today");
        var browser = BrowserOver(index, previewReader: previews, egressGuard: egress.Guard);

        // Act
        var page = await browser.SearchPageAsync(RequestFor(Query), TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(page.Results);
        Assert.Equal("the key is [redacted:CloudKey]", result.Email.Subject);
        Assert.Equal("sign in with [redacted:CloudKey] today", result.Preview);
        Assert.Equal(["use [redacted:CloudKey] to sign in"], result.Snippets);
    }

    /// <summary>A scanner that cannot answer refuses the page rather than letting it out unscanned.</summary>
    [Fact]
    public async Task SearchPageAsync_AScannerThatCannotAnswer_RefusesThePageRatherThanServingItUnscanned()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(TimeProvider.System);
        var index = new InMemoryEmailSearchIndex()
            .With(SyntheticEmailSummaries.Create(FirstJuly, subject: "the invoice"), 0.9f, Query);
        var browser = BrowserOver(index, egressGuard: egress.Guard);

        // Act, Assert
        await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(
            () => browser.SearchPageAsync(RequestFor(Query), TestContext.Current.CancellationToken));
    }

    /// <summary>An owner who owns no account this deployment serves is still told what semantic retrieval can do, because that describes the instance.</summary>
    [Fact]
    public async Task SearchPageAsync_AnOwnerOwningNoAccount_ReturnsAnEmptyPageStillReportingTheCapability()
    {
        // Arrange
        var browser = BrowserOver(IndexOver(RankedCorpus(1)), accountCatalog: CatalogServing());

        // Act
        var page = await browser.SearchPageAsync(RequestFor(Query), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(page.Results);
        Assert.Null(page.NextCursor);
        Assert.Equal(SemanticSearchCapability.Inactive, page.SemanticSearch);
    }

    /// <summary>A search of the local copy never reaches an account its owner does not own.</summary>
    [Fact]
    public async Task SearchPageAsync_AnAccountThisOwnerDoesNotOwn_IsRefused()
    {
        // Arrange
        var browser = BrowserOver(IndexOver(RankedCorpus(1)));

        // Act, Assert
        await Assert.ThrowsAsync<MailAccountNotAccessibleException>(
            () => browser.SearchPageAsync(
                RequestFor(Query) with { Accounts = [MailAccountSelector.Create("nobody")] },
                TestContext.Current.CancellationToken));
    }

    /// <summary>Orders identities so two sets can be compared as sets, whatever order the two rankings put them in.</summary>
    private static int ByIdentity(StoredEmailId left, StoredEmailId right) =>
        left.Value.CompareTo(right.Value);

    /// <summary>Builds a corpus whose relevance order is the order it is returned in, so a page can be asserted by identity.</summary>
    private static EmailSummary[] RankedCorpus(int count) =>
    [
        .. Enumerable
            .Range(0, count)
            .Select(place => SyntheticEmailSummaries.Create(FirstJuly.AddDays(place))),
    ];

    /// <summary>Indexes a corpus so that the first element ranks highest and every element matches the query under test.</summary>
    private static InMemoryEmailSearchIndex IndexOver(EmailSummary[] ranked)
    {
        var index = new InMemoryEmailSearchIndex();

        // A substitute's arrangement is a side effect, so this stays a loop rather than becoming a projection.
        for (var place = 0; place < ranked.Length; place++)
        {
            index.With(ranked[place], relevanceRank: 1f - (place * 0.1f), matchedText: Query);
        }

        return index;
    }

    private static BrowseSearchRequest RequestFor(string? queryText, int? pageSize = null, string? cursor = null) =>
        new() { QueryText = queryText, PageSize = pageSize, Cursor = cursor };

    private static MailSearchBrowser BrowserOver(
        InMemoryEmailSearchIndex index,
        SemanticEmailSearch? semanticSearch = null,
        IStoredEmailPreviewReader? previewReader = null,
        ICallerMailAccountCatalog? accountCatalog = null,
        SensitiveContentEgressGuard? egressGuard = null,
        AccessAuthorization? authorization = null) => new(
        index,
        semanticSearch ?? LexicalOnlySemanticSearch(),
        previewReader ?? new InMemoryStoredEmailPreviews(),
        new MailboxScopeResolver(
            accountCatalog ?? CatalogServing(MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId)),
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing),
        EmailSearchSnippetBounds.Default,
        egressGuard ?? SensitiveContentEgressGuards.Inactive(),
        new RecordingMailboxReadTelemetry(),
        authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

    /// <summary>Builds the semantic half of a deployment that configured no embedding provider and activated nothing.</summary>
    private static SemanticEmailSearch LexicalOnlySemanticSearch() => new(
        Substitute.For<IActiveEmbeddingProfileReader>(),
        new InMemoryEmailVectorSearchIndex(),
        ServingProviderHealth(),
        new FakeTimeProvider(SearchedAt),
        textEmbeddingGenerator: null);

    /// <summary>Builds the semantic half of a deployment that has activated a profile its generator agrees with.</summary>
    private static SemanticEmailSearch SemanticSearchOver(InMemoryEmailVectorSearchIndex vectorIndex)
    {
        var identity = ProfileIdentity();
        var profileReader = Substitute.For<IActiveEmbeddingProfileReader>();
        profileReader.FindActiveProfileAsync(Arg.Any<CancellationToken>())
            .Returns(new RegisteredEmbeddingProfile(ProfileId, identity));

        return new SemanticEmailSearch(
            profileReader,
            vectorIndex,
            ServingProviderHealth(),
            new FakeTimeProvider(SearchedAt),
            new ScriptedTextEmbeddingGenerator(identity, maximumPassagesPerCall: 8));
    }

    /// <summary>Reports every provider as answering, so a test decides retrieval through the profile and the generator alone.</summary>
    private static IAiProviderHealthReader ServingProviderHealth()
    {
        var reader = Substitute.For<IAiProviderHealthReader>();
        reader.Read(Arg.Any<AiProviderRole>())
            .Returns(call => new AiProviderHealth(
                call.Arg<AiProviderRole>(),
                AiProviderHealthState.Serving,
                SearchedAt));

        return reader;
    }

    private static EmbeddingProfileIdentity ProfileIdentity() => EmbeddingProfileIdentity.Create(
        "a-provider",
        "a-model",
        modelVersion: null,
        dimension: 8,
        EmbeddingDistanceMetric.Cosine,
        EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    /// <summary>Builds a catalog that serves exactly the accounts named, in the order the port promises.</summary>
    private static ICallerMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns(
        [
            .. servedAccountIds
                .OrderBy(accountId => accountId.Value, StringComparer.Ordinal)
                .Select(accountId => SyntheticServedAccount.Of(accountId)),
        ]);

        return catalog;
    }
}
