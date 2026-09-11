// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.ThreadStates;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.AI.UnitTests.ThreadStates;

/// <summary>Covers what survives the reading of a model's answer about where a conversation stands.</summary>
/// <remarks>
/// Everything here is about an answer this system did not write. What is asserted is that a usable answer is read, that
/// an unusable one produces nothing rather than an exception, and above all that no statement leaves the reading citing
/// a message the turn did not publish.
/// </remarks>
public sealed class ThreadStateReadingTests
{
    private static readonly StoredEmailId First = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly StoredEmailId Second = StoredEmailId.Create(Guid.CreateVersion7());

    [Fact]
    public void Read_AnAnswerAboutEveryAspect_ReadsThemInAspectOrder()
    {
        // Arrange
        const string answer = """
            {
              "agreements": [{ "text": "The response time stays at two hours.", "messages": [0] }],
              "openQuestions": [{ "text": "The upper limit is unsettled.", "messages": [1] }],
              "commitments": [
                { "text": "Karolina sends the figures.", "messages": [1], "owedBy": "Karolina", "dueAt": "2026-09-12" }
              ],
              "differences": [{ "text": "The second quotation raises the price by eight percent.", "messages": [0, 1] }]
            }
            """;

        // Act
        var entries = ThreadStateReading.Read(answer, Messages());

        // Assert
        Assert.Equal(
            [
                ThreadStateAspect.Agreement,
                ThreadStateAspect.OpenQuestion,
                ThreadStateAspect.Commitment,
                ThreadStateAspect.VersionDifference,
            ],
            entries.Select(static entry => entry.Aspect));
        Assert.Equal([First], entries[0].Sources);
        Assert.Equal("Karolina", entries[2].OwedBy);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), entries[2].DueAt);
        Assert.Equal([First, Second], entries[3].Sources);
    }

    /// <summary>The whole point of numbering the turn: a citation is a position this deployment published.</summary>
    [Theory]
    [InlineData("[-1]")]
    [InlineData("[7]")]
    [InlineData("[]")]
    public void Read_AStatementCitingAMessageTheTurnNeverPublished_DropsTheStatement(string messages)
    {
        // Arrange
        var answer = $$"""{ "agreements": [{ "text": "They settled the price.", "messages": {{messages}} }] }""";

        // Act
        var entries = ThreadStateReading.Read(answer, Messages());

        // Assert
        Assert.Empty(entries);
    }

    /// <summary>A citation partly outside the turn keeps the part inside it rather than losing the statement.</summary>
    [Fact]
    public void Read_AStatementCitingOnePublishedMessageAndOneNot_KeepsThePublishedOne()
    {
        // Arrange
        const string answer =
            """{ "agreements": [{ "text": "They settled the price.", "messages": [9, 1] }] }""";

        // Act
        var entries = ThreadStateReading.Read(answer, Messages());

        // Assert
        Assert.Equal([Second], Assert.Single(entries).Sources);
    }

    [Fact]
    public void Read_AStatementCitingOneMessageTwice_CitesItOnce()
    {
        // Arrange
        const string answer =
            """{ "agreements": [{ "text": "They settled the price.", "messages": [0, 0, 0] }] }""";

        // Act
        var entries = ThreadStateReading.Read(answer, Messages());

        // Assert
        Assert.Equal([First], Assert.Single(entries).Sources);
    }

    /// <summary>A model handed a long exchange writes one statement per message; the block keeps what it leads with.</summary>
    [Fact]
    public void Read_MoreStatementsOfOneAspectThanTheBlockHolds_KeepsTheLeadingOnes()
    {
        // Arrange
        var written = string.Join(
            ',',
            Enumerable
                .Range(0, EmailThreadState.MaximumEntriesPerAspect + 4)
                .Select(static ordinal => $$"""{ "text": "Agreement {{ordinal}}.", "messages": [0] }"""));
        var answer = $$"""{ "agreements": [{{written}}] }""";

        // Act
        var entries = ThreadStateReading.Read(answer, Messages());

        // Assert
        Assert.Equal("Agreement 0.", Assert.Single(entries).Text);
    }

    /// <summary>An owner and a date belong to a commitment, so one written on another aspect is dropped rather than refused.</summary>
    [Fact]
    public void Read_AnAgreementWrittenWithAnOwnerAndADate_KeepsTheStatementWithoutThem()
    {
        // Arrange
        const string answer = """
            { "agreements": [
                { "text": "They settled the price.", "messages": [0], "owedBy": "Karolina", "dueAt": "2026-09-12" }
            ] }
            """;

        // Act
        var entry = Assert.Single(ThreadStateReading.Read(answer, Messages()));

        // Assert
        Assert.Null(entry.OwedBy);
        Assert.Null(entry.DueAt);
    }

    /// <summary>A date a reader would act on is dropped where it cannot be read, and the commitment stays.</summary>
    [Theory]
    [InlineData("next Friday")]
    [InlineData("")]
    [InlineData("2026-13-45")]
    public void Read_ACommitmentWhoseDateCannotBeRead_KeepsTheCommitmentWithoutADate(string dueAt)
    {
        // Arrange
        var answer = $$"""
            { "commitments": [{ "text": "Karolina sends the figures.", "messages": [0], "dueAt": "{{dueAt}}" }] }
            """;

        // Act
        var entry = Assert.Single(ThreadStateReading.Read(answer, Messages()));

        // Assert
        Assert.Equal(ThreadStateAspect.Commitment, entry.Aspect);
        Assert.Null(entry.DueAt);
    }

    /// <summary>A model told to answer with one object still fences it and writes a sentence around it.</summary>
    [Theory]
    [InlineData("""Here is the state: ```json { "agreements": [{ "text": "They settled.", "messages": [0] }] } ``` I hope this helps.""")]
    [InlineData("""```{ "agreements": [{ "text": "They settled.", "messages": [0] }] }```""")]
    public void Read_AnObjectWrittenInsideProseOrAFence_IsStillRead(string answer)
    {
        // Act
        var entry = Assert.Single(ThreadStateReading.Read(answer, Messages()));

        // Assert
        Assert.Equal("They settled.", entry.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I could not read the conversation.")]
    [InlineData("{ not json at all }")]
    [InlineData("{}")]
    public void Read_AnAnswerNothingCanBeReadOutOf_ProducesNoStatements(string? answer)
    {
        // Act
        var entries = ThreadStateReading.Read(answer, Messages());

        // Assert
        Assert.Empty(entries);
    }

    [Theory]
    [InlineData("""{ "agreements": [{ "messages": [0] }] }""")]
    [InlineData("""{ "agreements": [{ "text": "   ", "messages": [0] }] }""")]
    [InlineData("""{ "agreements": [null] }""")]
    public void Read_AStatementSayingNothing_IsDropped(string answer)
    {
        // Act
        var entries = ThreadStateReading.Read(answer, Messages());

        // Assert
        Assert.Empty(entries);
    }

    private static IReadOnlyList<DerivableThreadMessage> Messages() =>
    [
        new(First, 0, "Karolina", new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero), "the price holds"),
        new(Second, 1, "Piotr", new DateTimeOffset(2026, 9, 7, 15, 0, 0, TimeSpan.Zero), "what about the limit"),
    ];
}
