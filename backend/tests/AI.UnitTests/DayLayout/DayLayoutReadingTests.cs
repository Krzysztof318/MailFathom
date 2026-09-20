// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.DayLayout;
using MailFathom.Application.Tasks;
using MailFathom.Domain.Tasks;
using Xunit;

namespace MailFathom.AI.UnitTests.DayLayout;

/// <summary>Covers what a model's arrangement of one day is read as, and what is dropped when the day cannot back it.</summary>
/// <remarks>
/// Every case here is a pure function of the text and the day the turn published, which is what lets an arrangement be
/// asserted rather than observed: the answers a provider produces once in a thousand runs are ordinary examples in this
/// class.
/// </remarks>
public sealed class DayLayoutReadingTests
{
    private static readonly DateTimeOffset DayStart = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DayEnd = new(2026, 9, 21, 20, 0, 0, TimeSpan.Zero);

    private static readonly PersonalTaskId FirstTask =
        PersonalTaskId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    private static readonly PersonalTaskId SecondTask =
        PersonalTaskId.Create(new Guid("22222222-2222-2222-2222-222222222222"));

    private static readonly DayLayoutQuestion Question = new(
        DayStart,
        DayEnd,
        [
            new DayLayoutTask(FirstTask, "Answer the supplier", new DateOnly(2026, 9, 21)),
            new DayLayoutTask(SecondTask, "Draft the tender response", new DateOnly(2026, 9, 21)),
        ],
        [new DayLayoutCommitment("Standup", DayStart.AddHours(1), DayStart.AddHours(1.5))]);

    [Fact]
    public void Read_AWellFormedArrangement_NamesEachTaskItPlacedAndTheWindowItPlacedItIn()
    {
        // Arrange
        const string answer = """
            {
              "placements": [
                { "task": 0, "startAt": "2026-09-21T10:00:00Z", "minutes": 45 },
                { "task": 1, "startAt": "2026-09-21T11:00:00Z", "minutes": 90 }
              ],
              "notToday": []
            }
            """;

        // Act
        var suggestion = DayLayoutReading.Read(answer, Question);

        // Assert
        Assert.Equal([FirstTask, SecondTask], suggestion.Placements.Select(placement => placement.Task));
        Assert.Equal(DayStart.AddHours(2), suggestion.Placements[0].StartAt);
        Assert.Equal(TimeSpan.FromMinutes(45), suggestion.Placements[0].Duration);
        Assert.Empty(suggestion.NotToday);
    }

    /// <summary>What does not fit is offered to the person as such rather than left out of the answer.</summary>
    [Fact]
    public void Read_AnArrangementThatLeftATaskOut_ReportsItAsOneThatDoesNotFitTheDay()
    {
        // Arrange
        const string answer = """
            {
              "placements": [{ "task": 0, "startAt": "2026-09-21T10:00:00Z", "minutes": 45 }],
              "notToday": [1]
            }
            """;

        // Act
        var suggestion = DayLayoutReading.Read(answer, Question);

        // Assert
        Assert.Equal(FirstTask, Assert.Single(suggestion.Placements).Task);
        Assert.Equal(SecondTask, Assert.Single(suggestion.NotToday));
    }

    /// <summary>A citation is a position in the list the turn published, so a number outside it names no task.</summary>
    [Theory]
    [InlineData(7)]
    [InlineData(-1)]
    public void Read_APlacementNamingATaskTheTurnNeverPublished_DropsThePlacement(int ordinal)
    {
        // Arrange
        var answer = $$"""
            { "placements": [{ "task": {{ordinal}}, "startAt": "2026-09-21T10:00:00Z", "minutes": 45 }] }
            """;

        // Act
        var suggestion = DayLayoutReading.Read(answer, Question);

        // Assert
        Assert.Empty(suggestion.Placements);
    }

    /// <summary>An hour outside the day somebody asked about is an hour they did not ask to be given.</summary>
    [Theory]
    [InlineData("2026-09-21T07:59:00Z")]
    [InlineData("2026-09-21T20:00:00Z")]
    [InlineData("not an instant")]
    public void Read_APlacementOutsideTheDay_DropsThePlacement(string startAt)
    {
        // Arrange
        var answer = $$"""
            { "placements": [{ "task": 0, "startAt": "{{startAt}}", "minutes": 45 }] }
            """;

        // Act
        var suggestion = DayLayoutReading.Read(answer, Question);

        // Assert
        Assert.Empty(suggestion.Placements);
    }

