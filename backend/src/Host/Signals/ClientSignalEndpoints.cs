// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
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
/// </remarks>
internal static class ClientSignalEndpoints
{
    /// <summary>The route a connection ticket is minted on, relative to the client prefix.</summary>
    internal const string TicketRoute = "/signals/ticket";

    /// <summary>The path the hub answers on, which is absolute because a hub is mapped outside the client group.</summary>
    internal const string HubPath = ClientEndpointOptions.RoutePrefix + "/signals";

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
    /// <param name="authorization">Reports the grant the caller holds and the owner it acts for.</param>
    /// <param name="tickets">Mints the ticket and holds it until it is spent or expires.</param>
    /// <returns><c>200</c> with the ticket, or <c>503</c> where too many tickets already stand outstanding.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required service is <see langword="null" />.</exception>
    /// <remarks>
    /// A <c>POST</c> rather than a <c>GET</c>, because minting a single-use credential changes state: a <c>GET</c> would
    /// be a route a cache, a prefetch, or a link preview could spend a ticket through.
    /// </remarks>
    internal static Results<Ok<ClientSignalTicketResponse>, ProblemHttpResult> MintTicket(
        [FromServices] AccessAuthorization authorization,
        [FromServices] ClientSignalTickets tickets)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(tickets);

        var minted = tickets.Mint(authorization.RequireOwner());

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
