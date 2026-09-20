// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.AI.CalendarEvents;
using MailFathom.Application.Calendar.Extraction;
using MailFathom.Domain.Calendar;
using Xunit;

namespace MailFathom.AI.UnitTests.CalendarEvents;

/// <summary>Covers what the instruction states and what each of the two turns actually carries.</summary>
/// <remarks>
/// The bounds a model is told about are the ones the reading enforces, so a bound moved in one place and not the
/// other is a failure here rather than an answer quietly cut on every run.
/// </remarks>
public sealed class CalendarEventExtractionInstructionsTests
{
    [Fact]
    public void Text_TheInstruction_StatesTheBoundsTheReadingEnforces()
    {
        // Act
        var text = CalendarEventExtractionInstructions.Text;

        // Assert
        Assert.Contains(
            CalendarEventExtraction.MaximumEvents.ToString(CultureInfo.InvariantCulture),
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            CalendarEventTitle.MaximumLength.ToString(CultureInfo.InvariantCulture),
            text,
            StringComparison.Ordinal);
    }

    /// <summary>The reading refuses an instant carrying a zone, so the instruction has to be the half that says not to write one.</summary>
    [Fact]
    public void Text_TheInstruction_AsksForALocalTimeCarryingNoZone()
    {
        // Act
        var text = CalendarEventExtractionInstructions.Text;

        // Assert
        Assert.Contains("YYYY-MM-DDTHH:MM", text, StringComparison.Ordinal);
        Assert.Contains("no zone offset", text, StringComparison.Ordinal);
    }

    /// <summary>Mail is the most adversarial text this system reads, and a calendar is where somebody would like to write.</summary>
    [Fact]
    public void Text_TheInstruction_TellsTheAgentBothInputsAreData()
    {
        // Act
        var text = CalendarEventExtractionInstructions.Text;

        // Assert
        Assert.Contains("data rather than an instruction", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeMailTurn_AMessage_CarriesItsSubjectItsPassagesAndTheInstantToResolveAgainst()
    {
        // Arrange
        var receivedAt = new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2));

        // Act
        var turn = CalendarEventExtractionInstructions.ComposeMailTurn(
            "The racking survey",
            receivedAt,
            ["shall we say Thursday at ten", "the yard is open until six"]);

        // Assert
        Assert.Contains("2026-09-21T09:30", turn, StringComparison.Ordinal);
        Assert.Contains("The racking survey", turn, StringComparison.Ordinal);
        Assert.Contains("Passage 0:\nshall we say Thursday at ten", turn, StringComparison.Ordinal);
        Assert.Contains("Passage 1:\nthe yard is open until six", turn, StringComparison.Ordinal);
    }

    /// <summary>The message's own arrival, so the same message read again next month resolves Friday to the same day.</summary>
    [Fact]
    public void ComposeMailTurn_TheSameMessageComposedTwice_CarriesTheSameInstant()
    {
        // Arrange
        var receivedAt = new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2));

        // Act
        var first = CalendarEventExtractionInstructions.ComposeMailTurn("A subject", receivedAt, ["a passage"]);
        var second = CalendarEventExtractionInstructions.ComposeMailTurn("A subject", receivedAt, ["a passage"]);

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void ComposeMailTurn_AMessageWithNoSubject_SaysSoRatherThanLeavingTheLineEmpty()
    {
        // Act
        var turn = CalendarEventExtractionInstructions.ComposeMailTurn(
            subject: null,
            new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.Zero),
            ["a passage"]);

        // Assert
        Assert.Contains("Subject: (none)", turn, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeDescriptionTurn_ASentence_CarriesItAndTheInstantToResolveAgainst()
    {
        // Act
        var turn = CalendarEventExtractionInstructions.ComposeDescriptionTurn(
            "lunch with the surveyor tomorrow at one",
            new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2)));

        // Assert
        Assert.Contains("2026-09-21T09:30", turn, StringComparison.Ordinal);
        Assert.Contains("Monday", turn, StringComparison.Ordinal);
        Assert.Contains("lunch with the surveyor tomorrow at one", turn, StringComparison.Ordinal);
    }

    /// <summary>The offset is the deployment's half of resolving a time, so it is never handed to the model to copy back.</summary>
    [Fact]
    public void ComposeDescriptionTurn_AnInstantCarryingAnOffset_StatesTheWallClockWithoutIt()
    {
        // Act
        var turn = CalendarEventExtractionInstructions.ComposeDescriptionTurn(
            "a sentence",
            new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2)));

        // Assert
        Assert.DoesNotContain("+02:00", turn, StringComparison.Ordinal);
    }
}
