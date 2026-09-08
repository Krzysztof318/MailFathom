// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Storage;

public sealed class StoredContentCeilingTests
{
    [Fact]
    public void TryClaim_WithinBothCeilings_TakesTheRoomAndReportsTheNewLevel()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(1000, 800);
        Measure(ceiling, SyntheticMailUser.Deployment, measuredBytes: 200, measuredUserBytes: 200);

        // Act
        var attempt = ceiling.TryClaim(SyntheticMailUser.Deployment, 300);

        // Assert
        using var claim = attempt.Claim;
        Assert.NotNull(claim);
        Assert.Equal(StoredContentBound.None, attempt.ReachedBound);
        Assert.Equal(300, claim.ClaimedBytes);
        Assert.Equal(500, ceiling.OccupiedBytes);
        Assert.Equal(500, ceiling.OccupiedBytesFor(SyntheticMailUser.Deployment));
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
        Measure(ceiling, SyntheticMailUser.Deployment, measuredBytes: 0, measuredUserBytes: 0);

        // Act
        // Every run reads the same pre-write occupancy, exactly as concurrent runs would.
        var attempts = Enumerable
            .Range(0, 4)
            .Select(_ => ceiling.TryClaim(SyntheticMailUser.Deployment, 400))
            .ToArray();

        // Assert
        Assert.Equal(2, attempts.Count(attempt => attempt.Claim is not null));
        Assert.Equal(800, ceiling.OccupiedBytes);

