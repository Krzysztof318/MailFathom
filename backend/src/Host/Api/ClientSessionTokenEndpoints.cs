// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Access.Sessions;
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
/// request it makes from then on costs one indexed read instead of a PBKDF2 record sized for authenticating a
/// person.
/// </para>
/// <para>
/// <b>An access token is the one method it refuses</b>, and <see cref="AuthenticatedByAnAccessToken" /> holds why: a
/// session standing in for a token would outlive the token, the authorization server's revocation of it, and the
/// scopes that server's entry requires — while saving a caller nothing, a token costing no derivation to validate.
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

    /// <summary>What a caller is told where this deployment will hold no session for what the request presented.</summary>
    /// <remarks>
    /// Sorted apart from the bound rather than collapsed into it, because a client does two different things with
    /// them: this one signs in again, and the bound is tried again in a moment. Both cases reach it — a presented
    /// token the deployment is not holding, which nothing refuses at authentication on an endpoint requiring no
    /// credential, and a credential or a user an operator ended, which the write that would have held the session
    /// finds rather than guesses.
    /// </remarks>
    private static ProblemHttpResult SessionNoLongerAccepted() => TypedResults.Problem(
        "This deployment will hold no session for what this request presented. Sign in again.",
        statusCode: StatusCodes.Status401Unauthorized);

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
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the token, <c>401</c> where this deployment will hold no session for what was presented, <c>403</c> where an access token is what admitted the request, or <c>503</c> where the deployment is already holding as many sessions as it will hold.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required service is <see langword="null" />.</exception>
    /// <exception cref="ClientSessionStoreUnavailableException">Thrown when the deployment's sessions could not be reached, which <see cref="ClientSessionStoreUnavailableHandler" /> answers as unavailable rather than as unauthenticated.</exception>
    /// <remarks>
    /// Renewal is recognized from the credential rather than from a second route or a body: a request already
    /// authenticated by a session token is one, and replacing the presented token is what makes one sign-in hold one
    /// live token however long a client stays open. What a renewal carries forward is what the sign-in established, so
    /// the grant on the answer is the grant the exchange resolved.
    /// </remarks>
    internal static async Task<Results<Ok<ClientSessionTokenResponse>, ProblemHttpResult>> Exchange(
        HttpContext context,
        [FromServices] AccessAuthorization authorization,
        [FromServices] ClientSessionTokens sessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(sessions);

        var presented = PresentedToken(context);

        if (presented is null && AuthenticatedByAnAccessToken(context))
        {
            return TypedResults.Problem(
                "An access token authenticates each request on its own, so this deployment exchanges none for a "
                + "session. Present the token on every request instead.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (presented is not null)
        {
            return await sessions.RenewAsync(presented, cancellationToken) is { } renewed
                ? TypedResults.Ok(new ClientSessionTokenResponse(renewed.Value, renewed.ExpiresAt))
                : SessionNoLongerAccepted();
        }

        var admitted = new AdmittedUserCredential(
            CredentialBehind(context),
            authorization.RequireUser(),
            [.. MailFathomPermission.All.Where(authorization.Permits)]);

        // Which of the three the store reports is the whole answer, and nothing here asks a second question about it:
        // whether the user and the credential still admit a session is decided inside the transaction that would have
        // written the row, holding both rows while it decides, rather than by a check an operator's act could commit
        // between.
        var mint = await sessions.MintAsync(admitted, cancellationToken);

        return mint switch
        {
            { Outcome: ClientSessionMintOutcome.Minted, Token: { } minted } =>
                TypedResults.Ok(new ClientSessionTokenResponse(minted.Value, minted.ExpiresAt)),
            { Outcome: ClientSessionMintOutcome.NoLongerAdmitted } => SessionNoLongerAccepted(),
            _ => TypedResults.Problem(
                "This deployment is holding as many client sessions as it will hold; try again in a moment.",
                statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }

    /// <summary>Ends the session the request presented, so the token is refused on the next request rather than at expiry.</summary>
    /// <param name="context">The request, whose <c>Authorization</c> header carries the token to end.</param>
    /// <param name="sessions">Holds the sessions the deployment minted.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c>, whether or not the request was carrying a session to end.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required service is <see langword="null" />.</exception>
    /// <exception cref="ClientSessionStoreUnavailableException">Thrown when the deployment's sessions could not be reached, which <see cref="ClientSessionStoreUnavailableHandler" /> answers as unavailable — reporting a sign-out as complete against a store that could not be written would leave the token working.</exception>
    /// <remarks>
    /// One answer either way, and deliberately so: a client signing out has nothing to do differently on being told
    /// that what it presented was a password rather than a session, and answering differently would let a caller ask
    /// this route which of the two somebody else is holding. Signing out is complete when the head has forgotten what
    /// it kept, which it does whatever this answers.
    /// </remarks>
    internal static async Task<NoContent> Revoke(
        HttpContext context,
        [FromServices] ClientSessionTokens sessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sessions);

        await sessions.RevokeAsync(PresentedToken(context), cancellationToken);

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

    /// <summary>Whether an authorization server's access token is what admitted this request.</summary>
    /// <remarks>
    /// A token is the one method this exchange refuses, and refusing it is what keeps the session's life the
    /// deployment's to end. Everything else it accepts is a credential this deployment holds, so disabling or deleting
    /// the row ends the sessions it minted; a token is judged per request against the issuer that signed it, and a
    /// session standing in for one would outlive the token's own expiry, survive the server revoking it, and stop
    /// being measured against the scopes that issuer's entry requires — none of which this process can observe. The
    /// cost of refusing is nothing a token pays: validating one derives no key, which is what the exchange exists to
    /// stop spending per request.
    /// </remarks>
    private static bool AuthenticatedByAnAccessToken(HttpContext context) =>
        context.User.FindFirst(OAuthIdentity.IssuerClaimType) is not null;

    /// <summary>The credential the exchange authenticated, which a session names so that revoking that credential ends it.</summary>
    /// <remarks>
    /// <para>
    /// Read from the claim every user-facing method writes rather than from the principal's own identity, which four of
    /// the five name the credential in and the fifth does not: an access token's principal is named by the issuer and
    /// the subject the deployment authorized. Reading the identity would therefore have minted a session naming no
    /// credential for exactly one method and said nothing about it, so an authenticated principal carrying no such
    /// claim is a fault here rather than a session an operator would later find they could not end.
    /// </para>
    /// <para>
    /// An unauthenticated caller is the one case that names no credential and is not a fault: a client endpoint
    /// requiring no credential admits everybody as the deployment's own user, so there is no row behind the session
    /// and nothing for an operator to revoke it by. The empty identifier says exactly that, and matches no credential
    /// this deployment could ever hold.
    /// </para>
    /// </remarks>
    private static Guid CredentialBehind(HttpContext context) => TransportCallerCredential.CarriedBy(context.User)
        ?? (context.User.Identity is { IsAuthenticated: true }
            ? throw new InvalidOperationException("The exchange was reached by a principal no user credential admitted.")
            : Guid.Empty);
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
