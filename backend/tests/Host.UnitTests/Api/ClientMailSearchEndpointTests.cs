// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.BrowseSearch;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the mail search route accepts, what it refuses, and what it puts on the wire.</summary>
/// <remarks>
/// The ranking, the paging, and the extracts are covered where they are decided. What is asserted here is the
/// transport: that a value this deployment cannot honour is refused rather than ignored, that each refusal says which
/// one it is, and that a result reaches the wire carrying both the row a screen draws and why the row is there.
/// </remarks>
public sealed class ClientMailSearchEndpointTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly IEmailSearchIndexReader index = Substitute.For<IEmailSearchIndexReader>();

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void MailSearchRoute_IsThePathAClientComposes() =>
        Assert.Equal("/emails/search", ClientMailSearchEndpoint.MailSearchRoute);

    /// <summary>A search with no text is a list, and answering it here would rank the whole mailbox against nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_ARequestCarryingNoQuery_IsRefused(string? query)
    {
        // Act
        var result = await this.SearchAsync(query);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A name no account or folder of this deployment is spelled with is refused rather than searched for.</summary>
    [Theory]
    [InlineData("finance", null)]
    [InlineData(null, "role:")]
    public async Task SearchAsync_AScopeNamingAValueThisDeploymentDoesNotIssue_IsRefused(string? account, string? folder)
    {
        // Act
        var result = await this.SearchAsync("invoice", account: account, folder: folder);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A page size outside the accepted range is refused rather than clamped, so a client is never served a page it did not ask for.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(EmailSearchResultLimit.MaximumValue + 1)]
    public async Task SearchAsync_APageSizeOutsideTheAcceptedRange_IsRefused(int pageSize)
    {
        // Act
        var result = await this.SearchAsync("invoice", pageSize: pageSize);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A cursor this deployment never issued and one issued for a different search are two mistakes with two repairs, so they read differently.</summary>
    [Fact]
    public async Task SearchAsync_ACursorThisDeploymentNeverIssued_SaysSoRatherThanAnsweringFromTheTop()
    {
        // Act
        var result = await this.SearchAsync("invoice", cursor: "a cursor nobody issued");

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Equal("The cursor is not one this deployment issued.", refusal.ProblemDetails.Detail);
    }

    /// <summary>A filter the screen has nothing to put in yet arrives empty, which is the same request as one that omits it.</summary>
    [Fact]
    public async Task SearchAsync_AnEmptyFilterParameter_IsTheSameRequestAsOneThatOmitsIt()
    {
        // Act
        var result = await this.SearchAsync("invoice", account: "", folder: "", sender: "");

        // Assert
        Assert.IsType<Ok<ClientMailSearchResponse>>(result.Result);
    }

    /// <summary>What a result carries on the wire: the row a list draws, the extracts, and which ranking found it.</summary>
    [Fact]
    public async Task SearchAsync_AMatchedMessage_PutsTheRowTheExtractsAndTheOriginOnTheWire()
    {
        // Act
        var result = await this.SearchAsync("invoice");

        // Assert
        var page = Assert.IsType<Ok<ClientMailSearchResponse>>(result.Result).Value;
        var row = Assert.Single(page!.Results);
        Assert.Equal("work", row.Account);
        Assert.Equal("INBOX", row.Folder);
        Assert.Equal("the invoice", row.Subject);
        Assert.True(row.Unread);
        Assert.Equal(["the **invoice** is attached"], row.Snippets);
        Assert.Equal(nameof(SearchMatchOrigin.LexicalRanking), row.MatchedBy);
    }

    /// <summary>How a page was ranked and what the instance can do are published, which is what keeps a narrower answer from being a quieter one.</summary>
    [Fact]
    public async Task SearchAsync_AnInstanceThatEmbedsNothing_PublishesTheModeAndTheCapabilityItAnsweredUnder()
    {
        // Act
        var result = await this.SearchAsync("invoice");

        // Assert
        var page = Assert.IsType<Ok<ClientMailSearchResponse>>(result.Result).Value;
        Assert.Equal(nameof(EmailSearchRetrievalMode.Lexical), page!.RetrievalMode);
        Assert.Equal(nameof(SemanticSearchCapability.Inactive), page.SemanticSearch);
        Assert.False(page.IncludedJunkMail);
    }

    /// <summary>The page the use case read reaches the wire whole, cursor and page size included.</summary>
    [Fact]
    public void For_APageTheUseCaseRead_DescribesItWhole()
    {
        // Arrange
        var page = new BrowsedSearchPage(
            [new BrowsedSearchResult(SyntheticMatchedEmail(), "the invoice is attached", ["**invoice**"], SearchMatchOrigin.BothRankings)],
            NextCursor: "after",
            PageSize: 20,
            EmailSearchRetrievalMode.Hybrid,
            SemanticSearchCapability.Available,
            IncludedJunkMail: true);

        // Act
        var response = ClientMailSearchResponse.For(page);

        // Assert
        Assert.Equal("after", response.NextCursor);
        Assert.Equal(20, response.PageSize);
        Assert.Equal(nameof(EmailSearchRetrievalMode.Hybrid), response.RetrievalMode);
        Assert.Equal(nameof(SemanticSearchCapability.Available), response.SemanticSearch);
        Assert.True(response.IncludedJunkMail);
        Assert.Equal(nameof(SearchMatchOrigin.BothRankings), Assert.Single(response.Results).MatchedBy);
    }

    private static EmailSummary SyntheticMatchedEmail() => new()
    {
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
        Account = MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work")),
        FolderAlias = MailFolderAlias.Create("INBOX"),
        Subject = "the invoice",
        SentAt = FirstJuly,
        ReceivedAt = FirstJuly,
        SizeOctets = 2048,
        SenderAddress = "sender@example.test",
        ToAddresses = ["someone@example.test"],
        SenderVerification = SenderVerification.NotEstablished,
        SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Attachments = new StoredEmailAttachmentSummary(
            AttachmentCount: 0,
            TotalSizeOctets: 0,
            InlineResourceCount: 0,
            IsEncrypted: false,
            CarriesUnverifiedSignature: false,
            ContainsUnexpandedTnefPart: false),
        ContentAvailability = StoredEmailContentAvailability.Available,
        RemoteFlags = new RemoteEmailFlagSnapshot(
            FirstJuly,
            IsSeen: false,
            IsAnswered: false,
            IsFlagged: false,
            IsDraft: false,
            IsDeleted: false,
            RemoteEmailKeywords.None),
    };

    private Task<Results<Ok<ClientMailSearchResponse>, ProblemHttpResult>> SearchAsync(
        string? query,
        string? account = null,
        string? folder = null,
        string? sender = null,
        int? pageSize = null,
        string? cursor = null)
    {
        var matched = SyntheticMatchedEmail();

        this.index
            .ReadRankedCandidatesAsync(
                Arg.Any<MailboxEmailSelection>(),
                Arg.Any<EmailSearchQueryText>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RankedEmailCandidate>>(
                [new RankedEmailCandidate(matched.Position, 0.9f)]));

        this.index
            .ReadMatchesAsync(
                Arg.Any<MailboxEmailSelection>(),
                Arg.Any<EmailSearchQueryText>(),
                Arg.Any<EmailSearchSnippetBounds>(),
                Arg.Any<IReadOnlyList<RankedEmailCandidate>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmailSearchMatch>>(
                [new EmailSearchMatch(matched, 0.9f, ["the **invoice** is attached"])]));

        return ClientMailSearchEndpoint.SearchAsync(
            query,
            account,
            folder,
            includeJunk: null,
            sender,
            recipient: null,
            unread: null,
            flagged: null,
            hasAttachments: null,
            receivedOnOrAfter: null,
            receivedBefore: null,
            pageSize,
            cursor,
            this.Browser(),
            TestContext.Current.CancellationToken);
    }

    /// <summary>Builds the use case behind the route over the real scope resolution, with storage and the instruments stood in for.</summary>
    private MailSearchBrowser Browser()
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([SyntheticServedAccount.Of(MailAccountId.Create("work"))]);

        var readTelemetry = Substitute.For<IMailboxReadTelemetry>();
        readTelemetry.BeginRead(Arg.Any<MailboxReadOperation>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IMailboxReadScope>());
        readTelemetry.BeginSearchRanking(Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IMailboxReadScope>());

        var previews = Substitute.For<IStoredEmailPreviewReader>();
        previews.ReadPreviewsAsync(Arg.Any<IReadOnlyList<StoredEmailId>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<StoredEmailId, string>>(
                new Dictionary<StoredEmailId, string>()));

        return new MailSearchBrowser(
            this.index,
            LexicalOnlySemanticSearch(),
            previews,
            new MailboxScopeResolver(
                catalog,
                StubMailFolderParticipation.Nothing,
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            EmailSearchSnippetBounds.Default,
            SensitiveContentEgressGuards.Inactive(),
            readTelemetry,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));
    }

    /// <summary>Builds the semantic half of a deployment that configured no embedding provider and activated nothing.</summary>
    private static SemanticEmailSearch LexicalOnlySemanticSearch()
    {
        var providerHealth = Substitute.For<IAiProviderHealthReader>();
        providerHealth.Read(Arg.Any<AiProviderRole>())
            .Returns(call => new AiProviderHealth(call.Arg<AiProviderRole>(), AiProviderHealthState.Serving, FirstJuly));

        return new SemanticEmailSearch(
            Substitute.For<IActiveEmbeddingProfileReader>(),
            Substitute.For<IEmailVectorSearchIndexReader>(),
            providerHealth,
            new FakeTimeProvider(FirstJuly),
            textEmbeddingGenerator: null);
    }
}
