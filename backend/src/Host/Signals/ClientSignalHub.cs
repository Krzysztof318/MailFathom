// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Access;
using Microsoft.AspNetCore.SignalR;

namespace MailFathom.Host.Signals;

/// <summary>The one-directional channel a running client is told what changed over.</summary>
/// <remarks>
/// <para>
/// <b>It declares no hub method.</b> Nothing travels from a client to the deployment here: a client acts over the HTTP
/// routes it already has, which is where its permission is judged, its request is bounded, and its act is recorded. A
/// method on this hub would be a second way in, unbounded by the surface's limiter and unrecorded by its audit.
/// </para>
/// <para>
/// <b>A connection is authenticated by a ticket and by nothing else.</b> It carries no <c>Authorization</c> header,
/// because a browser cannot put one on a WebSocket, so <see cref="ClientSignalTickets" /> is what names the owner —
/// minted over an authenticated route that already required <see cref="MailFathomPermission.MailRead" />, spent here
/// once, and never seen again. A connection presenting nothing, something malformed, something expired, or something
/// already spent is aborted without being told which.
/// </para>
/// <para>
/// <b>A connection joins its owner's group and no other.</b> The group is the whole of the addressing: a signal is
/// published to one owner's group, so a connection can only ever be reached by statements about the mail it was
/// already entitled to read. Nothing here reads a group name a caller supplied, because nothing here takes one.
/// </para>
/// </remarks>
internal sealed partial class ClientSignalHub : Hub
{
    /// <summary>The name of the one method this hub invokes on a client.</summary>
    /// <remarks>One method rather than one per kind, because the kind is in the payload and a client keys its handler by that: a method per kind would put the vocabulary in two places, and a second delivery channel could not render one of them.</remarks>
    internal const string SignalMethod = "signal";

    /// <summary>The query parameter a browser hands the ticket over, which is SignalR's own name for it.</summary>
    internal const string TicketParameter = "access_token";

    private readonly ClientSignalTickets tickets;
    private readonly ILogger<ClientSignalHub> logger;

    /// <summary>Initializes the hub over the ticket store a connection is judged by.</summary>
    /// <param name="tickets">Spends the ticket a connection presented and names the owner it was minted for.</param>
    /// <param name="logger">Records a refused connection as a count of refusals rather than as anything a caller wrote.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ClientSignalHub(ClientSignalTickets tickets, ILogger<ClientSignalHub> logger)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(logger);

        this.tickets = tickets;
        this.logger = logger;
    }

    /// <summary>Names the group one owner's connections are addressed as.</summary>
    /// <param name="owner">Whose connections the group holds.</param>
    /// <returns>The group name.</returns>
    /// <remarks>Composed from the owner's identifier alone, which is a value this deployment generated rather than anything a caller can state, so no group name is reachable by writing one.</remarks>
    internal static string GroupOf(MailOwnerId owner) =>
        string.Create(CultureInfo.InvariantCulture, $"owner:{owner.Value}");

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        var presented = this.Context.GetHttpContext()?.Request.Query[TicketParameter].ToString();

        if (this.tickets.Redeem(presented) is not { } owner)
        {
            this.LogConnectionRefused();
            this.Context.Abort();

            return;
        }

        await this.Groups.AddToGroupAsync(this.Context.ConnectionId, GroupOf(owner), this.Context.ConnectionAborted);

        await base.OnConnectedAsync();
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "A signal connection presented no usable ticket and was refused.")]
    private partial void LogConnectionRefused();
}
