// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Claims;
using MailMcp.Infrastructure.Security;

namespace MailMcp.Host.Security;

/// <summary>What an authenticated caller must satisfy before a tool runs.</summary>
/// <remarks>
/// <para>
/// The endpoint asks one question today: is this a caller the deployment recognizes. Which mailboxes a tool then reads
/// is decided by the configured owner and has always been, so nothing here distinguishes between two recognized callers.
/// The seam for that distinction is the scope list, which is why it exists before anything varies by it.
/// </para>
/// <para>
/// A required scope constrains an access token and never an API key. A key is a credential the operator provisioned by
/// writing it into this deployment's configuration, so the authorization it carries is that decision; a token is issued
/// by a server that decides for itself who receives one, which is what makes a scope the thing worth checking. Requiring
/// a scope of a key would mean asking a credential for something nothing can ever put in it.
/// </para>
/// </remarks>
internal static class McpAccessPolicy
{
    /// <summary>The name the endpoint's authorization requirement is registered under.</summary>
    internal const string PolicyName = "MailMcpEndpoint";

    /// <summary>Judges an authenticated principal against the scopes the deployment requires.</summary>
    /// <param name="principal">The principal a validated credential produced.</param>
    /// <param name="requiredScopes">The scopes an access token must carry, empty when any valid credential suffices.</param>
    /// <returns><see langword="true" /> when the caller may reach a tool; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principal" /> or <paramref name="requiredScopes" /> is <see langword="null" />.</exception>
    internal static bool IsAuthorized(ClaimsPrincipal principal, IReadOnlyCollection<string> requiredScopes)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(requiredScopes);

        if (principal.Identity is not { IsAuthenticated: true })
        {
            return false;
        }

        return AuthenticatedWithAnApiKey(principal) || McpOAuthIdentity.CarriesEveryScope(principal, requiredScopes);
    }

    /// <summary>Reports whether an API key produced this principal, judged by what the principal carries rather than by which scheme named it.</summary>
    private static bool AuthenticatedWithAnApiKey(ClaimsPrincipal principal) =>
        principal.HasClaim(claim => claim.Type == McpApiKeyAuthentication.ApiKeyNameClaimType);
}
