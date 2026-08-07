// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
}
