// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Audit;

/// <summary>Covers how far back one account's audit trail is allowed to reach.</summary>
public sealed class MailboxMutationAuditTrailRetentionTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private static readonly DateTimeOffset RunInstant = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Everything that ended before the configured window is erased, and nothing inside it is.</summary>
    [Fact]
    public async Task EraseExpiredAsync_ConfiguredWindow_ErasesWhatEndedBeforeIt()
    {
        // Arrange
        var store = Substitute.For<IMailboxMutationAuditEntryStore>();
        var retention = CreateRetention(store, new MailboxMutationAuditSettings(IsEnabled: true, TimeSpan.FromDays(30)));

        // Act
        await retention.EraseExpiredAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).EraseCompletedBeforeAsync(
            Account,
            RunInstant.AddDays(-30),
            MailboxMutationAuditTrailRetention.MaximumEntriesErasedPerPass,
            Arg.Any<CancellationToken>());
    }

    /// <summary>An account that has just turned the trail off still ages out what it wrote while it was on.</summary>
    [Fact]
    public async Task EraseExpiredAsync_TrailSwitchedOff_StillAgesOutWhatItAlreadyHolds()
    {
        // Arrange
        var store = Substitute.For<IMailboxMutationAuditEntryStore>();
        var retention = CreateRetention(store, new MailboxMutationAuditSettings(IsEnabled: false, TimeSpan.FromDays(30)));

        // Act
        await retention.EraseExpiredAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).EraseCompletedBeforeAsync(
            Account,
            RunInstant.AddDays(-30),
            MailboxMutationAuditTrailRetention.MaximumEntriesErasedPerPass,
            Arg.Any<CancellationToken>());
    }

    /// <summary>An account this deployment no longer configures names no window, so nothing of its history is destroyed.</summary>
    [Fact]
    public async Task EraseExpiredAsync_AccountThatNamesNoWindow_ErasesNothing()
    {
        // Arrange
        var store = Substitute.For<IMailboxMutationAuditEntryStore>();
        var retention = CreateRetention(store, MailboxMutationAuditSettings.Disabled);

        // Act
        var erasedCount = await retention.EraseExpiredAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        await store.DidNotReceive().EraseCompletedBeforeAsync(
            Arg.Any<MailAccountIdentity>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(0, erasedCount);
    }

    private static MailboxMutationAuditTrailRetention CreateRetention(
        IMailboxMutationAuditEntryStore store,
        MailboxMutationAuditSettings settings)
    {
        var settingsReader = Substitute.For<IMailboxMutationAuditSettingsReader>();
        settingsReader.GetAuditSettings(Account.Id).Returns(settings);

        return new MailboxMutationAuditTrailRetention(settingsReader, store, new FakeTimeProvider(RunInstant));
    }
}
