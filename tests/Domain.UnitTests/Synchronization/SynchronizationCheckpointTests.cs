// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Synchronization;
using Xunit;

namespace MailFathom.Domain.UnitTests.Synchronization;

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

    /// <summary>
    /// The compare guards the progress that decides which mail is fetched. A modification sequence is an optimization
    /// hint beside it, and widening the compare to include one would turn a harmless race into a refused advance.
    /// </summary>
    [Fact]
    public void RepresentsSameProgressAs_SameUidProgressWithADifferentModificationSequence_ReturnsTrue()
    {
        // Arrange
        var checkpoint = Checkpoint() with { ReconciledThroughModSeq = 91UL };
        var otherCheckpoint = Checkpoint() with { ReconciledThroughModSeq = 40UL };

        // Act
        var representsSameProgress = checkpoint.RepresentsSameProgressAs(otherCheckpoint);

        // Assert
        Assert.True(representsSameProgress);
    }

    /// <summary>A folder nobody has reconciled by sequence has none, which is also how a checkpoint stored before sequences existed reads.</summary>
    [Fact]
    public void None_UidValidityScope_CarriesNoModificationSequence()
    {
        // Act
        var checkpoint = SynchronizationCheckpoint.None(ImapUidValidity.Create(5));

        // Assert
        Assert.Null(checkpoint.ReconciledThroughModSeq);
    }

    [Fact]
    public void ReconciledThrough_NoSequenceRecordedYet_RecordsTheOneThePassReached()
    {
        // Act
        var checkpoint = Checkpoint().ReconciledThrough(91UL);

        // Assert
        Assert.Equal(91UL, checkpoint.ReconciledThroughModSeq);
    }

    /// <summary>
    /// Two runs can complete a pass over the same folder, and the one that finishes second may have read the folder
    /// first. Keeping the later sequence costs the earlier run nothing, because everything it observed is covered by it.
    /// </summary>
    [Fact]
    public void ReconciledThrough_SequenceOlderThanTheRecordedOne_KeepsTheRecordedOne()
    {
        // Arrange
        var checkpoint = Checkpoint().ReconciledThrough(91UL);

        // Act
        var reconciledCheckpoint = checkpoint.ReconciledThrough(40UL);

        // Assert
        Assert.Equal(91UL, reconciledCheckpoint.ReconciledThroughModSeq);
        Assert.Same(checkpoint, reconciledCheckpoint);
    }

    /// <summary>The forward pass advances UID progress and must not drop what the backward pass has already covered.</summary>
    [Fact]
    public void AdvanceTo_CheckpointCarryingAModificationSequence_KeepsIt()
    {
        // Arrange
        var checkpoint = Checkpoint().ReconciledThrough(91UL);

        // Act
        var advancedCheckpoint = checkpoint.AdvanceTo(
            ImapUid.Create(20),
            new DateTimeOffset(2026, 7, 25, 13, 0, 0, TimeSpan.Zero));

        // Assert
        Assert.Equal(91UL, advancedCheckpoint.ReconciledThroughModSeq);
        Assert.Equal(20U, advancedCheckpoint.LastSeenUid!.Value.Value);
    }

    private static SynchronizationCheckpoint Checkpoint() => new(
        ImapUidValidity.Create(5),
        ImapUid.Create(10),
        new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));
}
