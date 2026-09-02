// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval.AskMail;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval.AskMail;

/// <summary>Covers the bounds one answer is published under.</summary>
public sealed class MailAnswerBoundsTests
{
    [Fact]
    public void Default_TheBoundsADeploymentReceives_LeaveRoomForAnOrdinaryAnswer()
    {
        // Act
        var bounds = MailAnswerBounds.Default;

        // Assert
        Assert.Equal(20_000, bounds.MaximumAnswerCharacters);
        Assert.Equal(20, bounds.MaximumCitations);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(20_000, 0)]
    [InlineData(20_000, -1)]
    public void Create_ABoundNoAnswerCouldBePublishedUnder_IsRefused(int answerCharacters, int citations)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MailAnswerBounds.Create(answerCharacters, citations));
    }

    /// <summary>The rendering reaches a log, so it states the numbers and nothing that was measured against them.</summary>
    [Fact]
    public void ToString_TheBounds_ReportsBothNumbers()
    {
        // Act
        var rendered = MailAnswerBounds.Create(1_200, 8).ToString();

        // Assert
        Assert.Equal("at most 1200 characters citing at most 8 emails", rendered);
    }
}
