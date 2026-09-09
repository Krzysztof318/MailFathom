// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Search.Phrasing;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Host.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the phrase-reading route accepts, what it refuses, and what a deployment that reads no sentence answers.</summary>
/// <remarks>
/// The reading itself is covered where it is decided. What is asserted here is the transport: that a value this
/// deployment cannot honour is refused rather than guessed at, that a refusal never quotes back what somebody typed,
/// and that a client searching by words is never told it made a mistake.
/// </remarks>
public sealed class ClientMailSearchPhraseEndpointTests
{
    private const string AskedOn = "2026-09-09";

    private readonly IMailSearchPhraseReader reader = Substitute.For<IMailSearchPhraseReader>();

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void MailSearchPhrasingRoute_IsThePathAClientComposes() =>
        Assert.Equal("/emails/search/phrasing", ClientMailSearchPhraseEndpoint.MailSearchPhrasingRoute);

    /// <summary>The field may promise a description only where something reads one, which is what this answers before anybody types.</summary>
    [Fact]
    public void ReadsPhrases_ADeploymentThatReadsASentence_SaysSo()
    {
        // Act
        var result = ClientMailSearchPhraseEndpoint.ReadsPhrases(this.reader);

        // Assert
        Assert.True(result.Value?.ReadsPhrases);
    }

    /// <summary>No endpoint and an operator who turned it off are one answer, so no configuration is published to a browser.</summary>
    [Fact]
    public void ReadsPhrases_ADeploymentThatReadsNone_SaysSoWithoutSayingWhy()
    {
        // Act
        var result = ClientMailSearchPhraseEndpoint.ReadsPhrases(reader: null);

        // Assert
        Assert.False(result.Value?.ReadsPhrases);
    }

    [Fact]
    public async Task ReadPhraseAsync_ARequestWithNoBodyAtAll_IsRefused()
    {
        // Act
        var result = await ClientMailSearchPhraseEndpoint.ReadPhraseAsync(
            request: null,
            this.reader,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A day read the wrong way round narrows a search to months nobody asked for, so only one shape is accepted.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("09/09/2026")]
    [InlineData("2026-13-01")]
    public async Task ReadPhraseAsync_ADayThatIsNotTheOneShapeThisRouteAccepts_IsRefused(string? askedOn)
    {
        // Act
        var result = await this.ReadAsync("unread mail about the invoice", askedOn);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A sentence carrying nothing to read is a search this route cannot make, and it is refused as the search route refuses one.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReadPhraseAsync_ARequestCarryingNoSentence_IsRefused(string? phrase)
    {
        // Act
        var result = await this.ReadAsync(phrase, AskedOn);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task ReadPhraseAsync_ASentenceLongerThanASearchMayCarry_IsRefused()
    {
        // Act
        var result = await this.ReadAsync(new string('a', EmailSearchQueryText.MaximumLength + 1), AskedOn);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A sentence is the most revealing value this surface carries, and a problem detail is what reaches a log by default.</summary>
    [Fact]
    public async Task ReadPhraseAsync_ARefusedSentence_SaysWhatToChangeWithoutQuotingWhatWasTyped()
    {
        // Arrange
        var overLong = new string('z', EmailSearchQueryText.MaximumLength + 1);

        // Act
        var result = await this.ReadAsync(overLong, AskedOn);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.DoesNotContain(overLong, refusal.ProblemDetails.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>A client searching by words has made no mistake, so a deployment that reads no sentence answers rather than refusing.</summary>
    [Fact]
    public async Task ReadPhraseAsync_ADeploymentThatReadsNoSentence_AnswersThePlainWordSearchRatherThanAFailure()
    {
        // Act
        var result = await ClientMailSearchPhraseEndpoint.ReadPhraseAsync(
            new ClientMailSearchPhraseRequest("unread mail about the invoice", AskedOn),
            reader: null,
            TestContext.Current.CancellationToken);

        // Assert
        var answer = Assert.IsType<Ok<ClientMailSearchPhraseResponse>>(result.Result).Value;
        Assert.False(answer?.Read);
        Assert.Empty(answer?.Criteria ?? []);
    }

    /// <summary>What a reading puts on the wire: the constraints as days a screen can draw, the criteria, and what was left over.</summary>
    [Fact]
    public async Task ReadPhraseAsync_ASentenceThatWasRead_PutsTheFiltersTheCriteriaAndWhatWasLeftOverOnTheWire()
    {
        // Arrange
        this.reader
            .ReadAsync(Arg.Any<MailSearchPhrase>(), Arg.Any<CancellationToken>())
            .Returns(new MailSearchPhraseReading(
                new MailSearchPhraseFilters
                {
                    SenderAddress = "sales@example.test",
                    ReceivedFrom = new DateOnly(2026, 8, 1),
                    ReceivedTo = new DateOnly(2026, 8, 31),
                    Unread = true,
                },
                ["racking quotation"],
                "urgent",
                WasRead: true));

        // Act
        var result = await this.ReadAsync("unread mail from sales about racking last month, urgent", AskedOn);

        // Assert
        var answer = Assert.IsType<Ok<ClientMailSearchPhraseResponse>>(result.Result).Value;
        Assert.True(answer?.Read);
        Assert.Equal("sales@example.test", answer?.Filters.Sender);
        Assert.Equal("2026-08-01", answer?.Filters.ReceivedFrom);
        Assert.Equal("2026-08-31", answer?.Filters.ReceivedTo);
        Assert.True(answer?.Filters.Unread);
        Assert.Equal(["racking quotation"], answer?.Criteria);
        Assert.Equal("urgent", answer?.Unaccounted);
    }

    /// <summary>The day the client is standing on is what relative time is resolved against, so it reaches the reading unchanged.</summary>
    [Fact]
    public async Task ReadPhraseAsync_ASentence_ReadsItAgainstTheDayTheClientStatedRatherThanTheDeploymentsOwn()
    {
        // Arrange
        this.reader
            .ReadAsync(Arg.Any<MailSearchPhrase>(), Arg.Any<CancellationToken>())
            .Returns(MailSearchPhraseReading.Nothing);

        // Act
        await this.ReadAsync("mail from last week", AskedOn);

        // Assert
        await this.reader
            .Received(1)
            .ReadAsync(
                Arg.Is<MailSearchPhrase>(phrase => phrase != null && phrase.AskedOn == new DateOnly(2026, 9, 9)),
                Arg.Any<CancellationToken>());
    }

    /// <summary>A ceiling travels rather than falling back, or a person goes on typing sentences the operator already declined to pay for.</summary>
    [Fact]
    public async Task ReadPhraseAsync_ADeploymentThatHasSpentWhatItAllows_SaysSoRatherThanAnsweringAPlainSearch()
    {
        // Arrange
        this.reader
            .ReadAsync(Arg.Any<MailSearchPhrase>(), Arg.Any<CancellationToken>())
            .Returns<MailSearchPhraseReading>(_ => throw MailAnsweringBudgetExhaustedException.PeriodSpent());

        // Act
        var result = await this.ReadAsync("unread mail about the invoice", AskedOn);

        // Assert
        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    private Task<Results<Ok<ClientMailSearchPhraseResponse>, ProblemHttpResult>> ReadAsync(
        string? phrase,
        string? askedOn) =>
        ClientMailSearchPhraseEndpoint.ReadPhraseAsync(
            new ClientMailSearchPhraseRequest(phrase, askedOn),
            this.reader,
            TestContext.Current.CancellationToken);
}