        foreach (var attempt in attempts)
        {
            attempt.Claim?.Dispose();
        }
    }

    /// <summary>A refusal names the wider fact, because raising one user's share would not answer a full instance.</summary>
    [Fact]
    public void TryClaim_TheDeploymentHasNoRoom_RefusesNamingTheDeployment()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(1000, 1000);
        Measure(ceiling, SyntheticMailUser.Deployment, measuredBytes: 900, measuredUserBytes: 100);

        // Act
        var attempt = ceiling.TryClaim(SyntheticMailUser.Deployment, 200);

        // Assert
        Assert.Null(attempt.Claim);
        Assert.Equal(StoredContentBound.Deployment, attempt.ReachedBound);
        Assert.Equal(900, ceiling.OccupiedBytes);
    }

    /// <summary>A user at their share is a different fact from a full instance, and the refusal has to say which.</summary>
    [Fact]
    public void TryClaim_TheUserIsAtTheirShare_RefusesNamingTheUserAndGivesTheDeploymentClaimBack()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(10_000, 1000);
        Measure(ceiling, SyntheticMailUser.Deployment, measuredBytes: 900, measuredUserBytes: 900);

        // Act
        var attempt = ceiling.TryClaim(SyntheticMailUser.Deployment, 200);

        // Assert
        Assert.Null(attempt.Claim);
        Assert.Equal(StoredContentBound.User, attempt.ReachedBound);

        // The deployment's level is taken first, so a refusal by the user has to hand it straight back: leaving it
        // charged would let one user meeting their share consume room the rest of the instance is entitled to.
        Assert.Equal(900, ceiling.OccupiedBytes);
        Assert.Equal(900, ceiling.OccupiedBytesFor(SyntheticMailUser.Deployment));
    }

    /// <summary>What one user's share bounds is that user's mail, and nobody else's run notices.</summary>
    /// <remarks>
    /// This is the whole point of bounding storage per user rather than only per deployment: an instance serving
    /// several people stops storing content for the one who has reached their share and keeps storing it whole for
    /// everybody else, instead of every mailbox on the deployment degrading together.
    /// </remarks>
    [Fact]
    public void TryClaim_OneUserIsAtTheirShare_LeavesAnotherUserStoringContentNormally()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(10_000, 1000);
        Measure(ceiling, SyntheticMailUser.Deployment, measuredBytes: 1200, measuredUserBytes: 1000);
        Measure(ceiling, SyntheticMailUser.Another, measuredBytes: 1200, measuredUserBytes: 200);

        // Act
        var refused = ceiling.TryClaim(SyntheticMailUser.Deployment, 300);
        var admitted = ceiling.TryClaim(SyntheticMailUser.Another, 300);

        // Assert
        using var claim = admitted.Claim;
        Assert.Null(refused.Claim);
        Assert.Equal(StoredContentBound.User, refused.ReachedBound);
        Assert.NotNull(claim);
        Assert.Equal(StoredContentBound.None, admitted.ReachedBound);
        Assert.Equal(500, ceiling.OccupiedBytesFor(SyntheticMailUser.Another));
        Assert.Equal(1000, ceiling.OccupiedBytesFor(SyntheticMailUser.Deployment));
    }

    /// <summary>Room claimed for a payload that was never written goes back, so an abandoned fetch costs nothing.</summary>
    [Fact]
    public void Dispose_ClaimThatStoredNothing_ReturnsTheRoomToBothLevels()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(1000, 1000);
        Measure(ceiling, SyntheticMailUser.Deployment, measuredBytes: 100, measuredUserBytes: 100);

        // Act
        using (var claim = ceiling.TryClaim(SyntheticMailUser.Deployment, 500).Claim)
        {
            Assert.NotNull(claim);
        }

        // Assert
        Assert.Equal(100, ceiling.OccupiedBytes);
        Assert.Equal(100, ceiling.OccupiedBytesFor(SyntheticMailUser.Deployment));
    }

    /// <summary>A payload smaller than its advertised size gives the difference back to both levels.</summary>
    [Fact]
    public void Settle_StoredLessThanClaimed_KeepsOnlyWhatWasStored()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(1000, 1000);
        Measure(ceiling, SyntheticMailUser.Deployment, measuredBytes: 100, measuredUserBytes: 100);

        // Act
        using (var claim = ceiling.TryClaim(SyntheticMailUser.Deployment, 500).Claim)
        {
            claim!.Settle(200);
        }

        // Assert
        Assert.Equal(300, ceiling.OccupiedBytes);
        Assert.Equal(300, ceiling.OccupiedBytesFor(SyntheticMailUser.Deployment));
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
        var ceiling = new StoredContentCeiling(10_000, 10_000);
        var markBeforeMeasuring = ceiling.MarkBefore(SyntheticMailUser.Deployment);

        // Act
        // A concurrent run claims and stores while the measurement is in flight.
        var concurrent = ceiling.TryClaim(SyntheticMailUser.Deployment, 700);
        concurrent.Claim!.Settle(700);
        ceiling.Observe(
            SyntheticMailUser.Deployment,
            measuredBytes: 1000,
            measuredUserBytes: 400,
            markBeforeMeasuring);

        // Assert
        Assert.Equal(1700, ceiling.OccupiedBytes);
        Assert.Equal(1100, ceiling.OccupiedBytesFor(SyntheticMailUser.Deployment));
    }

    /// <summary>A slower measurement does not overwrite a newer one that already landed.</summary>
    [Fact]
    public void Observe_MeasurementOlderThanOneAlreadyAdopted_IsDiscarded()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(10_000, 10_000);
        var olderMark = ceiling.MarkBefore(SyntheticMailUser.Deployment);
        using var claim = ceiling.TryClaim(SyntheticMailUser.Deployment, 500).Claim;
        claim!.Settle(500);
        var newerMark = ceiling.MarkBefore(SyntheticMailUser.Deployment);

        // Act
        ceiling.Observe(SyntheticMailUser.Deployment, measuredBytes: 4000, measuredUserBytes: 3000, newerMark);
        ceiling.Observe(SyntheticMailUser.Deployment, measuredBytes: 100, measuredUserBytes: 50, olderMark);

        // Assert
        Assert.Equal(4000, ceiling.OccupiedBytes);
        Assert.Equal(3000, ceiling.OccupiedBytesFor(SyntheticMailUser.Deployment));
    }

    /// <summary>With no ceiling configured nothing is ever refused, and both levels are still tracked.</summary>
    [Fact]
    public void TryClaim_NoCeilingConfigured_AlwaysGrantsRoom()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(ceilingBytes: null);
        Measure(
            ceiling,
            SyntheticMailUser.Deployment,
            measuredBytes: 500_000_000,
            measuredUserBytes: 500_000_000);

        // Act
        var attempt = ceiling.TryClaim(SyntheticMailUser.Deployment, 100_000_000);

        // Assert
        using var claim = attempt.Claim;
        Assert.False(ceiling.IsConfigured);
        Assert.False(ceiling.IsConfiguredPerUser);
        Assert.NotNull(claim);
        Assert.Equal(600_000_000, ceiling.OccupiedBytes);
        Assert.Equal(600_000_000, ceiling.OccupiedBytesFor(SyntheticMailUser.Deployment));
    }

    /// <summary>A deployment bounding only itself leaves every user free of a share, which is the default shape.</summary>
    [Fact]
    public void TryClaim_OnlyTheDeploymentIsBounded_AdmitsWhateverThatCeilingAdmits()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(1000);
        Measure(ceiling, SyntheticMailUser.Deployment, measuredBytes: 0, measuredUserBytes: 0);

        // Act
        var attempt = ceiling.TryClaim(SyntheticMailUser.Deployment, 900);

        // Assert
        using var claim = attempt.Claim;
        Assert.True(ceiling.IsConfigured);
        Assert.False(ceiling.IsConfiguredPerUser);
        Assert.NotNull(claim);
    }

    /// <summary>A ceiling is measured and claimed for a named user, so every member refuses one naming nobody.</summary>
    /// <remarks>
    /// Without the refusal an unnamed user would be given a level of its own, and bytes would be admitted against a
    /// ceiling for "nobody" — which reads as a working bound until somebody asks whose share it was.
    /// </remarks>
    [Fact]
    public void EveryMember_AUserNamingNobody_IsRefused()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(1000, 800);
        var nobody = default(MailUserId);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ceiling.MarkBefore(nobody));
        Assert.Throws<ArgumentException>(() => ceiling.OccupiedBytesFor(nobody));
        Assert.Throws<ArgumentException>(() => ceiling.Observe(nobody, 100, 100, default));
        Assert.Throws<ArgumentException>(() => ceiling.TryClaim(nobody, 100));
    }

    /// <summary>The refusal leaves nothing claimed, because a claim whose scope was never handed back is never released.</summary>
    [Fact]
    public void TryClaim_AUserNamingNobody_LeavesTheDeploymentLevelUntouched()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(1000, 800);
        Measure(ceiling, SyntheticMailUser.Deployment, measuredBytes: 200, measuredUserBytes: 200);

        // Act
        Assert.Throws<ArgumentException>(() => ceiling.TryClaim(default, 300));

        // Assert
        Assert.Equal(200, ceiling.OccupiedBytes);
    }

    [Fact]
    public void Constructor_ACeilingThatIsNotPositive_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new StoredContentCeiling(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StoredContentCeiling(1000, -1));
    }

    private static void Measure(
        StoredContentCeiling ceiling,
        MailUserId user,
        long measuredBytes,
        long measuredUserBytes) =>
        ceiling.Observe(user, measuredBytes, measuredUserBytes, ceiling.MarkBefore(user));
}
