// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Delivery.Governance;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery.Governance;

public sealed class OutgoingMailCeilingsTests
{
    /// <summary>A deployment that declared nothing is bounded by nothing, which is what writing no ceiling asks for.</summary>
    [Fact]
    public void Create_NoCeilingDeclared_IsUnbounded()
    {
        // Arrange
        var ceilings = OutgoingMailCeilings.Create(
            TimeSpan.FromHours(1),
            maxMessagesPerAccount: 0,
            maxRecipientsPerAccount: 0,
            maxMessagesPerDeployment: 0,
            maxRecipientsPerDeployment: 0);

        // Act
        var reached = ceilings.FindReachedCeiling(
            new OutgoingMailUsage(1_000, 1_000, 1_000, 1_000),
            recipientCount: 50);

        // Assert
        Assert.True(ceilings.IsUnbounded);
        Assert.Null(reached);
    }

    /// <summary>Each ceiling admits the message that fills it and refuses the one after it, naming which it was.</summary>
    [Theory]
    [InlineData(1, 0, 0, 0, 1, 0, 0, 0, OutgoingMailCeiling.AccountMessages)]
    [InlineData(0, 4, 0, 0, 0, 3, 0, 0, OutgoingMailCeiling.AccountRecipients)]
    [InlineData(0, 0, 1, 0, 0, 0, 1, 0, OutgoingMailCeiling.DeploymentMessages)]
    [InlineData(0, 0, 0, 4, 0, 0, 0, 3, OutgoingMailCeiling.DeploymentRecipients)]
    public void FindReachedCeiling_PeriodAtItsCeiling_RefusesTheNextMessageNamingWhichCeiling(
        long maxAccountMessages,
        long maxAccountRecipients,
        long maxDeploymentMessages,
        long maxDeploymentRecipients,
        long accountMessages,
        long accountRecipients,
        long deploymentMessages,
        long deploymentRecipients,
        OutgoingMailCeiling expected)
    {
        // Arrange
        var ceilings = OutgoingMailCeilings.Create(
            TimeSpan.FromDays(1),
            maxAccountMessages,
            maxAccountRecipients,
            maxDeploymentMessages,
            maxDeploymentRecipients);

        // Act
        var reached = ceilings.FindReachedCeiling(
            new OutgoingMailUsage(accountMessages, accountRecipients, deploymentMessages, deploymentRecipients),
            recipientCount: 2);

        // Assert
        Assert.Equal(expected, reached);
    }

    /// <summary>The message that exactly fills a ceiling is admitted, because a ceiling states what a period may send.</summary>
    [Fact]
    public void FindReachedCeiling_MessageThatExactlyFillsEveryCeiling_IsAdmitted()
    {
        // Arrange
        var ceilings = OutgoingMailCeilings.Create(
            TimeSpan.FromDays(1),
            maxMessagesPerAccount: 3,
            maxRecipientsPerAccount: 5,
            maxMessagesPerDeployment: 6,
            maxRecipientsPerDeployment: 9);

        // Act
        var reached = ceilings.FindReachedCeiling(
            new OutgoingMailUsage(
                AccountMessageCount: 2,
                AccountRecipientCount: 3,
                DeploymentMessageCount: 5,
                DeploymentRecipientCount: 7),
            recipientCount: 2);

        // Assert
        Assert.Null(reached);
    }

    /// <summary>The message is weighed by the people it names, so one message can reach a recipient ceiling on its own.</summary>
    [Fact]
    public void FindReachedCeiling_OneMessageNamingMorePeopleThanTheCeiling_IsRefused()
    {
        // Arrange
        var ceilings = OutgoingMailCeilings.Create(
            TimeSpan.FromDays(1),
            maxMessagesPerAccount: 0,
            maxRecipientsPerAccount: 3,
            maxMessagesPerDeployment: 0,
            maxRecipientsPerDeployment: 0);

        // Act
        var reached = ceilings.FindReachedCeiling(OutgoingMailUsage.None, recipientCount: 4);

        // Assert
        Assert.Equal(OutgoingMailCeiling.AccountRecipients, reached);
    }

    /// <summary>The account's own bound is named first, because it is the narrower one an operator acts on.</summary>
    [Fact]
    public void FindReachedCeiling_MessageOverBothBounds_NamesTheAccountsOwn()
    {
        // Arrange
        var ceilings = OutgoingMailCeilings.Create(
            TimeSpan.FromDays(1),
            maxMessagesPerAccount: 1,
            maxRecipientsPerAccount: 0,
            maxMessagesPerDeployment: 2,
            maxRecipientsPerDeployment: 0);

        // Act
        var reached = ceilings.FindReachedCeiling(new OutgoingMailUsage(1, 1, 2, 2), recipientCount: 1);

        // Assert
        Assert.Equal(OutgoingMailCeiling.AccountMessages, reached);
    }

    /// <summary>A period is placed from the Unix epoch, so every process and every restart agrees where it began.</summary>
    [Theory]
    [InlineData("2026-08-19T00:00:00Z", "2026-08-19T00:00:00Z", "2026-08-20T00:00:00Z")]
    [InlineData("2026-08-19T23:59:59Z", "2026-08-19T00:00:00Z", "2026-08-20T00:00:00Z")]
    [InlineData("2026-08-20T00:00:00Z", "2026-08-20T00:00:00Z", "2026-08-21T00:00:00Z")]
    public void PeriodStartAt_InstantsAroundARollOver_PlacesEachInTheWindowAnchoredAtTheEpoch(
        string instant,
        string expectedStart,
        string expectedEnd)
    {
        // Arrange
        var ceilings = OutgoingMailCeilings.Create(
            TimeSpan.FromDays(1),
            maxMessagesPerAccount: 1,
            maxRecipientsPerAccount: 0,
            maxMessagesPerDeployment: 0,
            maxRecipientsPerDeployment: 0);
        var moment = DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture);

        // Act
        var start = ceilings.PeriodStartAt(moment);

        // Assert
        Assert.Equal(DateTimeOffset.Parse(expectedStart, CultureInfo.InvariantCulture), start);
        Assert.Equal(DateTimeOffset.Parse(expectedEnd, CultureInfo.InvariantCulture), ceilings.PeriodEndAt(moment));
    }

    /// <summary>A period is the window counts are taken over, so a length that names no window is a defect in the caller.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_PeriodThatIsNotPositive_IsRefused(int periodSeconds)
    {
        // Act
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => OutgoingMailCeilings.Create(
            TimeSpan.FromSeconds(periodSeconds),
            maxMessagesPerAccount: 1,
            maxRecipientsPerAccount: 0,
            maxMessagesPerDeployment: 0,
            maxRecipientsPerDeployment: 0));

        // Assert
        Assert.Equal("period", thrown.ParamName);
    }

    /// <summary>A message names at least one person, so a count below one describes no send at all.</summary>
    [Fact]
    public void FindReachedCeiling_MessageNamingNobody_IsRefusedAsADefect()
    {
        // Arrange
        var ceilings = OutgoingMailCeilings.Create(
            TimeSpan.FromDays(1),
            maxMessagesPerAccount: 1,
            maxRecipientsPerAccount: 0,
            maxMessagesPerDeployment: 0,
            maxRecipientsPerDeployment: 0);

        // Act
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => ceilings.FindReachedCeiling(OutgoingMailUsage.None, recipientCount: 0));

        // Assert
        Assert.Equal("recipientCount", thrown.ParamName);
    }
}
