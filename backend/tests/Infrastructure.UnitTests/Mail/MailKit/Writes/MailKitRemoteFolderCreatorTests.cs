// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapSessionTestContext;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapWriteSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit.Writes;

public sealed class MailKitRemoteFolderCreatorTests
{
    private static readonly MailFolderAlias ArchiveAlias = MailFolderAlias.Create("archive");

    /// <summary>The whole point of the reopened decision: a folder an operator configured comes into existence.</summary>
    [Fact]
    public async Task CreateFolderAsync_ServerAdvertisesNothingAtThePath_CreatesTheFolderAndSubscribesToIt()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var root = AdvertisedFolder(string.Empty);
        var created = AdvertisedFolder("Archief");
        AnswerCreationOf(root, "Archief", created);
        PrepareMissingFolders(client, root, "Archief");
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act
        var advertisedPath = await harness.CreateFolderAsync(ArchiveAlias, "Archief");

        // Assert
        Assert.Equal("Archief", advertisedPath.Value);
        Assert.Equal('/', advertisedPath.HierarchyDelimiter);
        await created.Received(1).SubscribeAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A folder already at the path is the answer rather than a failure, and nothing is created beside it. Resolution
    /// only asks for a creation when its listing found nothing, so reaching this means something else put the folder
    /// there — which is exactly the state the operator wanted.
    /// </summary>
    [Fact]
    public async Task CreateFolderAsync_FolderIsAlreadyThere_ReturnsItWithoutCreatingASecond()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var root = AdvertisedFolder(string.Empty);
        PrepareMissingFolders(client, root);
        client.FoldersByPath["Archief"] = AdvertisedFolder("Archief");
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act
        var advertisedPath = await harness.CreateFolderAsync(ArchiveAlias, "Archief");

        // Assert
        Assert.Equal("Archief", advertisedPath.Value);
        await root.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An IMAP <c>CREATE</c> against an existing folder is answered as an error, so the refusal alone says nothing
    /// about whether the folder is there. The one lookup that follows is what separates another client winning the race
    /// from a server that will not hold a folder at that path.
    /// </summary>
    [Fact]
    public async Task CreateFolderAsync_AnotherClientCreatedTheFolderFirst_ReadsTheRefusalAsSuccess()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var root = AdvertisedFolder(string.Empty);
        var racedFolder = AdvertisedFolder("Archief");
        PrepareMissingFolders(client, root, "Archief");
        root
            .CreateAsync("Archief", Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<Task<IMailFolder?>>(_ =>
            {
                client.AbsentFolderPaths.Remove("Archief");
                client.FoldersByPath["Archief"] = racedFolder;

                throw new ImapCommandException(ImapCommandResponse.No, "Mailbox already exists.");
            });
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act
        var advertisedPath = await harness.CreateFolderAsync(ArchiveAlias, "Archief");

        // Assert
        Assert.Equal("Archief", advertisedPath.Value);
        await racedFolder.DidNotReceive().SubscribeAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The configured text is the server's own path and is never rewritten. What the delimiter decides is only where
    /// its levels are, so a server separating them with a dot builds the same hierarchy the same way.
    /// </summary>
    [Fact]
    public async Task CreateFolderAsync_ServerSeparatesLevelsWithSomethingOtherThanASlash_BuildsTheSameHierarchy()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { PersonalNamespaces = [new FolderNamespace('.', string.Empty)] };
        var root = AdvertisedFolder(string.Empty, '.');
        var parent = AdvertisedFolder("Archief", '.');
        var leaf = AdvertisedFolder("Archief.2026", '.');
        AnswerCreationOf(root, "Archief", parent);
        AnswerCreationOf(parent, "2026", leaf);
        PrepareMissingFolders(client, root, "Archief", "Archief.2026");
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act
        var advertisedPath = await harness.CreateFolderAsync(ArchiveAlias, "Archief.2026");

        // Assert
        Assert.Equal("Archief.2026", advertisedPath.Value);
        Assert.Equal('.', advertisedPath.HierarchyDelimiter);
        Received.InOrder(() =>
        {
            _ = root.CreateAsync("Archief", Arg.Any<bool>(), Arg.Any<CancellationToken>());
            _ = parent.CreateAsync("2026", Arg.Any<bool>(), Arg.Any<CancellationToken>());
        });
    }

    /// <summary>Every level of the path is a name the operator wrote, and a level already there is stepped over rather than recreated.</summary>
    [Fact]
    public async Task CreateFolderAsync_OnlyTheLastLevelIsMissing_CreatesThatLevelAlone()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var root = AdvertisedFolder(string.Empty);
        var parent = AdvertisedFolder("Archief");
        var leaf = AdvertisedFolder("Archief/2026");
        AnswerCreationOf(parent, "2026", leaf);
        PrepareMissingFolders(client, root, "Archief/2026");
        client.FoldersByPath["Archief"] = parent;
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act
        var advertisedPath = await harness.CreateFolderAsync(ArchiveAlias, "Archief/2026");

