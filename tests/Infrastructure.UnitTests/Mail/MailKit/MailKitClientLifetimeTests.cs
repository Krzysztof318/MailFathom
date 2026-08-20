// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail.MailKit;
using MailKit;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit;

/// <summary>
/// Covers how a mail client is ended, which both protocol adapters now reach through one implementation. What matters
/// is the ordering a failure must not disturb: a client is released whatever the disconnect did, and a cleanup that
/// fails is either reported once or deliberately swallowed, never allowed to replace the failure that caused it.
/// </summary>
public sealed class MailKitClientLifetimeTests
{
    [Fact]
    public async Task DisconnectAndDisposeAsync_ConnectedClient_LogsOutBeforeReleasingIt()
    {
        // Arrange
        var client = Substitute.For<IMailService>();
        client.IsConnected.Returns(true);

        // Act
        await MailKitClientLifetime.DisconnectAndDisposeAsync(client);

        // Assert
        await client.Received(1).DisconnectAsync(quit: true, Arg.Any<CancellationToken>());
        client.Received(1).Dispose();
    }

    [Fact]
    public async Task DisconnectAndDisposeAsync_ClientAlreadyDisconnected_ReleasesItWithoutSpeakingToTheServer()
    {
        // Arrange
        var client = Substitute.For<IMailService>();
        client.IsConnected.Returns(false);

        // Act
        await MailKitClientLifetime.DisconnectAndDisposeAsync(client);

        // Assert
        await client.DidNotReceive().DisconnectAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        client.Received(1).Dispose();
    }

    /// <summary>A logout that failed must not cost the release, since the socket would otherwise outlive the session.</summary>
    [Fact]
    public async Task DisconnectAndDisposeAsync_DisconnectFailing_StillReleasesTheClientAndReportsThatFailure()
    {
        // Arrange
        var client = Substitute.For<IMailService>();
        client.IsConnected.Returns(true);
        client.DisconnectAsync(quit: true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("The server stopped answering.")));

        // Act
        var failure = await Assert.ThrowsAsync<IOException>(async () =>
            await MailKitClientLifetime.DisconnectAndDisposeAsync(client));

        // Assert
        Assert.Equal("The server stopped answering.", failure.Message);
        client.Received(1).Dispose();
    }

    /// <summary>With nothing else to report, the release's own failure is the one the caller sees.</summary>
    [Fact]
    public async Task DisconnectAndDisposeAsync_ReleaseFailingAfterACleanLogout_ReportsTheReleaseFailure()
    {
        // Arrange
        var client = Substitute.For<IMailService>();
        client.IsConnected.Returns(true);
        client.When(released => released.Dispose())
            .Do(_ => throw new InvalidOperationException("The client could not be released."));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await MailKitClientLifetime.DisconnectAndDisposeAsync(client));

        // Assert
        Assert.Equal("The client could not be released.", failure.Message);
    }

    /// <summary>The first failure is the one worth reporting: the second happened to a client the first had already doomed.</summary>
    [Fact]
    public async Task DisconnectAndDisposeAsync_BothCleanupsFailing_ReportsTheFirstOne()
    {
        // Arrange
        var client = Substitute.For<IMailService>();
        client.IsConnected.Returns(true);
        client.DisconnectAsync(quit: true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("The server stopped answering.")));
        client.When(released => released.Dispose())
            .Do(_ => throw new InvalidOperationException("The client could not be released."));

        // Act, Assert
        await Assert.ThrowsAsync<IOException>(async () =>
            await MailKitClientLifetime.DisconnectAndDisposeAsync(client));
    }

    /// <summary>An abandonment happens while a failure is already on its way to the caller, so it may not raise one of its own.</summary>
    [Fact]
    public void Abandon_ReleaseFailing_SwallowsTheCleanupFailure()
    {
        // Arrange
        var client = Substitute.For<IMailService>();
        client.When(released => released.Dispose())
            .Do(_ => throw new InvalidOperationException("The client could not be released."));

        // Act
        MailKitClientLifetime.Abandon(client);

        // Assert
        client.Received(1).Dispose();
    }

    /// <summary>Abandoning asks the server for nothing, because a server that stopped answering would never reply.</summary>
    [Fact]
    public void Abandon_ConnectedClient_ReleasesItWithoutLoggingOut()
    {
        // Arrange
        var client = Substitute.For<IMailService>();
        client.IsConnected.Returns(true);

        // Act
        MailKitClientLifetime.Abandon(client);

        // Assert
        client.Received(1).Dispose();
        client.DidNotReceive().Disconnect(Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
