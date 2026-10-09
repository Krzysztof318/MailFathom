// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.Basic;
using MailFathom.Host.Security.ClientAssertions;
using MailFathom.Host.Security.Sessions;
using MailFathom.Infrastructure.Security.OAuth;

namespace MailFathom.Host.Security.Transport;

/// <summary>What an authenticated caller must satisfy before a protected surface serves it.</summary>
/// <remarks>
/// <para>
/// Whether a surface is served at all is decided by its configuration and not by who is asking, so admission is the
/// same judgement for every caller one of these policies lets through. That is what makes the two questions below the
/// whole of the boundary: a token proves which person an authorization server signed in, and this decides whether that
/// person is one this deployment serves at all. What an admitted caller then reaches does vary — the paragraph below on
/// the grant says how — and it is decided past this point rather than here.
/// </para>
/// <para>
/// The rule is the same for every surface, and it is the registration that differs: each surface names its own policy
/// through <see cref="TransportSurface.AccessPolicyName" /> and hands in its own required scopes, keyed by the issuer
/// that signed the token. Sharing the judgement while separating the inputs is what keeps two surfaces from drifting into two
/// definitions of what an authorized caller is.
/// </para>
/// <para>
/// A required scope is never asked of a credential this deployment holds — an API key,
/// a client public key an assertion was verified against, or a user's password. Such a credential exists because
/// somebody provisioned it here, so the authorization it carries is that decision; a token is issued by a server that
/// decides for itself who receives one, which is what makes both worth checking. Asking either of a held credential
/// would mean asking it for something nothing can ever put in it.
/// </para>
/// <para>
/// What an admitted caller may then <em>do</em> is a separate question and is not asked here. The permissions travel on
/// the principal this judges, written by whichever scheme authenticated it, and narrow what the user the credential names
/// holds. Admission stays one shared judgement while each surface comes to enforce the grant in the terms its own
/// callers are answered in. <see cref="TransportGrant" /> is how one is read back, through the caller the
/// application layer is handed. The MCP surface serves each caller the tools its grant permits and answers a call for
/// any other as a tool that does not exist; the administrative surface refuses a route the grant does not admit and
/// names the one permission that would have sufficed, because the caller there is an operator at their own terminal.
/// </para>
/// </remarks>
internal static class TransportAccessPolicy
{
    /// <summary>Judges an authenticated principal on a surface whose every credential resolves the user it acts for.</summary>
    /// <param name="principal">The principal a validated credential produced.</param>
    /// <param name="requiredScopesByIssuer">The scopes an access token must carry, keyed by the issuer whose entry asks for them.</param>
    /// <returns><see langword="true" /> when the caller may reach the surface; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// There is no set of authorized identities to compare against here, because who this deployment serves is a set of
    /// records rather than a list an operator wrote: a key, a public key, a password, and a validated subject each
    /// resolve one, and a credential that resolves none was already refused where it was judged. What is left worth
    /// asking is that the credential did resolve a user — which is why the user claim is required rather than
    /// assumed, so a principal something else assembled cannot reach a mailbox by carrying a grant alone.
    /// </para>
    /// <para>
    /// The scopes are still asked of a token, and only of a token, for the reason the shared judgement gives: a scope is
    /// something an authorization server decides per issuance, and no credential this deployment holds can carry one.
    /// </para>
    /// </remarks>
    internal static bool IsUserAuthorized(
        ClaimsPrincipal principal,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> requiredScopesByIssuer)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(requiredScopesByIssuer);

        if (principal.Identity is not { IsAuthenticated: true } || TransportCallerUser.CarriedBy(principal) is null)
        {
            return false;
        }

        return AuthenticatedWithACredentialThisDeploymentHolds(principal)
            || CarriesEveryScopeItsIssuerRequires(principal, requiredScopesByIssuer);
    }

    /// <summary>Reports whether a token carries every scope the entry that trusts its issuer asks for.</summary>
    private static bool CarriesEveryScopeItsIssuerRequires(
        ClaimsPrincipal principal,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> requiredScopesByIssuer) =>
        principal.FindFirst(OAuthIdentity.IssuerClaimType)?.Value is { } issuer
        && requiredScopesByIssuer.TryGetValue(issuer, out var requiredScopes)
        && OAuthIdentity.CarriesEveryScope(principal, requiredScopes);

    /// <summary>Reports whether a credential this deployment holds rather than a token an authorization server issued produced this principal, judged by what the principal carries rather than by which scheme named it.</summary>
    /// <remarks>
    /// <para>
    /// Each claim type is read rather than one of them standing for the others, because each names a different kind of
    /// credential and a principal carrying none of them has to fall through to the token rules. On every surface all
    /// three name one of this deployment's own credential rows, and the identity is established before this runs, so
    /// what is left to decide is that it was not an unrecognized subject.
    /// </para>
    /// <para>
    /// A session token is the fourth, and it is read as its own claim rather than as the credential it was minted for:
    /// the session is a credential this deployment holds — minted here, held here, and revoked here — whatever method
    /// was presented at the exchange. Reading <see cref="TransportCallerCredential.CredentialClaimType" /> instead
    /// would admit a principal an access token produced without the scopes its issuer asks for, because that claim
    /// travels on a token's principal too and would answer here before those scopes were ever compared.
    /// </para>
    /// </remarks>
    private static bool AuthenticatedWithACredentialThisDeploymentHolds(ClaimsPrincipal principal) =>
        principal.HasClaim(claim =>
            claim.Type == ApiKeyAuthentication.ApiKeyNameClaimType
            || claim.Type == ClientAssertionAuthentication.KeyNameClaimType
            || claim.Type == BasicAuthentication.CredentialIdClaimType
            || claim.Type == ClientSessionTokenAuthentication.CredentialIdClaimType);
}
