// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Folders.Local;

/// <summary>Covers where synchronization's arrivals land on a held account, and that a mirrored account's synchronization writes nothing new.</summary>
public sealed class LocalMailFolderArrivalsTests
{
    private static readonly MailAccountId Account =
        MailAccountId.Create("primary");

    private static readonly LocalMailFolderArrivalSource SourceInbox =
        new(MailFolderAlias.Create("INBOX"), MailFolderSpecialUse.Inbox, "INBOX");

    private static readonly LocalMailFolderArrivalSource SourceProjects =
        new(MailFolderAlias.Create("PROJECTS"), Role: null, "Projects");

    [Theory]
    [InlineData(MailAccountCustodyPhase.Mirrored)]
    [InlineData(MailAccountCustodyPhase.Restoring)]
    public async Task PlaceAsync_AnAccountNotHeld_WritesNothing(MailAccountCustodyPhase phase)
    {
        // Arrange
        var store = new InMemoryLocalMailFolderStore(Account, phase);

        // Act
        await ArrivalsOver(store).PlaceAsync(Session, Account, Email(1), SourceInbox, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, store.SaveCount);
        Assert.Empty(store.Placements);
    }

    [Fact]
    public async Task PlaceAsync_AnArrivalInTheSourceInbox_LandsInTheProtectedInboxSuppliedWithTheOtherFour()
    {
        // Arrange
        var store = new InMemoryLocalMailFolderStore(Account, MailAccountCustodyPhase.Held);

        // Act
        await ArrivalsOver(store).PlaceAsync(Session, Account, Email(1), SourceInbox, TestContext.Current.CancellationToken);

        // Assert
        var inbox = Assert.Single(store.Folders, folder => folder.Role == MailFolderSpecialUse.Inbox);

        Assert.Equal(inbox.Id, store.Placements[Email(1)]);
        Assert.Equal(LocalMailFolderTree.ProtectedRoles.Count, store.Folders.Count);
    }

    /// <summary>The correspondent is matched by the source folder's alias, so the second arrival finds the folder the first one created.</summary>
    [Fact]
    public async Task PlaceAsync_TwoArrivalsFromOneOrdinarySourceFolder_LandInOneCorrespondingFolder()
    {
        // Arrange
        var store = new InMemoryLocalMailFolderStore(Account, MailAccountCustodyPhase.Held);
        var arrivals = ArrivalsOver(store);

        // Act
        await arrivals.PlaceAsync(Session, Account, Email(1), SourceProjects, TestContext.Current.CancellationToken);
        await arrivals.PlaceAsync(Session, Account, Email(2), SourceProjects, TestContext.Current.CancellationToken);

        // Assert
        var projects = Assert.Single(store.Folders, folder => folder.SourceFolderAlias == SourceProjects.Alias);

        Assert.Equal("Projects", projects.Name.Value);
        Assert.Equal([projects.Id, projects.Id], [store.Placements[Email(1)], store.Placements[Email(2)]]);
    }

    /// <summary>A source whose folder was erased keeps no folder of its own, so a later arrival from it goes to the inbox rather than recreating one.</summary>
    [Fact]
    public async Task PlaceAsync_AnArrivalFromASourceWhoseFolderWasErased_LandsInTheInbox()
    {
        // Arrange
        var store = new InMemoryLocalMailFolderStore(Account, MailAccountCustodyPhase.Held);
        var arrivals = ArrivalsOver(store);
        await arrivals.PlaceAsync(Session, Account, Email(1), SourceProjects, TestContext.Current.CancellationToken);
        var projects = Assert.Single(store.Folders, folder => folder.SourceFolderAlias == SourceProjects.Alias);
        await store.SaveAsync(Session, Account, [], [projects.Id], TestContext.Current.CancellationToken);

        // Act
        var folderSetMoved = await arrivals.PlaceAsync(Session, Account, Email(2), SourceProjects, TestContext.Current.CancellationToken);

        // Assert
        var inbox = Assert.Single(store.Folders, folder => folder.Role == MailFolderSpecialUse.Inbox);

        Assert.False(folderSetMoved);
        Assert.Equal(inbox.Id, store.Placements[Email(2)]);
        Assert.DoesNotContain(store.Folders, folder => folder.SourceFolderAlias == SourceProjects.Alias);
    }

    private static IPersistenceSession Session { get; } = Substitute.For<IPersistenceSession>();

    private static StoredEmailId Email(int number) =>
        StoredEmailId.Create(Guid.Parse($"0199a0c0-0000-7000-8000-{number:D12}"));

    private static LocalMailFolderArrivals ArrivalsOver(ILocalMailFolderStore store) =>
        new(store, ClientSignalPublishers.ReachingNobody, new FakeTimeProvider());
}
