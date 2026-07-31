// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Domain.Emails;
using MailMcp.Domain.Synchronization;
using Xunit;

namespace MailMcp.Domain.UnitTests;

public sealed class SynchronizationCheckpointTests
{
    [Fact]
    public void RepresentsSameProgressAs_SameUidValidityAndUidWithDifferentTimestamp_ReturnsTrue()
    {
        // Arrange
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var checkpoint = new SynchronizationCheckpoint(
            uidValidity,
            uid,
            new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero).AddTicks(9));
        var roundTrippedCheckpoint = new SynchronizationCheckpoint(
            uidValidity,
            uid,
            new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

        // Act
        var representsSameProgress = checkpoint.RepresentsSameProgressAs(roundTrippedCheckpoint);

        // Assert
        Assert.True(representsSameProgress);
    }
}
