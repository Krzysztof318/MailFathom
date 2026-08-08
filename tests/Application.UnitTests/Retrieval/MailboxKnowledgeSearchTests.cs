// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval;

/// <summary>Covers the retrieval a model reaches mail through: what it bounds, what it carries, and what it refuses to send.</summary>
public sealed class MailboxKnowledgeSearchTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId[] EveryServedAccount =
    [
        MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId),
        MailAccountId.Create("secondary"),
    ];

    [Fact]
    public async Task FindPassagesAsync_MailMatchingTheQuery_ReturnsOnePassagePerMessageMostRelevantFirst()
    {
        // Arrange
        var mostRelevant = SyntheticEmailSummaries.Create(FirstJuly, subject: "the invoice");
        var lessRelevant = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1));
        var index = new InMemoryEmailSearchIndex()
            .With(lessRelevant, relevanceRank: 0.2f, matchedText: "invoice", "a later mention")
            .With(mostRelevant, relevanceRank: 0.9f, matchedText: "invoice", "the invoice is attached");
        var search = SearchOver(index);

        // Act
        var passages = await search.FindPassagesAsync(
            MailboxScope.Unrestricted,
            "invoice",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [mostRelevant.StoredEmailId, lessRelevant.StoredEmailId],
            passages.Select(passage => passage.StoredEmailId));
    }

    /// <summary>An answer that cannot say which message a claim came from cannot be checked.</summary>
    [Fact]
    public async Task FindPassagesAsync_AMatch_CarriesItsIdentityAndSourceCoordinates()
    {
        // Arrange
        var matched = SyntheticEmailSummaries.Create(
            FirstJuly,
            accountId: "secondary",
            folderAlias: "ARCHIVE",
            subject: "the invoice");
        var index = new InMemoryEmailSearchIndex().With(matched, snippets: "the invoice is attached");
        var search = SearchOver(index);

        // Act
        var passages = await search.FindPassagesAsync(
            MailboxScope.Unrestricted,
            "invoice",
            TestContext.Current.CancellationToken);

        // Assert
        var passage = Assert.Single(passages);

        Assert.Equal(matched.StoredEmailId, passage.StoredEmailId);
        Assert.Equal(MailAccountId.Create("secondary"), passage.AccountId);
        Assert.Equal(MailFolderAlias.Create("ARCHIVE"), passage.FolderAlias);
        Assert.Equal("the invoice", passage.Subject);
        Assert.Equal(FirstJuly, passage.ReceivedAt);
        Assert.Equal("the invoice is attached", passage.Text);
    }

    /// <summary>The count is the bound on how many messages one question can draw on, and it is asked of the search itself.</summary>
    [Fact]
    public async Task FindPassagesAsync_MoreMatchesThanTheBoundAllows_AsksForOnlyThatMany()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex();
        foreach (var email in SyntheticEmailSummaries.CreateDailyRun(10, FirstJuly))
        {
            index.With(email, snippets: "a mention");
        }

        var search = SearchOver(index, EmailKnowledgeBounds.Create(maximumPassages: 3, maximumCharactersPerPassage: 100));

        // Act
        var passages = await search.FindPassagesAsync(
            MailboxScope.Unrestricted,
            "invoice",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, passages.Count);
        Assert.Equal(3, Assert.Single(index.RankedCandidatesCalls).Limit);
    }

    [Fact]
    public async Task FindPassagesAsync_AMatchLongerThanOnePassageMayCarry_CutsItToTheBound()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex()
            .With(SyntheticEmailSummaries.Create(FirstJuly), snippets: new string('a', 400));
        var search = SearchOver(index, EmailKnowledgeBounds.Create(maximumPassages: 4, maximumCharactersPerPassage: 120));

        // Act
        var passages = await search.FindPassagesAsync(
            MailboxScope.Unrestricted,
            "invoice",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(120, Assert.Single(passages).Text.Length);
    }

    /// <summary>
    /// Mail carries emoji and every script outside the basic plane. A cut through a surrogate pair leaves a lone
    /// surrogate, which is not text: it survives no serialization the passage is about to cross.
    /// </summary>
    [Fact]
    public async Task FindPassagesAsync_ACutThatWouldFallInsideACharacter_TakesTheWholeCharacterInstead()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex()
            .With(SyntheticEmailSummaries.Create(FirstJuly), snippets: new string('a', 9) + "🧾🧾");
        var search = SearchOver(index, EmailKnowledgeBounds.Create(maximumPassages: 4, maximumCharactersPerPassage: 10));

        // Act
        var passages = await search.FindPassagesAsync(
            MailboxScope.Unrestricted,
            "invoice",
            TestContext.Current.CancellationToken);

        // Assert
        var text = Assert.Single(passages).Text;

        Assert.Equal(9, text.Length);
        Assert.DoesNotContain(text, char.IsSurrogate);
    }

    /// <summary>
    /// A message matched on its subject or its participants while its body yielded no text. Sending its identity with
    /// nothing beside it would spend a model's context on a message it cannot read a word of.
    /// </summary>
    [Fact]
    public async Task FindPassagesAsync_AMatchWithNoExtracts_IsNotHandedOver()
    {
        // Arrange
        var withText = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1));
        var index = new InMemoryEmailSearchIndex()
            .With(SyntheticEmailSummaries.Create(FirstJuly), relevanceRank: 0.9f)
            .With(withText, relevanceRank: 0.2f, matchedText: null, "a mention");
        var search = SearchOver(index);

        // Act
        var passages = await search.FindPassagesAsync(
            MailboxScope.Unrestricted,
            "invoice",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(withText.StoredEmailId, Assert.Single(passages).StoredEmailId);
    }

    /// <summary>
    /// The query is written by a model rather than by a caller who could be told to correct it, so unusable text is a
    /// retrieval that found nothing rather than a failed run.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FindPassagesAsync_AQueryWithNoUsableText_FindsNothingWithoutSearching(string queryText)
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex().With(SyntheticEmailSummaries.Create(FirstJuly), snippets: "a mention");
        var search = SearchOver(index);

        // Act
        var passages = await search.FindPassagesAsync(
            MailboxScope.Unrestricted,
            queryText,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(passages);
        Assert.Empty(index.RankedCandidatesCalls);
    }

    /// <summary>The scope decides which mail a question can be answered from, and it comes from the caller rather than the query.</summary>
    [Fact]
    public async Task FindPassagesAsync_AScopeNamingOneAccount_FindsNothingInAnother()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex()
            .With(SyntheticEmailSummaries.Create(FirstJuly, accountId: "secondary"), snippets: "a mention");
        var search = SearchOver(index);
        var scope = MailboxScope.Create([MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId)], []);

        // Act
        var passages = await search.FindPassagesAsync(scope, "invoice", TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(passages);
    }

    [Fact]
    public async Task FindPassagesAsync_AScopeNamingAFolder_ForwardsItToTheSearch()
    {
        // Arrange
        var inArchive = SyntheticEmailSummaries.Create(FirstJuly, folderAlias: "ARCHIVE");
        var index = new InMemoryEmailSearchIndex()
            .With(SyntheticEmailSummaries.Create(FirstJuly), snippets: "an inbox mention")
            .With(inArchive, snippets: "an archived mention");
        var search = SearchOver(index);
        var scope = MailboxScope.Create([], [MailFolderAlias.Create("ARCHIVE")]);

        // Act
        var passages = await search.FindPassagesAsync(scope, "invoice", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(inArchive.StoredEmailId, Assert.Single(passages).StoredEmailId);
    }

    private static MailboxKnowledgeSearch SearchOver(
        InMemoryEmailSearchIndex index,
        EmailKnowledgeBounds? bounds = null) => new(
        new MailboxSearchReader(
            index,
            LexicalOnlySemanticSearch(),
            FreshnessReaderReturningNothing(),
            new MailboxScopeResolver(CatalogServing(EveryServedAccount)),
            EmailSearchSnippetBounds.Default),
        bounds ?? EmailKnowledgeBounds.Default);

    /// <summary>Builds the semantic half of a deployment that configured no embedding provider.</summary>
    private static SemanticEmailSearch LexicalOnlySemanticSearch() => new(
        Substitute.For<IActiveEmbeddingProfileReader>(),
        new InMemoryEmailVectorSearchIndex(),
        textEmbeddingGenerator: null);

    private static IMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.ServedAccountIds.Returns(
        [
            .. servedAccountIds.OrderBy(accountId => accountId.Value, StringComparer.Ordinal),
        ]);

        return catalog;
    }

    private static ISynchronizationFreshnessReader FreshnessReaderReturningNothing()
    {
        var reader = Substitute.For<ISynchronizationFreshnessReader>();
        reader.ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailboxFolderFreshness>>([]));

        return reader;
    }
}
