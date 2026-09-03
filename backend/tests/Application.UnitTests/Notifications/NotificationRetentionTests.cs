// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Notifications;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Notifications;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Notifications;

/// <summary>Covers how far back a person's notification centre is allowed to reach.</summary>
public sealed class NotificationRetentionTests
{
    private static readonly MailOwnerId Owner = SyntheticMailOwner.Deployment;

    private static readonly DateTimeOffset RunInstant = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The window is measured back from now and the pass is bounded, so a backlog clears over several runs.</summary>
    [Fact]
    public async Task EraseExpiredAsync_AnyOwner_ErasesWhatHappenedBeforeTheWindowUpToTheBound()
    {
        // Arrange
        var store = Substitute.For<INotificationStore>();
        var retention = new NotificationRetention(store, new FakeTimeProvider(RunInstant));

        // Act
        await retention.EraseExpiredAsync(Owner, TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).EraseOccurredBeforeAsync(
            Owner,
            RunInstant - NotificationRetention.Window,
            NotificationRetention.MaximumNotificationsErasedPerPass,
            Arg.Any<CancellationToken>());
    }

    /// <summary>The bound is a storage-limitation decision rather than a reading list, so an unread statement ages out like any other.</summary>
    [Fact]
    public async Task EraseExpiredAsync_UnreadNotificationOlderThanTheWindow_IsErasedWithTheRest()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        await store.RecordAsync(
            NotificationOccurredAt(RunInstant - NotificationRetention.Window - TimeSpan.FromDays(1), "old"),
            TestContext.Current.CancellationToken);
        await store.RecordAsync(
            NotificationOccurredAt(RunInstant - TimeSpan.FromDays(1), "recent"),
            TestContext.Current.CancellationToken);

        var retention = new NotificationRetention(store, new FakeTimeProvider(RunInstant));

        // Act
        var erasedCount = await retention.EraseExpiredAsync(Owner, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, erasedCount);
        Assert.Equal("recent", Assert.Single(store.Recorded).DeduplicationKey.Value);
    }

    private static Notification NotificationOccurredAt(DateTimeOffset occurredAt, string deduplicationKey) =>
        Notification.Compose(
            NotificationId.Create(Guid.CreateVersion7(occurredAt)),
            Owner,
            NotificationKind.System,
            title: "Something happened",
            body: "Something happened that nobody was at the screen for.",
            source: "work",
            NotificationTarget.Nothing,
            NotificationDeduplicationKey.Create(deduplicationKey),
            occurredAt);
}
