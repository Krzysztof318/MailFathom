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
/// through <see cref="TransportSurface.AccessPolicyName" /> and hands in its own required scopes — keyed by the
/// administrator's identity on the administrative surface, and by the issuer that signed the token on a mail-serving
/// one. Sharing the judgement while separating the inputs is what keeps two surfaces from drifting into two
/// definitions of what an authorized caller is.
/// </para>
/// <para>
/// Neither an authorized subject nor a required scope is ever asked of a credential this deployment holds — an API key,
/// a client public key an assertion was verified against, or a user's password. Such a credential exists because
/// somebody provisioned it here, so the authorization it carries is that decision; a token is issued by a server that
/// decides for itself who receives one, which is what makes both worth checking. Asking either of a held credential
/// would mean asking it for something nothing can ever put in it.
/// </para>
/// <para>
/// There are two judgements rather than one, and the difference is what a subject decides. On the administrative
/// surface the subjects written under each administrator are who may sign in, so <see cref="IsAuthorized" /> compares
/// against them. On a
/// mail-serving surface a subject resolves one user's credential record, so <see cref="IsUserAuthorized" /> asks that
/// the credential named a user at all. Everything else — the scopes, the held-credential shortcut, the requirement
/// that the principal be authenticated — is shared, which is what keeps the two from drifting into two definitions of
/// an admitted caller.
/// </para>
/// <para>
/// What an admitted caller may then <em>do</em> is a separate question and is not asked here. The permissions travel on
/// the principal this judges, written by whichever scheme authenticated it, and where they were granted follows the
/// same split as the user: on a mail-serving surface the credential record carries the grant beside the user it
/// names, and on the administrative surface the administrator it admits states it. Either way admission stays one shared
/// judgement while each surface comes to enforce the grant in the terms its own callers are answered in. <see cref="TransportGrant" /> is how one is read back, through the caller the
/// application layer is handed. The MCP surface serves each caller the tools its grant permits and answers a call for
/// any other as a tool that does not exist; the administrative surface refuses a route the grant does not admit and
/// names the one permission that would have sufficed, because the caller there is an operator at their own terminal.
/// </para>
/// </remarks>
internal static class TransportAccessPolicy
{
    /// <summary>Judges an authenticated principal against the administrators the deployment names and the scopes each one's credential requires.</summary>
    /// <param name="principal">The principal a validated credential produced.</param>
    /// <param name="requiredScopesByIdentity">The scopes an access token must carry, keyed by the issuer and subject of the administrator's credential that asks for them.</param>
    /// <returns><see langword="true" /> when the caller may reach the surface; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A token has to satisfy both: a subject an administrator was written with and every scope that administrator's
    /// credential requires. Neither substitutes for the other, because a scope says what a token was issued for and a
    /// subject says whose it is. Authentication has already refused a token naming nobody, so the first half is asked
    /// again here only so a principal something else assembled cannot pass on a grant alone.
    /// <para>
    /// The scopes are looked up by the token's own identity rather than by its issuer, because two administrators
    /// signing in through one server may each be asked for different ones. An identity no credential names is refused
    /// rather than admitted with nothing asked of it — treating the absence as "no scopes required" is the reading that
    /// turns a future gap into an open door.
    /// </para>
    /// </remarks>
    internal static bool IsAuthorized(
        ClaimsPrincipal principal,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> requiredScopesByIdentity)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(requiredScopesByIdentity);

        if (principal.Identity is not { IsAuthenticated: true })
        {
            return false;
        }

        if (AuthenticatedWithACredentialThisDeploymentHolds(principal))
        {
            return true;
        }

        return OAuthIdentity.IdentityCarriedBy(principal) is { } identity
            && requiredScopesByIdentity.TryGetValue(identity, out var requiredScopes)
            && OAuthIdentity.CarriesEveryScope(principal, requiredScopes);
    }

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
    /// credential and a principal carrying none of them has to fall through to the token rules. What each one names
    /// follows the surface: on the administrative surface an API key and a client public key are stated in
    /// configuration, and on a mail-serving surface all three name one of this deployment's own credential rows. The
    /// distinction does not matter here — the identity is established before this runs either way, and what is left to
    /// decide is that it was not an unrecognized subject.
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
