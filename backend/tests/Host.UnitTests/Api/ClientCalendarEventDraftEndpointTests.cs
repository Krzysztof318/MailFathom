// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Calendar.Extraction;
using MailFathom.Domain.Calendar;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the drafting route accepts, what it refuses, and what a deployment reading no description answers.</summary>
/// <remarks>
/// The reading itself is covered where it is decided. What is asserted here is the transport: that a value this
/// deployment cannot honour is refused rather than guessed at, that a refusal never quotes back what somebody typed,
/// and that a person filling the fields in themselves is never told they made a mistake.
/// </remarks>
public sealed class ClientCalendarEventDraftEndpointTests
{
    /// <summary>The instant the deployment's own clock stands at, read in the asking person's zone, while these tests run.</summary>
    private static readonly DateTimeOffset WrittenAt = new(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2));

    private readonly ICalendarEventExtractor extractor = Substitute.For<ICalendarEventExtractor>();

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void CalendarEventDraftRoute_IsThePathAClientComposes() =>
        Assert.Equal("/calendar/drafts", ClientCalendarEventDraftEndpoint.CalendarEventDraftRoute);

    /// <summary>The dialog may offer the field only where something reads one, which is what this answers before anybody types.</summary>
    [Fact]
    public void ReadsDescriptions_ADeploymentThatReadsADescription_SaysSo()
    {
        // Arrange
        this.extractor.IsActive.Returns(true);

        // Act
        var result = ClientCalendarEventDraftEndpoint.ReadsDescriptions(this.extractor);

        // Assert
        Assert.True(result.Value?.ReadsDescriptions);
    }

    /// <summary>No endpoint and an operator who turned it off are one answer, so no configuration is published to a browser.</summary>
    [Fact]
    public void ReadsDescriptions_ADeploymentThatReadsNone_SaysSoWithoutSayingWhy()
    {
        // Arrange
        this.extractor.IsActive.Returns(false);

        // Act
        var result = ClientCalendarEventDraftEndpoint.ReadsDescriptions(this.extractor);

        // Assert
        Assert.False(result.Value?.ReadsDescriptions);
    }

    [Fact]
    public async Task DraftEventAsync_ARequestWithNoBodyAtAll_IsRefused()
    {
        // Act
        var result = await ClientCalendarEventDraftEndpoint.DraftEventAsync(
            request: null,
            this.extractor,
            UserClocks.Reading(WrittenAt),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DraftEventAsync_ARequestCarryingNoDescription_IsRefused(string? description)
    {
        // Act
        var result = await this.DraftAsync(description);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>What somebody is arranging is the most revealing value this route carries, and a problem detail reaches a log.</summary>
    [Fact]
    public async Task DraftEventAsync_ADescriptionThisRouteCannotRead_IsRefusedWithoutQuotingIt()
    {
        // Arrange
        var tooLong = new string('a', CalendarEventDescription.MaximumTextLength + 1);

        // Act
        var result = await this.DraftAsync(tooLong);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.DoesNotContain(tooLong, refusal.ProblemDetails.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftEventAsync_AReadingThatFoundAnEvent_AnswersWithIt()
    {
        // Arrange
        this.AnswerWith(CalendarEventExtraction.Settled([Drafted()]));

        // Act
        var result = await this.DraftAsync("racking survey on Thursday at ten");

        // Assert
        var drafted = Assert.IsType<Ok<ClientCalendarEventDraftResponse>>(result.Result).Value;
        Assert.True(drafted?.Drafted);
        Assert.Equal("Racking survey", drafted?.Title);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.FromHours(2)), drafted?.Start);
    }

    /// <summary>A sentence naming no occasion is the dialog's own empty fields rather than a mistake somebody made.</summary>
    [Fact]
    public async Task DraftEventAsync_AReadingThatFoundNothing_SaysNothingWasDraftedRatherThanRefusing()
    {
        // Arrange
        this.AnswerWith(CalendarEventExtraction.Settled([]));

        // Act
        var result = await this.DraftAsync("we should catch up some time");

        // Assert
        var drafted = Assert.IsType<Ok<ClientCalendarEventDraftResponse>>(result.Result).Value;
        Assert.False(drafted?.Drafted);
        Assert.Null(drafted?.Title);
    }

    /// <summary>A provider that could not be reached leaves the same dialog a deployment with no provider serves.</summary>
    [Fact]
    public async Task DraftEventAsync_AProviderThatCouldNotBeReached_SaysNothingWasDrafted()
    {
        // Arrange
        this.AnswerWith(
            CalendarEventExtraction.Withholding(CalendarEventExtractionWithholding.ProviderUnavailable));

        // Act
        var result = await this.DraftAsync("racking survey on Thursday");

        // Assert
        Assert.False(Assert.IsType<Ok<ClientCalendarEventDraftResponse>>(result.Result).Value?.Drafted);
    }

    /// <summary>A deployment reading no description answers the dialog rather than a mistake, exactly as an unreachable provider does.</summary>
    [Fact]
    public async Task DraftEventAsync_ADeploymentThatReadsNoDescription_SaysNothingWasDrafted()
    {
        // Arrange
        this.AnswerWith(CalendarEventExtraction.Withholding(CalendarEventExtractionWithholding.NotActivated));

        // Act
        var result = await this.DraftAsync("racking survey on Thursday");

        // Assert
        Assert.False(Assert.IsType<Ok<ClientCalendarEventDraftResponse>>(result.Result).Value?.Drafted);
    }

    /// <summary>A spent allowance travels, because a field that has quietly stopped working leaves somebody typing into it.</summary>
    [Fact]
    public async Task DraftEventAsync_APeriodThatHasSpentItsAllowance_IsReportedRatherThanAnsweredEmpty()
    {
        // Arrange
        this.AnswerWith(
            CalendarEventExtraction.Withholding(CalendarEventExtractionWithholding.AllowanceExhausted));

        // Act
        var result = await this.DraftAsync("racking survey on Thursday");

        // Assert
        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    private static ExtractedCalendarEvent Drafted() =>
        new(
            CalendarEventTitle.Create("Racking survey"),
            new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.FromHours(2)),
            End: null);

    /// <summary>
    /// The strict binding, which is what a client still sending the instant it wrote at meets: the field left this
    /// contract with the zone, and a body carrying it is refused rather than read as a description the deployment
    /// drafted from its own clock while dropping what the caller thought it had stated.
    /// </summary>
    [Fact]
    public void Deserialize_ABodyCarryingTheWithdrawnWrittenAtField_IsRefused()
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientCalendarEventDraftRequest>(
            """{"description":"survey tomorrow at nine","writtenAt":"2026-09-23T09:00:00+02:00"}""",
            WebFormat));
    }

    [Fact]
    public void Deserialize_ABodyStatingOnlyTheDescription_BindsIt()
    {
        // Act
        var request = JsonSerializer.Deserialize<ClientCalendarEventDraftRequest>(
            """{"description":"survey tomorrow at nine"}""",
            WebFormat);

        // Assert
        Assert.Equal("survey tomorrow at nine", request!.Description);
    }

    /// <summary>How the transport reads a body, so the binding these assert is the one a request actually meets.</summary>
    private static JsonSerializerOptions WebFormat => new(JsonSerializerDefaults.Web);

    private void AnswerWith(CalendarEventExtraction extraction)
    {
        this.extractor.IsActive.Returns(true);
        this.extractor
            .DraftFromDescriptionAsync(Arg.Any<CalendarEventDescription>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(extraction));
    }

    private Task<Results<Ok<ClientCalendarEventDraftResponse>, ProblemHttpResult>> DraftAsync(string? description) =>
        ClientCalendarEventDraftEndpoint.DraftEventAsync(
            new ClientCalendarEventDraftRequest(description),
            this.extractor,
            UserClocks.Reading(WrittenAt),
            TestContext.Current.CancellationToken);
}
