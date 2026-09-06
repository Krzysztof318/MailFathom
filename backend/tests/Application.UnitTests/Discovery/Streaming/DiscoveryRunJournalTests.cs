// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Streaming;

/// <summary>Covers the ordering, the bound, and the resumption a client renders one run from.</summary>
/// <remarks>
/// What is asserted here is the contract a reconnecting client depends on: that a sequence starts at one and never
/// skips, that reading from a stated point hands back exactly what came after it, and that a run reaching its bound
/// still publishes an ending rather than leaving a reader waiting.
/// </remarks>
public sealed class DiscoveryRunJournalTests
{
    /// <summary>Every event names its run and its place in it, because a client renders in arrival order without sorting.</summary>
    [Fact]
    public void Append_SeveralEvents_StampsEachWithTheRunAndTheNextSequence()
    {
        // Arrange
        var journal = NewJournal();

        // Act
        journal.Append(new DiscoveryRunStarted());
        journal.Append(Progressed(1));
        journal.Append(new DiscoveryRunCompleted([]));

        // Assert
        var published = Published(journal);
        Assert.Equal([1, 2, 3], published.Select(@event => @event.Sequence));
        Assert.All(published, @event => Assert.Equal(journal.Id, @event.RunId));
    }

    /// <summary>An ending is the last thing a run publishes, so a late report cannot appear after it.</summary>
    [Fact]
    public void Append_AfterTheRunHasEnded_PublishesNothingFurther()
    {
        // Arrange
        var journal = NewJournal();
        journal.Append(new DiscoveryRunCompleted([]));

        // Act
        var accepted = journal.Append(Progressed(1));

        // Assert
        Assert.False(accepted);
        Assert.Equal(1, journal.PublishedCount);
        Assert.True(journal.HasEnded);
    }

    /// <summary>A run that reached the bound stops composing, and the ending it reserves room for still reaches the client.</summary>
    [Fact]
    public void Append_PastTheEventBound_RefusesEverythingButTheEnding()
    {
        // Arrange
        var journal = NewJournal();
        Enumerable.Range(0, DiscoveryRunBounds.MaximumEvents - 1)
            .ToList()
            .ForEach(lookup => journal.Append(Progressed(lookup)));

        // Act
        var refused = journal.Append(Progressed(DiscoveryRunBounds.MaximumEvents));
        var ending = journal.Append(new DiscoveryRunCompleted([PresentationLimitation.BlocksOmitted]));

        // Assert
        Assert.False(refused);
        Assert.True(ending);
        Assert.Equal(DiscoveryRunBounds.MaximumEvents, journal.PublishedCount);
    }

    /// <summary>Reading from where a dropped connection left off hands back exactly what it missed, which is the whole of resumption.</summary>
    [Fact]
    public async Task ReadFromAsync_FromAStatedPoint_ReplaysOnlyWhatCameAfterIt()
    {
        // Arrange
        var journal = NewJournal();
        journal.Append(new DiscoveryRunStarted());
        journal.Append(Progressed(1));
        journal.Append(new DiscoveryRunCompleted([]));

        // Act
        var resumed = await journal
            .ReadFromAsync(afterSequence: 1, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([2, 3], resumed.Select(@event => @event.Sequence));
    }

    /// <summary>A place this run never reached belongs to some other run, so the client is given this one from its beginning.</summary>
    [Fact]
    public async Task ReadFromAsync_APointTheRunNeverReached_ReadsTheRunFromItsBeginning()
    {
        // Arrange
        var journal = NewJournal();
        journal.Append(new DiscoveryRunStarted());
        journal.Append(new DiscoveryRunCompleted([]));

        // Act
        var resumed = await journal
            .ReadFromAsync(afterSequence: 9, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([1, 2], resumed.Select(@event => @event.Sequence));
    }

    /// <summary>A reader attached while the run is executing is handed each event as it is published rather than at the end.</summary>
    [Fact]
    public async Task ReadFromAsync_WhileTheRunIsExecuting_YieldsAnEventBeforeTheRunEnds()
    {
        // Arrange
        var journal = NewJournal();
        var reader = journal
            .ReadFromAsync(afterSequence: 0, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        // Act
        journal.Append(new DiscoveryRunStarted());
        var arrived = await reader.MoveNextAsync();

        // Assert
        Assert.True(arrived);
        Assert.IsType<DiscoveryRunStarted>(reader.Current);
        Assert.False(journal.HasEnded);

        journal.Append(new DiscoveryRunCompleted([]));
        await reader.DisposeAsync();
    }

    private static DiscoveryRunJournal NewJournal() =>
        new(DiscoveryRunId.New(), SyntheticMailOwner.Deployment);

    private static DiscoveryRetrievalProgressed Progressed(int lookupsRun) =>
        new(new DiscoveryRetrievalProgress(lookupsRun, LookupsRefused: 0, LookupsPlanned: 6, PassagesFound: 0));

    private static DiscoveryRunEvent[] Published(DiscoveryRunJournal journal) =>
        [
            .. journal.ReadFromAsync(afterSequence: 0, TestContext.Current.CancellationToken)
                .ToBlockingEnumerable(TestContext.Current.CancellationToken),
        ];
}
