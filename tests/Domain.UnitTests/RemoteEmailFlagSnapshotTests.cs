// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests;

/// <summary>Covers the one distinction the flag booleans cannot express on their own.</summary>
public sealed class RemoteEmailFlagSnapshotTests
{
    /// <summary>
    /// "The server reported no flags" and "nobody has looked yet" are different facts, and only the observation
    /// timestamp separates them. A reader that filters on a flag needs the difference, because an email nobody has
    /// looked at carries every flag unset.
    /// </summary>
    [Fact]
    public void NeverObserved_CarriesNoFlagsAndReportsThatNothingWasObserved()
    {
        // Act
        var snapshot = RemoteEmailFlagSnapshot.NeverObserved;

        // Assert
        Assert.Null(snapshot.ObservedAt);
        Assert.False(snapshot.WasObserved);
        Assert.Equal([false, false, false, false, false], Flags(snapshot));
    }

    [Fact]
    public void WasObserved_SnapshotCarryingATimestamp_ReportsThatAServerWasRead()
    {
        // Arrange
        var observedAt = new DateTimeOffset(2026, 7, 30, 6, 0, 0, TimeSpan.Zero);

        // Act
        var snapshot = new RemoteEmailFlagSnapshot(
            observedAt,
            IsSeen: true,
            IsAnswered: false,
            IsFlagged: true,
            IsDraft: false,
            IsDeleted: false);

        // Assert
        Assert.True(snapshot.WasObserved);
        Assert.Equal(observedAt, snapshot.ObservedAt);
        Assert.Equal([true, false, true, false, false], Flags(snapshot));
    }

    /// <summary>An unobserved snapshot reads the same whichever code path produced it.</summary>
    [Fact]
    public void Equality_ASnapshotBuiltLikeTheNeverObservedOne_IsThatSnapshot()
    {
        // Act
        var built = new RemoteEmailFlagSnapshot(
            ObservedAt: null,
            IsSeen: false,
            IsAnswered: false,
            IsFlagged: false,
            IsDraft: false,
            IsDeleted: false);

        // Assert
        Assert.Equal(RemoteEmailFlagSnapshot.NeverObserved, built);
    }

    private static bool[] Flags(RemoteEmailFlagSnapshot snapshot) =>
    [
        snapshot.IsSeen,
        snapshot.IsAnswered,
        snapshot.IsFlagged,
        snapshot.IsDraft,
        snapshot.IsDeleted,
    ];
}
