// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Delivery.Governance;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery.Governance;

public sealed class AuthoredSendCeilingsTests
{
    /// <summary>A deployment that bounded no caller is bounded by nothing, which is what writing no ceiling asks for.</summary>
    [Fact]
    public void Create_NoCeilingDeclared_IsUnbounded()
    {
        // Arrange
        var ceilings = AuthoredSendCeilings.Create(
            TimeSpan.FromHours(1),
            maxMessagesPerCaller: 0,
            maxRecipientsPerCaller: 0);

        // Act
        var reached = ceilings.FindReachedCeiling(new AuthoredSendUsage(1_000, 1_000), recipientCount: 50);

        // Assert
        Assert.True(ceilings.IsUnbounded);
        Assert.Null(reached);
    }

    /// <summary>A caller one message below its ceiling is admitted, which is the message that fills the period.</summary>
    [Theory]
    [InlineData(3, 0, 2, 0, 1)]
    [InlineData(0, 6, 0, 4, 2)]
    public void FindReachedCeiling_MessageThatFillsThePeriod_IsAdmitted(
        long maxMessages,
        long maxRecipients,
        long messagesSoFar,
        long recipientsSoFar,
        int recipientCount)
    {
        // Arrange
        var ceilings = AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessages, maxRecipients);

        // Act
        var reached = ceilings.FindReachedCeiling(
            new AuthoredSendUsage(messagesSoFar, recipientsSoFar),
            recipientCount);

        // Assert
        Assert.Null(reached);
    }

    /// <summary>Each ceiling refuses the message past it and names which of the two was reached.</summary>
    [Theory]
    [InlineData(2, 0, 2, 0, 1, AuthoredSendCeiling.CallerMessages)]
    [InlineData(0, 4, 0, 3, 2, AuthoredSendCeiling.CallerRecipients)]
    public void FindReachedCeiling_MessagePastTheCeiling_RefusesNamingWhichCeiling(
        long maxMessages,
        long maxRecipients,
        long messagesSoFar,
        long recipientsSoFar,
        int recipientCount,
        AuthoredSendCeiling expected)
    {
        // Arrange
        var ceilings = AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessages, maxRecipients);

        // Act
        var reached = ceilings.FindReachedCeiling(
            new AuthoredSendUsage(messagesSoFar, recipientsSoFar),
            recipientCount);

        // Assert
        Assert.Equal(expected, reached);
    }

    /// <summary>The message is weighed rather than admitted on anything at all being left, so an overshoot is refused.</summary>
    [Fact]
    public void FindReachedCeiling_MessageWiderThanWhatIsLeft_IsRefusedRatherThanAdmitted()
    {
        // Arrange
        var ceilings = AuthoredSendCeilings.Create(
            TimeSpan.FromDays(1),
            maxMessagesPerCaller: 0,
            maxRecipientsPerCaller: 10);

        // Act
        var reached = ceilings.FindReachedCeiling(new AuthoredSendUsage(1, 9), recipientCount: 2);

        // Assert
        Assert.Equal(AuthoredSendCeiling.CallerRecipients, reached);
    }

    /// <summary>The window is anchored at the epoch, so a period starts where arithmetic puts it and nothing is stored.</summary>
    [Fact]
    public void PeriodStartAt_AnyInstant_IsAnchoredAtTheUnixEpoch()
    {
        // Arrange
        var ceilings = AuthoredSendCeilings.Create(
            TimeSpan.FromHours(6),
            maxMessagesPerCaller: 1,
            maxRecipientsPerCaller: 0);

        // Act
        var start = ceilings.PeriodStartAt(
            DateTimeOffset.Parse("2026-08-19T16:30:00Z", CultureInfo.InvariantCulture));

        // Assert
        Assert.Equal(DateTimeOffset.Parse("2026-08-19T12:00:00Z", CultureInfo.InvariantCulture), start);
    }

    /// <summary>A period is not positive-length by accident: a window nobody could count over is refused.</summary>
    [Fact]
    public void Create_PeriodThatIsNotPositive_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => AuthoredSendCeilings.Create(
            TimeSpan.Zero,
            maxMessagesPerCaller: 1,
            maxRecipientsPerCaller: 1));
    }

    /// <summary>A negative ceiling is a value an operator cannot have meant, so it is refused rather than read as none.</summary>
    [Fact]
    public void Create_NegativeCeiling_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => AuthoredSendCeilings.Create(
            TimeSpan.FromDays(1),
            maxMessagesPerCaller: -1,
            maxRecipientsPerCaller: 0));
    }

    /// <summary>A message naming nobody is a defect in whoever asked rather than a ceiling to weigh it against.</summary>
    [Fact]
    public void FindReachedCeiling_MessageNamingNobody_IsADefect()
    {
        // Arrange
        var ceilings = AuthoredSendCeilings.Create(
            TimeSpan.FromDays(1),
            maxMessagesPerCaller: 1,
            maxRecipientsPerCaller: 1);

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ceilings.FindReachedCeiling(AuthoredSendUsage.None, recipientCount: 0));
    }
}
