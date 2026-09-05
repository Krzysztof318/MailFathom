// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Signals;

/// <summary>Covers what a window of statements folds into, whose connections it reaches, and what a deployment with no channel holds.</summary>
public sealed class ClientSignalsTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private static readonly MailAccountIdentity SomebodyElsesAccount =
        MailAccountIdentity.Create(SyntheticMailOwner.Another, MailAccountId.Create("work"));

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("inbox");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");

    /// <summary>A run that commits forty messages into one folder is one arrival, and the window is a fake clock's rather than a wait.</summary>
    [Fact]
    public async Task Publish_ManyArrivalsInOneFolderWithinTheWindow_DeliversOneSignalCarryingTheirSum()
    {
        // Arrange
        var channel = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider();
        await using var signals = new ClientSignals([channel], clock);

        // Act
        for (var arrival = 0; arrival < 40; arrival++)
        {
            signals.Publish(ClientSignal.MailArrived(Account, Inbox, newEmailCount: 1));
        }

        Assert.Empty(channel.Published);

        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        var delivered = Assert.Single(channel.Published);
        Assert.Equal(ClientSignalKind.MailArrived, delivered.Kind);
        Assert.Equal(40, delivered.Count);
        Assert.Equal(Inbox, delivered.Folder);
    }

    /// <summary>Two folders' arrivals stay two statements, because a client told only that mail arrived has nowhere to look.</summary>
    [Fact]
    public async Task Publish_ArrivalsInTwoFolders_DeliversOneSignalPerFolder()
    {
        // Arrange
        var channel = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider();
        await using var signals = new ClientSignals([channel], clock);

        // Act
        signals.Publish(ClientSignal.MailArrived(Account, Inbox, newEmailCount: 2));
        signals.Publish(ClientSignal.MailArrived(Account, Archive, newEmailCount: 3));

        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        Assert.Equal(2, channel.Published.Count);
        Assert.Equal(
            [Archive.Value, Inbox.Value],
            [.. channel.Published.Select(signal => signal.Folder!.Value.Value).Order(StringComparer.Ordinal)]);
    }

    /// <summary>Two owners synchronizing at once are two statements, each naming only its own owner.</summary>
    [Fact]
    public async Task Publish_TheSameChangeForTwoOwners_KeepsEachOwnersSignalToThatOwner()
    {
        // Arrange
        var channel = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider();
        await using var signals = new ClientSignals([channel], clock);

        // Act
        signals.Publish(ClientSignal.MailArrived(Account, Inbox, newEmailCount: 1));
        signals.Publish(ClientSignal.MailArrived(SomebodyElsesAccount, Inbox, newEmailCount: 7));

        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        Assert.Equal(2, channel.Published.Count);

        var mine = Assert.Single(channel.Published, signal => signal.Owner == SyntheticMailOwner.Deployment);
        var theirs = Assert.Single(channel.Published, signal => signal.Owner == SyntheticMailOwner.Another);

        Assert.Equal(1, mine.Count);
        Assert.Equal(7, theirs.Count);
    }

    /// <summary>The identities two statements named are one set, so a client re-reads each row once.</summary>
    [Fact]
    public async Task Publish_TwoChangesNamingOverlappingEmails_DeliversTheirUnionOnce()
    {
        // Arrange
        var channel = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider();
        await using var signals = new ClientSignals([channel], clock);

        var first = StoredEmailId.Create(Guid.CreateVersion7());
        var second = StoredEmailId.Create(Guid.CreateVersion7());
        var third = StoredEmailId.Create(Guid.CreateVersion7());

        // Act
        signals.Publish(ClientSignal.MailChanged(Account, Inbox, [first, second]));
        signals.Publish(ClientSignal.MailChanged(Account, Inbox, [second, third]));

        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        var delivered = Assert.Single(channel.Published);
        Assert.Equal(3, delivered.Emails.Count);
        Assert.Contains(first, delivered.Emails);
        Assert.Contains(second, delivered.Emails);
        Assert.Contains(third, delivered.Emails);
    }

    /// <summary>Every registered channel is handed the same fold, which is what lets a second delivery channel be added without touching a raise site.</summary>
    [Fact]
    public async Task Publish_SeveralRegisteredChannels_HandsTheFoldToEachOfThem()
    {
        // Arrange
        var first = new RecordingClientSignalChannel();
        var second = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider();
        await using var signals = new ClientSignals([first, second], clock);

        // Act
        signals.Publish(ClientSignal.FoldersChanged(Account));

        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        Assert.Equal(ClientSignalKind.FoldersChanged, Assert.Single(first.Published).Kind);
        Assert.Equal(ClientSignalKind.FoldersChanged, Assert.Single(second.Published).Kind);
    }

    /// <summary>A channel that could not deliver never reaches the run that raised the signal, and never stops the next window.</summary>
    [Fact]
    public async Task Publish_AChannelThatFails_LeavesTheRemainingChannelDeliveredAndTheNextWindowRunning()
    {
        // Arrange
        var failing = Substitute.For<IClientSignalChannel>();
        failing.PublishAsync(Arg.Any<ClientSignal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("This channel cannot deliver.")));
        var recording = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider();
        await using var signals = new ClientSignals([failing, recording], clock);

        // Act
        signals.Publish(ClientSignal.FoldersChanged(Account));
        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        signals.Publish(ClientSignal.MailArrived(Account, Inbox, newEmailCount: 1));
        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        Assert.Equal(
            [ClientSignalKind.FoldersChanged, ClientSignalKind.MailArrived],
            [.. recording.Published.Select(signal => signal.Kind)]);
    }

    /// <summary>A deployment serving no client holds nothing about signals nobody can receive.</summary>
    [Fact]
    public async Task Publish_NoRegisteredChannel_ReachesNobodyAndHoldsNothing()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        await using var signals = new ClientSignals([], clock);

        // Act
        signals.Publish(ClientSignal.MailArrived(Account, Inbox, newEmailCount: 1));
        clock.Advance(ClientSignals.FoldingWindow);

        // Assert
        Assert.False(signals.Reaches);
        await signals.DrainAsync();
    }

    /// <summary>A scope arriving past the bound is delivered straight away, so a deployment under load says more rather than less.</summary>
    [Fact]
    public async Task Publish_PastTheHeldScopeBound_DeliversTheFurtherSignalWithoutWaitingForAWindow()
    {
        // Arrange
        var channel = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider();
        await using var signals = new ClientSignals([channel], clock);

        for (var scope = 0; scope < ClientSignals.MostFoldedScopes; scope++)
        {
            signals.Publish(ClientSignal.MailArrived(
                Account,
                MailFolderAlias.Create($"folder-{scope}"),
                newEmailCount: 1));
        }

        // Act
        signals.Publish(ClientSignal.MailArrived(Account, Inbox, newEmailCount: 1));
        await signals.DrainAsync();

        // Assert
        var delivered = Assert.Single(channel.Published);
        Assert.Equal(Inbox, delivered.Folder);
    }

    /// <summary>Disposal delivers what a window was still holding rather than dropping it.</summary>
    [Fact]
    public async Task DisposeAsync_WithAHeldWindow_DeliversWhatItWasHolding()
    {
        // Arrange
        var channel = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider();
        var signals = new ClientSignals([channel], clock);

        signals.Publish(ClientSignal.MailArrived(Account, Inbox, newEmailCount: 4));

        // Act
        await signals.DisposeAsync();

        // Assert
        Assert.Equal(4, Assert.Single(channel.Published).Count);
    }
}
