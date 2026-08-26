// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Observability;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Results;
using MailFathom.Mcp.Tools.Summaries;
using MailFathom.Mcp.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers what the <c>search_emails</c> tool itself owns: converting arguments and publishing a ranked window.</summary>
/// <remarks>
/// <para>
/// The tool calls the real <see cref="MailboxSearchReader" /> rather than a substitute for it, because the use case is
/// where the query bound, the result-count range, and every authorization decision live, and a substitute would only
/// prove that the tool composes with a fiction. What the stub replaces is the lexical index, the boundary below the use
/// case — and it deliberately returns whatever it was given, so the bounds this boundary applies to what it publishes
/// are observable rather than hidden behind an adapter that had already applied them.
/// </para>
/// <para>
/// Two properties are asserted throughout rather than in one test of their own: a refused call never reaches the index,
/// and no failure message carries the query text or the value that was refused. Both hold for every path through the
/// boundary, so proving them once would prove them for one path.
/// </para>
/// </remarks>
public sealed class SearchEmailsToolTests
{
    private const string ServedAccountId = "personal";
    private const string Query = "quarterly invoice";

    [Fact]
    public async Task SearchEmailsAsync_QueryTextAlone_SearchesEveryServedAccountWithTheDefaultResultCount()
    {
        // Arrange
        var index = new StubEmailSearchIndexReader();
        var tool = ToolOver(index);

        // Act
        await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(index.LastSelection);
        Assert.Equal([MailAccountId.Create(ServedAccountId)], index.LastSelection.Scope.AccountIds);
        Assert.Empty(index.LastSelection.Scope.SelectedFolders);
        Assert.Equal(Query, index.LastQueryText?.Value);
        Assert.Equal(EmailSearchResultLimit.DefaultValue, index.LastLimit);
    }

