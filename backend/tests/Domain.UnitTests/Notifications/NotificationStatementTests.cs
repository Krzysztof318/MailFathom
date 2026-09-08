// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Notifications;
using Xunit;

namespace MailFathom.Domain.UnitTests.Notifications;

/// <summary>Covers what a notification says as a condition and its numbers, rather than as a sentence.</summary>
public sealed class NotificationStatementTests
{
    /// <summary>Mail counts what one run committed and counts it against nothing.</summary>
    [Fact]
    public void MailArrived_ARunThatCommittedMail_CountsTheMessagesAgainstNothing()
    {
        // Arrange, Act
        var statement = NotificationStatement.MailArrived(40);

        // Assert
        Assert.Equal(NotificationCause.MailArrived, statement.Cause);
        Assert.Equal(40, statement.Counted);
        Assert.Null(statement.OutOf);
    }

    /// <summary>An unfinished run counts the folders it did not finish against the folders it scheduled.</summary>
    [Fact]
    public void SynchronizationIncomplete_ARunThatLeftFoldersUnfinished_CountsThemAgainstWhatItScheduled()
    {
        // Arrange, Act
        var statement = NotificationStatement.SynchronizationIncomplete(2, 5);

        // Assert
        Assert.Equal(NotificationCause.SynchronizationIncomplete, statement.Cause);
        Assert.Equal(2, statement.Counted);
        Assert.Equal(5, statement.OutOf);
    }

    /// <summary>A refused credential is a condition and nothing more, so neither number is invented for it.</summary>
    [Fact]
    public void CredentialRefused_ARefusedAccount_CountsNothing()
    {
        // Arrange, Act
        var statement = NotificationStatement.CredentialRefused();

        // Assert
        Assert.Equal(NotificationCause.CredentialRefused, statement.Cause);
        Assert.Null(statement.Counted);
        Assert.Null(statement.OutOf);
    }

    /// <summary>A count no run could have produced is refused where it is composed rather than where it is drawn.</summary>
    [Theory]
    [InlineData(-1, 5, "failedFolderCount")]
    [InlineData(1, -5, "scheduledFolderCount")]
    [InlineData(6, 5, "failedFolderCount")]
    public void SynchronizationIncomplete_CountsNoRunCouldProduce_AreRefused(
        int failed,
        int scheduled,
        string parameterName)
    {
        // Arrange, Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => NotificationStatement.SynchronizationIncomplete(failed, scheduled));

        // Assert
        Assert.Equal(parameterName, refusal.ParamName);
    }

    /// <summary>A negative arrival is refused for the same reason, and by the count's own guard.</summary>
    [Fact]
    public void MailArrived_ANegativeCount_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationStatement.MailArrived(-1));

    /// <summary>A row written before conditions were kept names none, which is a row to draw rather than to refuse.</summary>
    [Fact]
    public void Restore_ARowNamingNoCause_IsNoStatement() =>
        Assert.Null(NotificationStatement.Restore(cause: null, counted: 3, outOf: null));

    /// <summary>A stored row is input from outside this process however it got there, so it is validated like any other.</summary>
    [Fact]
    public void Restore_ACauseThisBuildDoesNotDeclare_IsRefused()
    {
        // Arrange, Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => NotificationStatement.Restore((NotificationCause)(-1), counted: null, outOf: null));

        // Assert
        Assert.Equal("cause", refusal.ParamName);
    }

    /// <summary>A restored row keeps the numbers it was stored with, whichever of them the cause left absent.</summary>
    /// <summary>
    /// A restored row is held to being a declared cause and to counting nothing negative, and to nothing else. The
    /// remarks on <see cref="NotificationStatement.Restore" /> say why, and this is what would fail if a later change
    /// made it apply the per-cause shape its factories enforce: a deployment upgraded over rows an older build wrote
    /// would stop being able to read them, which loses a notification rather than a number.
    /// </summary>
    [Fact]
    public void Restore_ARowCountingMoreThanItCountsAgainst_KeepsBothRatherThanRefusingTheRow()
    {
        // Act
        var statement = NotificationStatement.Restore(
            NotificationCause.SynchronizationIncomplete,
            counted: 6,
            outOf: 5);

        // Assert
        Assert.Equal(NotificationCause.SynchronizationIncomplete, statement?.Cause);
        Assert.Equal(6, statement?.Counted);
        Assert.Equal(5, statement?.OutOf);
    }

    [Fact]
    public void Restore_ARowNamingACauseAndItsCounts_KeepsBoth()
    {
        // Arrange, Act
        var statement = NotificationStatement.Restore(NotificationCause.SynchronizationIncomplete, 2, 5);

        // Assert
        Assert.NotNull(statement);
        Assert.Equal(NotificationCause.SynchronizationIncomplete, statement.Cause);
        Assert.Equal(2, statement.Counted);
        Assert.Equal(5, statement.OutOf);
    }

    /// <summary>A count a hand-edited row could carry and no producer could is refused rather than drawn.</summary>
    [Theory]
    [InlineData(-1, null, "counted")]
    [InlineData(null, -1, "outOf")]
    public void Restore_ANegativeCount_IsRefused(int? counted, int? outOf, string parameterName)
    {
        // Arrange, Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => NotificationStatement.Restore(NotificationCause.MailArrived, counted, outOf));

        // Assert
        Assert.Equal(parameterName, refusal.ParamName);
    }
}
