// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Signals;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests.Signals;

/// <summary>Covers which connections a signal is addressed to, what is sent to them, and what a failed send does to the run that raised it.</summary>
public sealed class SignalRClientSignalChannelTests
{
    private static readonly MailAccountId Account =
        MailAccountId.Create("work");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("inbox");

    /// <summary>A signal reaches the assigned user's group, under the one method name a client keys its handler by, as the payload rather than as itself.</summary>
    [Fact]
    public async Task PublishAsync_ASignal_SendsItsRenderingToTheAssignedUsersGroupUnderTheOnePublishedMethod()
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
        clients.Received(1).Group(ClientSignalHub.GroupOf(SyntheticUser.Deployment));
        Assert.Equal(ClientSignalHub.SignalMethod, method);
        Assert.NotNull(sent);
        var payload = Assert.IsType<ClientSignalPayload>(Assert.Single(sent));
        Assert.Equal(ClientSignalKind.MailArrived.Name, payload.Kind);
        Assert.Equal(Account.Value, payload.Account);
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

    /// <summary>Builds the channel over a deployment assigning the mailbox to whoever the test named.</summary>
    /// <remarks>
    /// The scope factory is a real one rather than a substitute, because the channel resolves the assignment relation
    /// per signal from a scope of its own — that is what makes an assignment change between two signals visible to
    /// the second — and a substituted factory would prove the wiring against itself.
    /// </remarks>
    /// <summary>
    /// One mailbox assigned to two people wakes both of their clients. The recipients are resolved from the
    /// assignment relation at the moment the signal is published rather than carried on the signal, so a mailbox
    /// somebody was assigned after a run started still reaches them.
    /// </summary>
    [Fact]
    public async Task PublishAsync_ASignalNamingAMailboxAssignedToTwoUsers_ReachesBothTheirGroups()
    {
        // Arrange
        var group = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(group);
        var channel = ChannelOver(
            clients,
            new StubMailAccountAssignments()
                .Assigning(SyntheticUser.Deployment, Account)
                .Assigning(SyntheticUser.Another, Account));

        // Act
        await channel.PublishAsync(ClientSignal.FoldersChanged(Account), CancellationToken.None);

        // Assert
        clients.Received(1).Group(ClientSignalHub.GroupOf(SyntheticUser.Deployment));
        clients.Received(1).Group(ClientSignalHub.GroupOf(SyntheticUser.Another));
    }

    /// <summary>A mailbox assigned to nobody reaches nobody, which is what an empty assignment answer has to mean.</summary>
    [Fact]
    public async Task PublishAsync_ASignalNamingAMailboxAssignedToNobody_SendsNothing()
    {
        // Arrange
        var group = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(group);
        var channel = ChannelOver(clients, new StubMailAccountAssignments());

        // Act
        await channel.PublishAsync(ClientSignal.FoldersChanged(Account), CancellationToken.None);

        // Assert
        Assert.Empty(clients.ReceivedCalls());
        Assert.Empty(group.ReceivedCalls());
    }

    private static SignalRClientSignalChannel ChannelOver(
        IHubClients clients,
        IMailAccountAssignments? assignments = null)
    {
        var hub = Substitute.For<IHubContext<ClientSignalHub>>();
        hub.Clients.Returns(clients);

        var services = new ServiceCollection();
        services.AddSingleton(assignments
            ?? new StubMailAccountAssignments().Assigning(SyntheticUser.Deployment, Account));

        return new SignalRClientSignalChannel(
            hub,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SignalRClientSignalChannel>.Instance);
    }
}