    [Fact]
    public async Task SearchEmailsAsync_EveryFilterNamed_PassesEachOneToTheUseCase()
    {
        // Arrange
        var index = new StubEmailSearchIndexReader();
        var tool = ToolOver(index);
        var rangeStart = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var rangeEnd = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        // Act
        await tool.SearchEmailsAsync(
            Query,
            accounts: [ServedAccountId],
            folders: ["archive"],
            senderAddress: "sender@example.test",
            recipientAddress: "recipient@example.test",
            subjectFragment: "invoice",
            receivedOnOrAfter: rangeStart,
            receivedBefore: rangeEnd,
            isRemotelySeen: false,
            isRemotelyFlagged: true,
            keyword: "$Junk",
            hasAttachments: true,
            resultLimit: 10,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(index.LastSelection);
        var selection = index.LastSelection;
        Assert.Equal([MailAccountId.Create(ServedAccountId)], selection.Scope.AccountIds);
        Assert.Equal(
            [new MailFolderIdentity(MailAccountId.Create(ServedAccountId), MailFolderAlias.Create("ARCHIVE"))],
            selection.Scope.SelectedFolders);
        Assert.Equal("invoice", selection.SubjectFragment);
        Assert.Equal(rangeStart, selection.ReceivedOnOrAfter);
        Assert.Equal(rangeEnd, selection.ReceivedBefore);
        Assert.False(selection.IsRemotelySeen);
        Assert.True(selection.IsRemotelyFlagged);
        Assert.Equal("$JUNK", selection.Keyword);
        Assert.True(selection.HasAttachments);
        Assert.Equal(Query, index.LastQueryText?.Value);
        Assert.Equal(10, index.LastLimit);
    }

    /// <summary>The subject fragment narrows which emails are eligible; the query text is what the eligible ones are ranked against.</summary>
    [Fact]
    public async Task SearchEmailsAsync_SubjectFragmentAndQueryText_KeepsThemApart()
    {
        // Arrange
        var index = new StubEmailSearchIndexReader();
        var tool = ToolOver(index);

        // Act
        await tool.SearchEmailsAsync(
            Query,
            subjectFragment: "statement",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("statement", index.LastSelection?.SubjectFragment);
        Assert.Equal(Query, index.LastQueryText?.Value);
    }

    /// <summary>
    /// A text filter a client sent empty names no filter rather than a value nothing can match, which is what the
    /// argument descriptions promise and what a client that always sends its whole form depends on.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchEmailsAsync_BlankTextFilter_ReadsAsThoughItWereNotNamed(string blank)
    {
        // Arrange
        var index = new StubEmailSearchIndexReader();
        var tool = ToolOver(index);

        // Act
        await tool.SearchEmailsAsync(
            Query,
            senderAddress: blank,
            recipientAddress: blank,
            subjectFragment: blank,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(index.LastSelection);
        Assert.Null(index.LastSelection.SenderNormalizedAddress);
        Assert.Null(index.LastSelection.RecipientNormalizedAddress);
        Assert.Null(index.LastSelection.SubjectFragment);
        Assert.Equal(1, index.ReadCount);
    }

    /// <summary>A search with no text is a listing, which <c>list_emails</c> answers in a stable order and with a cursor.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchEmailsAsync_BlankQueryText_IsRefusedWithoutReading(string blank)
    {
        // Arrange
        var index = new StubEmailSearchIndexReader();
        var tool = ToolOver(index);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.SearchEmailsAsync(blank, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxQueryFilterInvalid, failure.ErrorCode);
        Assert.Equal("search query", failure.FilterName);
        Assert.Equal(0, index.ReadCount);
    }

    [Fact]
    public async Task SearchEmailsAsync_QueryTextLongerThanTheSearchAccepts_IsRefusedWithoutReading()
    {
        // Arrange
        var index = new StubEmailSearchIndexReader();
        var tool = ToolOver(index);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.SearchEmailsAsync(
                new string('a', EmailSearchQueryText.MaximumLength + 1),
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("search query", failure.FilterName);
        Assert.Equal(0, index.ReadCount);
    }

    /// <summary>What somebody is searching their own mailbox for is personal data, so no refusal may repeat any of it.</summary>
    [Fact]
    public async Task SearchEmailsAsync_RefusedQueryText_NamesNoPartOfIt()
    {
        // Arrange
        const string PersonalData = "victim@example.test";
        var tool = ToolOver(new StubEmailSearchIndexReader());

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.SearchEmailsAsync(
                $"{PersonalData}\u0001",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain(PersonalData, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A clamped window looks exactly like the window a caller asked for, so an unserved count is refused instead.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(EmailSearchResultLimit.MaximumValue + 1)]
    [InlineData(int.MaxValue)]
    public async Task SearchEmailsAsync_ResultCountOutsideTheServedRange_RaisesTheUseCaseRefusalWithoutReading(int resultLimit)
    {
        // Arrange
        var index = new StubEmailSearchIndexReader();
        var tool = ToolOver(index);

        // Act
        var failure = await Assert.ThrowsAsync<EmailSearchResultLimitOutOfRangeException>(
            () => tool.SearchEmailsAsync(
                Query,
                resultLimit: resultLimit,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailSearchResultLimitOutOfRange, failure.ErrorCode);
        Assert.Equal(EmailSearchResultLimit.MaximumValue, failure.MaximumResultLimit);
        Assert.Equal(0, index.ReadCount);
    }

    /// <summary>The use case decides authorization, so the tool lets its refusal travel rather than answering an empty window.</summary>
    [Fact]
    public async Task SearchEmailsAsync_AccountThisDeploymentDoesNotServe_RaisesTheUseCaseRefusalWithoutReading()
    {
        // Arrange
        var index = new StubEmailSearchIndexReader();
        var tool = ToolOver(index);

        // Act
        var failure = await Assert.ThrowsAsync<MailAccountNotAccessibleException>(
            () => tool.SearchEmailsAsync(
                Query,
                accounts: [ServedAccountId, "someone-elses"],
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailAccountNotAccessible, failure.ErrorCode);
        Assert.Equal(0, index.ReadCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("in\u0001box")]
    public async Task SearchEmailsAsync_UnusableFolderAlias_IsRefusedWithoutReading(string unusable)
    {
        // Arrange
        var index = new StubEmailSearchIndexReader();
        var tool = ToolOver(index);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.SearchEmailsAsync(
                Query,
                folders: [unusable],
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("folders", failure.FilterName);
        Assert.Equal(0, index.ReadCount);
    }

    /// <summary>
    /// A keyword no stored keyword could be would narrow the search to nothing, and an empty result set reads as an
    /// answer about the mailbox rather than as a filter the boundary refused, so the refusal reaches the caller.
    /// </summary>
    [Theory]
    [InlineData("$Ju\u0001nk")]
    [InlineData("keyword\u0000")]
    public async Task SearchEmailsAsync_KeywordNoStoredKeywordCouldBe_IsRefusedWithoutReading(string unusable)
    {
        // Arrange
        var index = new StubEmailSearchIndexReader();
        var tool = ToolOver(index);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.SearchEmailsAsync(
                Query,
                keyword: unusable,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxQueryFilterInvalid, failure.ErrorCode);
        Assert.Equal("keyword", failure.FilterName);
        Assert.Equal(0, index.ReadCount);
    }

    /// <summary>The bound is the one the stored keywords were kept under, so a longer filter could match none of them.</summary>
    [Fact]
    public async Task SearchEmailsAsync_KeywordLongerThanTheBound_IsRefusedWithoutReading()
    {
        // Arrange
        var index = new StubEmailSearchIndexReader();
        var tool = ToolOver(index);
        var overlyLongKeyword = new string('a', RemoteEmailKeywords.MaximumKeywordLength + 1);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.SearchEmailsAsync(
                Query,
                keyword: overlyLongKeyword,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("keyword", failure.FilterName);
        Assert.Equal(0, index.ReadCount);
    }

    [Fact]
    public async Task SearchEmailsAsync_MatchedEmail_PublishesTheSummaryTheRankAndTheSnippetsAsTheyWereMatched()
    {
        // Arrange
        var storedEmailId = EmailIdentityAt(1);
        var receivedAt = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var match = new EmailSearchMatch(
            SummaryOf(storedEmailId, receivedAt),
            RelevanceRank: 0.75f,
            Snippets: ["the **invoice** for March", "your **invoice** is attached"]);
        var tool = ToolOver(new StubEmailSearchIndexReader(match));

        // Act
        var result = await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var published = Assert.Single(result.Matches);
        Assert.Equal(storedEmailId.ToString(), published.Summary.StoredEmailId);
        Assert.Equal(ServedAccountId, published.Summary.AccountId);
        Assert.Equal("INBOX", published.Summary.FolderAlias);
        Assert.Equal(receivedAt, published.Summary.ReceivedAt);
        Assert.Equal(0.75f, published.RelevanceRank);
        Assert.Equal(["the **invoice** for March", "your **invoice** is attached"], published.Snippets);
    }

    /// <summary>A search publishes the sender verdict by republishing the listing's summary rather than a shape of its own.</summary>
    /// <remarks>
    /// The assertion is against the summary the listing tool would publish for the same email, so a client written
    /// against a listing needs nothing new to read a match's verdict.
    /// </remarks>
    [Fact]
    public async Task SearchEmailsAsync_MatchedEmail_RepublishesTheListingsSenderVerdict()
    {
        // Arrange
        var summary = SummaryOf(EmailIdentityAt(1), new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
        var tool = ToolOver(new StubEmailSearchIndexReader(
            new EmailSearchMatch(summary, RelevanceRank: 0.5f, Snippets: [])));

        // Act
        var result = await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var published = Assert.Single(result.Matches);
        Assert.Equal(
            ListedEmailSummary.From(summary, PublishedAccountNames.From(new StubMailAccountCatalog(ServedAccountId)))
                .SenderVerification,
            published.Summary.SenderVerification);
    }

    /// <summary>The authorship reading rides on that same summary, so a match carries it exactly as a listing does.</summary>
    [Fact]
    public async Task SearchEmailsAsync_AnAssessedMatch_RepublishesTheListingsAuthorshipReading()
    {
        // Arrange
        var summary = SummaryOf(EmailIdentityAt(1), new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)) with
        {
            MachineAuthorship = MachineAuthorshipAssessment.Assessed(
                MachineAuthorshipBand.Likely,
                likelihood: 0.9,
                MachineAuthorshipSignals.TagCharacters,
                MachineAuthorshipProfile.Standard.Revision),
        };
        var tool = ToolOver(new StubEmailSearchIndexReader(
            new EmailSearchMatch(summary, RelevanceRank: 0.5f, Snippets: [])));

        // Act
        var result = await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var published = Assert.Single(result.Matches);
        Assert.Equal("Likely", published.Summary.MachineAuthorship.State.ToString());
        Assert.Equal(0.9, published.Summary.MachineAuthorship.Likelihood);
    }

    /// <summary>An email matched on its subject or a participant carries no extract, because the summary publishes both whole.</summary>
    [Fact]
    public async Task SearchEmailsAsync_MatchWithNoIndexedBodyText_PublishesNoSnippets()
    {
        // Arrange
        var tool = ToolOver(new StubEmailSearchIndexReader(MatchWith(rank: 0.1f, snippets: [])));

        // Act
        var result = await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(Assert.Single(result.Matches).Snippets);
    }

    /// <summary>A client branches on the mode rather than assuming one, so the later hybrid work widens a field instead of reshaping a response.</summary>
    [Fact]
    public async Task SearchEmailsAsync_AnyWindow_ReportsThatTheseResultsWereRetrievedLexically()
    {
        // Arrange
        var tool = ToolOver(new StubEmailSearchIndexReader(MatchWith(rank: 0.5f, snippets: ["**invoice**"])));

        // Act
        var result = await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailRetrievalMode.Lexical, result.RetrievalMode);
    }

    /// <summary>
    /// The mode says what this call did and the capability says what the server can do. A client that read only the mode
    /// could not tell a server that never embeds from one whose embedding credential has expired, and only the second is
    /// worth telling a user about.
    /// </summary>
    [Fact]
    public async Task SearchEmailsAsync_AServerThatDoesNotEmbed_PublishesSemanticRetrievalAsInactive()
    {
        // Arrange
        var tool = ToolOver(new StubEmailSearchIndexReader(MatchWith(rank: 0.5f, snippets: ["**invoice**"])));

        // Act
        var result = await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SemanticSearchAvailability.Inactive, result.SemanticSearch);
    }

    /// <summary>
    /// Matching nothing is an ordinary answer, and it is a fully shaped one: a client reads the same fields it reads
    /// from a window that matched, so a search cannot be used to establish which accounts or folders exist.
    /// </summary>
    [Fact]
    public async Task SearchEmailsAsync_NothingMatched_PublishesAnEmptyWindowRatherThanFailing()
    {
        // Arrange
        var tool = ToolOver(
            new StubEmailSearchIndexReader(),
            new StubSynchronizationFreshnessReader(FreshnessOf("INBOX", synchronizedAt: null)));

        // Act
        var result = await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Matches);
        Assert.Equal(EmailRetrievalMode.Lexical, result.RetrievalMode);
        Assert.Single(result.FolderFreshness);
    }

    /// <summary>The bound is the privacy control on how much mail one query draws out, so a defective adapter must not be able to widen it.</summary>
    [Fact]
    public async Task SearchEmailsAsync_MoreSnippetsThanTheDeploymentAllows_PublishesOnlyWhatItAllows()
    {
        // Arrange
        var snippetBounds = EmailSearchSnippetBounds.Create(snippetsPerEmail: 2, wordsPerSnippet: 24);
        var oversuppliedSnippets = Enumerable
            .Range(0, 6)
            .Select(index => $"extract **{index}** of the body")
            .ToArray();
        var tool = ToolOver(
            new StubEmailSearchIndexReader(MatchWith(rank: 0.5f, snippets: oversuppliedSnippets)),
            snippetBounds: snippetBounds);

        // Act
        var result = await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(oversuppliedSnippets.Take(2), Assert.Single(result.Matches).Snippets);
    }

    [Fact]
    public async Task SearchEmailsAsync_SnippetLongerThanAnyTheBoundsProduce_PublishesItCut()
    {
        // Arrange
        var snippetBounds = EmailSearchSnippetBounds.Create(snippetsPerEmail: 3, wordsPerSnippet: 4);
        var wholeBody = new string('a', snippetBounds.MaximumCharacters * 10);
        var tool = ToolOver(
            new StubEmailSearchIndexReader(MatchWith(rank: 0.5f, snippets: [wholeBody])),
            snippetBounds: snippetBounds);

        // Act
        var result = await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var published = Assert.Single(Assert.Single(result.Matches).Snippets);
        Assert.True(published.Length < wholeBody.Length, "The extract was published whole.");
        Assert.EndsWith("…", published, StringComparison.Ordinal);

        // Three times the character bound is the ceiling the boundary publishes under, and the mark it adds while
        // cutting counts against it: a ceiling a truncation can push past is not a bound.
        Assert.True(
            published.Length <= (snippetBounds.MaximumCharacters * 3) + 1,
            $"The cut extract is {published.Length} characters, above the ceiling it was cut to.");
    }

    /// <summary>An extract within the bound is published exactly as it was matched, markers and all.</summary>
    [Fact]
    public async Task SearchEmailsAsync_SnippetWithinTheBounds_PublishesItUnchanged()
    {
        // Arrange
        const string Snippet = "the **invoice** for March is attached";
        var tool = ToolOver(new StubEmailSearchIndexReader(MatchWith(rank: 0.5f, snippets: [Snippet])));

        // Act
        var result = await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Snippet, Assert.Single(Assert.Single(result.Matches).Snippets));
    }

    /// <summary>The window bound limits how much mail content one call draws out, so it holds against whatever the index returned.</summary>
    [Fact]
    public async Task SearchEmailsAsync_MoreMatchesThanASearchServes_PublishesOnlyWhatItServes()
    {
        // Arrange
        var oversuppliedWindow = Enumerable
            .Range(1, EmailSearchResultLimit.MaximumValue + 5)
            .Select(position => MatchWith(rank: 1.0f - (position * 0.01f), snippets: ["**invoice**"], position))
            .ToArray();
        var tool = ToolOver(new StubEmailSearchIndexReader(oversuppliedWindow));

        // Act
        var result = await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSearchResultLimit.MaximumValue, result.Matches.Count);
    }

    /// <summary>Freshness travels with every window, because a search is served from local state whether or not a server is reachable.</summary>
    [Fact]
    public async Task SearchEmailsAsync_AnyWindow_PublishesTheFreshnessOfEveryCoveredFolder()
    {
        // Arrange
        var synchronizedAt = new DateTimeOffset(2026, 3, 2, 6, 0, 0, TimeSpan.Zero);
        var tool = ToolOver(
            new StubEmailSearchIndexReader(),
            new StubSynchronizationFreshnessReader(
                FreshnessOf("INBOX", synchronizedAt),
                FreshnessOf("ARCHIVE", synchronizedAt: null)));

        // Act
        var result = await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("INBOX", true), ("ARCHIVE", false)],
            [.. result.FolderFreshness.Select(entry => (entry.FolderAlias, entry.WasSynchronized))]);
        Assert.Equal(synchronizedAt, result.FolderFreshness[0].SynchronizedAt);
        Assert.Null(result.FolderFreshness[1].SynchronizedAt);
    }

    [Fact]
    public async Task SearchEmailsAsync_CancelledCaller_StopsRatherThanAnsweringFromWhatItHad()
    {
        // Arrange
        var tool = ToolOver(new StubEmailSearchIndexReader(MatchWith(rank: 0.5f, snippets: ["**invoice**"])));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.SearchEmailsAsync(Query, cancellationToken: cancellation.Token));
    }

    private static EmailSearchMatch MatchWith(float rank, IReadOnlyList<string> snippets, int position = 1) => new(
        SummaryOf(EmailIdentityAt(position), new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)),
        rank,
        snippets);

    /// <summary>Names one email by its position, so the same run of a test always uses the same identifiers.</summary>
    /// <remarks>
    /// Derived rather than generated: a match's identity participates in no assertion here, but a failure has to be
    /// reproducible from the data the test names, which a fresh UUID per invocation is not.
    /// </remarks>
    private static Guid EmailIdentityAt(int position) =>
        new($"00000000-0000-0000-0000-{position:D12}");

    private static EmailSummary SummaryOf(Guid storedEmailId, DateTimeOffset receivedAt) => new()
    {
        StoredEmailId = StoredEmailId.Create(storedEmailId),
        Account = MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create(ServedAccountId)),
        FolderAlias = MailFolderAlias.Create("INBOX"),
        Subject = "Quarterly invoice",
        SenderAddress = "billing@example.test",
        ReceivedAt = receivedAt,
        SentAt = receivedAt,
        SizeOctets = 1024,
        ToAddresses = ["finance@example.test"],
        Attachments = StoredEmailAttachmentSummary.None,
        ContentAvailability = StoredEmailContentAvailability.Available,
        RemoteFlags = RemoteEmailFlagSnapshot.NeverObserved,
        SenderVerification = new SenderVerification
        {
            AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
            DeploymentTrust = SenderTrustLevel.Unknown,
        },
        SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
    };

    private static MailboxFolderFreshness FreshnessOf(string folderAlias, DateTimeOffset? synchronizedAt) => new(
        MailAccountId.Create(ServedAccountId),
        MailFolderAlias.Create(folderAlias),
        synchronizedAt);

    /// <summary>Builds the semantic half of a deployment that configured no embedding provider.</summary>
    /// <remarks>
    /// This suite is about what the protocol boundary publishes, and the retrieval mode it publishes for a
    /// lexical-only instance is one of those things. A generator here would make every assertion below depend on a
    /// provider call the boundary neither makes nor sees.
    /// </remarks>
    private static SemanticEmailSearch LexicalOnlySemanticSearch() => new(
        Substitute.For<IActiveEmbeddingProfileReader>(),
        Substitute.For<IEmailVectorSearchIndexReader>(),
        Substitute.For<IAiProviderHealthReader>(),
        new FakeTimeProvider(),
        textEmbeddingGenerator: null);

    /// <summary>Junk is mail a filter already set aside, so a search that says nothing about it leaves it out.</summary>
    [Fact]
    public async Task SearchEmailsAsync_NoAnswerAboutJunk_LeavesTheJunkFolderOutAndSaysSo()
    {
        // Arrange
        var index = new StubEmailSearchIndexReader();
        var junkFolder = new MailFolderIdentity(MailAccountId.Create(ServedAccountId), MailFolderAlias.Create("JUNK"));
        var tool = ToolOver(index, junkFolders: StubJunkMailFolderCatalog.Naming(junkFolder));

        // Act
        var result = await tool.SearchEmailsAsync(Query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(index.LastSelection);
        Assert.Equal([junkFolder], index.LastSelection.Scope.WithheldJunkFolders);
        Assert.False(result.IncludedJunkMail);
    }

    /// <summary>Somebody looking for a message a filter took asks for it, and the answer says which search they got.</summary>
    [Fact]
    public async Task SearchEmailsAsync_JunkAskedFor_SearchesItAndSaysSo()
    {
        // Arrange
        var index = new StubEmailSearchIndexReader();
        var junkFolder = new MailFolderIdentity(MailAccountId.Create(ServedAccountId), MailFolderAlias.Create("JUNK"));
        var tool = ToolOver(index, junkFolders: StubJunkMailFolderCatalog.Naming(junkFolder));

        // Act
        var result = await tool.SearchEmailsAsync(
            Query,
            includeJunkMail: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(index.LastSelection);
        Assert.Empty(index.LastSelection.Scope.WithheldJunkFolders);
        Assert.True(result.IncludedJunkMail);
    }

    private static SearchEmailsTool ToolOver(
        StubEmailSearchIndexReader index,
        StubSynchronizationFreshnessReader? freshness = null,
        EmailSearchSnippetBounds? snippetBounds = null,
        StubJunkMailFolderCatalog? junkFolders = null,
        SensitiveContentEgressGuard? egressGuard = null)
    {
        // One instance for both, as the host composes them: the use case asks the index to cut extracts by these bounds
        // and the boundary publishes what came back under the same ones.
        var bounds = snippetBounds ?? EmailSearchSnippetBounds.Default;

        return new SearchEmailsTool(
            new MailboxSearchReader(
                index,
                LexicalOnlySemanticSearch(),
                freshness ?? new StubSynchronizationFreshnessReader(),
                new MailboxScopeResolver(
                    new StubMailAccountCatalog(ServedAccountId),
                    StubMailFolderParticipation.Nothing,
                    junkFolders ?? StubJunkMailFolderCatalog.None,
                    StubMailFolderMappings.ResolvingNothing),
                bounds,
                egressGuard ?? SensitiveContentEgressGuards.Inactive(),
                Substitute.For<IMailboxReadTelemetry>(),
                AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead)),
            bounds,
            new StubMailAccountCatalog(ServedAccountId));
    }
}
