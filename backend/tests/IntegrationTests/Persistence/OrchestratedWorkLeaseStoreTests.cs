// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the guarantee the work lease gets from PostgreSQL rather than from its own code.</summary>
/// <remarks>
/// <para>
/// The guarantee is structural and unreachable from a unit test: one scope has at most one holder, because the claim
/// inserts on the primary key and resolves the conflict against the recorded expiry in one statement. A conflict
/// target naming the wrong column, an expiry comparison lost in an edit, or a compare-and-set that stopped comparing
/// would all pass a unit test and would reach an operator as work done twice.
/// </para>
/// <para>
/// Every test names a scope of its own, because the suite shares one database and a scope is the primary key. Nothing
/// here drains the table first for that reason — a test cannot meet another test's row.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedWorkLeaseStoreTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>A lease long enough that nothing in a test expires underneath it.</summary>
    private static readonly TimeSpan HeldLease = TimeSpan.FromMinutes(10);

    /// <summary>A lease that has run out by the time the next statement reaches the database, which is how a crash looks.</summary>
    private static readonly TimeSpan ExpiredLease = TimeSpan.FromMilliseconds(1);

    /// <summary>How many replicas reach one scope at once, enough that a caller reliably loses.</summary>
    private const int ContendingReplicas = 8;

    [Fact]
    public async Task ClaimAsync_AScopeNothingHolds_TakesItAndRecordsTheHold()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scope = WorkScope.Create("work-lease-free");
        var holder = WorkLeaseHolder.NewHold();

        // Act
        var lease = await ClaimAsync(services, scope, holder, HeldLease, cancellationToken);

        // Assert
        Assert.NotNull(lease);
        Assert.True(lease.IsHeldBy(holder));
        var stored = await FindLeaseAsync(services, scope, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(holder.Value, stored.Holder);
    }

    /// <summary>
    /// Eight replicas claiming one scope at the same moment leave one holder. Only the primary key closes the window
    /// in which each of them reads a scope nothing holds, so this is what separates the constraint that exists from a
    /// check the application could have made instead.
    /// </summary>
    [Fact]
    public async Task ClaimAsync_ManyReplicasReachingOneScopeAtOnce_LeaveExactlyOneHolder()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scope = WorkScope.Create("work-lease-contended");

        // Act
        var attempts = await ConcurrentIdempotency.RunAsync(
            "Claiming one work scope from several replicas at once",
            ContendingReplicas,
            (_, token) => ClaimAsync(services, scope, WorkLeaseHolder.NewHold(), HeldLease, token),
            cancellationToken);

        // Assert
        attempts.AssertSingleEffect(attempts.Results.Count(lease => lease is not null));
        var granted = Assert.Single(attempts.Results.OfType<WorkLease>());
        var stored = await FindLeaseAsync(services, scope, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(granted.Holder.Value, stored.Holder);
    }

    /// <summary>A live lease is refused whoever asks, so a second replica waits rather than joining the work.</summary>
    [Fact]
    public async Task ClaimAsync_AScopeAnotherHolderStillHolds_IsRefusedAndLeavesTheHoldWhereItWas()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scope = WorkScope.Create("work-lease-held");
        var holder = WorkLeaseHolder.NewHold();
        await ClaimAsync(services, scope, holder, HeldLease, cancellationToken);

        // Act
        var second = await ClaimAsync(services, scope, WorkLeaseHolder.NewHold(), HeldLease, cancellationToken);

        // Assert
        Assert.Null(second);
        var stored = await FindLeaseAsync(services, scope, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(holder.Value, stored.Holder);
    }

    /// <summary>
    /// An expired lease is takeable, which is the whole of the crash recovery: nothing has to be told a replica died,
    /// because a lease its holder stopped renewing is indistinguishable from one whose holder is gone.
    /// </summary>
    [Fact]
    public async Task ClaimAsync_AScopeWhoseHolderStoppedRenewing_IsTakenByTheNextReplicaThatAsks()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scope = WorkScope.Create("work-lease-expired");
        var lostHolder = WorkLeaseHolder.NewHold();
        await ClaimAsync(services, scope, lostHolder, ExpiredLease, cancellationToken);
        var replacement = WorkLeaseHolder.NewHold();

        // Act
        var taken = await ClaimAsync(services, scope, replacement, HeldLease, cancellationToken);

        // Assert
        Assert.NotNull(taken);
        Assert.True(taken.IsHeldBy(replacement));
        var stored = await FindLeaseAsync(services, scope, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(replacement.Value, stored.Holder);
    }

    [Fact]
    public async Task RenewAsync_AHoldThatStillHoldsTheScope_MovesTheExpiryFurtherOut()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scope = WorkScope.Create("work-lease-renewed");
        var holder = WorkLeaseHolder.NewHold();
        var taken = await ClaimAsync(services, scope, holder, TimeSpan.FromMinutes(1), cancellationToken);

        // Act
        var renewed = await services.InScopeAsync(
            (serviceScope, token) => serviceScope.GetRequiredService<IWorkLeaseStore>()
                .RenewAsync(scope, holder, HeldLease, token),
            cancellationToken);

        // Assert
        Assert.NotNull(renewed);
        Assert.NotNull(taken);
        Assert.True(renewed.ExpiresAt > taken.ExpiresAt);
        var stored = await FindLeaseAsync(services, scope, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(renewed.ExpiresAt, stored.ExpiresAt);
    }

    /// <summary>
    /// A hold whose scope was taken over renews nothing, which is the signal that stops its work. Without the
    /// compare-and-set it would push out the expiry of the lease that replaced it and two replicas would run on.
    /// </summary>
    [Fact]
    public async Task RenewAsync_AHoldAnotherReplicaTookTheScopeFrom_RenewsNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scope = WorkScope.Create("work-lease-renewed-late");
        var lostHolder = WorkLeaseHolder.NewHold();
        await ClaimAsync(services, scope, lostHolder, ExpiredLease, cancellationToken);
        var replacement = WorkLeaseHolder.NewHold();
        var takenOver = await ClaimAsync(services, scope, replacement, HeldLease, cancellationToken);

        // Act
        var renewed = await services.InScopeAsync(
            (serviceScope, token) => serviceScope.GetRequiredService<IWorkLeaseStore>()
                .RenewAsync(scope, lostHolder, HeldLease, token),
            cancellationToken);

        // Assert
        Assert.Null(renewed);
        Assert.NotNull(takenOver);
        var stored = await FindLeaseAsync(services, scope, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(replacement.Value, stored.Holder);
        Assert.Equal(takenOver.ExpiresAt, stored.ExpiresAt);
    }

    /// <summary>A graceful shutdown gives the scope back at once rather than leaving the next replica to wait out an expiry.</summary>
    [Fact]
    public async Task ReleaseAsync_AHoldGivingItsScopeBack_LeavesItTakeableImmediately()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scope = WorkScope.Create("work-lease-released");
        var holder = WorkLeaseHolder.NewHold();
        await ClaimAsync(services, scope, holder, HeldLease, cancellationToken);

        // Act
        var released = await ReleaseAsync(services, scope, holder, cancellationToken);

        // Assert
        Assert.True(released);
        Assert.Null(await FindLeaseAsync(services, scope, cancellationToken));
        var next = WorkLeaseHolder.NewHold();
        Assert.NotNull(await ClaimAsync(services, scope, next, HeldLease, cancellationToken));
    }

    /// <summary>
    /// A release from a hold that was already reclaimed frees nothing. Without the holder predicate a replica shutting
    /// down late would hand away a scope another replica is working under, which is the one way a release could cause
    /// the duplication the lease exists to prevent.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_AHoldAnotherReplicaTookTheScopeFrom_FreesNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scope = WorkScope.Create("work-lease-released-late");
        var lostHolder = WorkLeaseHolder.NewHold();
        await ClaimAsync(services, scope, lostHolder, ExpiredLease, cancellationToken);
        var replacement = WorkLeaseHolder.NewHold();
        await ClaimAsync(services, scope, replacement, HeldLease, cancellationToken);

        // Act
        var released = await ReleaseAsync(services, scope, lostHolder, cancellationToken);

        // Assert
        Assert.False(released);
        var stored = await FindLeaseAsync(services, scope, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(replacement.Value, stored.Holder);
    }

    private static Task<WorkLease?> ClaimAsync(
        OrchestratedMailFathomServices services,
        WorkScope scope,
        WorkLeaseHolder holder,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (serviceScope, token) => serviceScope.GetRequiredService<IWorkLeaseStore>()
                .ClaimAsync(scope, holder, leaseDuration, token),
            cancellationToken);

    private static Task<bool> ReleaseAsync(
        OrchestratedMailFathomServices services,
        WorkScope scope,
        WorkLeaseHolder holder,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (serviceScope, token) => serviceScope.GetRequiredService<IWorkLeaseStore>()
                .ReleaseAsync(scope, holder, token),
            cancellationToken);

    private static Task<WorkLeaseEntity?> FindLeaseAsync(
        OrchestratedMailFathomServices services,
        WorkScope scope,
        CancellationToken cancellationToken)
    {
        var scopeValue = scope.Value;

        return services.InScopeAsync(
            (serviceScope, token) => serviceScope.GetRequiredService<MailFathomDbContext>()
                .WorkLeases
                .AsNoTracking()
                .SingleOrDefaultAsync(lease => lease.Scope == scopeValue, token),
            cancellationToken);
    }
}
