// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Signals;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Signals;

/// <summary>Covers what the minting route answers with, and what it answers when there is no ticket left to mint.</summary>
/// <remarks>
/// The route is the only way a connection is opened, so the two answers are what a client branches on: one carries a
/// credential to present and the other says to come back later. A deployment holding as many unspent tickets as it will
/// hold is a bound rather than a fault, which is why the refusal is <c>503</c> and not <c>500</c>.
/// </remarks>
public sealed class ClientSignalEndpointsTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void TicketRoute_IsThePathAClientComposes() =>
        Assert.Equal("/signals/ticket", ClientSignalEndpoints.TicketRoute);

    /// <summary>A minted ticket is answered with the value to present and the moment presenting it stops working.</summary>
    [Fact]
    public void MintTicket_ACallerActingForAnOwner_AnswersTheTicketAndWhenItExpires()
    {
        // Arrange
        var tickets = new ClientSignalTickets(new FakeTimeProvider(Instant));

        // Act
        var result = ClientSignalEndpoints.MintTicket(AuthorizationFor(SyntheticMailOwner.Deployment), tickets);

        // Assert
        var answered = Assert.IsType<Ok<ClientSignalTicketResponse>>(result.Result);
        Assert.NotNull(answered.Value);
        Assert.Equal(Instant + ClientSignalTickets.Lifetime, answered.Value.ExpiresAt);
        Assert.Equal(SyntheticMailOwner.Deployment, tickets.Redeem(answered.Value.Ticket));
    }

    /// <summary>A deployment already holding every ticket it will hold says so as a condition that passes, not as a fault.</summary>
    [Fact]
    public void MintTicket_WithAsManyTicketsOutstandingAsAreHeld_AnswersServiceUnavailableRatherThanATicket()
    {
        // Arrange
        var tickets = new ClientSignalTickets(new FakeTimeProvider(Instant));
        var authorization = AuthorizationFor(SyntheticMailOwner.Deployment);

        for (var minted = 0; minted < ClientSignalTickets.MostOutstandingTickets; minted++)
        {
            tickets.Mint(SyntheticMailOwner.Deployment);
        }

        // Act
        var result = ClientSignalEndpoints.MintTicket(authorization, tickets);

        // Assert
        var refused = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, refused.StatusCode);
    }

    private static AccessAuthorization AuthorizationFor(MailOwnerId owner)
    {
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(
            AuthorizedPrincipal.CallerActingFor(owner, "a-client", [MailFathomPermission.MailRead]));

        return new AccessAuthorization(principals);
    }
}
