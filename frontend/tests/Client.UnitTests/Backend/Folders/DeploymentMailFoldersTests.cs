// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Folders;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend.Folders;

/// <summary>What the client makes of the folders a deployment reports beneath each mailbox it owns.</summary>
public sealed class DeploymentMailFoldersTests
{
    private const string OneMailbox =
        """
        {
          "synchronizationEnabled": true,
          "accounts": [
            {
              "account": { "id": "work", "displayName": "Work mail", "synchronizationState": "Synchronized",
                           "lastSynchronizedAt": "2026-08-25T11:50:00+00:00", "behind": false },
              "folders": [
                { "alias": "INBOX", "role": "Inbox", "path": [ "INBOX" ], "storedEmailCount": 40,
                  "unreadEmailCount": 3, "synchronizationState": "Synchronized",
                  "lastSynchronizedAt": "2026-08-25T11:50:00+00:00", "behind": false },
                { "alias": "PROJECTS-2024", "role": null, "path": [ "Projects", "2024" ], "storedEmailCount": 9,
                  "unreadEmailCount": 1, "synchronizationState": "Unreachable",
                  "lastSynchronizedAt": null, "behind": true }
              ]
            }
          ]
        }
        """;

    /// <summary>Every field of the contract is read, because the tree draws a row out of each of them.</summary>
    [Fact]
    public async Task ReadMailFoldersAsync_ADeploymentAnswering_ReadsEveryFieldOfTheContract()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => StubTransport.JsonResponse(OneMailbox));

        // Act
        var answered = await harness.Client.ReadMailFoldersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(answered.SynchronizationEnabled);
        var mailbox = Assert.Single(answered.Owned);
        Assert.Equal("work", mailbox.Account.Id);
        Assert.Equal(2, mailbox.Held.Count);

        var inbox = mailbox.Held[0];
        Assert.Equal("INBOX", inbox.Alias);
        Assert.Equal(MailFolderRole.Inbox, inbox.SpecialUse);
        Assert.Equal(["INBOX"], inbox.HierarchyLevels);
        Assert.Equal(40, inbox.StoredEmailCount);
        Assert.Equal(3, inbox.UnreadEmailCount);
        Assert.Equal(MailSynchronizationStanding.Synchronized, inbox.Standing);
        Assert.False(inbox.Behind);

        var nested = mailbox.Held[1];
        Assert.Equal(MailFolderRole.None, nested.SpecialUse);
        Assert.Equal(["Projects", "2024"], nested.HierarchyLevels);
        Assert.Equal(MailSynchronizationStanding.Unreachable, nested.Standing);
        Assert.Null(nested.LastSynchronizedAt);
        Assert.True(nested.Behind);
    }

    /// <summary>The route is the client surface's own, rather than the administrative one that serves a different reader.</summary>
    [Fact]
    public async Task ReadMailFoldersAsync_AnyRequest_GoesToTheClientSurface()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => StubTransport.JsonResponse(OneMailbox));

        // Act
        await harness.Client.ReadMailFoldersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            new Uri("https://mail.example/api/client/folders"),
            Assert.Single(harness.Deployment.Requests).RequestUri);
    }

    /// <summary>
    /// A document naming no mailbox, and a mailbox holding no folder, both read as nothing rather than as a shape the
    /// tree has to remember it received.
    /// </summary>
    [Fact]
    public async Task ReadMailFoldersAsync_ADocumentNamingNothing_ReadsAsEmptyRatherThanFailing()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse("""{"synchronizationEnabled":false}"""));

        // Act
        var answered = await harness.Client.ReadMailFoldersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(answered.Owned);
        Assert.False(answered.SynchronizationEnabled);
    }

    /// <summary>
    /// A credential the deployment will not serve is refused rather than answered with nothing, which is what keeps it
    /// from reading as an owner whose mailboxes hold no folder.
    /// </summary>
    [Fact]
    public async Task ReadMailFoldersAsync_ACredentialWithoutTheGrant_IsRefusedRatherThanAnsweredWithNothing()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse("{}", HttpStatusCode.Forbidden));

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.ReadMailFoldersAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.CredentialRefused, failure.Reason);
    }

    /// <summary>The ten roles the contract publishes are the ten the client places a folder by, matched exactly.</summary>
    [Theory]
    [InlineData("Inbox", MailFolderRole.Inbox)]
    [InlineData("Drafts", MailFolderRole.Drafts)]
    [InlineData("Sent", MailFolderRole.Sent)]
    [InlineData("Outbox", MailFolderRole.Outbox)]
    [InlineData("Archive", MailFolderRole.Archive)]
    [InlineData("Junk", MailFolderRole.Junk)]
    [InlineData("Trash", MailFolderRole.Trash)]
    [InlineData("All", MailFolderRole.All)]
    [InlineData("Flagged", MailFolderRole.Flagged)]
    [InlineData("Important", MailFolderRole.Important)]
    public void SpecialUse_ARolePublishedByTheContract_IsReadAsItself(string published, MailFolderRole expected)
    {
        // Arrange
        var folder = FolderPlaying(published);

        // Act, Assert
        Assert.Equal(expected, folder.SpecialUse);
    }

    /// <summary>
    /// A folder the service gave no role is an ordinary one, and a role this build does not know is neither ordinary
    /// nor placeable — reading the second as the first would draw a folder somewhere its own server did not put it.
    /// </summary>
    [Fact]
    public void SpecialUse_ARoleThisClientDoesNotPlace_IsKeptApartFromAFolderWithNoRoleAtAll()
    {
        // Act, Assert
        Assert.Equal(MailFolderRole.None, FolderPlaying(null).SpecialUse);
        Assert.Equal(MailFolderRole.Unrecognized, FolderPlaying("Snoozed").SpecialUse);
        Assert.Equal(MailFolderRole.Unrecognized, FolderPlaying("inbox").SpecialUse);
    }

    /// <summary>
    /// A folder whose hierarchy the service did not report is drawn under its own name rather than dropped, because a
    /// mailbox missing a folder reads as mail that is not there.
    /// </summary>
    [Fact]
    public void HierarchyLevels_AFolderReportedWithNoPath_FallsBackToNothingRatherThanFailing()
    {
        // Arrange
        var folder = new DeploymentMailFolder(
            "PROJECTS",
            Role: null,
            Path: null!,
            StoredEmailCount: 0,
            UnreadEmailCount: 0,
            "Synchronized",
            LastSynchronizedAt: null,
            Behind: false);

        // Act, Assert
        Assert.Empty(folder.HierarchyLevels);
    }

    private static DeploymentMailFolder FolderPlaying(string? role) =>
        new(
            "INBOX",
            role,
            ["INBOX"],
            StoredEmailCount: 0,
            UnreadEmailCount: 0,
            "Synchronized",
            LastSynchronizedAt: null,
            Behind: false);
}
