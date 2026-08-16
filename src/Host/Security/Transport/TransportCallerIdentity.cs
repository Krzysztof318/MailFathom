// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Host.Security.ApiKeys;

namespace MailFathom.Host.Security.Transport;

/// <summary>MailFathom's own name for whoever a validated credential turned out to be.</summary>
/// <remarks>
/// <para>
/// Every scheme sets its identity's name claim to the thing this deployment configured — an API key's name, a client
/// public key's name, or the subject the access policy checked against the authorization servers an operator wrote
/// down — so one reading covers all three and none of them discloses credential material.
/// </para>
/// <para>
/// It is read in two places that must not drift: what the session route reports back to a caller, and what the
/// application layer is told the work is running for. A second copy of the rule would let a deployment name a caller
/// one way in its own answers and another in its record of a refusal.
/// </para>
/// </remarks>
internal static class TransportCallerIdentity
{
    /// <summary>Names the caller a validated credential produced.</summary>
    /// <param name="caller">The principal an authentication scheme produced.</param>
    /// <returns>The configured name, or <see langword="null" /> when nothing authenticated.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="caller" /> is <see langword="null" />.</exception>
    /// <remarks>The API key claim is read ahead of the name claim rather than instead of it, so a scheme that stops naming its identity is still reported by the claim it writes.</remarks>
    internal static string? NameOf(ClaimsPrincipal caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        return caller.FindFirstValue(ApiKeyAuthentication.ApiKeyNameClaimType) ?? caller.Identity.Name;
    }
}
