// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.CalendarEvents;
using MailFathom.Application.Calendar.Extraction;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.AI.UnitTests.CalendarEvents;

/// <summary>Covers what a deployment that has not turned the extraction on reads, which is nothing at all.</summary>
/// <remarks>
/// The absence is a renderable state rather than a failure: the pass reads a reason it can act on, and the new-event
/// screen draws its own fields rather than a field that fails.
/// </remarks>
public sealed class InactiveCalendarEventExtractorTests
{
    /// <summary>What both callers read before they compose anything, so a deployment reading nothing scans nothing.</summary>
    [Fact]
    public void IsActive_ADeploymentThatHasNotTurnedItOn_SaysSoWithoutBeingAskedAboutAnyText()
    {
        // Act & Assert
        Assert.False(InactiveCalendarEventExtractor.Instance.IsActive);
    }

    [Fact]
    public async Task ProposeFromEmailAsync_ADeploymentThatHasNotTurnedItOn_WithholdsRatherThanFailing()
    {
        // Act
        var extraction = await InactiveCalendarEventExtractor.Instance.ProposeFromEmailAsync(
            Enrichable(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(extraction.IsSettled);
        Assert.Equal(CalendarEventExtractionWithholding.NotActivated, extraction.Withheld);
        Assert.Empty(extraction.Events);
    }

    [Fact]
    public async Task DraftFromDescriptionAsync_ADeploymentThatHasNotTurnedItOn_WithholdsRatherThanFailing()
    {
        // Act
        var extraction = await InactiveCalendarEventExtractor.Instance.DraftFromDescriptionAsync(
            Description(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventExtractionWithholding.NotActivated, extraction.Withheld);
        Assert.Empty(extraction.Events);
    }

    [Fact]
    public async Task ProposeFromEmailAsync_ACancelledCaller_StopsRatherThanAnswering()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            InactiveCalendarEventExtractor.Instance.ProposeFromEmailAsync(Enrichable(), cancellation.Token));
    }

    [Fact]
    public async Task DraftFromDescriptionAsync_ACancelledCaller_StopsRatherThanAnswering()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            InactiveCalendarEventExtractor.Instance.DraftFromDescriptionAsync(Description(), cancellation.Token));
    }

    private static EnrichableEmail Enrichable() =>
        new(
            StoredEmailId.Create(Guid.CreateVersion7()),
            "The racking survey",
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
            [new EnrichablePassage(EmailChunkId.Create(Guid.CreateVersion7()), 0, "shall we say Thursday at ten")]);

    private static CalendarEventDescription Description()
    {
        Assert.True(
            CalendarEventDescription.TryCreate(
                "lunch with the surveyor tomorrow at one",
                new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
                out var description));

        return description;
    }
}
