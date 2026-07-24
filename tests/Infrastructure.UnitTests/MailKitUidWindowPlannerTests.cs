// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Messages;
using MailMcp.Infrastructure.Mail.MailKit;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class MailKitUidWindowPlannerTests
{
    [Fact]
    public void CreateBatchCursor_CurrentHighWaterBelowRequestedWindow_DoesNotAdvancePastExistingUid()
    {
        // Arrange
        var lastSeenUid = (ImapUid?)null;
        var maxMessageCount = 100;
        var uidNext = 51U;
        var returnedUids = new uint[] { 10, 50 };

        // Act
        var cursor = MailKitUidWindowPlanner.CreateBatchCursor(lastSeenUid, maxMessageCount, uidNext, 100U, returnedUids);

        // Assert
        Assert.Equal(ImapUid.Create(50), cursor.InspectedThroughUid);
        Assert.False(cursor.HasMore);
    }

    [Fact]
    public void CreateBatchCursor_NoReturnedUids_DoesNotAdvanceCheckpointIntoFutureUidSpace()
    {
        // Arrange
        var lastSeenUid = ImapUid.Create(50);
        var maxMessageCount = 100;
        var uidNext = 51U;
        var returnedUids = Array.Empty<uint>();

        // Act
        var cursor = MailKitUidWindowPlanner.CreateBatchCursor(lastSeenUid, maxMessageCount, uidNext, 50U, returnedUids);

        // Assert
        Assert.Equal(ImapUid.Create(50), cursor.InspectedThroughUid);
        Assert.False(cursor.HasMore);
    }

    [Fact]
    public void CreateBatchCursor_EmptyFolderWithoutLastSeenUid_ReturnsNoCheckpointUid()
    {
        // Arrange
        var lastSeenUid = (ImapUid?)null;
        var maxMessageCount = 100;
        var uidNext = 1U;
        var returnedUids = Array.Empty<uint>();

        // Act
        var cursor = MailKitUidWindowPlanner.CreateBatchCursor(lastSeenUid, maxMessageCount, uidNext, 0U, returnedUids);

        // Assert
        Assert.Null(cursor.InspectedThroughUid);
        Assert.False(cursor.HasMore);
    }

    [Fact]
    public void CreateBatchCursor_NoReturnedUidsBelowUidNext_AdvancesThroughKnownEmptyUidWindow()
    {
        // Arrange
        var lastSeenUid = ImapUid.Create(50);
        var maxMessageCount = 100;
        var uidNext = 1000U;
        var inclusiveWindowEnd = 150U;
        var returnedUids = Array.Empty<uint>();

        // Act
        var cursor = MailKitUidWindowPlanner.CreateBatchCursor(lastSeenUid, maxMessageCount, uidNext, inclusiveWindowEnd, returnedUids);

        // Assert
        Assert.Equal(ImapUid.Create(150), cursor.InspectedThroughUid);
        Assert.True(cursor.HasMore);
    }

}
