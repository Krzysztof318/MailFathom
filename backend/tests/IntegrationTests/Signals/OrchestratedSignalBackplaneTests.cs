// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using MailFathom.AppHost;
using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Signals;
using MailFathom.IntegrationTests.Hosting;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Signals;

/// <summary>Proves that a signal raised on one replica reaches a client connection held by another.</summary>
/// <remarks>
/// <para>
/// This is the one claim about the backplane nothing smaller settles. Which lifetime manager a shape registers is
/// asserted in <c>HostCompositionTests</c> without starting anything, and what a signal renders as is asserted against
/// a substituted hub — neither of them can say whether a statement actually crosses a process boundary, which is the
/// whole of what the section exists to buy. Since #1290 gives an account to one replica, the replica that raised a
/// signal is routinely not the one holding the connection that has to hear it; above one replica and without this, an
/// established connection receives almost nothing and nothing an operator reads says so.
/// </para>
/// <para>
/// Two hosts in this process rather than two orchestrated resources, for the reason <see cref="InProcessComposedHost" />
/// gives: a deployment shape a running process was configured once cannot be varied, and this shape — the client
/// surface served over a declared backplane — is one no other test wants. They are started over real sockets, which is
/// that helper's one exception and is needed here because the claim is about a WebSocket standing open: a request
/// feature can carry a route and a credential, and it cannot carry a connection.
/// </para>
/// <para>
/// It joins the composed-host collection for that collection's ordering rather than for its fixture — the two hosts
/// bind ports of their own and reach neither the orchestrated database nor the orchestrated mailbox — and takes the
/// fixture for one value, which is the address the orchestration published the RESP server at.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class OrchestratedSignalBackplaneTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How long a statement is given to cross, which is a ceiling rather than an expectation.</summary>
    /// <remarks>The crossing itself is a publish and a subscription delivery over a loopback socket. What the budget is actually for is the subscription being in place on both sides — each host connects to the endpoint when its lifetime manager is first asked for, and the group is joined when the connection is admitted.</remarks>
    private static readonly TimeSpan CrossingBudget = TimeSpan.FromSeconds(30);

    /// <summary>How often the statement is raised again while the budget runs.</summary>
    /// <remarks>
    /// Raised repeatedly rather than once, because nothing in the client's own handshake says the group has been joined
    /// on the backplane yet: a connection is admitted, the hub adds it to its user's group, and the subscription
    /// reaches the endpoint after the handshake the client awaited has already completed. A signal is an instruction to
    /// look again and carries no sequence, so raising it twice is the same statement rather than a second event — which
    /// is what makes retrying the honest arrangement here instead of a sleep long enough to hope.
    /// </remarks>
    private static readonly TimeSpan RaiseInterval = TimeSpan.FromMilliseconds(250);

    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailUser.Deployment, MailAccountId.Create("work"));

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("inbox");

    /// <summary>
    /// The deployment the section exists for: one replica synchronized a folder, the connection that has to be told
    /// about it is held by a different process, and the statement crosses the RESP endpoint both of them declared.
    /// </summary>
    [Fact]
    public async Task PublishAsync_OnOneReplica_ReachesAConnectionHeldByAnother()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var ports = OrchestrationContract.FindFreePorts(2);

        await using var raising = await this.StartAsync(ports[0], cancellationToken);
        await using var holding = await this.StartAsync(ports[1], cancellationToken);

        var ticket = holding.Services.GetRequiredService<ClientSignalTickets>().Mint(SyntheticMailUser.Deployment)
            ?? throw new InvalidOperationException("The host holding the connection refused to mint a signal ticket.");

        await using var connection = ConnectionTo(ports[1], ticket.Value);

        var delivered = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>(
            ClientSignalHub.SignalMethod,
            payload => delivered.TrySetResult(payload.Clone()));

        await connection.StartAsync(cancellationToken);

        // Act
        var signal = ClientSignal.MailArrived(Account, Inbox, newEmailCount: 3);
        var channel = raising.Services.GetRequiredService<IClientSignalChannel>();

        var payload = await RaiseUntilDeliveredAsync(channel, signal, delivered.Task, cancellationToken);

        // Assert
        Assert.Equal(signal.Kind.Name, payload.GetProperty("kind").GetString());
        Assert.Equal(Account.Id.Value, payload.GetProperty("account").GetString());
        Assert.Equal(Inbox.Value, payload.GetProperty("folder").GetString());
        Assert.Equal(3, payload.GetProperty("count").GetInt32());
    }

    /// <summary>Raises the statement until the connection on the other host reports it, or the budget runs out.</summary>
    private static async Task<JsonElement> RaiseUntilDeliveredAsync(
        IClientSignalChannel channel,
        ClientSignal signal,
        Task<JsonElement> delivered,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CrossingBudget);

        while (!delivered.IsCompleted)
        {
            // Named rather than left as a bare cancellation, because this is the one failure the class exists to
            // report and a suite reading an unnamed one here would read it as a host that failed to start.
            if (budget.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();

                throw new InvalidOperationException(
                    "The signal raised on one replica did not reach the connection held by the other within the crossing budget.");
            }

            await channel.PublishAsync(signal, cancellationToken);

            var raisedAgain = Task.Delay(RaiseInterval, budget.Token);

            if (await Task.WhenAny(delivered, raisedAgain) == delivered)
            {
                break;
            }
        }

        return await delivered;
    }

    /// <summary>Opens the connection a running client would open, over the transport the client uses and nothing else.</summary>
    /// <remarks>
    /// WebSockets alone with negotiation skipped, which is the shape the browser client opens and the only one a
    /// single-use ticket works with: a negotiation would be a second request presenting the same ticket, and the ticket
    /// is spent the first time it is presented.
    /// </remarks>
    private static HubConnection ConnectionTo(int port, string ticket) =>
        new HubConnectionBuilder()
            .WithUrl(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"http://127.0.0.1:{port}{ClientSignalEndpoints.HubPath}?{ClientSignalHub.TicketParameter}={Uri.EscapeDataString(ticket)}"),
                options =>
                {
                    options.Transports = HttpTransportType.WebSockets;
                    options.SkipNegotiation = true;
                })
            .Build();

    /// <summary>Composes one replica: the client surface on its own socket, over the endpoint the orchestration published.</summary>
    private Task<InProcessComposedHost> StartAsync(int port, CancellationToken cancellationToken) =>
        InProcessComposedHost.StartAsync(
            [
                new("ClientEndpoint:Enabled", "true"),
                new("ClientEndpoint:BindAddress", "127.0.0.1"),
                new("ClientEndpoint:Port", port.ToString(CultureInfo.InvariantCulture)),
                new("SignalBackplane:ConnectionString:Name", "signal-backplane"),

                // Inline rather than a file, for the reason every other secret in a composed shape is: what the section
                // has to prove here is that the endpoint is reached, and a path on the machine running the suite would
                // decide whether the shape composes at all.
                new(
                    "SignalBackplane:ConnectionString:SecretReference",
                    $"plaintext:{orchestration.SignalBackplaneConnectionString}"),
            ],
            cancellationToken,
            overRealSockets: true);
}
