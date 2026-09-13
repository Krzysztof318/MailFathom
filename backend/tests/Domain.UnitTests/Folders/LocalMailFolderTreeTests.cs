// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Domain.UnitTests.Folders;

/// <summary>Covers the rules every act on a held account's folder hierarchy is decided by.</summary>
public sealed class LocalMailFolderTreeTests
{
    private static readonly MailFolderAlias ProjectsAlias = MailFolderAlias.Create("PROJECTS");

    [Fact]
    public void MissingProtectedFolders_AnEmptyHierarchy_SuppliesTheFiveProtectedFoldersAtTheTop()
    {
        // Arrange
        var tree = new LocalMailFolderTree([], []);

        // Act
        var missing = tree.MissingProtectedFolders(Mint);

        // Assert
        Assert.Equal(LocalMailFolderTree.ProtectedRoles, missing.Select(static folder => folder.Role!.Value));
        Assert.All(missing, static folder => Assert.Null(folder.ParentId));
        Assert.Equal(["INBOX", "Drafts", "Sent", "Junk", "Trash"], missing.Select(static folder => folder.Name.Value));
    }

    [Fact]
    public void MissingProtectedFolders_ACompleteHierarchy_SuppliesNothing()
    {
        // Arrange
        var tree = HeldTree();

        // Act
        var missing = tree.MissingProtectedFolders(Mint);

        // Assert
        Assert.Empty(missing);
    }

    /// <summary>A top-level folder already named for a role takes it, because a second folder beside it could not carry the same name.</summary>
    [Fact]
    public void MissingProtectedFolders_AnOrdinaryTopLevelFolderNamedForARole_GivesThatFolderTheRole()
    {
        // Arrange
        var sent = Ordinary("sent");
        var tree = new LocalMailFolderTree([sent], []);

        // Act
        var missing = tree.MissingProtectedFolders(Mint);

        // Assert
        Assert.Contains(sent with { Role = MailFolderSpecialUse.Sent }, missing);
        Assert.Equal(LocalMailFolderTree.ProtectedRoles.Count, missing.Count);
    }

    [Fact]
    public void Create_ANameNoSiblingCarries_SavesTheFolderBeneathItsParent()
    {
        // Arrange
        var projects = Ordinary("Projects");
        var tree = HeldTree(projects);
        var id = Mint();

        // Act
        var edit = tree.Create(id, projects.Id, "2026");

        // Assert
        Assert.Null(edit.Refusal);
        Assert.Equal([new LocalMailFolder(id, projects.Id, LocalMailFolderName.Create("2026"), null, null)], edit.Saved);
        Assert.Empty(edit.Erased);
    }

    [Theory]
    [InlineData("projects", LocalMailFolderRefusal.NameTaken)]
    [InlineData("inbox", LocalMailFolderRefusal.InboxNameAtTopLevel)]
    [InlineData("A/B", LocalMailFolderRefusal.NameInvalid)]
    [InlineData("", LocalMailFolderRefusal.NameInvalid)]
    public void Create_ANameTheTopLevelCannotTake_IsRefusedForTheReason(string name, LocalMailFolderRefusal expected)
    {
        // Arrange
        var tree = HeldTree(Ordinary("Projects"));

        // Act
        var edit = tree.Create(Mint(), parentId: null, name);

        // Assert
        Assert.Equal(expected, edit.Refusal);
        Assert.Empty(edit.Saved);
    }

    /// <summary>Only the top of the hierarchy is where a second inbox would be mistaken for the first.</summary>
    [Fact]
    public void Create_TheInboxNameBeneathAnotherFolder_IsAccepted()
    {
        // Arrange
        var projects = Ordinary("Projects");
        var tree = HeldTree(projects);

        // Act
        var edit = tree.Create(Mint(), projects.Id, "INBOX");

        // Assert
        Assert.Null(edit.Refusal);
    }

    [Fact]
    public void Create_BeneathAFolderTheAccountDoesNotHave_IsRefused()
    {
        // Arrange
        var tree = HeldTree();

        // Act
        var edit = tree.Create(Mint(), Mint(), "Projects");

        // Assert
        Assert.Equal(LocalMailFolderRefusal.ParentMissing, edit.Refusal);
    }

    [Fact]
    public void Create_BeneathAFolderAtTheDeepestLevel_IsRefused()
    {
        // Arrange
        var chain = Chain(LocalMailFolderTree.MaximumDepth);
        var tree = HeldTree([.. chain]);

        // Act
        var atLimit = HeldTree([.. chain[..^1]]).Create(Mint(), chain[^2].Id, "last");
        var pastLimit = tree.Create(Mint(), chain[^1].Id, "deeper");

        // Assert
        Assert.Null(atLimit.Refusal);
        Assert.Equal(LocalMailFolderRefusal.TooDeep, pastLimit.Refusal);
    }

