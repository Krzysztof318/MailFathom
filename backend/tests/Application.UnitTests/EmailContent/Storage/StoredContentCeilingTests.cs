// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Storage;

public sealed class StoredContentCeilingTests
{
    [Fact]
    public async Task TryClaimAsync_WithinBothCeilings_TakesTheRoom()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore()
            .HoldingInTotal(200)
            .Holding(SyntheticMailUser.Deployment, 200);
        var ceiling = new StoredContentCeiling(claims, ceilingBytes: 1000, userCeilingBytes: 800);

        // Act
        var attempt = await ceiling.TryClaimAsync(
            SyntheticMailUser.Deployment,
            300,
            TestContext.Current.CancellationToken);

        // Assert
        await using var claim = attempt.Claim;
        Assert.NotNull(claim);
        Assert.Equal(StoredContentBound.None, attempt.ReachedBound);
        Assert.Equal(300, claim.ClaimedBytes);
        Assert.Equal(300, claims.ReservedBytes);
    }

    /// <summary>The ceiling holds across everything that shares the store, which is why the claim is not a process's.</summary>
    /// <remarks>
    /// Several folder work units run at the same moment by default, in one process and — above one replica — in
    /// several. Each of them would read the same pre-write occupancy, so a claim each of them held for itself would let
    /// every one of them find the same room and take it, and the deployment would pass its configured limit by as much
    /// as those runs were allowed to fetch between them.
    /// </remarks>
    [Fact]
    public async Task TryClaimAsync_SeveralRunsAgainstOneOccupancy_StopsAtTheCeilingRatherThanPerRun()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore();
        var ceiling = new StoredContentCeiling(claims, ceilingBytes: 1000);

        // Act
        var attempts = new List<StoredContentClaimAttempt>();

        for (var run = 0; run < 4; run++)
        {
            attempts.Add(await ceiling.TryClaimAsync(
                SyntheticMailUser.Deployment,
                400,
                TestContext.Current.CancellationToken));
        }

        // Assert
        Assert.Equal(2, attempts.Count(attempt => attempt.Claim is not null));
        Assert.Equal(800, claims.ReservedBytes);

        foreach (var attempt in attempts)
        {
            await DisposeAsync(attempt);
        }
    }

    /// <summary>A claim one replica took binds the next, which is what makes the ceiling the deployment's.</summary>
    /// <remarks>
    /// Two ceilings over one store are what two replicas are: each reads the same occupancy and neither can see what
    /// the other reserved since. Sharing the claims is the whole of the difference, and without it both would admit a
    /// payload the store has room for only once.
    /// </remarks>
    [Fact]
    public async Task TryClaimAsync_AClaimAnotherReplicaHolds_RefusesTheSecondPayload()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore();
        var oneReplica = new StoredContentCeiling(claims, ceilingBytes: 1000);
        var anotherReplica = new StoredContentCeiling(claims, ceilingBytes: 1000);

        // Act
        var first = await oneReplica.TryClaimAsync(
            SyntheticMailUser.Deployment,
            700,
            TestContext.Current.CancellationToken);
        var second = await anotherReplica.TryClaimAsync(
            SyntheticMailUser.Deployment,
            700,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(first.Claim);
        Assert.Null(second.Claim);
        Assert.Equal(StoredContentBound.Deployment, second.ReachedBound);

        await DisposeAsync(first);
    }

    [Fact]
    public async Task TryClaimAsync_ThePayloadWouldPassTheDeploymentCeiling_RefusesAndNamesIt()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore().HoldingInTotal(900);
        var ceiling = new StoredContentCeiling(claims, ceilingBytes: 1000, userCeilingBytes: 1000);

        // Act
        var attempt = await ceiling.TryClaimAsync(
            SyntheticMailUser.Deployment,
            200,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(attempt.Claim);
        Assert.Equal(StoredContentBound.Deployment, attempt.ReachedBound);
        Assert.Equal(0, claims.ReservedBytes);
    }

    [Fact]
    public async Task TryClaimAsync_OneUserAtTheirShareWhileTheDeploymentHasRoom_RefusesAndNamesTheUser()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore()
            .HoldingInTotal(300)
            .Holding(SyntheticMailUser.Deployment, 300);
        var ceiling = new StoredContentCeiling(claims, ceilingBytes: 10_000, userCeilingBytes: 400);

        // Act
        var attempt = await ceiling.TryClaimAsync(
            SyntheticMailUser.Deployment,
            200,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(attempt.Claim);
        Assert.Equal(StoredContentBound.User, attempt.ReachedBound);
    }

    /// <summary>Both refusals at once report the deployment's, because raising a share changes nothing while it is full.</summary>
    [Fact]
    public async Task TryClaimAsync_BothCeilingsReached_ReportsTheDeploymentRatherThanTheUser()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore()
            .HoldingInTotal(900)
            .Holding(SyntheticMailUser.Deployment, 900);
        var ceiling = new StoredContentCeiling(claims, ceilingBytes: 1000, userCeilingBytes: 1000);

        // Act
        var attempt = await ceiling.TryClaimAsync(
            SyntheticMailUser.Deployment,
            200,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentBound.Deployment, attempt.ReachedBound);
    }

    /// <summary>One user's share is theirs, so another user's payloads never count against it.</summary>
    [Fact]
    public async Task TryClaimAsync_AnotherUserHoldingTheirShare_LeavesThisUsersRoomWhole()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore()
            .HoldingInTotal(800)
            .Holding(SyntheticMailUser.Another, 800);
        var ceiling = new StoredContentCeiling(claims, ceilingBytes: 10_000, userCeilingBytes: 900);

        // Act
        var attempt = await ceiling.TryClaimAsync(
            SyntheticMailUser.Deployment,
            700,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(attempt.Claim);

        await DisposeAsync(attempt);
    }

    /// <summary>A claim nothing wrote is given back, so an abandoned fetch leaves the ceiling where it found it.</summary>
    [Fact]
    public async Task DisposeAsync_AClaimNothingWrote_GivesTheRoomBack()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore();
        var ceiling = new StoredContentCeiling(claims, ceilingBytes: 1000);
        var attempt = await ceiling.TryClaimAsync(
            SyntheticMailUser.Deployment,
            900,
            TestContext.Current.CancellationToken);

        // Act
        await DisposeAsync(attempt);

        // Assert
        Assert.Equal(0, claims.OutstandingClaimCount);
        Assert.Equal(0, claims.ReservedBytes);
    }

    /// <summary>Releasing twice releases once, because the expiry covers a holder that never reached disposal.</summary>
    [Fact]
    public async Task DisposeAsync_TheSameClaimTwice_ReleasesItOnce()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore();
        var ceiling = new StoredContentCeiling(claims, ceilingBytes: 1000);
        var attempt = await ceiling.TryClaimAsync(
            SyntheticMailUser.Deployment,
            400,
            TestContext.Current.CancellationToken);
        var claim = attempt.Claim;
        Assert.NotNull(claim);

        // Act
        await claim.DisposeAsync();
        await claim.DisposeAsync();

        // Assert
        // The release count rather than what is left: removing a claim is idempotent in the store, so the outstanding
        // count reads zero whether the release ran once or twice and would pass with the guard deleted.
        Assert.Equal(1, claims.ReleaseCount);
        Assert.Equal(0, claims.OutstandingClaimCount);
    }

    /// <summary>A replica that died holds room until its claim expires, and no longer.</summary>
    [Fact]
    public async Task TryClaimAsync_AClaimWhoseHolderStoppedAnswering_AdmitsAgainOnceItHasExpired()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore();
        var ceiling = new StoredContentCeiling(claims, ceilingBytes: 1000);
        var abandoned = await ceiling.TryClaimAsync(
            SyntheticMailUser.Deployment,
            900,
            TestContext.Current.CancellationToken);
        Assert.NotNull(abandoned.Claim);

        var refusedWhileHeld = await ceiling.TryClaimAsync(
            SyntheticMailUser.Deployment,
            900,
            TestContext.Current.CancellationToken);

        // Act
        claims.ExpireEveryClaim();

        var admittedAfterExpiry = await ceiling.TryClaimAsync(
            SyntheticMailUser.Deployment,
            900,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentBound.Deployment, refusedWhileHeld.ReachedBound);
        Assert.NotNull(admittedAfterExpiry.Claim);

        await DisposeAsync(admittedAfterExpiry);
    }

    /// <summary>A deployment that bounds neither population reaches no store at all.</summary>
    [Fact]
    public async Task TryClaimAsync_NeitherCeilingConfigured_GrantsWithoutClaimingAnything()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore().HoldingInTotal(long.MaxValue / 2);
        var ceiling = new StoredContentCeiling(claims, ceilingBytes: null);

        // Act
        var attempt = await ceiling.TryClaimAsync(
            SyntheticMailUser.Deployment,
            900,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(attempt.Claim);
        Assert.Equal(0, claims.OutstandingClaimCount);
        Assert.False(ceiling.IsConfigured);
        Assert.False(ceiling.IsConfiguredPerUser);

        await DisposeAsync(attempt);
    }

    [Fact]
    public async Task TryClaimAsync_AUserNamingNobody_IsRefused()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(new InMemoryStoredContentClaimStore(), ceilingBytes: 1000);

        // Act
        // Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => ceiling.TryClaimAsync(default, 100, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task TryClaimAsync_APayloadOfNoSize_IsRefused(long bytes)
    {
        // Arrange
        var ceiling = new StoredContentCeiling(new InMemoryStoredContentClaimStore(), ceilingBytes: 1000);

        // Act
        // Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ceiling.TryClaimAsync(SyntheticMailUser.Deployment, bytes, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_ACeilingOfNoSize_IsRefused()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore();

        // Act
        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new StoredContentCeiling(claims, ceilingBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StoredContentCeiling(claims, ceilingBytes: 1000, userCeilingBytes: -1));
    }

    [Fact]
    public void Constructor_WithoutAClaimStore_IsRefused()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentNullException>(() => new StoredContentCeiling(null!, ceilingBytes: 1000));
    }

    [Fact]
    public void Constructor_BothCeilingsConfigured_ReportsBothAsConfigured()
    {
        // Arrange
        var ceiling = new StoredContentCeiling(
            new InMemoryStoredContentClaimStore(),
            ceilingBytes: 1000,
            userCeilingBytes: 400);

        // Act
        // Assert
        Assert.True(ceiling.IsConfigured);
        Assert.True(ceiling.IsConfiguredPerUser);
    }

    private static ValueTask DisposeAsync(StoredContentClaimAttempt attempt) =>
        attempt.Claim?.DisposeAsync() ?? ValueTask.CompletedTask;
}
