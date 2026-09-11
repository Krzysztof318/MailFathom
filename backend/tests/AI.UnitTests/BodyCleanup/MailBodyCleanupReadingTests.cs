// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.BodyCleanup;
using MailFathom.Application.EmailContent.Cleaning;
using Xunit;

namespace MailFathom.AI.UnitTests.BodyCleanup;

/// <summary>
/// Covers the reading of an answer this system did not write. The claim under all of it is that nothing but two numbers
/// and a keyword survives: an answer carrying a word of the message is refused by the reading rather than repaired, which
/// is what makes the view's fidelity a property of the contract.
/// </summary>
public sealed class MailBodyCleanupReadingTests
{
    [Fact]
    public void Read_AnAnswerNamingRangesAndActions_ReadsThemInTheOrderTheyWereWritten()
    {
        // Arrange
        const string answer = """
            {"segments":[{"from":0,"to":1,"action":"drop"},{"from":2,"to":5,"action":"keep"}]}
            """;

        // Act
        var segments = MailBodyCleanupReading.Read(answer);

        // Assert
        Assert.Equal(
            [new MailBodyCleaningSegment(0, 1, Keep: false), new MailBodyCleaningSegment(2, 5, Keep: true)],
            segments);
    }

    /// <summary>A model told to answer with one object fences it often enough that treating that as a failure would throw away usable partitions.</summary>
    [Fact]
    public void Read_AnAnswerFencedAndPrefacedWithProse_ReadsTheObjectInsideIt()
    {
        // Arrange
        const string answer = """
            Here is the partition you asked for:

            ```json
            {"segments":[{"from":0,"to":3,"action":"keep"}]}
            ```
            """;

        // Act
        var segments = MailBodyCleanupReading.Read(answer);

        // Assert
        Assert.Equal([new MailBodyCleaningSegment(0, 3, Keep: true)], segments);
    }

    /// <summary>
    /// The answer carries no message text, and this is the mechanism rather than the instruction: a member nobody
    /// declared fails the binding, so a range that arrived with the block's own words beside it is refused whole.
    /// </summary>
    [Fact]
    public void Read_AnAnswerCarryingTheMessageTextBesideARange_IsRefusedWhole()
    {
        // Arrange
        const string answer = """
            {"segments":[{"from":0,"to":1,"action":"keep","text":"Your code is 558132"}]}
            """;

        // Act
        var segments = MailBodyCleanupReading.Read(answer);

        // Assert
        Assert.Empty(segments);
    }

    /// <summary>A rewritten body is the failure this pass is shaped to make unreachable, so an answer that is one reads as nothing.</summary>
    [Fact]
    public void Read_AnAnswerWritingACleanedBodyRatherThanRanges_IsRefused()
    {
        // Arrange
        const string answer = """
            {"body":"Your code is 558132. It expires in ten minutes."}
            """;

        // Act
        var segments = MailBodyCleanupReading.Read(answer);

        // Assert
        Assert.Empty(segments);
    }

    /// <summary>
    /// Refused whole rather than per range: the ranges are one statement about one document, so a range nobody could read
    /// leaves every range after it describing blocks nothing spoke for.
    /// </summary>
    [Fact]
    public void Read_OneUnreadableRangeAmongUsableOnes_RefusesAllOfThem()
    {
        // Arrange
        const string answer = """
            {"segments":[{"from":0,"to":1,"action":"keep"},{"to":4,"action":"drop"}]}
            """;

        // Act
        var segments = MailBodyCleanupReading.Read(answer);

        // Assert
        Assert.Empty(segments);
    }

    /// <summary>Two keywords and no third, because a range whose fate was described in a sentence is one nobody decided.</summary>
    [Theory]
    [InlineData("remove")]
    [InlineData("keep this one, it matters")]
    [InlineData("")]
    public void Read_ARangeNamingSomethingOtherThanTheTwoKeywords_IsRefused(string action)
    {
        // Act
        var segments = MailBodyCleanupReading.Read($$"""{"segments":[{"from":0,"to":1,"action":"{{action}}"}]}""");

        // Assert
        Assert.Empty(segments);
    }

    /// <summary>The keywords are the contract rather than their casing, so an answer shouting one is read.</summary>
    [Fact]
    public void Read_ARangeNamingAKeywordInAnotherCasing_ReadsIt()
    {
        // Act
        var segments = MailBodyCleanupReading.Read("""{"segments":[{"from":0,"to":1,"action":"KEEP"}]}""");

        // Assert
        Assert.Equal([new MailBodyCleaningSegment(0, 1, Keep: true)], segments);
    }

    /// <summary>An unreadable answer produces nothing rather than an exception, because the caller's next move is one more attempt.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I cannot help with that.")]
    [InlineData("{\"segments\":[{\"from\":\"first\",\"to\":\"second\",\"action\":\"keep\"}]}")]
    [InlineData("{\"segments\":")]
    public void Read_AnAnswerThatIsNotAPartitionOfBlockNumbers_AnswersNothing(string? answer)
    {
        // Act
        var segments = MailBodyCleanupReading.Read(answer);

        // Assert
        Assert.Empty(segments);
    }

    /// <summary>An answer stating an empty partition is read as one, and it is the pass above that refuses to draw nothing.</summary>
    [Fact]
    public void Read_AnAnswerStatingNoRangeAtAll_AnswersNothing()
    {
        // Act
        var segments = MailBodyCleanupReading.Read("""{"segments":[]}""");

        // Assert
        Assert.Empty(segments);
    }
}