    [Fact]
    public void Create_AnAccountHoldingTheMostFolders_IsRefused()
    {
        // Arrange
        var siblings = Enumerable
            .Range(0, LocalMailFolderTree.MaximumFolders - LocalMailFolderTree.ProtectedRoles.Count)
            .Select(static index => Ordinary($"Folder {index}"));
        var tree = HeldTree([.. siblings]);

        // Act
        var edit = tree.Create(Mint(), parentId: null, "One more");

        // Assert
        Assert.Equal(LocalMailFolderRefusal.TooManyFolders, edit.Refusal);
    }

    [Theory]
    [MemberData(nameof(ProtectedRoleNames))]
    public void RenameMoveAndDelete_AProtectedFolder_AreEachRefused(MailFolderSpecialUse role)
    {
        // Arrange
        var projects = Ordinary("Projects");
        var tree = HeldTree(projects);
        var folder = tree.Folders.Single(candidate => candidate.Role == role);

        // Act
        var renamed = tree.Rename(folder.Id, "Elsewhere");
        var moved = tree.Move(folder.Id, projects.Id);
        var deleted = tree.Delete(folder.Id);

        // Assert
        Assert.Equal(LocalMailFolderRefusal.ProtectedRole, renamed.Refusal);
        Assert.Equal(LocalMailFolderRefusal.ProtectedRole, moved.Refusal);
        Assert.Equal(LocalMailFolderRefusal.ProtectedRole, deleted.Refusal);
    }

    public static TheoryData<MailFolderSpecialUse> ProtectedRoleNames() => [.. LocalMailFolderTree.ProtectedRoles];

    [Fact]
    public void Rename_AFolderToADifferentCaseOfItsOwnName_IsAccepted()
    {
        // Arrange
        var projects = Ordinary("projects");
        var tree = HeldTree(projects);

        // Act
        var edit = tree.Rename(projects.Id, "Projects");

        // Assert
        Assert.Null(edit.Refusal);
        Assert.Equal("Projects", Assert.Single(edit.Saved).Name.Value);
    }

    [Fact]
    public void Rename_ToASiblingsName_IsRefused()
    {
        // Arrange
        var projects = Ordinary("Projects");
        var tree = HeldTree(projects, Ordinary("Receipts"));

        // Act
        var edit = tree.Rename(projects.Id, "RECEIPTS");

        // Assert
        Assert.Equal(LocalMailFolderRefusal.NameTaken, edit.Refusal);
    }

    [Fact]
    public void Rename_AFolderTheAccountDoesNotHave_IsRefused()
    {
        // Arrange
        var tree = HeldTree();

        // Act
        var edit = tree.Rename(Mint(), "Projects");

        // Assert
        Assert.Equal(LocalMailFolderRefusal.FolderMissing, edit.Refusal);
    }

    [Fact]
    public void Move_AFolderBeneathAnother_SavesItsNewParentAndKeepsEverythingElse()
    {
        // Arrange
        var projects = Ordinary("Projects");
        var archive = Ordinary("Archive");
        var tree = HeldTree(projects, archive);

        // Act
        var edit = tree.Move(projects.Id, archive.Id);

        // Assert
        Assert.Equal([projects with { ParentId = archive.Id }], edit.Saved);
    }

    [Fact]
    public void Move_AFolderBeneathItselfOrItsOwnDescendant_IsRefused()
    {
        // Arrange
        var projects = Ordinary("Projects");
        var year = Ordinary("2026") with { ParentId = projects.Id };
        var tree = HeldTree(projects, year);

        // Act
        var beneathItself = tree.Move(projects.Id, projects.Id);
        var beneathDescendant = tree.Move(projects.Id, year.Id);

        // Assert
        Assert.Equal(LocalMailFolderRefusal.NestedInItself, beneathItself.Refusal);
        Assert.Equal(LocalMailFolderRefusal.NestedInItself, beneathDescendant.Refusal);
    }

    /// <summary>The depth a move reaches is the deepest folder it carries, not only the folder named.</summary>
    [Fact]
    public void Move_ASubtreeWhoseDeepestFolderWouldPassTheLimit_IsRefused()
    {
        // Arrange
        var chain = Chain(LocalMailFolderTree.MaximumDepth - 1);
        var projects = Ordinary("Projects");
        var year = Ordinary("2026") with { ParentId = projects.Id };
        var tree = HeldTree([.. chain, projects, year]);

        // Act
        var edit = tree.Move(projects.Id, chain[^1].Id);

        // Assert
        Assert.Equal(LocalMailFolderRefusal.TooDeep, edit.Refusal);
    }

