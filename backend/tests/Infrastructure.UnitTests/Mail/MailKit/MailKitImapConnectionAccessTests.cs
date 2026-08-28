// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.MailKit;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit;
using NSubstitute;
using Xunit;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit;

/// <summary>
/// How a connection selects its folder is fixed when it is created, and this is what makes the never-writes guarantee
/// structural rather than a rule a reviewer has to notice. Every read path in MailFathom holds a connection from
/// <c>ForReading</c>, and one of those refuses a mutation here rather than sending a command a read-only selection
/// would reject on the server — a refusal that arrives as an IMAP error is a bug found in production, and this one is a
/// bug found on the first test that makes the mistake.
/// </summary>
public sealed class MailKitImapConnectionAccessTests
{
    [Fact]
    public async Task ExecuteMutationAsync_OnAConnectionOpenedForReading_IsRefusedWithoutReachingTheServer()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var openFolder = CreateSelectedFolder();
        var client = new FakeImapClient { Folder = openFolder };
        client.AuthenticationMechanisms.Add("PLAIN");
        await using var connection = MailKitImapConnection.ForReading(
            () => client.Client,
            CreateSettingsProvider(),
            new UnusedMailAccessTokenSource(),
            resilience.Executor,
            resilience.TransientFailureClassifier,
            ConnectionBudget,
            MailServerConnectionPurpose.Work,
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy);

        // Act
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.ExecuteMutationAsync(
                (_, _, _) => Task.FromResult(true),
                CancellationToken.None));

        // Assert
        Assert.Contains("read-only", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, client.ConnectCount);
        await openFolder.DidNotReceive().StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IStoreFlagsRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The same connection still serves reads, so the guard narrows what it can do rather than breaking it.</summary>
    [Fact]
    public async Task ExecuteFolderReadAsync_OnAConnectionOpenedForReading_StillRuns()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Folder = CreateSelectedFolder() };
        client.AuthenticationMechanisms.Add("PLAIN");
        await using var connection = MailKitImapConnection.ForReading(
            () => client.Client,
            CreateSettingsProvider(),
            new UnusedMailAccessTokenSource(),
            resilience.Executor,
            resilience.TransientFailureClassifier,
            ConnectionBudget,
            MailServerConnectionPurpose.Work,
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy);

        // Act
        var read = await connection.ExecuteFolderReadAsync(
            (_, folder, _) => Task.FromResult(folder.UidValidity),
            CancellationToken.None);

        // Assert
        Assert.Equal(7U, read);
        Assert.Equal(1, client.ConnectCount);
    }

    /// <summary>
    /// The two write permissions are separate connections rather than one. A connection that selects a folder is the
    /// one that moves messages in it, and asking it to change the mailbox's own shape fails here rather than reaching
    /// a server — which is what keeps a component able to file a message unable to create a folder.
    /// </summary>
    [Fact]
    public async Task ExecuteFolderManagementAsync_OnAConnectionThatSelectedAFolder_IsRefusedWithoutReachingTheServer()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Folder = CreateSelectedFolder() };
        client.AuthenticationMechanisms.Add("PLAIN");
        await using var connection = MailKitImapConnection.ForWriting(
            () => client.Client,
            CreateSettingsProvider(),
            new UnusedMailAccessTokenSource(),
            resilience.Executor,
            resilience.TransientFailureClassifier,
            ConnectionBudget,
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy);

        // Act
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.ExecuteFolderManagementAsync((_, _) => Task.FromResult(true), CancellationToken.None));

        // Assert
        Assert.Contains("manage folders", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, client.ConnectCount);
    }

    /// <summary>And the reverse, which is the half that keeps a component able to create a folder unable to touch a message in one.</summary>
    [Fact]
    public async Task ExecuteMutationAsync_OnAConnectionOpenedToManageFolders_IsRefusedWithoutReachingTheServer()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Folder = CreateSelectedFolder() };
        client.AuthenticationMechanisms.Add("PLAIN");
        await using var connection = MailKitImapConnection.ForFolderManagement(
            () => client.Client,
            CreateSettingsProvider(),
            new UnusedMailAccessTokenSource(),
            resilience.Executor,
            resilience.TransientFailureClassifier,
            ConnectionBudget,
            PrimaryAccount,
            TlsOnConnectWithPlainPolicy);

        // Act
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.ExecuteMutationAsync((_, _, _) => Task.FromResult(true), CancellationToken.None));

        // Assert
        Assert.Contains("selects no folder", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, client.ConnectCount);
    }

    /// <summary>A connection opened to manage folders authenticates and selects nothing, which is what a <c>CREATE</c> needs and all it needs.</summary>
    [Fact]
    public async Task ExecuteFolderManagementAsync_OnAConnectionOpenedToManageFolders_RunsWithoutSelectingAFolder()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var openFolder = CreateSelectedFolder();
        var client = new FakeImapClient { Folder = openFolder };
        client.AuthenticationMechanisms.Add("PLAIN");
        await using var connection = MailKitImapConnection.ForFolderManagement(
            () => client.Client,
            CreateSettingsProvider(),
            new UnusedMailAccessTokenSource(),
            resilience.Executor,
            resilience.TransientFailureClassifier,
            ConnectionBudget,
            PrimaryAccount,
            TlsOnConnectWithPlainPolicy);

        // Act
        var managed = await connection.ExecuteFolderManagementAsync(
            (managedClient, _) => Task.FromResult(managedClient.IsConnected),
            CancellationToken.None);

        // Assert
        Assert.True(managed);
        Assert.Equal(1, client.ConnectCount);
        await openFolder.DidNotReceive().OpenAsync(Arg.Any<FolderAccess>(), Arg.Any<CancellationToken>());
    }
}