        // Assert
        Assert.Equal("Archief/2026", advertisedPath.Value);
        await root.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await parent.Received(1).CreateAsync("2026", Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A refused creation has to be readable as itself. Telling it from an alias that resolves to nothing is what makes
    /// the operator's remedy findable, and the message may name the alias and nothing about the mailbox behind it.
    /// </summary>
    [Fact]
    public async Task CreateFolderAsync_ServerRefusesTheCreation_ReportsARefusalNamingTheAliasAndNotThePath()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var root = AdvertisedFolder(string.Empty);
        root
            .CreateAsync("Archief", Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<Task<IMailFolder?>>(_ =>
                throw new ImapCommandException(ImapCommandResponse.No, "Over quota."));
        PrepareMissingFolders(client, root, "Archief");
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act
        var refusal = await Assert.ThrowsAsync<RemoteFolderCreationRefusedException>(
            () => harness.CreateFolderAsync(ArchiveAlias, "Archief"));

        // Assert
        Assert.Equal(MailFathomErrorCode.RemoteFolderCreationRefused, refusal.ErrorCode);
        Assert.Contains("ARCHIVE", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Archief", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A folder that exists is what was asked for, so the subscription that follows it may fail without undoing it.</summary>
    [Fact]
    public async Task CreateFolderAsync_ServerRefusesTheSubscription_LeavesTheCreationSuccessfulAndWarns()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var root = AdvertisedFolder(string.Empty);
        var created = AdvertisedFolder("Archief");
        created
            .SubscribeAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw new ImapCommandException(ImapCommandResponse.No, "Subscriptions unavailable."));
        AnswerCreationOf(root, "Archief", created);
        PrepareMissingFolders(client, root, "Archief");
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act
        var advertisedPath = await harness.CreateFolderAsync(ArchiveAlias, "Archief");

        // Assert
        Assert.Equal("Archief", advertisedPath.Value);
        Assert.Contains(
            harness.RecordedLogs.Records,
            record => record.Level == LogLevel.Warning && record.Properties.ContainsKey("FolderAlias"));
    }

    /// <summary>
    /// Discovery leaves a container the server refuses to open out of its catalog, so the alias resolves to nothing
    /// while the name is already taken. What the operator needs then is a different path rather than an act this
    /// adapter can take for them.
    /// </summary>
    [Fact]
    public async Task CreateFolderAsync_NameIsAlreadyAContainerTheServerWillNotOpen_RefusesRatherThanCreating()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var root = AdvertisedFolder(string.Empty);
        PrepareMissingFolders(client, root);
        client.FoldersByPath["Archief"] = AdvertisedFolder("Archief", '/', FolderAttributes.NoSelect);
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act, Assert
        await Assert.ThrowsAsync<RemoteFolderCreationRefusedException>(
            () => harness.CreateFolderAsync(ArchiveAlias, "Archief"));

        await root.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A server whose personal namespace has a prefix resolves a name written without it underneath that prefix, so the
    /// folder would exist at a path the mapping never matches and every later run would ask for it again. Refusing says
    /// what the operator has to change; binding the path the server chose would hide it.
    /// </summary>
    [Fact]
    public async Task CreateFolderAsync_ServerPlacedTheFolderSomewhereElse_RefusesInsteadOfBindingThePathItChose()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { PersonalNamespaces = [new FolderNamespace('.', "INBOX.")] };
        var root = AdvertisedFolder("INBOX", '.');
        AnswerCreationOf(root, "Archief", AdvertisedFolder("INBOX.Archief", '.'));
        PrepareMissingFolders(client, root, "Archief");
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());

        // Act, Assert
        await Assert.ThrowsAsync<RemoteFolderCreationRefusedException>(
            () => harness.CreateFolderAsync(ArchiveAlias, "Archief"));
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

    /// <summary>Scripts one level of the hierarchy being created under its parent.</summary>
    private static void AnswerCreationOf(IMailFolder parent, string levelName, IMailFolder created) =>
        parent
            .CreateAsync(levelName, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IMailFolder?>(created));

    /// <summary>Points the server at its namespace root and states which paths it advertises no folder for.</summary>
    /// <remarks>
    /// Naming the absent paths is required rather than optional: the scripted server answers a lookup it was told
    /// nothing about with the selected folder, so a path left unnamed here would read as an existing folder and the
    /// creation under test would never be attempted.
    /// </remarks>
    private static void PrepareMissingFolders(FakeImapClient client, IMailFolder root, params string[] missingPaths)
    {
        client.NamespaceRootFolder = root;

        foreach (var missingPath in missingPaths)
        {
            client.AbsentFolderPaths.Add(missingPath);
        }
    }
}
