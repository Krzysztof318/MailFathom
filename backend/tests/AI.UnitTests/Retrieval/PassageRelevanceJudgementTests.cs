// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Retrieval;
using Xunit;

namespace MailFathom.AI.UnitTests.Retrieval;

/// <summary>Covers the one reading of a judgement: what counts as an answer to the schema, and what is refused instead of mined.</summary>
public sealed class PassageRelevanceJudgementTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("50", 50)]
    [InlineData("100", 100)]
    [InlineData(" 95 ", 95)]
    [InlineData("95\n", 95)]
    public void Read_AScoreOnTheScale_IsTheJudgement(string answerText, int expected)
    {
        // Act
        var score = PassageRelevanceJudgement.Read(answerText);

        // Assert
        Assert.Equal(expected, score);
    }

    /// <summary>A lenient reading would turn a model that answered something else into a score this system invented, about somebody's mail.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relevance: 95")]
    [InlineData("95%")]
    [InlineData("95 out of 100")]
    [InlineData("```95```")]
    [InlineData("0.95")]
    [InlineData("+95")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("1000")]
    [InlineData("9 5")]
    [InlineData("ninety-five")]
    [InlineData("yes")]
    public void Read_AnAnswerThatIsNotAScore_IsRefused(string answerText)
    {
        // Act
        var score = PassageRelevanceJudgement.Read(answerText);

        // Assert
        Assert.Null(score);
    }

    [Fact]
    public void Read_WithoutAnAnswer_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => PassageRelevanceJudgement.Read(null!));
    }
}
