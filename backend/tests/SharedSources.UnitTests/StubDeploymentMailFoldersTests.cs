// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the folder sets several suites read the deployment's folders through.</summary>
/// <remarks>
/// Each set has to agree with the per-folder double it is composed from, which is the reason it is composed rather than
/// arranged: a set naming a folder the participation double withholds would let a suite pass against a deployment
/// nothing produces.
/// </remarks>
public sealed class StubDeploymentMailFoldersTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    private static readonly MailAccountId Secondary = MailAccountId.Create("secondary");

    private static readonly MailFolderIdentity Inbox = new(Primary, MailFolderAlias.Create("INBOX"));

    private static readonly MailFolderIdentity Hidden = new(Primary, MailFolderAlias.Create("HIDDEN"));

    private static readonly MailFolderIdentity Unembedded = new(Primary, MailFolderAlias.Create("ARCHIVE"));

    private static readonly MailFolderIdentity Unmirrored = new(Primary, MailFolderAlias.Create("OLD"));

    private static readonly MailFolderIdentity Junk = new(Secondary, MailFolderAlias.Create("JUNK"));

    [Fact]
    public async Task ReadAsync_EachParticipationSet_AnswersTheFoldersTheParticipationAdmits()
    {
        // Arrange
        var folders = Arranged();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var mapped = await folders.ReadAsync(MailFolderSelection.Mapped, cancellationToken);
        var synchronized = await folders.ReadAsync(MailFolderSelection.Synchronized, cancellationToken);
        var visible = await folders.ReadAsync(MailFolderSelection.VisibleToTools, cancellationToken);
        var embedded = await folders.ReadAsync(MailFolderSelection.GeneratingEmbeddings, cancellationToken);

        // Assert
        Assert.Equal([Inbox, Hidden, Unembedded, Unmirrored], mapped);
        Assert.Equal([Inbox, Hidden, Unembedded], synchronized);
        Assert.Equal([Inbox, Unembedded], visible);
        Assert.Equal([Inbox, Hidden], embedded);
    }

    [Fact]
    public async Task ReadAsync_TheJunkSet_AnswersTheCatalogsFoldersAndNothingAboutClassification()
    {
        // Arrange
        var folders = Arranged();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var junk = await folders.ReadAsync(MailFolderSelection.Junk, cancellationToken);
        var classifying = await folders.ReadAsync(MailFolderSelection.OfAccountsClassifyingSpam, cancellationToken);
        var classified = await folders.ReadAsync(MailFolderSelection.ClassifiedForSpam, cancellationToken);

        // Assert
        Assert.Equal([Junk], junk);
        Assert.Empty(classifying);
        Assert.Empty(classified);
    }

    [Fact]
    public async Task ReadAsync_NarrowedToOneAccount_AnswersThatAccountsFoldersAlone()
    {
        // Arrange
        var folders = Arranged();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var primaryJunk = await folders.ReadAsync(MailFolderSelection.Junk, [Primary], cancellationToken);
        var secondaryJunk = await folders.ReadAsync(MailFolderSelection.Junk, [Secondary], cancellationToken);

        // Assert
        Assert.Empty(primaryJunk);
        Assert.Equal([Junk], secondaryJunk);
    }

    [Fact]
    public async Task None_ADeploymentMappingNoFolder_AnswersEverySetEmpty()
    {
        // Arrange
        var folders = StubDeploymentMailFolders.None;
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var answers = await Task.WhenAll(Enum.GetValues<MailFolderSelection>()
            .Select(selection => folders.ReadAsync(selection, cancellationToken)));

        // Assert
        Assert.All(answers, Assert.Empty);
    }

    private static StubDeploymentMailFolders Arranged() => new(
        StubMailFolderParticipation.Mapping(Inbox)
            .Hiding(Hidden)
            .WithoutEmbeddingsIn(Unembedded)
            .Unmirroring(Unmirrored),
        StubJunkMailFolderCatalog.Naming(Junk));
}