    [Fact]
    public void Move_ToTheTopBesideAFolderOfTheSameName_IsRefused()
    {
        // Arrange
        var archive = Ordinary("Archive");
        var nested = Ordinary("Projects") with { ParentId = archive.Id };
        var tree = HeldTree(archive, nested, Ordinary("projects"));

        // Act
        var edit = tree.Move(nested.Id, parentId: null);

        // Assert
        Assert.Equal(LocalMailFolderRefusal.NameTaken, edit.Refusal);
    }

    /// <summary>A deletion takes a folder out of the hierarchy rather than deeper into it, so the deepest legal hierarchy can still be deleted from its root.</summary>
    [Fact]
    public void Delete_ARootWhoseSubtreeReachesTheDeepestLevel_MovesItBeneathTheTrash()
    {
        // Arrange
        var chain = Chain(LocalMailFolderTree.MaximumDepth);
        var tree = HeldTree(chain);
        var trash = tree.Folders.Single(static folder => folder.Role == MailFolderSpecialUse.Trash);

        // Act
        var edit = tree.Delete(chain[0].Id);

        // Assert
        Assert.Null(edit.Refusal);
        Assert.Equal([chain[0] with { ParentId = trash.Id }], edit.Saved);
    }

    /// <summary>Rows written in a cycle end the downward walk a move measures its depth by, rather than the process.</summary>
    [Fact]
    public void Move_AFolderWhoseParentRowsFormACycle_DecidesRatherThanRecursingWithoutEnd()
    {
        // Arrange
        var first = Ordinary("First");
        var second = Ordinary("Second") with { ParentId = first.Id };
        var tree = HeldTree(first with { ParentId = second.Id }, second);

        // Act
        var edit = tree.Move(second.Id, parentId: null);

        // Assert
        Assert.Null(edit.Refusal);
        Assert.Equal([second with { ParentId = null }], edit.Saved);
    }

    /// <summary>A folder outside the trash is moved into it as one row, so everything beneath it goes with it whatever it holds.</summary>
    [Fact]
    public void Delete_AFolderOutsideTheTrash_MovesItBeneathTheTrash()
    {
        // Arrange
        var projects = Ordinary("Projects");
        var tree = HeldTree(projects, Ordinary("2026") with { ParentId = projects.Id });
        var trash = tree.Folders.Single(static folder => folder.Role == MailFolderSpecialUse.Trash);

        // Act
        var edit = tree.Delete(projects.Id);

        // Assert
        Assert.Equal([projects with { ParentId = trash.Id }], edit.Saved);
        Assert.Empty(edit.Erased);
    }

    [Fact]
    public void Delete_AFolderAlreadyInTheTrash_ErasesItAndEverythingBeneathIt()
    {
        // Arrange
        var trash = Protected(MailFolderSpecialUse.Trash);
        var projects = Ordinary("Projects") with { ParentId = trash.Id };
        var year = Ordinary("2026") with { ParentId = projects.Id };
        var month = Ordinary("01") with { ParentId = year.Id };
        var bystander = Ordinary("Receipts") with { ParentId = trash.Id };
        var tree = HeldTree([projects, year, month, bystander], trash);

        // Act
        var edit = tree.Delete(projects.Id);

        // Assert
        Assert.Empty(edit.Saved);
        Assert.Equal(
            new[] { projects.Id, year.Id, month.Id }.OrderBy(static id => id.Value),
            edit.Erased.OrderBy(static id => id.Value));
    }

    [Fact]
    public void Delete_IntoATrashAlreadyHoldingAFolderOfTheSameName_IsRefused()
    {
        // Arrange
        var trash = Protected(MailFolderSpecialUse.Trash);
        var projects = Ordinary("Projects");
        var tree = HeldTree([projects, Ordinary("projects") with { ParentId = trash.Id }], trash);

        // Act
        var edit = tree.Delete(projects.Id);

        // Assert
        Assert.Equal(LocalMailFolderRefusal.NameTaken, edit.Refusal);
    }

    [Theory]
    [MemberData(nameof(ProtectedRoleNames))]
    public void PlaceArrival_FromASourceFolderPlayingAProtectedRole_GoesToTheLocalFolderPlayingIt(MailFolderSpecialUse role)
    {
        // Arrange
        var tree = HeldTree();

        // Act
        var arrival = tree.PlaceArrival(ProjectsAlias, role, "Whatever", Mint);

        // Assert
        Assert.Equal(tree.Folders.Single(folder => folder.Role == role).Id, arrival.Folder);
        Assert.Empty(arrival.Saved);
    }

    [Fact]
    public void PlaceArrival_TheFirstMessageFromAnOrdinarySourceFolder_CreatesATopLevelFolderNamedForIt()
    {
        // Arrange
        var tree = HeldTree();

        // Act
        var arrival = tree.PlaceArrival(ProjectsAlias, MailFolderSpecialUse.Archive, "Projects", Mint);

        // Assert
        var created = Assert.Single(arrival.Saved);
        Assert.Equal(arrival.Folder, created.Id);
        Assert.Equal(new LocalMailFolder(created.Id, null, LocalMailFolderName.Create("Projects"), null, ProjectsAlias), created);
    }

