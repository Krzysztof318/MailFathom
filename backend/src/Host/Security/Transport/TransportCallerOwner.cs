// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Security.Transport;

/// <summary>How the owner a credential belongs to travels on the principal that credential produced.</summary>
/// <remarks>
/// <para>
/// Most credentials name no owner. A key, a public key, and an access token are things a deployment configured or an
/// authorization server issued, so whose mail a caller presenting one acts on is decided by the surface it reached
/// rather than by the credential — which is the arrangement
/// <see cref="TransportAuthorizedPrincipalSource" /> describes and which nothing here changes.
/// </para>
/// <para>
/// A username and password are different: the credential is a record of one owner's own, so the owner is a fact the
/// authentication established and has to survive to the point where a principal is composed. It travels as a claim
/// beside the grant for the same reason the grant does — the decision was taken where the credential was judged, and
/// nothing per request re-reads a table to recover it.
/// </para>
/// <para>
/// A principal carrying no such claim is not a principal acting for nobody; it is a principal whose credential said
/// nothing about an owner, and the surface answers for it. Reading the two apart is what keeps a credential that does
/// name an owner from being widened to whoever the deployment happens to serve.
/// </para>
/// </remarks>
internal static class TransportCallerOwner
{
    /// <summary>The claim type carrying the owner a credential belongs to.</summary>
    /// <remarks>
    /// A private claim type rather than a registered one: the value is MailFathom's own generated identity for an owner,
    /// not a subject any other system issued. It names nobody outside this deployment and carries nothing about the
    /// person, which is what makes it safe to sit on a principal that a diagnostic may render.
    /// </remarks>
    internal const string OwnerClaimType = "urn:mailfathom:owner";

    /// <summary>Turns an owner into the claim an identity carries it as.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <returns>The claim.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, which is a credential that resolved nothing rather than one acting for the deployment.</exception>
    internal static Claim ClaimFor(MailOwnerId owner) => owner.IsSpecified
        ? new Claim(OwnerClaimType, owner.Value.ToString("D", null))
        : throw new ArgumentException("A credential that names an owner names a specified one.", nameof(owner));

    /// <summary>Reports the owner an authenticated principal's credential belongs to.</summary>
    /// <param name="principal">The principal a validated credential produced.</param>
    /// <returns>The owner, or <see langword="null" /> when the credential named none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principal" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A claim that does not parse as a generated identifier is read as no owner rather than as a fault. Nothing outside
    /// this process writes one — every value here was composed from a resolved credential a moment earlier — so an
    /// unreadable value can only be a principal something else assembled, and answering "no owner" leaves the surface
    /// to decide instead of admitting a caller for whoever the value happened to parse as.
    /// </remarks>
    internal static MailOwnerId? CarriedBy(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.FindFirstValue(OwnerClaimType) is { } written
            && Guid.TryParse(written, out var owner)
            && owner != Guid.Empty
                ? MailOwnerId.Create(owner)
                : null;
    }
}
