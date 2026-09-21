// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Search.Phrasing;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
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
    /// <summary>The instant the deployment's own clock stands at while these tests run.</summary>
    private static readonly DateTimeOffset AskedAt = new(2026, 9, 9, 14, 30, 0, TimeSpan.FromHours(2));

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
            MailUserClocks.Reading(AskedAt),
            TestContext.Current.CancellationToken);

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
        var result = await this.ReadAsync(phrase);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task ReadPhraseAsync_ASentenceLongerThanASearchMayCarry_IsRefused()
    {
        // Act
        var result = await this.ReadAsync(new string('a', EmailSearchQueryText.MaximumLength + 1));

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
        var result = await this.ReadAsync(overLong);

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
            new ClientMailSearchPhraseRequest("unread mail about the invoice"),
            reader: null,
            MailUserClocks.Reading(AskedAt),
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
        var result = await this.ReadAsync("unread mail from sales about racking last month, urgent");

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

    /// <summary>The anchor is this deployment's clock read in the asking person's own zone rather than a value a caller sent.</summary>
    [Fact]
    public async Task ReadPhraseAsync_ASentence_ReadsItAgainstTheAskingPersonsOwnClockRatherThanTheHostsZone()
    {
        // Arrange
        this.reader
            .ReadAsync(Arg.Any<MailSearchPhrase>(), Arg.Any<CancellationToken>())
            .Returns(MailSearchPhraseReading.Nothing);

        // Act
        await ClientMailSearchPhraseEndpoint.ReadPhraseAsync(
            new ClientMailSearchPhraseRequest("mail from last week"),
            this.reader,

            // Half past eleven in the evening in Warsaw is still the ninth there and already the tenth in Tokyo, which
            // is the pair of days a person searching for "yesterday" would be answered with the wrong one of.
            MailUserClocks.Reading(new DateTimeOffset(2026, 9, 9, 21, 30, 0, TimeSpan.Zero), "Europe/Warsaw"),
            TestContext.Current.CancellationToken);

        // Assert
        await this.reader
            .Received(1)
            .ReadAsync(
                Arg.Is<MailSearchPhrase>(phrase =>
                    phrase != null
                    && phrase.AskedAt == new DateTimeOffset(2026, 9, 9, 23, 30, 0, TimeSpan.FromHours(2))),
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
        var result = await this.ReadAsync("unread mail about the invoice");

        // Assert
        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>
    /// The strict binding, which is what a client still sending the day it asked on meets: the field left this contract
    /// with the zone, and a body carrying it is refused rather than read as a request the deployment answered from its
    /// own clock while dropping what the caller thought it had stated.
    /// </summary>
    [Fact]
    public void Deserialize_ABodyCarryingTheWithdrawnAskedOnField_IsRefused()
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientMailSearchPhraseRequest>(
            """{"phrase":"unread mail about the invoice","askedOn":"2026-09-14"}""",
            WebFormat));
    }

    [Fact]
    public void Deserialize_ABodyStatingOnlyThePhrase_BindsIt()
    {
        // Act
        var request = JsonSerializer.Deserialize<ClientMailSearchPhraseRequest>(
            """{"phrase":"unread mail about the invoice"}""",
            WebFormat);

        // Assert
        Assert.Equal("unread mail about the invoice", request!.Phrase);
    }

    /// <summary>How the transport reads a body, so the binding these assert is the one a request actually meets.</summary>
    private static JsonSerializerOptions WebFormat => new(JsonSerializerDefaults.Web);

    private Task<Results<Ok<ClientMailSearchPhraseResponse>, ProblemHttpResult>> ReadAsync(string? phrase) =>
        ClientMailSearchPhraseEndpoint.ReadPhraseAsync(
            new ClientMailSearchPhraseRequest(phrase),
            this.reader,
            MailUserClocks.Reading(AskedAt),
            TestContext.Current.CancellationToken);
}
