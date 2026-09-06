// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Streaming;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Streaming;

/// <summary>Covers who may read a run, how many this process holds, and how long it holds one.</summary>
public sealed class DiscoveryRunRegistryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider timeProvider = new(Now);

    /// <summary>A run is addressed by its identifier the moment it is opened, which is what the asking request answers with.</summary>
    [Fact]
    public void TryOpen_ARunForAnOwner_IsFoundBackByThatOwner()
    {
        // Arrange
        var registry = new DiscoveryRunRegistry(this.timeProvider);

        // Act
        var opened = registry.TryOpen(SyntheticMailOwner.Deployment, out var journal);

        // Assert
        Assert.True(opened);
        Assert.NotNull(journal);
        Assert.True(registry.TryFind(journal.Id, SyntheticMailOwner.Deployment, out var found));
        Assert.Same(journal, found);
    }

    /// <summary>A run holds one person's mail, so an identifier alone is not what decides who is shown it.</summary>
    [Fact]
    public void TryFind_ARunAnotherOwnerStarted_ReportsNoSuchRun()
    {
        // Arrange
        var registry = new DiscoveryRunRegistry(this.timeProvider);
        registry.TryOpen(SyntheticMailOwner.Deployment, out var journal);
        Assert.NotNull(journal);

        // Act
        var found = registry.TryFind(journal.Id, SyntheticMailOwner.Another, out var reachable);

        // Assert
        Assert.False(found);
        Assert.Null(reachable);
    }

    /// <summary>A client looping over the start route is refused rather than allowed to fill this process's memory.</summary>
    [Fact]
    public void TryOpen_BeyondWhatThisProcessHolds_RefusesTheRun()
    {
        // Arrange
        var registry = new DiscoveryRunRegistry(this.timeProvider);
        Enumerable.Range(0, DiscoveryRunBounds.MaximumConcurrentRuns)
            .ToList()
            .ForEach(run => registry.TryOpen(SyntheticMailOwner.Deployment, out _));

        // Act
        var opened = registry.TryOpen(SyntheticMailOwner.Deployment, out var refused);

        // Assert
        Assert.False(opened);
        Assert.Null(refused);
        Assert.Equal(DiscoveryRunBounds.MaximumConcurrentRuns, registry.HeldCount);
    }

    /// <summary>A run nobody came back for is forgotten, which is what keeps an abandoned one from being held for the life of the process.</summary>
    [Fact]
    public void TryFind_AnEndedRunPastItsRetention_ReportsNoSuchRun()
    {
        // Arrange
        var registry = new DiscoveryRunRegistry(this.timeProvider);
        registry.TryOpen(SyntheticMailOwner.Deployment, out var journal);
        Assert.NotNull(journal);
        journal.Append(new DiscoveryRunCompleted([], []));
        registry.MarkEnded(journal.Id);

        // Act
        this.timeProvider.Advance(DiscoveryRunBounds.RetentionAfterLastUse);

        // Assert
        Assert.False(registry.TryFind(journal.Id, SyntheticMailOwner.Deployment, out _));
    }

    /// <summary>A run still working is kept past the window an ended one is kept for, because a client reconnecting to it must not be told it never existed.</summary>
    [Fact]
    public void TryFind_ARunStillExecutingPastTheRetentionWindow_StillFindsIt()
    {
        // Arrange
        var registry = new DiscoveryRunRegistry(this.timeProvider);
        registry.TryOpen(SyntheticMailOwner.Deployment, out var journal);
        Assert.NotNull(journal);

        // Act
        this.timeProvider.Advance(DiscoveryRunBounds.RetentionAfterLastUse);

        // Assert
        Assert.True(registry.TryFind(journal.Id, SyntheticMailOwner.Deployment, out _));
    }

    /// <summary>
    /// The slot is given back whatever happened to the run. Every run this process starts is stopped at the longest one
    /// may take and ends there, so one still unended past that and its retention is a run whose execution never
    /// reported — and holding it would spend a slot of eight until the process was restarted.
    /// </summary>
    [Fact]
    public void TryOpen_ARunThatNeverEndedPastEveryWindow_ForgetsItAndOpensAnother()
    {
        // Arrange
        var registry = new DiscoveryRunRegistry(this.timeProvider);
        Enumerable.Range(0, DiscoveryRunBounds.MaximumConcurrentRuns)
            .ToList()
            .ForEach(run => registry.TryOpen(SyntheticMailOwner.Deployment, out _));

        // Act
        this.timeProvider.Advance(DiscoveryRunBounds.MaximumDuration + DiscoveryRunBounds.RetentionAfterLastUse);

        // Assert
        Assert.True(registry.TryOpen(SyntheticMailOwner.Deployment, out _));
        Assert.Equal(1, registry.HeldCount);
    }

    /// <summary>Reading a run is use, so a client that is still watching is never forgotten out from under itself.</summary>
    [Fact]
    public void TryFind_AnEndedRunARecentReadTouched_StillFindsIt()
    {
        // Arrange
        var registry = new DiscoveryRunRegistry(this.timeProvider);
        registry.TryOpen(SyntheticMailOwner.Deployment, out var journal);
        Assert.NotNull(journal);
        journal.Append(new DiscoveryRunCompleted([], []));
        registry.MarkEnded(journal.Id);

        // Act
        this.timeProvider.Advance(DiscoveryRunBounds.RetentionAfterLastUse - TimeSpan.FromSeconds(1));
        registry.TryFind(journal.Id, SyntheticMailOwner.Deployment, out _);
        this.timeProvider.Advance(DiscoveryRunBounds.RetentionAfterLastUse - TimeSpan.FromSeconds(1));

        // Assert
        Assert.True(registry.TryFind(journal.Id, SyntheticMailOwner.Deployment, out _));
    }
}
