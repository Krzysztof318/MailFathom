// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Accounts;
using MailMcp.Application.Emails;
using MailMcp.Application.Emails.SearchEmails;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using NSubstitute;
using Xunit;

namespace MailMcp.Application.UnitTests;

/// <summary>Covers the lexical search use case: what it validates, how it orders, and what it bounds.</summary>
public sealed class MailboxSearchReaderTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId[] EveryAccountTheSyntheticIndexUses =
    [
        MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId),
        MailAccountId.Create("secondary"),
    ];

    private static readonly MailboxFolderFreshness InboxFreshness = new(
        MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId),
        MailFolderAlias.Create(SyntheticEmailSummaries.DefaultFolderAlias),
        new DateTimeOffset(2026, 7, 30, 6, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task SearchEmailsAsync_TextMatchingSomeEmails_ReturnsThoseRankedMostRelevantFirst()
    {
        // Arrange
        var mostRelevant = SyntheticEmailSummaries.Create(FirstJuly);
        var lessRelevant = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1));
        var unrelated = SyntheticEmailSummaries.Create(FirstJuly.AddDays(2));
        var index = new InMemoryEmailSearchIndex()
            .With(lessRelevant, relevanceRank: 0.2f, matchedText: "invoice")
            .With(mostRelevant, relevanceRank: 0.9f, matchedText: "invoice")
            .With(unrelated, relevanceRank: 0.9f, matchedText: "holiday");
        var reader = ReaderOver(index);

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [mostRelevant.StoredEmailId, lessRelevant.StoredEmailId],
            result.Matches.Select(match => match.Summary.StoredEmailId));
    }

    /// <summary>Rank alone ties, and an unbroken tie makes two identical requests disagree about what matched best.</summary>
    [Fact]
    public async Task SearchEmailsAsync_EmailsSharingOneRank_OrdersThemByTheTimelineTiebreaker()
    {
        // Arrange
        var older = SyntheticEmailSummaries.Create(FirstJuly);
        var newer = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1));
        var index = new InMemoryEmailSearchIndex()
            .With(older, relevanceRank: 0.5f)
            .With(newer, relevanceRank: 0.5f);
        var reader = ReaderOver(index);

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [newer.StoredEmailId, older.StoredEmailId],
            result.Matches.Select(match => match.Summary.StoredEmailId));
    }

    /// <summary>A window has no cursor, so its bound is the only thing that closes it.</summary>
    [Fact]
    public async Task SearchEmailsAsync_MoreMatchesThanTheRequestedCount_ReturnsOnlyThatMany()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex();
        foreach (var email in SyntheticEmailSummaries.CreateDailyRun(10, FirstJuly))
        {
            index.With(email);
        }

        var reader = ReaderOver(index);

        // Act
        var result = await reader.SearchEmailsAsync(
            RequestFor("invoice") with { ResultLimit = 3 },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Matches.Count);
        Assert.Equal(3, Assert.Single(index.Calls).Limit);
    }

    [Fact]
    public async Task SearchEmailsAsync_NoResultCountNamed_AsksForTheDefaultWindow()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex();
        var reader = ReaderOver(index);

        // Act
        await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSearchResultLimit.DefaultValue, Assert.Single(index.Calls).Limit);
    }

    /// <summary>Refused rather than clamped: a clamped window looks exactly like the one the caller asked for.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(EmailSearchResultLimit.MaximumValue + 1)]
    public async Task SearchEmailsAsync_ResultCountOutsideTheAcceptedRange_IsRejectedWithoutReadingAnything(
        int requestedResultLimit)
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex();
        var reader = ReaderOver(index);

        // Act
        var failure = await Assert.ThrowsAsync<EmailSearchResultLimitOutOfRangeException>(() =>
            reader.SearchEmailsAsync(
                RequestFor("invoice") with { ResultLimit = requestedResultLimit },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(requestedResultLimit, failure.RequestedResultLimit);
        Assert.Empty(index.Calls);
    }

    /// <summary>A search with no text is a listing, and answering it here would return an arbitrary ranked window.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchEmailsAsync_BlankQueryText_IsRejectedWithoutReadingAnything(string? queryText)
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex();
        var reader = ReaderOver(index);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(() =>
            reader.SearchEmailsAsync(RequestFor(queryText), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("search query", failure.FilterName);
        Assert.Empty(index.Calls);
    }

    /// <summary>Nothing matching is an answer, not a failure, so search cannot be used to probe for what exists.</summary>
    [Fact]
    public async Task SearchEmailsAsync_TextMatchingNothing_ReturnsAnEmptyWindowWithFreshness()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex()
            .With(SyntheticEmailSummaries.Create(FirstJuly), matchedText: "holiday");
        var reader = ReaderOver(index);

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Matches);
        Assert.Equal([InboxFreshness], result.FolderFreshness);
    }

    /// <summary>The structured filters narrow which emails are eligible before any of them is ranked.</summary>
    [Fact]
    public async Task SearchEmailsAsync_TextCombinedWithStructuredFilters_ReturnsOnlyEmailsMeetingBoth()
    {
        // Arrange
        var wanted = SyntheticEmailSummaries.Create(
            FirstJuly,
            folderAlias: "ARCHIVE",
            senderAddress: "anna@example.test",
            attachmentCount: 1);
        var wrongFolder = SyntheticEmailSummaries.Create(FirstJuly, senderAddress: "anna@example.test", attachmentCount: 1);
        var wrongSender = SyntheticEmailSummaries.Create(
            FirstJuly,
            folderAlias: "ARCHIVE",
            senderAddress: "bob@example.test",
            attachmentCount: 1);
        var noAttachment = SyntheticEmailSummaries.Create(
            FirstJuly,
            folderAlias: "ARCHIVE",
            senderAddress: "anna@example.test");
        var index = new InMemoryEmailSearchIndex()
            .With(wanted)
            .With(wrongFolder)
            .With(wrongSender)
            .With(noAttachment);
        var reader = ReaderOver(index);

        // Act
        var result = await reader.SearchEmailsAsync(
            RequestFor("invoice") with
            {
                FolderAliases = [MailFolderAlias.Create("ARCHIVE")],
                SenderAddress = "Anna@Example.test",
                HasAttachments = true,
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(wanted.StoredEmailId, Assert.Single(result.Matches).Summary.StoredEmailId);
    }

    /// <summary>An unusable filter is refused for the reason a listing refuses one: an empty window would read as an answer.</summary>
    [Fact]
    public async Task SearchEmailsAsync_UnusableSenderFilter_IsRejectedWithoutReadingAnything()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex();
        var reader = ReaderOver(index);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(() =>
            reader.SearchEmailsAsync(
                RequestFor("invoice") with { SenderAddress = "not-an-address" },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("sender address", failure.FilterName);
        Assert.Empty(index.Calls);
    }

    /// <summary>An empty window would confirm the identifier, so an account nobody serves is refused instead.</summary>
    [Fact]
    public async Task SearchEmailsAsync_AccountThisDeploymentDoesNotServe_IsRejectedWithoutReadingAnything()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex();
        var reader = ReaderOver(index, CatalogServing(MailAccountId.Create("primary")));

        // Act
        var failure = await Assert.ThrowsAsync<MailAccountNotAccessibleException>(() =>
            reader.SearchEmailsAsync(
                RequestFor("invoice") with { AccountIds = [MailAccountId.Create("someone-elses")] },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("someone-elses", failure.AccountId.Value);
        Assert.Empty(index.Calls);
    }

    /// <summary>Removing an account from configuration leaves its rows stored, so an unscoped search must not reach them.</summary>
    [Fact]
    public async Task SearchEmailsAsync_NoAccountNamed_SearchesOnlyTheAccountsThisDeploymentServes()
    {
        // Arrange
        var served = SyntheticEmailSummaries.Create(FirstJuly, accountId: "primary");
        var retired = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1), accountId: "retired");
        var index = new InMemoryEmailSearchIndex().With(served).With(retired);
        var reader = ReaderOver(index, CatalogServing(MailAccountId.Create("primary")));

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(served.StoredEmailId, Assert.Single(result.Matches).Summary.StoredEmailId);
        Assert.Equal(
            [MailAccountId.Create("primary")],
            Assert.Single(index.Calls).Selection.Scope.AccountIds);
    }

    /// <summary>Every filter is validated first, so a refusal never depends on how many accounts happen to be configured.</summary>
    [Fact]
    public async Task SearchEmailsAsync_NoAccountServedAtAll_ReturnsAnEmptyWindowWithoutReadingAnything()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex().With(SyntheticEmailSummaries.Create(FirstJuly));
        var reader = ReaderOver(index, CatalogServing());

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Matches);
        Assert.Empty(result.FolderFreshness);
        Assert.Empty(index.Calls);
    }

    /// <summary>The bounds are a deployment control, so the use case supplies them rather than the request.</summary>
    [Fact]
    public async Task SearchEmailsAsync_AnyRequest_AppliesTheConfiguredSnippetBounds()
    {
        // Arrange
        var configuredBounds = EmailSearchSnippetBounds.Create(snippetsPerEmail: 2, wordsPerSnippet: 12);
        var index = new InMemoryEmailSearchIndex();
        var reader = ReaderOver(index, snippetBounds: configuredBounds);

        // Act
        await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(configuredBounds, Assert.Single(index.Calls).SnippetBounds);
    }

    /// <summary>A search is answered from the local copy, so a folder nobody has synchronized has to be visible as such.</summary>
    [Fact]
    public async Task SearchEmailsAsync_AnyRequest_ReportsHowCurrentTheLocalCopyIs()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex().With(SyntheticEmailSummaries.Create(FirstJuly));
        var reader = ReaderOver(index);

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([InboxFreshness], result.FolderFreshness);
    }

    [Fact]
    public async Task SearchEmailsAsync_NoRequest_IsRejected()
    {
        // Arrange
        var reader = ReaderOver(new InMemoryEmailSearchIndex());

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            reader.SearchEmailsAsync(null!, TestContext.Current.CancellationToken));
    }

    private static SearchEmailsRequest RequestFor(string? queryText) => new() { QueryText = queryText };

    private static MailboxSearchReader ReaderOver(
        InMemoryEmailSearchIndex index,
        IMailAccountCatalog? accountCatalog = null,
        EmailSearchSnippetBounds? snippetBounds = null) => new(
        index,
        FreshnessReaderReturning(InboxFreshness),
        new MailboxScopeResolver(accountCatalog ?? CatalogServing(EveryAccountTheSyntheticIndexUses)),
        snippetBounds ?? EmailSearchSnippetBounds.Default);

    /// <summary>Builds a catalog that serves exactly the accounts named, in the order the port promises.</summary>
    private static IMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.ServedAccountIds.Returns(
        [
            .. servedAccountIds.OrderBy(accountId => accountId.Value, StringComparer.Ordinal),
        ]);

        return catalog;
    }

    private static ISynchronizationFreshnessReader FreshnessReaderReturning(params MailboxFolderFreshness[] freshness)
    {
        var reader = Substitute.For<ISynchronizationFreshnessReader>();
        reader.ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailboxFolderFreshness>>(freshness));

        return reader;
    }
}
