// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the shared claim store, whose arithmetic every stored-content ceiling test is measured against.</summary>
public sealed class InMemoryStoredContentClaimStoreTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ClaimAsync_RoomUnderBothCeilings_GrantsAClaimAndReservesItsBytes()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore().HoldingInTotal(100).Holding(SyntheticMailUser.Deployment, 40);

        // Act
        var record = await claims.ClaimAsync(
            SyntheticMailUser.Deployment,
            60,
            new StoredContentCeilings(DeploymentBytes: 1000, UserBytes: 500),
            Lifetime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(record.IsGranted);
        Assert.NotNull(record.ClaimId);
        Assert.Equal(60L, claims.ReservedBytes);
        Assert.Equal(1, claims.OutstandingClaimCount);
    }

    [Fact]
    public async Task ClaimAsync_TheDeploymentsCeilingHasNoRoom_RefusesAndNamesTheDeployment()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore().HoldingInTotal(950);

        // Act
        var record = await claims.ClaimAsync(
            SyntheticMailUser.Deployment,
            100,
            new StoredContentCeilings(DeploymentBytes: 1000, UserBytes: null),
            Lifetime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(record.IsGranted);
        Assert.Equal(StoredContentBound.Deployment, record.ReachedBound);
        Assert.Equal(0, claims.OutstandingClaimCount);
    }

    [Fact]
    public async Task ClaimAsync_TheUsersShareHasNoRoom_RefusesAndNamesTheUser()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore().Holding(SyntheticMailUser.Deployment, 450);

        // Act
        var record = await claims.ClaimAsync(
            SyntheticMailUser.Deployment,
            100,
            new StoredContentCeilings(DeploymentBytes: 100_000, UserBytes: 500),
            Lifetime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentBound.User, record.ReachedBound);
    }

    /// <summary>A claim another replica holds is what this one is refused against, which is the whole point of sharing it.</summary>
    [Fact]
    public async Task ClaimAsync_AClaimAnotherReplicaHolds_LeavesNoRoomForTheSecondPayload()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore();
        var ceilings = new StoredContentCeilings(DeploymentBytes: 1000, UserBytes: null);
        await claims.ClaimAsync(SyntheticMailUser.Deployment, 900, ceilings, Lifetime, TestContext.Current.CancellationToken);

        // Act
        var record = await claims.ClaimAsync(
            SyntheticMailUser.Another,
            200,
            ceilings,
            Lifetime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentBound.Deployment, record.ReachedBound);
    }

    /// <summary>One user's claims never reach another's share, which is what a per-user ceiling means.</summary>
    [Fact]
    public async Task ClaimAsync_AnotherUsersClaimIsOutstanding_LeavesThisUsersShareWhole()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore();
        var ceilings = new StoredContentCeilings(DeploymentBytes: 100_000, UserBytes: 500);
        await claims.ClaimAsync(SyntheticMailUser.Another, 450, ceilings, Lifetime, TestContext.Current.CancellationToken);

        // Act
        var record = await claims.ClaimAsync(
            SyntheticMailUser.Deployment,
            450,
            ceilings,
            Lifetime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(record.IsGranted);
    }

    /// <summary>An expired claim binds nothing, which is what a replica that stopped answering leaves behind.</summary>
    [Fact]
    public async Task ClaimAsync_EveryOutstandingClaimHasExpired_AdmitsThePayloadAgain()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore();
        var ceilings = new StoredContentCeilings(DeploymentBytes: 1000, UserBytes: null);
        await claims.ClaimAsync(SyntheticMailUser.Deployment, 900, ceilings, Lifetime, TestContext.Current.CancellationToken);

        // Act
        claims.ExpireEveryClaim();
        var record = await claims.ClaimAsync(
            SyntheticMailUser.Deployment,
            900,
            ceilings,
            Lifetime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(record.IsGranted);
        Assert.Equal(900L, claims.ReservedBytes);
        Assert.Equal(1, claims.OutstandingClaimCount);
    }

    [Fact]
    public async Task ReleaseAsync_AClaimThatWasGranted_GivesItsRoomBack()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore();
        var ceilings = new StoredContentCeilings(DeploymentBytes: 1000, UserBytes: null);
        var record = await claims.ClaimAsync(
            SyntheticMailUser.Deployment,
            900,
            ceilings,
            Lifetime,
            TestContext.Current.CancellationToken);

        // Act
        await claims.ReleaseAsync(record.ClaimId!.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, claims.OutstandingClaimCount);
        Assert.Equal(0L, claims.ReservedBytes);
        Assert.Equal(1, claims.ReleaseCount);
    }

    /// <summary>The count is what a caller asked for rather than what it met, which is what a release-once claim is proved by.</summary>
    [Fact]
    public async Task ReleaseAsync_AClaimThatWasAlreadyReleased_CountsBothAsks()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore();
        var record = await claims.ClaimAsync(
            SyntheticMailUser.Deployment,
            900,
            new StoredContentCeilings(DeploymentBytes: 1000, UserBytes: null),
            Lifetime,
            TestContext.Current.CancellationToken);

        // Act
        await claims.ReleaseAsync(record.ClaimId!.Value, TestContext.Current.CancellationToken);
        await claims.ReleaseAsync(record.ClaimId!.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, claims.ReleaseCount);
        Assert.Equal(0, claims.OutstandingClaimCount);
    }

    /// <summary>A deployment that bounds neither population reaches nothing, so nothing is reserved and nothing refused.</summary>
    [Fact]
    public async Task ClaimAsync_NeitherPopulationIsBounded_AnswersUnboundedWithoutReservingAnything()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore().HoldingInTotal(long.MaxValue / 2);

        // Act
        var record = await claims.ClaimAsync(
            SyntheticMailUser.Deployment,
            900,
            new StoredContentCeilings(DeploymentBytes: null, UserBytes: null),
            Lifetime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentClaimRecord.Unbounded, record);
        Assert.Equal(0, claims.OutstandingClaimCount);
    }

    [Fact]
    public async Task ClaimAsync_AnArgumentNoClaimCouldBeMadeFor_IsRefused()
    {
        // Arrange
        var claims = new InMemoryStoredContentClaimStore();
        var ceilings = new StoredContentCeilings(DeploymentBytes: 1000, UserBytes: null);

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => claims.ClaimAsync(SyntheticMailUser.Deployment, 0, ceilings, Lifetime, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => claims.ClaimAsync(SyntheticMailUser.Deployment, 10, ceilings, TimeSpan.Zero, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => claims.ClaimAsync(default, 10, ceilings, Lifetime, TestContext.Current.CancellationToken));
    }
}
