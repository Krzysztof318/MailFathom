// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval.AskMail.Audit;

/// <summary>Covers the storage-limitation half of what keeping an answering record commits an operator to.</summary>
public sealed class MailAnsweringAuditTrailRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private readonly IMailAnsweringAuditSettingsReader settings =
        Substitute.For<IMailAnsweringAuditSettingsReader>();

    private readonly IMailAnsweringAuditEntryStore store = Substitute.For<IMailAnsweringAuditEntryStore>();

    /// <summary>The window is measured back from now and erased in bounded batches, so one pass never locks the record.</summary>
    [Fact]
    public async Task EraseExpiredAsync_AConfiguredWindow_ErasesBackFromNowInOneBoundedBatch()
    {
        // Arrange
        this.settings.GetAnsweringAuditSettings(Account.Id)
            .Returns(new MailAnsweringAuditSettings(IsEnabled: true, TimeSpan.FromDays(30)));
        this.store.EraseCompletedBeforeAsync(
                Account,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(7);

        // Act
        var erasedCount = await this.Retention().EraseExpiredAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(7, erasedCount);
        await this.store.Received(1).EraseCompletedBeforeAsync(
            Account,
            Now - TimeSpan.FromDays(30),
            MailAnsweringAuditTrailRetention.MaximumEntriesErasedPerPass,
            Arg.Any<CancellationToken>());
    }

    /// <summary>An account that has switched the record off still holds what it wrote, and ages it out as configured.</summary>
    [Fact]
    public async Task EraseExpiredAsync_ARecordSwitchedOffWithAWindowStillConfigured_StillErases()
    {
        // Arrange
        this.settings.GetAnsweringAuditSettings(Account.Id)
            .Returns(new MailAnsweringAuditSettings(IsEnabled: false, TimeSpan.FromDays(30)));

        // Act
        await this.Retention().EraseExpiredAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        await this.store.Received(1).EraseCompletedBeforeAsync(
            Account,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An account this deployment no longer configures names no window, and a missing window destroys nothing.</summary>
    [Fact]
    public async Task EraseExpiredAsync_AnAccountThisDeploymentDoesNotConfigure_ErasesNothing()
    {
        // Arrange
        this.settings.GetAnsweringAuditSettings(Account.Id).Returns(MailAnsweringAuditSettings.Disabled);

        // Act
        var erasedCount = await this.Retention().EraseExpiredAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, erasedCount);
        await this.store.DidNotReceive().EraseCompletedBeforeAsync(
            Arg.Any<MailAccountIdentity>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_WithoutACollaborator_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            new MailAnsweringAuditTrailRetention(null!, this.store, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() =>
            new MailAnsweringAuditTrailRetention(this.settings, null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() =>
            new MailAnsweringAuditTrailRetention(this.settings, this.store, null!));
    }

    private MailAnsweringAuditTrailRetention Retention() =>
        new(this.settings, this.store, new FakeTimeProvider(Now));
}
