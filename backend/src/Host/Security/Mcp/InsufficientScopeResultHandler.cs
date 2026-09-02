// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Security.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Net.Http.Headers;

namespace MailFathom.Host.Security.Mcp;

/// <summary>Answers an authenticated caller whose token does not carry the scopes this deployment requires.</summary>
/// <remarks>
/// <para>
/// The two refusals say different things and must not be confused. A caller with no usable credential is told to
/// authenticate, with a <c>401</c> and the address of the metadata document that says where to do it. A caller who
/// authenticated and is still not allowed through has nothing to gain from authenticating again, so it receives a
/// <c>403</c> naming the scopes that would have sufficed — which is what lets a client ask its authorization server for
/// them rather than retrying the same token.
/// </para>
/// <para>
/// The scopes are safe to write into a header because they were validated as scope tokens at startup: a value carrying a
/// space, a quotation mark, or a backslash is refused there, so nothing configured here can split the parameter or end
/// it early. Nothing about the caller, the token, or which scope was missing appears; what the challenge states is what
/// the caller's own authorization server could have issued, which is the same for everyone that server signed in.
/// </para>
/// <para>
/// The handler is registered globally because that is the seam the authorization middleware offers, but it only replaces
/// the response for a refusal that an authenticated caller provoked, and the MCP endpoint is the only endpoint carrying
/// an authorization requirement. Everything else still reaches the framework's own handler.
/// </para>
/// </remarks>
internal sealed class InsufficientScopeResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly AuthorizationMiddlewareResultHandler FrameworkHandler = new();

    private readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> requiredScopesByIssuer;

    private readonly Dictionary<string, string> challengesByIssuer;

    /// <summary>Initializes a new handler for the scopes this deployment requires.</summary>
    /// <param name="requiredScopesByIssuer">The scopes an access token must carry, keyed by the issuer whose entry asks for them.</param>
    /// <param name="protectedResourceMetadataAddress">Where the protected resource metadata document is published.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requiredScopesByIssuer" /> or <paramref name="protectedResourceMetadataAddress" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// One challenge per issuer, composed once, because each configured entry states the scopes asked of the servers it
    /// configures. Naming the union instead would send a client to ask its own authorization server for a scope some
    /// other server's entry requires, which that server has no reason to issue.
    /// </remarks>
    internal InsufficientScopeResultHandler(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> requiredScopesByIssuer,
        string protectedResourceMetadataAddress)
    {
        ArgumentNullException.ThrowIfNull(requiredScopesByIssuer);
        ArgumentNullException.ThrowIfNull(protectedResourceMetadataAddress);

        this.requiredScopesByIssuer = requiredScopesByIssuer;
        this.challengesByIssuer = requiredScopesByIssuer.ToDictionary(
            issuerScopes => issuerScopes.Key,
            issuerScopes =>
                $"Bearer error=\"insufficient_scope\", scope=\"{string.Join(' ', issuerScopes.Value)}\", "
                + $"resource_metadata=\"{protectedResourceMetadataAddress}\"",
            StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        // A caller refused for who they are, rather than for what their token was issued for, receives the framework's
        // plain 403. Naming scopes there would send a client to ask its authorization server for something that would
        // change nothing, and would say that the scopes are all that stands between it and the mailbox. So does a caller
        // whose issuer this deployment configures no entry for, which no validated token can be.
        if (!authorizeResult.Forbidden
            || context.User.FindFirst(OAuthIdentity.IssuerClaimType)?.Value is not { } issuer
            || !this.requiredScopesByIssuer.TryGetValue(issuer, out var requiredScopes)
            || OAuthIdentity.CarriesEveryScope(context.User, requiredScopes))
        {
            return FrameworkHandler.HandleAsync(next, context, policy, authorizeResult);
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers[HeaderNames.WWWAuthenticate] = this.challengesByIssuer[issuer];

        return Task.CompletedTask;
    }
}
