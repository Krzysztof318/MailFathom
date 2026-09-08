// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Sessions;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.OAuth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the exchange a client signs in through, and the revocation it signs out through.</summary>
/// <remarks>
/// <para>
/// <b>This is where a password is derived, and it is the only place left that derives one.</b> The exchange is an
/// ordinary route on the client group, so the credential presented to it is judged by whichever scheme the surface
/// routes it to — the password scheme included, with the per-source and per-username attempt bounds that scheme has
/// always applied. What is different is what the caller does with the answer: it holds a token afterwards, and every
/// request it makes from then on costs a dictionary lookup instead of a PBKDF2 record sized for authenticating a
/// person.
/// </para>
/// <para>
/// <b>The same route renews.</b> A client presenting a live session token receives a fresh one and the presented one
/// stops working, so renewing needs no second route, no second credential, and nobody typing a password. That is the
/// whole of the renewal contract: read <c>expiresAt</c>, call this before it, keep what comes back.
/// </para>
/// <para>
/// Both are <c>POST</c>, which minting a credential has to be for the reason
/// <see cref="Signals.ClientSignalEndpoints.MintTicket" /> is one — a <c>GET</c> is a route a cache, a prefetch, or a
/// link preview would spend — and which revocation is because the surface publishes two verbs and adding a third for
/// one route would widen what a browser is told it may send to every route on it.
/// </para>
/// </remarks>
internal static class ClientSessionTokenEndpoints
{
    /// <summary>The route a credential is exchanged for a session token on, and renewed on, relative to the client prefix.</summary>
    internal const string ExchangeRoute = "/session/token";

    /// <summary>The route a session is ended on, relative to the client prefix.</summary>
    internal const string RevocationRoute = "/session/token/revocation";

    /// <summary>Maps the exchange and the revocation into the client group, so both inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientSessionTokens(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        // No permission on either, for the reason the session read carries none: a caller exchanging the credential it
        // already authenticated with, or ending the session it is holding, is asking about itself rather than about
        // anybody's mail. A grant narrowed to nothing still signs in and still signs out.
        api.MapPost(ExchangeRoute, Exchange).RequireNoPermission();
        api.MapPost(RevocationRoute, Revoke).RequireNoPermission();
    }

    /// <summary>Exchanges the credential this request authenticated with for a session token, or renews the one it presented.</summary>
    /// <param name="context">The request, whose <c>Authorization</c> header says whether this is a sign-in or a renewal.</param>
    /// <param name="authorization">Reports the user the credential named and what it grants.</param>
    /// <param name="sessions">Mints the token and holds the session until it is revoked or expires.</param>
    /// <returns><c>200</c> with the token, or <c>503</c> where this process is already holding as many sessions as it will hold.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required service is <see langword="null" />.</exception>
    /// <remarks>
    /// Renewal is recognized from the credential rather than from a second route or a body: a request already
    /// authenticated by a session token is one, and replacing the presented token is what makes one sign-in hold one
    /// live token however long a client stays open. What a renewal carries forward is what the sign-in established, so
    /// the grant on the answer is the grant the exchange resolved.
    /// </remarks>
    internal static Results<Ok<ClientSessionTokenResponse>, ProblemHttpResult> Exchange(
        HttpContext context,
        [FromServices] AccessAuthorization authorization,
        [FromServices] ClientSessionTokens sessions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(sessions);

        var minted = PresentedToken(context) is { } presented
            ? sessions.Renew(presented)
            : sessions.Mint(new AdmittedUserCredential(
                CredentialBehind(context),
                authorization.RequireUser(),
                [.. MailFathomPermission.All.Where(authorization.Permits)]));

        return minted is null
            ? TypedResults.Problem(
                "This deployment is holding as many client sessions as it will hold; try again in a moment.",
                statusCode: StatusCodes.Status503ServiceUnavailable)
            : TypedResults.Ok(new ClientSessionTokenResponse(minted.Value, minted.ExpiresAt));
    }

    /// <summary>Ends the session the request presented, so the token is refused on the next request rather than at expiry.</summary>
    /// <param name="context">The request, whose <c>Authorization</c> header carries the token to end.</param>
    /// <param name="sessions">Holds the sessions this process minted.</param>
    /// <returns><c>204</c>, whether or not the request was carrying a session to end.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required service is <see langword="null" />.</exception>
    /// <remarks>
    /// One answer either way, and deliberately so: a client signing out has nothing to do differently on being told
    /// that what it presented was a password rather than a session, and answering differently would let a caller ask
    /// this route which of the two somebody else is holding. Signing out is complete when the head has forgotten what
    /// it kept, which it does whatever this answers.
    /// </remarks>
    internal static NoContent Revoke(HttpContext context, [FromServices] ClientSessionTokens sessions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sessions);

        sessions.Revoke(PresentedToken(context));

        return TypedResults.NoContent();
    }

    /// <summary>The session token this request presented, or <see langword="null" /> where it authenticated some other way.</summary>
    /// <remarks>
    /// Read off the header rather than off the principal, because a token is the one credential on this surface that
    /// has to reach a route: the principal deliberately carries the credential the session resolved and never the
    /// material that resolved it, which is what keeps a live credential out of every diagnostic that renders one. What
    /// this reads is bounded and refused by the store, so nothing here judges the value it lifts.
    /// </remarks>
    private static string? PresentedToken(HttpContext context) =>
        BearerCredentialHeader.TryRead(context.Request.Headers.Authorization.ToString(), out var presented)
        && presented.StartsWith(ClientSessionTokens.TokenPrefix, StringComparison.Ordinal)
            ? presented
            : null;

    /// <summary>The credential the exchange authenticated, which a session names so that revoking that credential ends it.</summary>
    /// <remarks>
    /// Read from the claim every user-facing method writes rather than from the principal's own identity, which four of
    /// the five name the credential in and the fifth does not: an access token's principal is named by the issuer and
    /// the subject the deployment authorized. Reading the identity would therefore have minted an unrevocable session
    /// for exactly one method and said nothing about it, which is the failure this refuses instead — a principal
    /// carrying no such claim is one no user credential admitted, and no scheme this surface routes to produces one.
    /// </remarks>
    private static Guid CredentialBehind(HttpContext context) =>
        TransportCallerCredential.CarriedBy(context.User)
        ?? throw new InvalidOperationException("The exchange was reached by a principal no user credential admitted.");
}

/// <summary>What the exchange answers with.</summary>
/// <param name="Token">The value the client presents on every request afterwards, in place of the credential it signed in with.</param>
/// <param name="ExpiresAt">When presenting it stops working, so a client renews before that rather than finding out by being refused.</param>
/// <remarks>
/// It names no user and no grant, which the session route already answers and answers for whichever credential is
/// presented. What this route is for is the credential itself, and a body carrying anything beside it would be a second
/// place for the surface's answer about a caller to be composed.
/// </remarks>
internal sealed record ClientSessionTokenResponse(string Token, DateTimeOffset ExpiresAt);
