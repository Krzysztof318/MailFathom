// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Storage;

public sealed class StoredContentCeilingTests
{
    [Fact]
    public void TryClaim_WithinTheCeiling_TakesTheRoomAndReportsTheNewLevel()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(1000);
        ceiling.Observe(measuredBytes: 200, claimMark: ceiling.ClaimMark);

        // Act
        using var claim = ceiling.TryClaim(300);

        // Assert
        Assert.NotNull(claim);
        Assert.Equal(300, claim.ClaimedBytes);
        Assert.Equal(500, ceiling.OccupiedBytes);
    }

    /// <summary>The ceiling holds across the runs that share it, which is the whole reason it is not a per-run value.</summary>
    /// <remarks>
    /// Several folder work units run at the same moment by default. Each of them measures the store before it writes,
    /// so a ceiling each of them evaluated for itself would let every one of them find the same room and take it — and
    /// the deployment would pass its configured limit by as much as those runs were allowed to fetch between them.
    /// </remarks>
    [Fact]
    public void TryClaim_SeveralRunsAgainstOneMeasurement_StopsAtTheCeilingRatherThanPerRun()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(1000);
        var measurement = ceiling.ClaimMark;
        ceiling.Observe(measuredBytes: 0, claimMark: measurement);

        // Act
        // Every run reads the same pre-write occupancy, exactly as concurrent runs would.
        var claims = Enumerable
            .Range(0, 4)
            .Select(_ => ceiling.TryClaim(400))
            .ToArray();

        // Assert
        Assert.Equal(2, claims.Count(claim => claim is not null));
        Assert.Equal(800, ceiling.OccupiedBytes);

        foreach (var claim in claims)
        {
            claim?.Dispose();
        }
    }

    [Fact]
    public void TryClaim_CeilingHasNoRoom_ReportsNoneWithoutMovingTheLevel()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(1000);
        ceiling.Observe(measuredBytes: 900, claimMark: ceiling.ClaimMark);

        // Act
        var claim = ceiling.TryClaim(200);

        // Assert
        Assert.Null(claim);
        Assert.Equal(900, ceiling.OccupiedBytes);
    }

    /// <summary>Room claimed for a payload that was never written goes back, so an abandoned fetch costs nothing.</summary>
    [Fact]
    public void Dispose_ClaimThatStoredNothing_ReturnsTheRoomToTheCeiling()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(1000);
        ceiling.Observe(measuredBytes: 100, claimMark: ceiling.ClaimMark);

        // Act
        using (var claim = ceiling.TryClaim(500))
        {
            Assert.NotNull(claim);
        }

        // Assert
        Assert.Equal(100, ceiling.OccupiedBytes);
    }

    /// <summary>A payload smaller than its advertised size gives the difference back.</summary>
    [Fact]
    public void Settle_StoredLessThanClaimed_KeepsOnlyWhatWasStored()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(1000);
        ceiling.Observe(measuredBytes: 100, claimMark: ceiling.ClaimMark);

        // Act
        using (var claim = ceiling.TryClaim(500))
        {
            claim!.Settle(200);
        }

        // Assert
        Assert.Equal(300, ceiling.OccupiedBytes);
    }

    /// <summary>A measurement taken while another run was writing keeps those bytes rather than overwriting them.</summary>
    /// <remarks>
    /// The reading describes the store as it was when the query ran, so bytes claimed after that moment are not in it.
    /// Adopting the reading alone would forget them, and the ceiling would drift below what storage actually holds by
    /// however much was written during every measurement.
    /// </remarks>
    [Fact]
    public void Observe_ClaimTakenWhileMeasuring_CarriesItOntoTheNewReading()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(10_000);
        var markBeforeMeasuring = ceiling.ClaimMark;

        // Act
        // A concurrent run claims and stores while the measurement is in flight.
        var concurrent = ceiling.TryClaim(700);
        concurrent!.Settle(700);
        ceiling.Observe(measuredBytes: 1000, claimMark: markBeforeMeasuring);

        // Assert
        Assert.Equal(1700, ceiling.OccupiedBytes);
    }

    /// <summary>A slower measurement does not overwrite a newer one that already landed.</summary>
    [Fact]
    public void Observe_MeasurementOlderThanOneAlreadyAdopted_IsDiscarded()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(10_000);
        var olderMark = ceiling.ClaimMark;
        using var claim = ceiling.TryClaim(500);
        claim!.Settle(500);
        var newerMark = ceiling.ClaimMark;

        // Act
        ceiling.Observe(measuredBytes: 4000, claimMark: newerMark);
        ceiling.Observe(measuredBytes: 100, claimMark: olderMark);

        // Assert
        Assert.Equal(4000, ceiling.OccupiedBytes);
    }

    /// <summary>With no ceiling configured nothing is ever refused, and the level is still tracked.</summary>
    [Fact]
    public void TryClaim_NoCeilingConfigured_AlwaysGrantsRoom()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(ceilingBytes: null);
        ceiling.Observe(measuredBytes: 500_000_000, claimMark: ceiling.ClaimMark);

        // Act
        using var claim = ceiling.TryClaim(100_000_000);

        // Assert
        Assert.False(ceiling.IsConfigured);
        Assert.NotNull(claim);
        Assert.Equal(600_000_000, ceiling.OccupiedBytes);
    }
}
