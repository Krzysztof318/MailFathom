// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Conversations;

public sealed class AgentFollowUpsTests
{
    [Fact]
    public void Read_QuestionsAModelWrote_AreTrimmedAndRepeatsDroppedInTheOrderWritten()
    {
        // Act
        var read = AgentFollowUps.Read(["  Draft a reply to Anna ", "Show me the sources", "draft a reply to anna"]);

        // Assert
        Assert.NotNull(read);
        Assert.Equal(["Draft a reply to Anna", "Show me the sources"], read.Select(static followUp => followUp.Value));
    }

    [Fact]
    public void Read_WhatAnEndingAccepts_IsWhatAnEndingCarries()
    {
        // Arrange
        var read = AgentFollowUps.Read(["Draft a reply to Anna", "What if the supplier refuses the cap?"]);

        // Act
        var ended = new AgentAnswerEnded(AgentMessageId.New(), AgentAnswerOutcome.Completed, read);

        // Assert
        Assert.Equal(read, ended.FollowUps);
    }

    [Theory]
    [InlineData]
    [InlineData("One", "Two", "Three", "Four")]
    [InlineData("Draft a reply", "   ")]
    [InlineData("Draft a reply\nand send it")]
    public void Read_NothingOrTooManyOrOneThatIsNotALine_ReadsAsNone(params string[] written)
    {
        // Act, Assert
        Assert.Null(AgentFollowUps.Read(written));
    }

    [Fact]
    public void Read_AQuestionLongerThanALineAllows_ReadsAsNone()
    {
        // Act, Assert
        Assert.Null(AgentFollowUps.Read([new string('x', AgentConversationBounds.MaximumFollowUpLength + 1)]));
    }
}
