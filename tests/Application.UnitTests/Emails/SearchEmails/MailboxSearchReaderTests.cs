// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.SearchEmails;

/// <summary>Covers the search use case: what it validates, how it ranks, how it orders, and what it bounds.</summary>
public sealed class MailboxSearchReaderTests
{
    /// <summary>The literal the scanner in the guarded-egress tests reports, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset SearchedAt = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly EmbeddingProfileId ProfileId =
        EmbeddingProfileId.Create(new Guid("6f2f1f0e-6a1a-4a2a-9d1f-0f5a1b2c3d4e"));

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
        Assert.Equal(3, Assert.Single(index.RankedCandidatesCalls).Limit);
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
        Assert.Equal(EmailSearchResultLimit.DefaultValue, Assert.Single(index.RankedCandidatesCalls).Limit);
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
        Assert.Empty(index.RankedCandidatesCalls);
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
        Assert.Empty(index.RankedCandidatesCalls);
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
                Folders = [MailFolderReference.ToAlias(MailFolderAlias.Create("ARCHIVE"))],
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
        Assert.Empty(index.RankedCandidatesCalls);
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
                RequestFor("invoice") with { Accounts = [MailAccountSelector.Create("someone-elses")] },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("someone-elses", failure.RequestedAccount.Value);
        Assert.Empty(index.RankedCandidatesCalls);
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
            Assert.Single(index.RankedCandidatesCalls).Selection.Scope.AccountIds);
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
        Assert.Empty(index.RankedCandidatesCalls);
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
        Assert.Same(configuredBounds, Assert.Single(index.MatchesCalls).SnippetBounds);
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

    /// <summary>An instance that embeds nothing answers exactly as it did before hybrid retrieval existed, and says so.</summary>
    [Fact]
    public async Task SearchEmailsAsync_NoEmbeddingProviderConfigured_RanksLexicallyAndReportsThatMode()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex().With(SyntheticEmailSummaries.Create(FirstJuly));
        var reader = ReaderOver(index);

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Lexical, result.RetrievalMode);
        Assert.Equal(EmailSearchResultLimit.DefaultValue, Assert.Single(index.RankedCandidatesCalls).Limit);
    }

    /// <summary>Mail whose words the query never used is unreachable lexically, which is the whole point of the second ranking.</summary>
    [Fact]
    public async Task SearchEmailsAsync_ActiveProfile_ReturnsMailTheLexicalRankingNeverMatched()
    {
        // Arrange
        var lexicalMatch = SyntheticEmailSummaries.Create(FirstJuly);
        var semanticMatch = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1));

        // Both are stored mail; only one of them is written in the query's words.
        var index = new InMemoryEmailSearchIndex()
            .With(lexicalMatch, matchedText: "water damage")
            .With(semanticMatch, matchedText: "the roof is leaking again");
        var vectorIndex = new InMemoryEmailVectorSearchIndex().With(semanticMatch, distance: 0.1f);

        var reader = ReaderOver(index, semanticSearch: SemanticSearchOver(vectorIndex));

        // Act
        var result = await reader.SearchEmailsAsync(
            RequestFor("water damage"),
            TestContext.Current.CancellationToken);

        // Assert: each ranking placed one email first, so the timeline tiebreaker settles the fused order.
        Assert.Equal(EmailSearchRetrievalMode.Hybrid, result.RetrievalMode);
        Assert.Equal(
            [semanticMatch.StoredEmailId, lexicalMatch.StoredEmailId],
            result.Matches.Select(match => match.Summary.StoredEmailId));
    }

    /// <summary>Fusion rewards agreement, so mail both rankings found outranks mail only one of them did.</summary>
    [Fact]
    public async Task SearchEmailsAsync_MailBothRankingsFound_OutranksMailOnlyOneOfThemFound()
    {
        // Arrange
        var agreedUpon = SyntheticEmailSummaries.Create(FirstJuly);
        var lexicalOnly = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1));
        var semanticOnly = SyntheticEmailSummaries.Create(FirstJuly.AddDays(2));
        var index = new InMemoryEmailSearchIndex()
            .With(lexicalOnly, relevanceRank: 0.9f)
            .With(agreedUpon, relevanceRank: 0.8f)
            .With(semanticOnly, matchedText: "nothing this query asks about");
        var vectorIndex = new InMemoryEmailVectorSearchIndex()
            .With(semanticOnly, distance: 0.1f)
            .With(agreedUpon, distance: 0.2f);

        var reader = ReaderOver(index, semanticSearch: SemanticSearchOver(vectorIndex));

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(agreedUpon.StoredEmailId, result.Matches[0].Summary.StoredEmailId);
    }

    /// <summary>Fusion needs more than the window from each side, or a message either ranking placed late never scores twice.</summary>
    [Fact]
    public async Task SearchEmailsAsync_HybridRetrieval_AsksBothRankingsPastTheWindowItReturns()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex();
        var vectorIndex = new InMemoryEmailVectorSearchIndex();
        var reader = ReaderOver(index, semanticSearch: SemanticSearchOver(vectorIndex));

        // Act
        var result = await reader.SearchEmailsAsync(
            RequestFor("invoice") with { ResultLimit = 5 },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Hybrid, result.RetrievalMode);
        Assert.True(Assert.Single(index.RankedCandidatesCalls).Limit > 5);
        Assert.Equal(
            Assert.Single(index.RankedCandidatesCalls).Limit,
            Assert.Single(vectorIndex.Calls).Limit);
    }

    /// <summary>The fused window is still bounded by what the caller asked for, however deep the two rankings reached.</summary>
    [Fact]
    public async Task SearchEmailsAsync_HybridRetrieval_ReturnsNoMoreThanTheRequestedCount()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex();
        var vectorIndex = new InMemoryEmailVectorSearchIndex();
        var distance = 0.1f;
        foreach (var email in SyntheticEmailSummaries.CreateDailyRun(10, FirstJuly))
        {
            index.With(email);
            vectorIndex.With(email, distance += 0.01f);
        }

        var reader = ReaderOver(index, semanticSearch: SemanticSearchOver(vectorIndex));

        // Act
        var result = await reader.SearchEmailsAsync(
            RequestFor("invoice") with { ResultLimit = 3 },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Matches.Count);
    }

    /// <summary>An unreachable provider costs a search its second ranking and nothing else, which the mode reports honestly.</summary>
    [Fact]
    public async Task SearchEmailsAsync_ProviderFailure_FallsBackToTheLexicalRankingAndReportsIt()
    {
        // Arrange
        var matched = SyntheticEmailSummaries.Create(FirstJuly);
        var index = new InMemoryEmailSearchIndex().With(matched);
        var generator = GeneratorOf(ProfileIdentity());
        generator.Failure = EmbeddingGenerationFailure.TransportFaulted;

        var reader = ReaderOver(
            index,
            semanticSearch: SemanticSearchOver(new InMemoryEmailVectorSearchIndex(), generator));

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Lexical, result.RetrievalMode);
        Assert.Equal(SemanticSearchCapability.Degraded, result.SemanticSearch);
        Assert.Equal(matched.StoredEmailId, Assert.Single(result.Matches).Summary.StoredEmailId);
    }

    /// <summary>
    /// The mode alone cannot separate a deployment that never embeds from one whose provider just refused. Both answer
    /// lexically, and only the capability says which of the two the caller is looking at.
    /// </summary>
    [Fact]
    public async Task SearchEmailsAsync_NoEmbeddingProviderConfigured_ReportsSemanticRetrievalInactive()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex().With(SyntheticEmailSummaries.Create(FirstJuly));
        var reader = ReaderOver(index);

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Lexical, result.RetrievalMode);
        Assert.Equal(SemanticSearchCapability.Inactive, result.SemanticSearch);
    }

    /// <summary>A hybrid instance says so, which is what lets a caller read a lexical answer from it as an exception.</summary>
    [Fact]
    public async Task SearchEmailsAsync_AnActiveProfileAndAServingProvider_ReportsSemanticRetrievalAvailable()
    {
        // Arrange
        var matched = SyntheticEmailSummaries.Create(FirstJuly);
        var index = new InMemoryEmailSearchIndex().With(matched);
        var vectorIndex = new InMemoryEmailVectorSearchIndex().With(matched, distance: 0.1f);
        var reader = ReaderOver(index, semanticSearch: SemanticSearchOver(vectorIndex));

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Hybrid, result.RetrievalMode);
        Assert.Equal(SemanticSearchCapability.Available, result.SemanticSearch);
    }

    /// <summary>A window is one of the points mail content leaves the deployment, so what it publishes is scanned first.</summary>
    [Fact]
    public async Task SearchEmailsAsync_ASwitchedOnScanner_RedactsTheSubjectAndTheSnippetsBeforeTheyArePublished()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var matched = SyntheticEmailSummaries.Create(FirstJuly, subject: $"the key is {Marker}");
        var index = new InMemoryEmailSearchIndex().With(matched, 0.9f, "invoice", $"use {Marker} to sign in");
        var reader = ReaderOver(index, egressGuard: egress.Guard);

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        var match = Assert.Single(result.Matches);
        Assert.Equal("the key is [redacted:CloudKey]", match.Summary.Subject);
        Assert.Equal(["use [redacted:CloudKey] to sign in"], match.Snippets);
    }

    /// <summary>
    /// The name in front of an address is free text whoever sent the message chose, so it is scanned like the subject.
    /// The address beside it is a routing identity a server issued and is left alone, because a reply has to reach it.
    /// </summary>
    [Fact]
    public async Task SearchEmailsAsync_ASwitchedOnScanner_RedactsTheSenderDisplayNameAndLeavesTheAddress()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var matched = SyntheticEmailSummaries.Create(
            FirstJuly,
            subject: "an ordinary subject",
            senderAddress: "sender@example.test") with
        {
            SenderDisplayName = $"deploy bot {Marker}",
        };
        var index = new InMemoryEmailSearchIndex().With(matched, 0.9f, "invoice", "an ordinary extract");
        var reader = ReaderOver(index, egressGuard: egress.Guard);

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        var published = Assert.Single(result.Matches).Summary;

        Assert.Equal("deploy bot [redacted:CloudKey]", published.SenderDisplayName);
        Assert.Equal("sender@example.test", published.SenderAddress);
    }

    /// <summary>Serving a window a scanner could not read would be the leak the switch was turned on to prevent.</summary>
    [Fact]
    public async Task SearchEmailsAsync_ADetectorThatCannotAnswer_RefusesTheSearchRatherThanServingItUnscanned()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(TimeProvider.System);
        var index = new InMemoryEmailSearchIndex()
            .With(SyntheticEmailSummaries.Create(FirstJuly, subject: "an ordinary subject"), 0.9f, "invoice");
        var reader = ReaderOver(index, egressGuard: egress.Guard);

        // Act, Assert
        await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken));
    }

    /// <summary>An opt-in nobody took must not appear on this path at all.</summary>
    [Fact]
    public async Task SearchEmailsAsync_ADeploymentThatScansNothing_PublishesTheWindowUntouched()
    {
        // Arrange
        var matched = SyntheticEmailSummaries.Create(FirstJuly, subject: $"the key is {Marker}");
        var index = new InMemoryEmailSearchIndex().With(matched, 0.9f, "invoice", $"use {Marker} to sign in");
        var reader = ReaderOver(index);

        // Act
        var result = await reader.SearchEmailsAsync(RequestFor("invoice"), TestContext.Current.CancellationToken);

        // Assert
        var match = Assert.Single(result.Matches);
        Assert.Equal($"the key is {Marker}", match.Summary.Subject);
        Assert.Equal([$"use {Marker} to sign in"], match.Snippets);
    }

    private static SearchEmailsRequest RequestFor(string? queryText) => new() { QueryText = queryText };

    private static MailboxSearchReader ReaderOver(
        InMemoryEmailSearchIndex index,
        IMailAccountCatalog? accountCatalog = null,
        EmailSearchSnippetBounds? snippetBounds = null,
        SemanticEmailSearch? semanticSearch = null,
        SensitiveContentEgressGuard? egressGuard = null) => new(
        index,
        semanticSearch ?? LexicalOnlySemanticSearch(),
        FreshnessReaderReturning(InboxFreshness),
        new MailboxScopeResolver(
            accountCatalog ?? CatalogServing(EveryAccountTheSyntheticIndexUses),
            StubMailFolderParticipation.Everything,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing),
        snippetBounds ?? EmailSearchSnippetBounds.Default,
        egressGuard ?? SensitiveContentEgressGuards.Inactive());

    /// <summary>Builds the semantic half of a deployment that configured no embedding provider and activated nothing.</summary>
    private static SemanticEmailSearch LexicalOnlySemanticSearch() => new(
        Substitute.For<IActiveEmbeddingProfileReader>(),
        new InMemoryEmailVectorSearchIndex(),
        ServingProviderHealth(),
        new FakeTimeProvider(SearchedAt),
        textEmbeddingGenerator: null);

    /// <summary>Builds the semantic half of a deployment that has activated a profile its generator agrees with.</summary>
    private static SemanticEmailSearch SemanticSearchOver(
        InMemoryEmailVectorSearchIndex vectorIndex,
        ScriptedTextEmbeddingGenerator? generator = null)
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
            generator ?? GeneratorOf(identity));
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

    private static ScriptedTextEmbeddingGenerator GeneratorOf(EmbeddingProfileIdentity identity) =>
        new(identity, maximumPassagesPerCall: 8);

    private static EmbeddingProfileIdentity ProfileIdentity() => EmbeddingProfileIdentity.Create(
        "a-provider",
        "a-model",
        modelVersion: null,
        dimension: 8,
        EmbeddingDistanceMetric.Cosine,
        EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    /// <summary>Builds a catalog that serves exactly the accounts named, in the order the port promises.</summary>
    private static IMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.ServedAccounts.Returns(
        [
            .. servedAccountIds
                .OrderBy(accountId => accountId.Value, StringComparer.Ordinal)
                .Select(SyntheticServedAccount.Of),
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
