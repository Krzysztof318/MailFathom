// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Orchestration;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers what is read out of an answer a model wrapped in something else, which every agent here answers through.</summary>
/// <remarks>
/// It is one function rather than one per agent because the shapes a model wraps an object in are the model's habits
/// rather than any one derivation's: a second copy would be the one that stopped handling a shape the first learned.
/// </remarks>
public sealed class AgentJsonAnswerTests
{
    [Theory]
    [InlineData("""{"intent":"findFact"}""")]
    [InlineData("""```json{"intent":"findFact"}```""")]
    [InlineData("""Here it is: {"intent":"findFact"} — I hope it helps.""")]
    public void Unfenced_AnObjectWrittenInsideWhateverAModelWroteAroundIt_ReadsTheObject(string answer)
    {
        // Act
        var json = AgentJsonAnswer.Unfenced(answer);

        // Assert
        Assert.Equal("""{"intent":"findFact"}""", json);
    }

    /// <summary>The outermost braces, so a nested object does not cut the answer short.</summary>
    [Fact]
    public void Unfenced_AnObjectHoldingAnother_ReadsTheWholeOfTheOuterOne()
    {
        // Arrange
        const string answer = """{"filters":{"unread":true},"criteria":["invoice"]}""";

        // Act
        var json = AgentJsonAnswer.Unfenced(answer);

        // Assert
        Assert.Equal(answer, json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I am afraid I cannot help with that.")]
    [InlineData("{ an object that never closes")]
    [InlineData("} {")]
    public void Unfenced_AnAnswerHoldingNoObject_ReadsNothing(string? answer)
    {
        // Act, Assert
        Assert.Null(AgentJsonAnswer.Unfenced(answer));
    }
}
