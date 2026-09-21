// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Orchestration;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers the reading every agent applies to an instant a model wrote back against the anchor it was given.</summary>
/// <remarks>
/// The anchor is deliberately not UTC in every case here. A written instant carries no zone, so a reading that
/// reattached nothing and one that reattached the right thing produce the same value at offset zero — which is exactly
/// the drift these tests exist to tell apart, and exactly what makes it invisible on a maintainer's own machine.
/// </remarks>
public sealed class AnchoredInstantTests
{
    private static readonly DateTimeOffset AskedAt = new(2026, 9, 9, 14, 30, 0, TimeSpan.FromHours(2));

    /// <summary>The whole point: a wall clock resolved against the anchor is the instant that person's day reached.</summary>
    [Fact]
    public void Read_AWrittenWallClock_BecomesThatInstantWhereTheTurnWasWritten()
    {
        // Act
        var instant = AnchoredInstant.Read("2026-09-07T00:00", AskedAt);

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.FromHours(2)), instant);
    }

    /// <summary>Writing <c>:00</c> onto an instant says nothing about whether the date was understood, so it is admitted.</summary>
    [Fact]
    public void Read_AWrittenWallClockCarryingSeconds_IsReadTheSameWay()
    {
        // Act
        var instant = AnchoredInstant.Read("2026-09-07T00:00:00", AskedAt);

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.FromHours(2)), instant);
    }

    /// <summary>The same wall clock against two anchors is two instants, which is what stating an anchor buys at all.</summary>
    [Fact]
    public void Read_OneWrittenWallClockAgainstTwoAnchors_IsTwoInstants()
    {
        // Arrange
        var warsaw = AskedAt;
        var tokyo = AskedAt.ToOffset(TimeSpan.FromHours(9));

        // Act
        var readInWarsaw = AnchoredInstant.Read("2026-09-07T00:00", warsaw);
        var readInTokyo = AnchoredInstant.Read("2026-09-07T00:00", tokyo);

        // Assert
        Assert.Equal(TimeSpan.FromHours(7), readInWarsaw - readInTokyo);
    }

    /// <summary>
    /// A model that wrote a zone invented one, because its turn stated none. An instant invented with authority is
    /// worse than a filter the caller can report back, so nothing is guessed and the reading answers nothing.
    /// </summary>
    [Theory]
    [InlineData("2026-09-07T00:00Z")]
    [InlineData("2026-09-07T00:00+05:00")]
    [InlineData("2026-09-07T00:00:00Z")]
    public void Read_AWrittenInstantCarryingAZone_IsNotRead(string written)
    {
        // Act
        var instant = AnchoredInstant.Read(written, AskedAt);

        // Assert
        Assert.Null(instant);
    }

    /// <summary>Everything else a model might write instead of the form it was asked for.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026-09-07")]
    [InlineData("7 September 2026")]
    [InlineData("last Monday")]
    [InlineData("2026-09-07 00:00")]
    public void Read_AnythingOtherThanTheStatedForm_IsNotRead(string? written)
    {
        // Act
        var instant = AnchoredInstant.Read(written, AskedAt);

        // Assert
        Assert.Null(instant);
    }

    /// <summary>
    /// Applying an offset to a local time within a day of either bound of <see cref="DateTime" /> raises rather than
    /// answering, so the two guarded days are refused. A model writing a year one date has misread its turn anyway.
    /// </summary>
    [Theory]
    [InlineData("0001-01-01T00:00")]
    [InlineData("9999-12-31T23:59")]
    public void Read_AWrittenInstantAtTheEndsOfTheRange_IsRefusedRatherThanRaising(string written)
    {
        // Act
        var instant = AnchoredInstant.Read(written, AskedAt);

        // Assert
        Assert.Null(instant);
    }
}
