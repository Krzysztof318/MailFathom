// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers how one scope is given the settings it reads: the user it acts for, the account it works on, or a run's own snapshot.</summary>
/// <remarks>
/// The published snapshot carries no user and no account, so what a scope is prepared with is the only way a mailbox
/// reaches anything resolved inside it — and preparing it twice with different answers would hand one piece of work
/// two views of the same account.
/// </remarks>
public sealed class ScopedMailSynchronizationSettingsTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private readonly MailSynchronizationOptions published = new();

    private readonly IMailSynchronizationAccountSource accounts = Substitute.For<IMailSynchronizationAccountSource>();

    [Fact]
    public void Current_AScopeNobodyPrepared_AnswersThePublishedSettings()
    {
        // Act
        var current = this.Settings().Current;

        // Assert
        Assert.Same(this.published, current);
    }

    [Fact]
    public async Task UseAccountSettingsAsync_AnAccountTheRecordsHold_ServesTheScopeTheSettingsReadForIt()
    {
        // Arrange
        var forWork = new MailSynchronizationOptions();
        this.accounts.ReadRunSettingsAsync(Work, null, Arg.Any<CancellationToken>()).Returns(forWork);
        var settings = this.Settings();

        // Act
        var prepared = await settings.UseAccountSettingsAsync(Work, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(prepared);
        Assert.Same(forWork, settings.Current);
    }

    /// <summary>An account whose record is gone prepares nothing, so the caller refuses rather than working against settings that name no mailbox.</summary>
    [Fact]
    public async Task UseAccountSettingsAsync_AnAccountNoRecordHolds_ReportsItPreparedNothing()
    {
        // Arrange
        this.accounts.ReadRunSettingsAsync(Work, null, Arg.Any<CancellationToken>())
            .Returns((MailSynchronizationOptions?)null);
        var settings = this.Settings();

        // Act
        var prepared = await settings.UseAccountSettingsAsync(Work, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(prepared);
        Assert.Same(this.published, settings.Current);
    }

    [Fact]
    public async Task UseUserSettingsAsync_AUserARequestActsFor_ServesTheScopeTheSettingsReadForThem()
    {
        // Arrange
        var forUser = new MailSynchronizationOptions();
        this.accounts.ReadUserSettingsAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>()).Returns(forUser);
        var settings = this.Settings();

        // Act
        await settings.UseUserSettingsAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(forUser, settings.Current);
    }

    /// <summary>
    /// Something already resolved inside the scope read the settings it was handed then, so preparing it again with
    /// another answer would leave two readers of one scope disagreeing about the same account.
    /// </summary>
    [Fact]
    public void UseRunSnapshot_AfterTheScopeAlreadyReadOtherSettings_Refuses()
    {
        // Arrange
        var settings = this.Settings();
        _ = settings.Current;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => settings.UseRunSnapshot(new MailSynchronizationOptions()));
    }

    [Fact]
    public void UseRunSnapshot_TheSnapshotTheScopeAlreadyHolds_IsAccepted()
    {
        // Arrange
        var run = new MailSynchronizationOptions();
        var settings = this.Settings();
        settings.UseRunSnapshot(run);

        // Act
        settings.UseRunSnapshot(run);

        // Assert
        Assert.Same(run, settings.Current);
    }

    private ScopedMailSynchronizationSettings Settings() =>
        new(new StubSettingsSnapshot<MailSynchronizationOptions>(this.published), this.accounts);
}
