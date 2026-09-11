// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.OAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Security.Sessions;

/// <summary>Authenticates a request against the sessions the deployment holds, whichever replica minted one at a sign-in.</summary>
/// <remarks>
/// <para>
/// The cheapest handler on the surface that reads anything at all: it lifts the bearer credential out of the header,
/// hands it to <see cref="ClientSessionTokens" />, and turns the answer into the framework's own vocabulary. No key is
/// derived and nothing is written — which is the reason a client exchanges its password for one of these rather than
/// presenting the password on every request. What it does cost is one indexed read of the session the deployment
/// holds, on a request that is about to read mail out of the same database, and nothing caches it: a cached session is
/// a revoked session that goes on working for the cache's own window on every replica that had already read one.
/// </para>
/// <para>
/// Every refusal produces one indistinguishable answer: an empty <c>401</c> carrying the same bare challenge, whether
/// the request presented nothing, presented something that is not a token this deployment mints, presented one nobody
/// holds, presented an expired one, or presented one somebody revoked. The reason the framework records reaches the
/// server log only, and even there it names the rejection rather than the credential.
/// </para>
/// <para>
/// <b>A store that cannot answer is the one case that is not a refusal</b>, and it deliberately leaves this handler as
/// an exception rather than as a failed authentication: a client meets <c>401</c> by clearing what it holds and asking
/// for a password, so answering a database that could not be reached that way would turn a blip into a
/// deployment-wide sign-out. <see cref="ClientSessionStoreUnavailableHandler" /> is what turns it into the
/// <c>503</c> the record requires, for every route on the surface at once.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The authentication framework materializes this handler for its registered scheme.")]
internal sealed class ClientSessionTokenAuthenticationHandler
    : AuthenticationHandler<ClientSessionTokenAuthenticationSchemeOptions>
{
    private readonly ClientSessionTokens sessions;

    /// <summary>Initializes a new session-token authentication handler.</summary>
    /// <param name="schemeOptions">Which surface this registration protects.</param>
    /// <param name="loggerFactory">The framework's own logging, which records the reason a refusal carried.</param>
    /// <param name="urlEncoder">The framework's own encoder, unused here and required by the base class.</param>
    /// <param name="sessions">The sessions the deployment is holding, which is what a presented token is judged against.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sessions" /> is <see langword="null" />.</exception>
    public ClientSessionTokenAuthenticationHandler(
        IOptionsMonitor<ClientSessionTokenAuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory loggerFactory,
        UrlEncoder urlEncoder,
        ClientSessionTokens sessions)
        : base(schemeOptions, loggerFactory, urlEncoder)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        this.sessions = sessions;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The header is read through <see cref="object.ToString" /> on the header values, which yields an empty string
    /// when the request carried none and a joined value when it carried several. Both are refused identically, which is
    /// what a request supplying the header twice deserves rather than having one of the two picked for it.
    /// </remarks>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!BearerCredentialHeader.TryRead(this.Request.Headers.Authorization.ToString(), out var presented)
            || await this.sessions.VerifyAsync(presented, this.Context.RequestAborted) is not { } admitted)
        {
            return AuthenticateResult.Fail("The request presented no usable session token.");
        }

        var identity = TransportGrant.IdentityFor(
            admitted.CredentialId.ToString("D", CultureInfo.InvariantCulture),
            ClientSessionTokenAuthentication.CredentialIdClaimType,
            ClientSessionTokenAuthentication.RoleClaimType,
            this.Options.Surface.SessionTokenSchemeName,
            admitted.Permissions);

        // The user and the credential the session was minted under, so the surfaces answer for whose mail the request
        // acts on without the session having had to look anybody up. The credential is written here as well as on the
        // four methods that mint a session, because a claim absent on the one scheme every request after a sign-in
        // arrives under would read as "no credential admitted this" for the ordinary case rather than for none.
        identity.AddClaim(TransportCallerUser.ClaimFor(admitted.User));
        identity.AddClaim(TransportCallerCredential.ClaimFor(admitted.CredentialId));

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name));
    }

    /// <inheritdoc />
    /// <remarks>The bare challenge every method on the surface produces, written where the constant it names lives.</remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        ApiKeyAuthentication.WriteBareChallenge(this.Response);

        return Task.CompletedTask;
    }
}
