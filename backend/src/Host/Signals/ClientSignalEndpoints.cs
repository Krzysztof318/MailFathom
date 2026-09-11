// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Signals;

/// <summary>Serves the two halves of the live channel: the ticket a connection is opened against, and the hub itself.</summary>
/// <remarks>
/// <para>
/// The two are mapped differently on purpose. The ticket is an ordinary route on the client surface, so it inherits the
/// group's authentication, its permission filter, its CORS policy, its rate limiter, and its request timeout — which is
/// what bounds a client reconnecting in a loop, since every reconnection has to mint another ticket.
/// </para>
/// <para>
/// <b>The hub is mapped outside that group, and that is the treatment rather than an omission.</b>
/// <c>ClientEndpoint:RequestTimeout</c> would abandon a long-lived connection at the same bound it abandons a request
/// for a page of mail, and <c>ClientEndpoint:RateLimiting</c> would count a connection that stands open for hours
/// against the same capacity a browser spends reading mail. Neither is the right treatment for a connection, so neither
/// is attached to it; what is bounded instead is the minting above, which a reconnect cannot avoid.
/// </para>
/// <para>
/// It sits beneath the client endpoint's route prefix, so <c>SurfaceIsolation</c> reads it as one of this surface's
/// paths and a listener that does not serve the client surface answers it <c>404</c> like every other route here.
/// </para>
/// <para>
/// <b>It serves one transport</b>, for the reason <see cref="ServeOverWebSocketsAlone" /> gives: a connection that
/// skips negotiation is a single request, so nothing has to route a pair of requests to one replica and a deployment
/// scaling out configures no session affinity.
/// </para>
/// </remarks>
internal static class ClientSignalEndpoints
{
    /// <summary>The route a connection ticket is minted on, relative to the client prefix.</summary>
    internal const string TicketRoute = "/signals/ticket";

    /// <summary>The path the hub answers on, which is absolute because a hub is mapped outside the client group.</summary>
    internal const string HubPath = ClientEndpointOptions.RoutePrefix + "/signals";

    /// <summary>Serves the hub over WebSockets and refuses every other transport.</summary>
    /// <param name="options">What the hub's own connection dispatcher is configured with.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A SignalR handshake is ordinarily two requests — a negotiation and the transport it chose — and both have to
    /// reach the same process, which is why the framework's scale-out guidance asks for session affinity. It names
    /// WebSockets alone with negotiation skipped as one of the arrangements that needs none, and that is what the
    /// client already does. Refusing the other two transports here is what makes that a property of the deployment
    /// rather than a habit of one client: nothing can open a connection that would then have to be routed back to the
    /// replica that answered its negotiation.
    /// </para>
    /// <para>
    /// What it costs is stated in
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md">ADR 0032</see>:
    /// a network or a proxy that will not pass the WebSocket upgrade gives that client no live updates at all, and it
    /// reads its screens over the ordinary routes on its own schedule instead.
    /// </para>
    /// </remarks>
    internal static void ServeOverWebSocketsAlone(HttpConnectionDispatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Transports = HttpTransportType.WebSockets;
    }

    /// <summary>Maps the ticket route into the client group, so it inherits its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientSignalTicket(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapPost(TicketRoute, MintTicket)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Mints a ticket for the person the credential named.</summary>
    /// <param name="authorization">Reports the grant the caller holds and the user it acts for.</param>
    /// <param name="tickets">Mints the ticket and holds it until it is spent or expires.</param>
    /// <param name="cancellationToken">Cancels the mint with the request that asked for it.</param>
    /// <returns><c>200</c> with the ticket, or <c>503</c> where the deployment already holds every ticket it will hold or could not be asked.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required service is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A <c>POST</c> rather than a <c>GET</c>, because minting a single-use credential changes state: a <c>GET</c> would
    /// be a route a cache, a prefetch, or a link preview could spend a ticket through.
    /// </para>
    /// <para>
    /// A deployment whose tickets cannot be reached answers the same <c>503</c> as one that holds every ticket it will
    /// hold, because what the client does about either is identical — wait and mint again, reading its screens over the
    /// ordinary routes meanwhile. The two are told apart by the error code the answer carries and by the failure an
    /// operator's log holds, never by the status, which is the arrangement a scanner this surface cannot reach is
    /// answered under.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<ClientSignalTicketResponse>, ProblemHttpResult>> MintTicket(
        [FromServices] AccessAuthorization authorization,
        [FromServices] ClientSignalTickets tickets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(tickets);

        MintedClientSignalTicket? minted;

        try
        {
            minted = await tickets.MintAsync(authorization.RequireUser(), cancellationToken);
        }
        catch (ClientSignalTicketStoreUnavailableException unreachable)
        {
            return TypedResults.Problem(
                unreachable.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [RouteAuthorization.ErrorCodeExtension] = unreachable.ErrorCode.Value,
                });
        }

        return minted is null
            ? TypedResults.Problem(
                "This deployment is holding as many unspent signal tickets as it will hold; try again in a moment.",
                statusCode: StatusCodes.Status503ServiceUnavailable)
            : TypedResults.Ok(new ClientSignalTicketResponse(minted.Value, minted.ExpiresAt));
    }
}

/// <summary>What the minting route answers with.</summary>
/// <param name="Ticket">The value the client hands the connection, which is spent the first time it is presented.</param>
/// <param name="ExpiresAt">When presenting it stops working, so a client that could not connect mints another rather than retrying this one.</param>
internal sealed record ClientSignalTicketResponse(string Ticket, DateTimeOffset ExpiresAt);
