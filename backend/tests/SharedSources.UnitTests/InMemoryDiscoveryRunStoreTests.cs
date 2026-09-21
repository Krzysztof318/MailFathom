// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the fake every suite states a durable Discover run through, because a fault here reports a false result in all of them.</summary>
/// <remarks>
/// What has to hold is what the real statements settle: a sequence that starts at one and never skips, a cursor that
/// hands back only the tail and reads a cursor past the run as the beginning, a stop that refuses the next write but
/// never an ending, one person's bound on concurrent runs, and the two retention windows.
/// </remarks>
public sealed class InMemoryDiscoveryRunStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The sequence is the store's, starts at one, and never skips, which is the promise a cursor rests on.</summary>
    [Fact]
    public async Task AppendAsync_SeveralEvents_NumbersThemFromOneWithoutSkipping()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await OpenedAsync(store);

        // Act
        var sequences = new[]
        {
            await store.AppendAsync(id, new DiscoveryRunStarted(), Now, TestContext.Current.CancellationToken),
            await store.AppendAsync(id, Progressed(1), Now, TestContext.Current.CancellationToken),
            await store.AppendAsync(id, Progressed(2), Now, TestContext.Current.CancellationToken),
        };

        // Assert
        Assert.Equal([1L, 2L, 3L], sequences);
    }

    /// <summary>A read from a cursor hands back the tail alone, which is what makes an advance cost one read.</summary>
    [Fact]
    public async Task ReadAsync_FromACursor_ReturnsOnlyWhatCameAfterIt()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await WrittenRunAsync(store);

        // Act
        var read = await store.ReadAsync(
            id,
            SyntheticUser.Deployment,
            afterSequence: 1,
            Now,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(read);
        Assert.Equal([2L, 3L], read.Events.Select(@event => @event.Sequence));
        Assert.False(read.Running);
    }

    /// <summary>A cursor this run never reached belongs to some other run, so the reader is given this one from its beginning.</summary>
    [Fact]
    public async Task ReadAsync_FromACursorTheRunNeverReached_ReadsItFromTheBeginning()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await WrittenRunAsync(store);

        // Act
        var read = await store.ReadAsync(
            id,
            SyntheticUser.Deployment,
            afterSequence: 99,
            Now,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(read);
        Assert.Equal([1L, 2L, 3L], read.Events.Select(@event => @event.Sequence));
    }

    /// <summary>A run belonging to somebody else reads as no such run, exactly as one that never existed does.</summary>
    [Fact]
    public async Task ReadAsync_ForSomebodyElsesRun_ReportsNoSuchRun()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await WrittenRunAsync(store);

        // Act
        var read = await store.ReadAsync(
            id,
            SyntheticUser.Another,
            afterSequence: 0,
            Now,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(read);
    }

    /// <summary>A recorded stop refuses the next ordinary write, which is how it reaches an execution on another replica.</summary>
    [Fact]
    public async Task AppendAsync_AfterAStopWasRecorded_RefusesAnythingButAnEnding()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await OpenedAsync(store);
        await store.AppendAsync(id, new DiscoveryRunStarted(), Now, TestContext.Current.CancellationToken);
        await store.TryRequestStopAsync(id, SyntheticUser.Deployment, Now, TestContext.Current.CancellationToken);

        // Act
        var refused = await store.AppendAsync(id, Progressed(1), Now, TestContext.Current.CancellationToken);
        var ending = await store.AppendAsync(
            id,
            new DiscoveryRunFailed(DiscoveryRunFailure.Cancelled, MailAnsweringRunSpend.Nothing),
            Now,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(refused);
        Assert.Equal(2L, ending);
        Assert.True(store.WasAskedToStop(id));
    }

    /// <summary>The ninth run one person has executing is refused, and an ended run is not one of them.</summary>
    [Fact]
    public async Task TryOpenAsync_PastWhatOnePersonMayRunAtOnce_RefusesUntilOneEnds()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var opened = new List<DiscoveryRunId>();

        for (var run = 0; run < DiscoveryRunBounds.MaximumConcurrentRunsPerUser; run++)
        {
            opened.Add(await OpenedAsync(store));
        }

        // Act
        var admittedNinth = await store.TryOpenAsync(
            DiscoveryRunId.New(),
            SyntheticUser.Deployment,
            Now,
            TestContext.Current.CancellationToken);

        await store.AppendAsync(
            opened[0],
            new DiscoveryRunCompleted([], [], MailAnsweringRunSpend.Nothing),
            Now,
            TestContext.Current.CancellationToken);

        var admittedAfterOneEnded = await store.TryOpenAsync(
            DiscoveryRunId.New(),
            SyntheticUser.Deployment,
            Now,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(admittedNinth);
        Assert.True(admittedAfterOneEnded);
    }

    /// <summary>The bound is one person's, so another person's questions are not refused by this one's.</summary>
    [Fact]
    public async Task TryOpenAsync_ForASecondPersonAtTheFirstsBound_AdmitsTheirRun()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();

        for (var run = 0; run < DiscoveryRunBounds.MaximumConcurrentRunsPerUser; run++)
        {
            await OpenedAsync(store);
        }

        // Act
        var admitted = await store.TryOpenAsync(
            DiscoveryRunId.New(),
            SyntheticUser.Another,
            Now,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(admitted);
    }

    /// <summary>An ended run goes a retention window after it was last read, which is the storage limitation on what it composed.</summary>
    [Fact]
    public async Task RemoveForgottenAsync_ARunThatEndedAndNobodyCameBackFor_ForgetsIt()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var id = await WrittenRunAsync(store);

        // Act
        var forgotten = await store.RemoveForgottenAsync(
            Now + DiscoveryRunBounds.RetentionAfterLastUse,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, forgotten);
        Assert.Null(await store.ReadAsync(
            id,
            SyntheticUser.Deployment,
            afterSequence: 0,
            Now,
            TestContext.Current.CancellationToken));
    }

    /// <summary>A run whose execution never reported goes at the wider ceiling, so it cannot hold one of a person's slots forever.</summary>
    [Fact]
    public async Task RemoveForgottenAsync_ARunNothingEverEnded_ForgetsItAtTheWiderCeiling()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        await OpenedAsync(store);

        // Act
        var beforeTheCeiling = await store.RemoveForgottenAsync(
            Now + DiscoveryRunBounds.RetentionAfterLastUse,
            TestContext.Current.CancellationToken);
        var atTheCeiling = await store.RemoveForgottenAsync(
            Now + DiscoveryRunBounds.MaximumDuration + DiscoveryRunBounds.RetentionAfterLastUse,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, beforeTheCeiling);
        Assert.Equal(1, atTheCeiling);
        Assert.Equal(0, store.HeldCount);
    }

    private static async Task<DiscoveryRunId> OpenedAsync(InMemoryDiscoveryRunStore store)
    {
        var id = DiscoveryRunId.New();
        await store.TryOpenAsync(id, SyntheticUser.Deployment, Now, TestContext.Current.CancellationToken);

        return id;
    }

    private static async Task<DiscoveryRunId> WrittenRunAsync(InMemoryDiscoveryRunStore store)
    {
        var id = await OpenedAsync(store);
        await store.AppendAsync(id, new DiscoveryRunStarted(), Now, TestContext.Current.CancellationToken);
        await store.AppendAsync(id, Progressed(1), Now, TestContext.Current.CancellationToken);
        await store.AppendAsync(
            id,
            new DiscoveryRunCompleted([], [], MailAnsweringRunSpend.Nothing),
            Now,
            TestContext.Current.CancellationToken);

        return id;
    }

    private static DiscoveryRetrievalProgressed Progressed(int lookupsRun) =>
        new(
            new DiscoveryRetrievalProgress(lookupsRun, LookupsRefused: 0, LookupsPlanned: 6, PassagesFound: 0),
            MailAnsweringRunSpend.Nothing);
}
