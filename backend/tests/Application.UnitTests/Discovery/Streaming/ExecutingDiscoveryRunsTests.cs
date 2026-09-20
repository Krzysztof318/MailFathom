// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Streaming;

/// <summary>Covers the one thing about a run that is still this replica's: the token a stop landing here reaches it through.</summary>
/// <remarks>
/// The second replica is what these are written against. A stop is answered from the record on whichever replica it
/// reached, and this is what makes it immediate on the one holding the run — so what has to hold is that a run this
/// replica is not executing is reported as not reached here rather than as no such run, and that somebody else's run is
/// not reachable through an identifier at all.
/// </remarks>
public sealed class ExecutingDiscoveryRunsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A stop that lands on the replica holding the run reaches the work rather than only the record.</summary>
    [Fact]
    public async Task TryRequestStop_ForARunThisReplicaIsExecuting_CancelsWhatTheRunIsWaitingOn()
    {
        // Arrange
        var executing = new ExecutingDiscoveryRuns();
        await using var signals = NoChannels();
        using var journal = await OpenedAsync(signals, SyntheticMailUser.Deployment);
        executing.Register(journal);

        // Act
        var reached = executing.TryRequestStop(journal.Id, SyntheticMailUser.Deployment);

        // Assert
        Assert.True(reached);
        Assert.True(journal.Stopping.IsCancellationRequested);
    }

    /// <summary>A run executing on another replica is not reached here, which is why the record rather than this decides the answer.</summary>
    [Fact]
    public void TryRequestStop_ForARunThisReplicaIsNotExecuting_ReachesNothing()
    {
        // Arrange
        var executing = new ExecutingDiscoveryRuns();

        // Act
        var reached = executing.TryRequestStop(DiscoveryRunId.New(), SyntheticMailUser.Deployment);

        // Assert
        Assert.False(reached);
    }

    /// <summary>An identifier is a bearer value that travelled to a client and back, so somebody else's run is not reachable through it.</summary>
    [Fact]
    public async Task TryRequestStop_ForSomebodyElsesRun_LeavesItExecuting()
    {
        // Arrange
        var executing = new ExecutingDiscoveryRuns();
        await using var signals = NoChannels();
        using var journal = await OpenedAsync(signals, SyntheticMailUser.Deployment);
        executing.Register(journal);

        // Act
        var reached = executing.TryRequestStop(journal.Id, MailUserId.Create(Guid.NewGuid()));

        // Assert
        Assert.False(reached);
        Assert.False(journal.Stopping.IsCancellationRequested);
    }

    /// <summary>A run this replica has finished with is no longer held, which is what keeps this bounded without a sweep.</summary>
    [Fact]
    public async Task Release_AfterTheRunFinished_StopsHoldingIt()
    {
        // Arrange
        var executing = new ExecutingDiscoveryRuns();
        await using var signals = NoChannels();
        using var journal = await OpenedAsync(signals, SyntheticMailUser.Deployment);
        executing.Register(journal);

        // Act
        executing.Release(journal.Id);

        // Assert
        Assert.Equal(0, executing.Count);
        Assert.False(executing.TryRequestStop(journal.Id, SyntheticMailUser.Deployment));
    }

    private static async Task<DiscoveryRunJournal> OpenedAsync(ClientSignals signals, MailUserId user)
    {
        var store = new InMemoryDiscoveryRunStore();
        var id = DiscoveryRunId.New();
        await store.TryOpenAsync(id, user, Now, TestContext.Current.CancellationToken);

        return new DiscoveryRunJournal(id, user, store, signals, new FakeTimeProvider(Now));
    }

    private static ClientSignals NoChannels() => new([], TimeProvider.System);
}