    /// <summary>The correspondence is to the folder's identity, so renaming or moving it locally changes nothing about where arrivals go.</summary>
    [Fact]
    public void PlaceArrival_ASourceWhoseFolderWasRenamedAndMoved_GoesToThatFolder()
    {
        // Arrange
        var archive = Ordinary("Archive");
        var corresponding = new LocalMailFolder(Mint(), archive.Id, LocalMailFolderName.Create("Renamed"), null, ProjectsAlias);
        var tree = HeldTree(archive, corresponding);

        // Act
        var arrival = tree.PlaceArrival(ProjectsAlias, null, "Projects", Mint);

        // Assert
        Assert.Equal(corresponding.Id, arrival.Folder);
        Assert.Empty(arrival.Saved);
    }

    [Fact]
    public void PlaceArrival_ASourceWhoseFolderWasDeletedIntoTheTrash_GoesToTheInbox()
    {
        // Arrange
        var trash = Protected(MailFolderSpecialUse.Trash);
        var corresponding = new LocalMailFolder(Mint(), trash.Id, LocalMailFolderName.Create("Projects"), null, ProjectsAlias);
        var tree = HeldTree([corresponding], trash);

        // Act
        var arrival = tree.PlaceArrival(ProjectsAlias, null, "Projects", Mint);

        // Assert
        Assert.Equal(InboxOf(tree), arrival.Folder);
        Assert.Empty(arrival.Saved);
    }

    [Fact]
    public void PlaceArrival_ASourceWhoseFolderWasErased_GoesToTheInboxRatherThanRecreatingIt()
    {
        // Arrange
        var tree = new LocalMailFolderTree(ProtectedFolders(), [ProjectsAlias]);

        // Act
        var arrival = tree.PlaceArrival(ProjectsAlias, null, "Projects", Mint);

        // Assert
        Assert.Equal(InboxOf(tree), arrival.Folder);
        Assert.Empty(arrival.Saved);
    }

    [Fact]
    public void PlaceArrival_ASourceNameTheTopLevelCannotTake_FallsBackToTheAliasAndThenToTheInbox()
    {
        // Arrange
        var tree = HeldTree(Ordinary("Projects"));
        var crowded = HeldTree(Ordinary("Projects"), Ordinary("PROJECTS 2"));
        var alias = MailFolderAlias.Create("PROJECTS 2");

        // Act
        var byAlias = tree.PlaceArrival(alias, null, "projects", Mint);
        var toInbox = crowded.PlaceArrival(alias, null, "INBOX", Mint);

        // Assert
        Assert.Equal("PROJECTS 2", Assert.Single(byAlias.Saved).Name.Value);
        Assert.Equal(InboxOf(crowded), toInbox.Folder);
        Assert.Empty(toInbox.Saved);
    }

    private static LocalMailFolderId Mint() => LocalMailFolderId.Create(Guid.NewGuid());

    private static LocalMailFolder Ordinary(string name) =>
        new(Mint(), ParentId: null, LocalMailFolderName.Create(name), Role: null, SourceFolderAlias: null);

    private static LocalMailFolder Protected(MailFolderSpecialUse role) =>
        new(
            Mint(),
            ParentId: null,
            role == MailFolderSpecialUse.Inbox ? LocalMailFolderName.Inbox : LocalMailFolderName.Create(role.ToString()),
            role,
            SourceFolderAlias: null);

    private static IReadOnlyList<LocalMailFolder> ProtectedFolders(params LocalMailFolder[] replacing) =>
    [
        .. LocalMailFolderTree.ProtectedRoles
            .Select(role => replacing.FirstOrDefault(folder => folder.Role == role) ?? Protected(role)),
    ];

    private static LocalMailFolderTree HeldTree(params LocalMailFolder[] folders) =>
        new([.. ProtectedFolders(), .. folders], []);

    private static LocalMailFolderTree HeldTree(IReadOnlyList<LocalMailFolder> folders, LocalMailFolder replacingProtected) =>
        new([.. ProtectedFolders(replacingProtected), .. folders], []);

    private static LocalMailFolder[] Chain(int length) =>
        Enumerable.Range(0, length).Aggregate(
            Array.Empty<LocalMailFolder>(),
            static (chain, level) => [.. chain, Ordinary($"Level {level}") with { ParentId = chain.LastOrDefault()?.Id }]);

    private static LocalMailFolderId InboxOf(LocalMailFolderTree tree) =>
        tree.Folders.Single(static folder => folder.Role == MailFolderSpecialUse.Inbox).Id;
}
