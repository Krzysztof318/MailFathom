// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Signals;
using MailFathom.Application.UnitTests.Discovery.Presentation;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Streaming;

/// <summary>Covers what one run writes down, what the hub is told about it, and what refusing a write does to the run.</summary>
/// <remarks>
/// What is asserted here is the contract a client depends on: that a sequence starts at one and never skips, that every
/// write is announced as a place to read from and never as the answer, and that a run whose write is refused stops
/// itself rather than composing into a record nobody will read.
/// </remarks>
public sealed class DiscoveryRunJournalTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider clock = new(Now);

    /// <summary>Every event names its run and its place in it, because a client renders in arrival order without sorting.</summary>
    [Fact]
    public async Task AppendAsync_SeveralEvents_StampsEachWithTheRunAndTheNextSequence()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        await using var signals = this.Signals(out _);
        using var journal = await this.OpenedAsync(store, signals);

        // Act
        await journal.AppendAsync(new DiscoveryRunStarted(), TestContext.Current.CancellationToken);
        await journal.AppendAsync(Progressed(1), TestContext.Current.CancellationToken);
        await journal.AppendAsync(Completed(), TestContext.Current.CancellationToken);

        // Assert
        var written = store.Written(journal.Id);
        Assert.Equal([1, 2, 3], written.Select(@event => @event.Sequence));
        Assert.All(written, @event => Assert.Equal(journal.Id, @event.RunId));
    }

    /// <summary>A write is announced as a run and a place to read from, and never as any part of what the run composed.</summary>
    [Fact]
    public async Task AppendAsync_ABlockComposedFromMail_AnnouncesOnlyTheRunAndTheSequenceItReached()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        await using var signals = this.Signals(out var channel);
        using var journal = await this.OpenedAsync(store, signals);
        await journal.AppendAsync(new DiscoveryRunStarted(), TestContext.Current.CancellationToken);

        // Act
        await journal.AppendAsync(
            new DiscoveryBlockComposed(PresentationPlanExample.EveryBlock()[0]),
            TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        var announced = Assert.Single(channel.Published, signal => signal.Sequence == 2);
        Assert.Equal(ClientSignalKind.DiscoveryRunAdvanced, announced.Kind);
        Assert.Equal(journal.Id, announced.Run);
        Assert.Equal(SyntheticMailUser.Deployment, announced.User);
        Assert.Equal(0, announced.Count);
        Assert.Empty(announced.Emails);
        Assert.Empty(announced.Flags);
        Assert.Null(announced.Headline);
        Assert.Null(announced.SecondLine);
        Assert.Null(announced.NotificationKind);
        Assert.Null(announced.Account);
        Assert.Null(announced.Folder);
    }

    /// <summary>An ending is the last thing a run writes, so a late report cannot appear after it.</summary>
    [Fact]
    public async Task AppendAsync_AfterTheRunHasEnded_WritesNothingFurther()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        await using var signals = this.Signals(out _);
        using var journal = await this.OpenedAsync(store, signals);
        await journal.AppendAsync(Completed(), TestContext.Current.CancellationToken);

        // Act
        var accepted = await journal.AppendAsync(Progressed(1), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(accepted);
        Assert.Single(store.Written(journal.Id));
        Assert.True(journal.HasEnded);
    }

    /// <summary>A run that reached the bound stops composing, and the ending it reserves room for is still written.</summary>
    [Fact]
    public async Task AppendAsync_PastTheEventBound_RefusesEverythingButTheEnding()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        await using var signals = this.Signals(out _);
        using var journal = await this.OpenedAsync(store, signals);

        for (var lookup = 0; lookup < DiscoveryRunBounds.MaximumEvents - 1; lookup++)
        {
            await journal.AppendAsync(Progressed(lookup), TestContext.Current.CancellationToken);
        }

        // Act
        var refused = await journal.AppendAsync(
            Progressed(DiscoveryRunBounds.MaximumEvents),
            TestContext.Current.CancellationToken);
        var ending = await journal.AppendAsync(
            new DiscoveryRunCompleted([PresentationLimitation.BlocksOmitted], [], MailAnsweringRunSpend.Nothing),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(refused);
        Assert.True(ending);
        Assert.Equal(DiscoveryRunBounds.MaximumEvents, store.Written(journal.Id).Count);
    }

    /// <summary>A stop recorded on another replica reaches this execution as a refused write, which is what stops the spending.</summary>
    [Fact]
    public async Task AppendAsync_AfterAnotherReplicaRecordedAStop_RefusesTheWriteAndStopsTheRun()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        await using var signals = this.Signals(out _);
        using var journal = await this.OpenedAsync(store, signals);
        await journal.AppendAsync(new DiscoveryRunStarted(), TestContext.Current.CancellationToken);
        await store.TryRequestStopAsync(
            journal.Id,
            SyntheticMailUser.Deployment,
            Now,
            TestContext.Current.CancellationToken);

        // Act
        var accepted = await journal.AppendAsync(Progressed(1), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(accepted);
        Assert.True(journal.Stopping.IsCancellationRequested);
        Assert.False(journal.HasEnded);
    }

    /// <summary>A stop that lands on this replica reaches the execution directly rather than a provider call later.</summary>
    [Fact]
    public async Task RequestStop_OnTheReplicaExecutingTheRun_CancelsWhatTheRunIsWaitingOn()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        await using var signals = this.Signals(out _);
        using var journal = await this.OpenedAsync(store, signals);

        // Act
        journal.RequestStop();

        // Assert
        Assert.True(journal.Stopping.IsCancellationRequested);
    }

    /// <summary>A run the deployment has forgotten is one nothing can be written to, and the execution stops rather than composing on.</summary>
    [Fact]
    public async Task AppendAsync_ForARunTheDeploymentHasForgotten_RefusesTheWriteAndStopsTheRun()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        await using var signals = this.Signals(out _);
        using var journal = new DiscoveryRunJournal(
            DiscoveryRunId.New(),
            SyntheticMailUser.Deployment,
            store,
            signals,
            this.clock);

        // Act
        var accepted = await journal.AppendAsync(new DiscoveryRunStarted(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(accepted);
        Assert.True(journal.Stopping.IsCancellationRequested);
    }

    private async Task<DiscoveryRunJournal> OpenedAsync(InMemoryDiscoveryRunStore store, ClientSignals signals)
    {
        var id = DiscoveryRunId.New();
        await store.TryOpenAsync(id, SyntheticMailUser.Deployment, Now, TestContext.Current.CancellationToken);

        return new DiscoveryRunJournal(id, SyntheticMailUser.Deployment, store, signals, this.clock);
    }

    private ClientSignals Signals(out RecordingClientSignalChannel channel)
    {
        channel = new RecordingClientSignalChannel();

        return new ClientSignals([channel], this.clock);
    }

    private static DiscoveryRetrievalProgressed Progressed(int lookupsRun) =>
        new(
            new DiscoveryRetrievalProgress(lookupsRun, LookupsRefused: 0, LookupsPlanned: 6, PassagesFound: 0),
            MailAnsweringRunSpend.Nothing);

    private static DiscoveryRunCompleted Completed() => new([], [], MailAnsweringRunSpend.Nothing);
}
