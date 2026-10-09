// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Grants;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Access.Grants;

/// <summary>Covers how a user's grant is read, remembered by the replica that read it, and forgotten when anything it was read from may have changed.</summary>
/// <remarks>
/// What the store returns is a union computed in PostgreSQL, and that half is the integration suite's. What is decided
/// here is the cache in front of it: a grant read once is answered from memory, a forget makes the next request read
/// again, and a read in flight across a forget is never remembered as though it were current.
/// </remarks>
public sealed class UserGrantResolverTests
{
    private static readonly ScopedGrant MailReader =
        ScopedGrant.Of([(MailFathomPermission.MailRead, AssignmentScope.User(SyntheticUser.Deployment))]);

    [Fact]
    public async Task ResolveAsync_AUser_AnswersWhatTheStoreHoldsForThem()
    {
        // Arrange
        var store = StoreHolding(MailReader);
        var resolver = new UserGrantResolver(store, new UserGrantCache());

        // Act
        var grant = await resolver.ResolveAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(MailReader, grant);
        await store.Received(1).ReadGrantOfAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>());
    }

    /// <summary>A grant this replica already computed is answered without a read, which is what keeps a request from costing a query per call.</summary>
    [Fact]
    public async Task ResolveAsync_TheSameUserTwice_ReadsTheStoreOnce()
    {
        // Arrange
        var store = StoreHolding(MailReader);
        var cache = new UserGrantCache();

        // Act
        await new UserGrantResolver(store, cache).ResolveAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);
        var second = await new UserGrantResolver(store, cache).ResolveAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(MailReader, second);
        await store.Received(1).ReadGrantOfAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>());
    }

    /// <summary>A grant is remembered per user, so one user's grant is never answered for another.</summary>
    [Fact]
    public async Task ResolveAsync_TwoUsers_ReadsEachOnesOwnGrant()
    {
        // Arrange
        var store = Substitute.For<IGrantStore>();
        store.ReadGrantOfAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>()).Returns(MailReader);
        store.ReadGrantOfAsync(SyntheticUser.Another, Arg.Any<CancellationToken>()).Returns(ScopedGrant.None);
        var resolver = new UserGrantResolver(store, new UserGrantCache());

        // Act
        await resolver.ResolveAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);
        var another = await resolver.ResolveAsync(SyntheticUser.Another, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(another.Permissions);
    }

    /// <summary>A change to a role, an assignment, a group, or an organization forgets what was computed, so the next request reads what was committed.</summary>
    [Fact]
    public async Task ResolveAsync_AfterAForget_ReadsTheStoreAgain()
    {
        // Arrange
        var revoked = ScopedGrant.None;
        var store = Substitute.For<IGrantStore>();
        store.ReadGrantOfAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>()).Returns(MailReader, revoked);
        var cache = new UserGrantCache();
        var resolver = new UserGrantResolver(store, cache);

        await resolver.ResolveAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Act
        cache.Forget();
        var afterwards = await resolver.ResolveAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(revoked, afterwards);
        await store.Received(2).ReadGrantOfAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A read that began before a change and finished after it was forgotten carries the grant from before the change.
    /// It answers the request that asked, and is not remembered, so the next request reads what was committed.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_AReadInFlightAcrossAForget_IsNotRemembered()
    {
        // Arrange
        var cache = new UserGrantCache();
        var store = Substitute.For<IGrantStore>();
        var inFlight = new TaskCompletionSource<ScopedGrant>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.ReadGrantOfAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>()).Returns(inFlight.Task, Task.FromResult(ScopedGrant.None));
        var resolver = new UserGrantResolver(store, cache);

        var stale = resolver.ResolveAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Act
        cache.Forget();
        inFlight.SetResult(MailReader);
        await stale;
        var next = await resolver.ResolveAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(next.Permissions);
    }

    [Fact]
    public async Task ResolveAsync_AUserThatNamesNobody_IsRejected()
    {
        // Arrange
        var resolver = new UserGrantResolver(StoreHolding(MailReader), new UserGrantCache());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            resolver.ResolveAsync(default, TestContext.Current.CancellationToken));
    }

    private static IGrantStore StoreHolding(ScopedGrant grant)
    {
        var store = Substitute.For<IGrantStore>();
        store.ReadGrantOfAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns(grant);

        return store;
    }
}
