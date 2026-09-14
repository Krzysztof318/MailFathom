// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit;
using MailKit.Net.Imap;
using NSubstitute;
using Xunit;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapSessionTestContext;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapWriteSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit.Writes;

/// <summary>
/// Covers what the adapter does with the two commands a person's folder act amounts to: the one <c>RENAME</c> that
/// carries both a rename and a move, the <c>DELETE</c>, the path each of them reads back, and how a server that refuses
/// or no longer advertises the folder is classified.
/// </summary>
public sealed class MailKitRemoteFolderEditorTests
{
    private static readonly MailFolderAlias ProjectsAlias = MailFolderAlias.Create("projects");

    /// <summary>A rename leaves the folder where it is, so the destination parent is the one it already sits beneath.</summary>
    [Fact]
    public async Task RenameFolderAsync_AFolderTheServerAdvertises_IssuesOneRenameAndReadsThePathBack()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var root = AdvertisedFolder(string.Empty);
        var projects = AdvertisedFolder("Projects");
        client.NamespaceRootFolder = root;
        client.FoldersByPath["Projects"] = projects;
        projects
            .RenameAsync(root, "Plans", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                projects.FullName.Returns("Plans");

                return Task.CompletedTask;
            });
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act
        var advertisedPath = await harness.RenameFolderAsync(ProjectsAlias, "Projects", newParentPath: null, "Plans");

        // Assert
        Assert.Equal("Plans", advertisedPath.Value);
        Assert.Equal('/', advertisedPath.HierarchyDelimiter);
        await projects.Received(1).RenameAsync(root, "Plans", Arg.Any<CancellationToken>());
    }

    /// <summary>A move is the same command with another destination, and the path comes back from the folder rather than from what was sent.</summary>
    [Fact]
    public async Task RenameFolderAsync_AParentTheAccountDeclares_MovesTheFolderBeneathIt()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var archive = AdvertisedFolder("Archief");
        var projects = AdvertisedFolder("Projects");
        client.NamespaceRootFolder = AdvertisedFolder(string.Empty);
        client.FoldersByPath["Projects"] = projects;
        client.FoldersByPath["Archief"] = archive;
        projects
            .RenameAsync(archive, "Projects", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                projects.FullName.Returns("Archief/Projects");

                return Task.CompletedTask;
            });
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act
        var advertisedPath = await harness.RenameFolderAsync(ProjectsAlias, "Projects", "Archief", "Projects");

        // Assert
        Assert.Equal("Archief/Projects", advertisedPath.Value);
        await projects.Received(1).RenameAsync(archive, "Projects", Arg.Any<CancellationToken>());
    }

    /// <summary>A refused act is reported as itself, and the message may name the alias and nothing about the mailbox behind it.</summary>
    [Fact]
    public async Task RenameFolderAsync_ServerRefusesTheRename_ReportsARefusalNamingTheAliasAndNotThePath()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var projects = AdvertisedFolder("Projecten");
        client.NamespaceRootFolder = AdvertisedFolder(string.Empty);
        client.FoldersByPath["Projecten"] = projects;
        projects
            .RenameAsync(Arg.Any<IMailFolder>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new ImapCommandException(ImapCommandResponse.No, "Mailbox already exists."));
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act
        var refusal = await Assert.ThrowsAsync<RemoteFolderEditRefusedException>(
            () => harness.RenameFolderAsync(ProjectsAlias, "Projecten", newParentPath: null, "Plannen"));

        // Assert
        Assert.Equal(MailFathomErrorCode.RemoteFolderEditRefused, refusal.ErrorCode);
        Assert.Equal(MailFolderAct.Rename, refusal.Act);
        Assert.Contains("PROJECTS", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Projecten", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Renaming a folder the server no longer advertises is refused rather than passed off as done: the act would have moved nothing.</summary>
    [Fact]
    public async Task RenameFolderAsync_ServerAdvertisesNoSuchFolder_IsRefused()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        client.NamespaceRootFolder = AdvertisedFolder(string.Empty);
        client.AbsentFolderPaths.Add("Projects");
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act, Assert
        await Assert.ThrowsAsync<RemoteFolderEditRefusedException>(
            () => harness.RenameFolderAsync(ProjectsAlias, "Projects", newParentPath: null, "Plans"));
    }

    [Fact]
    public async Task DeleteFolderAsync_AFolderTheServerAdvertises_IssuesTheDelete()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var projects = AdvertisedFolder("Projects");
        client.NamespaceRootFolder = AdvertisedFolder(string.Empty);
        client.FoldersByPath["Projects"] = projects;
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act
        await harness.DeleteFolderAsync(ProjectsAlias, "Projects");

        // Assert
        await projects.Received(1).DeleteAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>The question a deletion puts is whether the folder is gone, so a folder another client already deleted is the answer rather than a failure.</summary>
    [Fact]
    public async Task DeleteFolderAsync_ServerNoLongerAdvertisesTheFolder_ReadsItAsAlreadyDeleted()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        client.NamespaceRootFolder = AdvertisedFolder(string.Empty);
        client.AbsentFolderPaths.Add("Projects");
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act
        await harness.DeleteFolderAsync(ProjectsAlias, "Projects");

        // Assert
        Assert.Contains(
            harness.RecordedLogs.Records,
            record => record.Properties.TryGetValue("FolderAlias", out var alias) && Equals(alias, "PROJECTS"));
    }

    [Fact]
    public async Task DeleteFolderAsync_ServerRefusesTheDeletion_ReportsARefusalNamingTheAct()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var projects = AdvertisedFolder("Projects");
        projects
            .DeleteAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw new ImapCommandException(ImapCommandResponse.No, "Cannot delete a folder with children."));
        client.NamespaceRootFolder = AdvertisedFolder(string.Empty);
        client.FoldersByPath["Projects"] = projects;
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act
        var refusal = await Assert.ThrowsAsync<RemoteFolderEditRefusedException>(
            () => harness.DeleteFolderAsync(ProjectsAlias, "Projects"));

        // Assert
        Assert.Equal(MailFolderAct.Delete, refusal.Act);
    }

    /// <summary>A server reporting no personal namespace has said nothing about where its folders live, and guessing inside somebody's mailbox is not the answer.</summary>
    [Fact]
    public async Task RenameFolderAsync_ServerReportsNoPersonalNamespace_IsRefusedRatherThanGuessed()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { PersonalNamespaces = [] };
        client.NamespaceRootFolder = AdvertisedFolder(string.Empty);
        client.FoldersByPath["Projects"] = AdvertisedFolder("Projects");
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act, Assert
        await Assert.ThrowsAsync<RemoteFolderEditRefusedException>(
            () => harness.RenameFolderAsync(ProjectsAlias, "Projects", newParentPath: null, "Plans"));
    }

    /// <summary>Builds a folder the modelled server advertises, with the attributes and delimiter it reports for it.</summary>
    private static IMailFolder AdvertisedFolder(
        string fullName,
        char directorySeparator = '/',
        FolderAttributes attributes = FolderAttributes.None)
    {
        var folder = Substitute.For<IMailFolder>();
        folder.FullName.Returns(fullName);
        folder.DirectorySeparator.Returns(directorySeparator);
        folder.Attributes.Returns(attributes);

        return folder;
    }
}