    /// <summary>How long something takes is the part of a suggestion a person adjusts, so it is bounded rather than refused.</summary>
    [Theory]
    [InlineData(0, DayLayoutPlacement.MinimumMinutes)]
    [InlineData(100000, DayLayoutPlacement.MaximumMinutes)]
    public void Read_APlacementOfAnImplausibleLength_BringsItInsideWhatAPlacementIsOfferedWith(
        int written,
        int expected)
    {
        // Arrange
        var answer = $$"""
            { "placements": [{ "task": 0, "startAt": "2026-09-21T10:00:00Z", "minutes": {{written}} }] }
            """;

        // Act
        var suggestion = DayLayoutReading.Read(answer, Question);

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(expected), Assert.Single(suggestion.Placements).Duration);
    }

    /// <summary>A placement whose length nothing states is still a placement, at the length the arrangement falls back on.</summary>
    [Fact]
    public void Read_APlacementStatingNoLength_OffersTheLengthTheArrangementFallsBackOn()
    {
        // Arrange
        const string answer = """
            { "placements": [{ "task": 0, "startAt": "2026-09-21T10:00:00Z" }] }
            """;

        // Act
        var suggestion = DayLayoutReading.Read(answer, Question);

        // Assert
        Assert.Equal(
            TimeSpan.FromMinutes(DayLayoutPlacement.DefaultMinutes),
            Assert.Single(suggestion.Placements).Duration);
    }

    /// <summary>One task cannot be two answers, so the first window it was given is the one it keeps.</summary>
    [Fact]
    public void Read_ATaskPlacedTwice_KeepsTheFirstWindowItWasGiven()
    {
        // Arrange
        const string answer = """
            {
              "placements": [
                { "task": 0, "startAt": "2026-09-21T10:00:00Z", "minutes": 45 },
                { "task": 0, "startAt": "2026-09-21T15:00:00Z", "minutes": 45 }
              ]
            }
            """;

        // Act
        var suggestion = DayLayoutReading.Read(answer, Question);

        // Assert
        Assert.Equal(DayStart.AddHours(2), Assert.Single(suggestion.Placements).StartAt);
    }

    /// <summary>A task cannot be both arranged and deferred, and the arrangement is what the person was given.</summary>
    [Fact]
    public void Read_ATaskBothPlacedAndDeferred_KeepsThePlacementAndDropsTheDeferral()
    {
        // Arrange
        const string answer = """
            {
              "placements": [{ "task": 0, "startAt": "2026-09-21T10:00:00Z", "minutes": 45 }],
              "notToday": [0, 1]
            }
            """;

        // Act
        var suggestion = DayLayoutReading.Read(answer, Question);

        // Assert
        Assert.Equal(FirstTask, Assert.Single(suggestion.Placements).Task);
        Assert.Equal(SecondTask, Assert.Single(suggestion.NotToday));
    }

    /// <summary>A screen draws the rows in the order they are timed, so the reading settles that order rather than the answer's.</summary>
    [Fact]
    public void Read_AnArrangementListedOutOfOrder_ReadsItIntoTheOrderTheWindowsThemselvesState()
    {
        // Arrange
        const string answer = """
            {
              "placements": [
                { "task": 1, "startAt": "2026-09-21T15:00:00Z", "minutes": 45 },
                { "task": 0, "startAt": "2026-09-21T10:00:00Z", "minutes": 45 }
              ]
            }
            """;

        // Act
        var suggestion = DayLayoutReading.Read(answer, Question);

        // Assert
        Assert.Equal([FirstTask, SecondTask], suggestion.Placements.Select(placement => placement.Task));
    }

    /// <summary>A model told to answer with one object still fences it.</summary>
    [Fact]
    public void Read_AnAnswerInsideACodeFence_StillReadsTheArrangement()
    {
        // Arrange
        var answer = string.Join(
            Environment.NewLine,
            "Here is how the day could go:",
            "```json",
            """{ "placements": [{ "task": 0, "startAt": "2026-09-21T10:00:00Z", "minutes": 45 }] }""",
            "```");

        // Act
        var suggestion = DayLayoutReading.Read(answer, Question);

        // Assert
        Assert.Equal(FirstTask, Assert.Single(suggestion.Placements).Task);
    }

    /// <summary>An answer nothing can be read out of is a day arranged with nothing rather than a failure to report.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("I could not work out a plan for today.")]
    [InlineData("{ \"placements\": ")]
    public void Read_AnAnswerThatIsNotAnArrangement_ReadsAsNothingArranged(string answer)
    {
        // Act
        var suggestion = DayLayoutReading.Read(answer, Question);

        // Assert
        Assert.Empty(suggestion.Placements);
        Assert.Empty(suggestion.NotToday);
    }
}
