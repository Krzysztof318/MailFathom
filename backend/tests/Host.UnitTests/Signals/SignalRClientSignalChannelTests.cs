// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Signals;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests.Signals;

/// <summary>Covers which connections a signal is addressed to, what is sent to them, and what a failed send does to the run that raised it.</summary>
public sealed class SignalRClientSignalChannelTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("inbox");

    /// <summary>A signal reaches its owner's group alone, under the one method name a client keys its handler by, as the payload rather than as itself.</summary>
    [Fact]
    public async Task PublishAsync_ASignal_SendsItsRenderingToTheOwnersGroupUnderTheOnePublishedMethod()
    {
        // Arrange
        var group = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(group);

        object?[]? sent = null;
        var method = string.Empty;
        group.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                method = call.Arg<string>();
                sent = call.Arg<object?[]>();

                return Task.CompletedTask;
            });

        var channel = ChannelOver(clients);

        // Act
        await channel.PublishAsync(ClientSignal.MailArrived(Account, Inbox, newEmailCount: 3), CancellationToken.None);

        // Assert
        clients.Received(1).Group(ClientSignalHub.GroupOf(Account.Owner));
        Assert.Equal(ClientSignalHub.SignalMethod, method);
        Assert.NotNull(sent);
        var payload = Assert.IsType<ClientSignalPayload>(Assert.Single(sent));
        Assert.Equal(ClientSignalKind.MailArrived.Name, payload.Kind);
        Assert.Equal(Account.Id.Value, payload.Account);
        Assert.Equal(Inbox.Value, payload.Folder);
        Assert.Equal(3, payload.Count);
    }

    /// <summary>A send that failed stops here, because the work every signal describes is already committed.</summary>
    [Fact]
    public async Task PublishAsync_AHubThatCannotSend_AbsorbsTheFailureRatherThanFailingTheRunThatRaisedIt()
    {
        // Arrange
        var group = Substitute.For<IClientProxy>();
        group.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("This hub cannot send."));
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(group);
        var channel = ChannelOver(clients);

        // Act
        await channel.PublishAsync(ClientSignal.FoldersChanged(Account), CancellationToken.None);

        // Assert
        await group.Received(1).SendCoreAsync(
            ClientSignalHub.SignalMethod,
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    private static SignalRClientSignalChannel ChannelOver(IHubClients clients)
    {
        var hub = Substitute.For<IHubContext<ClientSignalHub>>();
        hub.Clients.Returns(clients);

        return new SignalRClientSignalChannel(hub, NullLogger<SignalRClientSignalChannel>.Instance);
    }
}
