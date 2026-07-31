// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Security.Claims;
using MailFathom.Infrastructure.Security;

namespace MailFathom.Host.Security;

/// <summary>What an authenticated caller must satisfy before a tool runs.</summary>
/// <remarks>
/// <para>
/// Which mailboxes a tool reads is decided by the configured accounts and not by who is asking, so every caller this
/// policy admits reads the same mail. That is what makes the two questions below the whole of the boundary: a token
/// proves which person an authorization server signed in, and this decides whether that person is one this deployment
/// serves at all.
/// </para>
/// <para>
/// Neither an authorized subject nor a required scope is ever asked of an API key. A key is a credential the operator
/// provisioned by writing it into this deployment's configuration, so the authorization it carries is that decision; a
/// token is issued by a server that decides for itself who receives one, which is what makes both worth checking.
/// Asking either of a key would mean asking a credential for something nothing can ever put in it.
/// </para>
/// </remarks>
internal static class McpAccessPolicy
{
    /// <summary>The name the endpoint's authorization requirement is registered under.</summary>
    internal const string PolicyName = "MailFathomEndpoint";

    /// <summary>Judges an authenticated principal against the people the deployment serves and the scopes it requires.</summary>
    /// <param name="principal">The principal a validated credential produced.</param>
    /// <param name="authorizedIdentities">The issuer and subject pairs a token may name, taken from the configured authorization servers.</param>
    /// <param name="requiredScopes">The scopes an access token must carry, empty when any token from an authorized subject suffices.</param>
    /// <returns><see langword="true" /> when the caller may reach a tool; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>A token has to satisfy both: a subject the deployment serves and every scope it requires. Neither substitutes for the other, because a scope says what a token was issued for and a subject says whose it is.</remarks>
    internal static bool IsAuthorized(
        ClaimsPrincipal principal,
        IReadOnlySet<string> authorizedIdentities,
        IReadOnlyCollection<string> requiredScopes)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(authorizedIdentities);
        ArgumentNullException.ThrowIfNull(requiredScopes);

        if (principal.Identity is not { IsAuthenticated: true })
        {
            return false;
        }

        if (AuthenticatedWithAnApiKey(principal))
        {
            return true;
        }

        return NamesAnAuthorizedSubject(principal, authorizedIdentities)
            && McpOAuthIdentity.CarriesEveryScope(principal, requiredScopes);
    }

    /// <summary>Reports whether an authenticated token names one of the people this deployment serves.</summary>
    /// <remarks>
    /// The comparison is against the issuer and subject together, so a subject one authorization server authorized is
    /// not authorized by another server that happens to name someone the same way. A principal carrying no identity at
    /// all is refused rather than treated as unrestricted.
    /// </remarks>
    private static bool NamesAnAuthorizedSubject(ClaimsPrincipal principal, IReadOnlySet<string> authorizedIdentities) =>
        McpOAuthIdentity.IdentityCarriedBy(principal) is { } identity && authorizedIdentities.Contains(identity);

    /// <summary>Reports whether an API key produced this principal, judged by what the principal carries rather than by which scheme named it.</summary>
    private static bool AuthenticatedWithAnApiKey(ClaimsPrincipal principal) =>
        principal.HasClaim(claim => claim.Type == McpApiKeyAuthentication.ApiKeyNameClaimType);
}
