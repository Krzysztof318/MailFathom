// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Signals;

/// <summary>Covers which connections the signal hub admits, and what a connection it admits can be reached by.</summary>
public sealed class ClientSignalHubTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A connection presenting a live ticket joins the group its user's signals are published to.</summary>
    [Fact]
    public async Task OnConnectedAsync_WithALiveTicket_JoinsTheUsersGroup()
    {
        // Arrange
        var tickets = new ClientSignalTickets(new InMemoryClientSignalTicketStore(), new FakeTimeProvider(Instant));
        var minted = await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);
        var groups = Substitute.For<IGroupManager>();
        var context = ConnectionPresenting(minted!.Value);

        using var hub = new ClientSignalHub(tickets, NullLogger<ClientSignalHub>.Instance)
        {
            Context = context,
            Groups = groups,
        };

        // Act
        await hub.OnConnectedAsync();

        // Assert
        await groups.Received(1).AddToGroupAsync(
            "connection",
            ClientSignalHub.GroupOf(SyntheticMailUser.Deployment),
            Arg.Any<CancellationToken>());
        context.DidNotReceive().Abort();
    }

    /// <summary>Nothing a caller can write opens a connection, so a hub reached without a usable ticket refuses it.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-ticket")]
    [InlineData("unknown.aGVsbG8")]
    public async Task OnConnectedAsync_WithoutAUsableTicket_AbortsWithoutJoiningAnyGroup(string presented)
    {
        // Arrange
        var tickets = new ClientSignalTickets(new InMemoryClientSignalTicketStore(), new FakeTimeProvider(Instant));
        var groups = Substitute.For<IGroupManager>();
        var context = ConnectionPresenting(presented);

        using var hub = new ClientSignalHub(tickets, NullLogger<ClientSignalHub>.Instance)
        {
            Context = context,
            Groups = groups,
        };

        // Act
        await hub.OnConnectedAsync();

        // Assert
        context.Received(1).Abort();
        await groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A ticket opens one connection, so a second one presenting the same value is refused.</summary>
    [Fact]
    public async Task OnConnectedAsync_ReplayingATicketASecondConnectionAlreadySpent_RefusesTheSecondConnection()
    {
        // Arrange
        var tickets = new ClientSignalTickets(new InMemoryClientSignalTicketStore(), new FakeTimeProvider(Instant));
        var minted = await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);
        var groups = Substitute.For<IGroupManager>();
        var replayed = ConnectionPresenting(minted!.Value);

        using var first = new ClientSignalHub(tickets, NullLogger<ClientSignalHub>.Instance)
        {
            Context = ConnectionPresenting(minted.Value),
            Groups = Substitute.For<IGroupManager>(),
        };

        using var second = new ClientSignalHub(tickets, NullLogger<ClientSignalHub>.Instance)
        {
            Context = replayed,
            Groups = groups,
        };

        // Act
        await first.OnConnectedAsync();
        await second.OnConnectedAsync();

        // Assert
        replayed.Received(1).Abort();
        await groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A deployment that cannot reach its own tickets refuses the connection rather than admitting one opened against
    /// a ticket nothing could confirm, and the outage travels no further than the log an operator reads.
    /// </summary>
    [Fact]
    public async Task OnConnectedAsync_WhenTheTicketStoreCannotBeReached_AbortsWithoutJoiningAnyGroup()
    {
        // Arrange
        var tickets = new ClientSignalTickets(
            new UnreachableClientSignalTicketStore(),
            new FakeTimeProvider(Instant));
        var groups = Substitute.For<IGroupManager>();
        var context = ConnectionPresenting($"identifier.{new string('A', 43)}");

        using var hub = new ClientSignalHub(tickets, NullLogger<ClientSignalHub>.Instance)
        {
            Context = context,
            Groups = groups,
        };

        // Act
        await hub.OnConnectedAsync();

        // Assert
        context.Received(1).Abort();
        await groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Two users are two groups, so nothing published to one can be addressed to the other.</summary>
    [Fact]
    public void GroupOf_TwoUsers_NamesADistinctGroupForEachOfThem() =>
        Assert.NotEqual(
            ClientSignalHub.GroupOf(SyntheticMailUser.Deployment),
            ClientSignalHub.GroupOf(SyntheticMailUser.Another));

    /// <summary>A group name is composed from the user's own identifier, which no caller writes.</summary>
    [Fact]
    public void GroupOf_AUser_ComposesTheNameFromTheUsersIdentifier() =>
        Assert.Contains(
            SyntheticMailUser.Deployment.Value.ToString(),
            ClientSignalHub.GroupOf(SyntheticMailUser.Deployment),
            StringComparison.Ordinal);

    private static HubCallerContext ConnectionPresenting(string ticket)
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = QueryString.Create(ClientSignalHub.TicketParameter, ticket);

        var features = new FeatureCollection();
        features.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = http });

        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("connection");
        context.Features.Returns(features);
        context.ConnectionAborted.Returns(CancellationToken.None);

        return context;
    }

    private sealed class HttpContextFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }
}
