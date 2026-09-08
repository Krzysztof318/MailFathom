// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;

namespace MailFathom.Host.Security.Transport;

/// <summary>How the user credential a request was admitted by travels on the principal it produced.</summary>
/// <remarks>
/// <para>
/// It exists for one reader: the route that exchanges a credential for a session has to record which credential minted
/// that session, so disabling or deleting the credential ends the sessions it minted rather than leaving them working
/// until they expire. Nothing else asks, and nothing per request re-reads a table to recover it.
/// </para>
/// <para>
/// It is a claim of its own rather than the identity's name, which is what
/// <see cref="Sessions.ClientSessionTokens" /> would otherwise have had to read. Four of the five user-facing methods
/// do name the credential there and the fifth does not: an access token's principal is named by the issuer and the
/// subject the deployment authorized, because that is what an operator diagnosing a refusal needs to see. Reading the
/// name would therefore have minted a session under no credential for exactly the method whose sessions an operator is
/// most likely to want ended, and it would have done so silently.
/// </para>
/// <para>
/// A principal carrying no such claim is one no user credential admitted — the deployment administrator, this process's
/// own identity, a key configured rather than provisioned — and the caller answers for that rather than reading it as a
/// credential it may act on.
/// </para>
/// </remarks>
internal static class TransportCallerCredential
{
    /// <summary>The claim type carrying the user credential a request was admitted by.</summary>
    /// <remarks>
    /// A private claim type rather than a registered one, for the reason
    /// <see cref="TransportCallerUser.UserClaimType" /> is: the value is MailFathom's own generated identity for a
    /// record beside a user, it names nothing outside this deployment, and it carries nothing about the person.
    /// </remarks>
    internal const string CredentialClaimType = "urn:mailfathom:user-credential";

    /// <summary>Turns a credential into the claim an identity carries it as.</summary>
    /// <param name="credentialId">The credential the request was admitted by.</param>
    /// <returns>The claim.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="credentialId" /> names no credential, which is a principal that resolved none rather than one this may be written for.</exception>
    internal static Claim ClaimFor(Guid credentialId) => credentialId != Guid.Empty
        ? new Claim(CredentialClaimType, credentialId.ToString("D", null))
        : throw new ArgumentException("A principal admitted by a credential names a specified one.", nameof(credentialId));

    /// <summary>Reports the user credential an authenticated principal was admitted by.</summary>
    /// <param name="principal">The principal a validated credential produced.</param>
    /// <returns>The credential, or <see langword="null" /> when no user credential admitted the request.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principal" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A claim that does not parse is read as no credential rather than as a fault, for the reason
    /// <see cref="TransportCallerUser.CarriedBy" /> reads an unreadable user as none: nothing outside this process
    /// writes one, so an unreadable value can only be a principal something else assembled.
    /// </remarks>
    internal static Guid? CarriedBy(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.FindFirstValue(CredentialClaimType) is { } written
            && Guid.TryParse(written, out var credentialId)
            && credentialId != Guid.Empty
                ? credentialId
                : null;
    }
}
