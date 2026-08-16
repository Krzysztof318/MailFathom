// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Security.Transport;

/// <summary>How the permissions a credential was granted travel on the principal that credential produced.</summary>
/// <remarks>
/// <para>
/// The grant is resolved once, while the host is composed, from the configuration entry that admits the credential. It
/// reaches a request as claims on the authenticated principal rather than as a lookup a policy performs, which is what
/// keeps the decision where it was made: nothing per request re-reads a configuration section, and nothing downstream
/// has to know which entry, which key, or which authorization server was involved.
/// </para>
/// <para>
/// A claim per permission rather than one carrying a joined list, because that is what a claims principal is for and it
/// leaves no separator for a permission name to be mistaken across. The names are MailFathom's own published
/// identities, so nothing here discloses a credential, a subject, or anything an authorization server sent.
/// </para>
/// <para>
/// A principal carrying none of these claims holds nothing, which is a credential whose entry granted nothing rather
/// than one whose grant was never read: every scheme that authenticates writes the resolved grant, including the empty
/// one. What a caller admitted where the surface configures no credential at all holds is a different question, decided
/// by the surface rather than by a principal, and answered where that posture is enforced.
/// </para>
/// </remarks>
internal static class TransportGrant
{
    /// <summary>The claim type carrying one permission the caller was granted.</summary>
    /// <remarks>
    /// A private claim type rather than a registered one: the value is a capability this repository publishes, not a
    /// subject or an entitlement any other system issued. One claim type across every surface and every credential
    /// kind, because the permission name already says which surface it belongs to and a principal never crosses one.
    /// </remarks>
    internal const string PermissionClaimType = "urn:mailfathom:permission";

    /// <summary>Turns a resolved grant into the claims an identity carries it as.</summary>
    /// <param name="permissions">The permissions the entry granted, empty when it granted none.</param>
    /// <returns>One claim per permission, in the order given.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permissions" /> is <see langword="null" />.</exception>
    internal static IReadOnlyList<Claim> ClaimsFor(IEnumerable<MailFathomPermission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return [.. permissions.Select(permission => new Claim(PermissionClaimType, permission.Name))];
    }

    /// <summary>Reports the permissions an authenticated principal was granted.</summary>
    /// <param name="principal">The principal a validated credential produced.</param>
    /// <returns>The granted permissions, empty when the principal holds none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principal" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A claim naming something this repository does not publish is dropped rather than reported. Nothing outside this
    /// process writes one — every claim here was composed from a published name a moment earlier — so an unmatched
    /// value can only be a name a later release retired, and reading it as a permission is the one outcome that would
    /// grant something.
    /// </remarks>
    internal static IReadOnlySet<MailFathomPermission> PermissionsCarriedBy(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal
            .FindAll(PermissionClaimType)
            .Select(claim => MailFathomPermission.TryParse(claim.Value, out var permission) ? permission : default)
            .Where(permission => permission.IsSpecified)
            .ToHashSet();
    }
}
