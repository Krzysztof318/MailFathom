// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Notifications;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Signals;

/// <summary>Covers what each kind of statement carries, and the bounds that keep one from growing into a payload.</summary>
public sealed class ClientSignalTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("inbox");

    private static readonly DateTimeOffset Instant = new(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An arrival of nothing is no arrival, so composing one is refused rather than delivered as a change of zero.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MailArrived_WithoutAPositiveCount_IsRefused(int newEmailCount) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ClientSignal.MailArrived(Account, Inbox, newEmailCount));

    /// <summary>A window that attributed one occurrence twice names it once, so a client re-reads each row once.</summary>
    [Fact]
    public void MailChanged_WithARepeatedIdentity_NamesItOnce()
    {
        // Arrange
        var email = StoredEmailId.Create(Guid.CreateVersion7());

        // Act
        var signal = ClientSignal.MailChanged(Account, Inbox, [email, email]);

        // Assert
        Assert.Equal([email], signal.Emails);
    }

    /// <summary>A window attributing more changes than a signal names still says which account and folder to re-read.</summary>
    [Fact]
    public void MailChanged_WithMoreIdentitiesThanItNames_KeepsTheBoundAndTheScope()
    {
        // Arrange
        var emails = Enumerable
            .Range(0, ClientSignal.MostNamedEmails + 25)
            .Select(_ => StoredEmailId.Create(Guid.CreateVersion7()));

        // Act
        var signal = ClientSignal.MailChanged(Account, Inbox, emails);

        // Assert
        Assert.Equal(ClientSignal.MostNamedEmails, signal.Emails.Count);
        Assert.Equal(Account.Id, signal.Account);
        Assert.Equal(Inbox, signal.Folder);
    }

    /// <summary>The two lines a notification row is drawn with are the record's own, and nothing else about the record travels.</summary>
    [Fact]
    public void NotificationRaised_FromARecordedNotification_CarriesItsOwnTwoLinesAndTheUnreadCount()
    {
        // Arrange
        var notification = Notification.Compose(
            NotificationId.Create(Guid.CreateVersion7(Instant)),
            SyntheticMailOwner.Deployment,
            NotificationKind.Mail,
            title: "Mail arrived",
            body: "Four messages arrived in work.",
            source: "work",
            NotificationTarget.Nothing,
            NotificationDeduplicationKey.Create("work:arrived"),
            Instant);

        // Act
        var signal = ClientSignal.NotificationRaised(notification, unreadCount: 3);

        // Assert
        Assert.Equal(SyntheticMailOwner.Deployment, signal.Owner);
        Assert.Equal(NotificationKind.Mail, signal.NotificationKind);
        Assert.Equal("Mail arrived", signal.Headline);
        Assert.Equal("Four messages arrived in work.", signal.SecondLine);
        Assert.Equal(3, signal.Count);
        Assert.Null(signal.Account);
        Assert.Null(signal.Folder);
        Assert.Empty(signal.Emails);
    }

    /// <summary>A run's end names the account and says nothing about the state it left it in, which is derived in one place and re-read from there.</summary>
    [Fact]
    public void AccountState_ForAFinishedRun_NamesTheAccountAndNothingAboutItsState()
    {
        // Act
        var signal = ClientSignal.AccountState(Account);

        // Assert
        Assert.Equal(ClientSignalKind.AccountState, signal.Kind);
        Assert.Equal(Account.Id, signal.Account);
        Assert.Null(signal.Folder);
        Assert.Equal(0, signal.Count);
        Assert.Empty(signal.Emails);
        Assert.Null(signal.Headline);
        Assert.Null(signal.SecondLine);
    }

    /// <summary>Every kind publishes a name of its own, which is what a client keys its handler by.</summary>
    [Fact]
    public void All_HoldsEveryKindUnderADistinctPublishedName()
    {
        // Act
        var names = ClientSignalKind.All.Select(kind => kind.Name).ToArray();

        // Assert
        Assert.Equal(
            ["account.state", "folders.changed", "mail.arrived", "mail.changed", "notification.raised"],
            [.. names.Order(StringComparer.Ordinal)]);
    }

    /// <summary>A kind nobody named is not one of the five, so a value that reached a channel by accident says so.</summary>
    [Fact]
    public void IsSpecified_ForTheStructDefault_ReportsThatNoKindWasNamed() =>
        Assert.False(default(ClientSignalKind).IsSpecified);
}
