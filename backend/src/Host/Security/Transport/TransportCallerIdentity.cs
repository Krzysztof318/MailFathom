// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Host.Security.ApiKeys;

namespace MailFathom.Host.Security.Transport;

/// <summary>What a validated credential turned out to name.</summary>
/// <remarks>
/// <para>
/// On the administrative surface every credential names the administrator it was written under, and that name is what
/// a caller is reported by: an administrative act is attributed to a person or a system rather than to whichever key
/// they happened to present. Elsewhere every scheme sets its identity's name claim to something this deployment
/// authorized — a credential's own identifier, or the issuer and subject a token was checked against — so one reading
/// covers all of them and none discloses credential material. The token case carries a host name and that server's own
/// identifier for a person, which is why a caller reported by this is never named in a failure message.
/// </para>
/// <para>
/// It is read in two places that must not drift: what the session route reports back to a caller, and what the
/// application layer is told the work is running for. A second copy of the rule would let a deployment name a caller
/// one way in its own answers and another in its record of a refusal.
/// </para>
/// </remarks>
internal static class TransportCallerIdentity
{
    /// <summary>What a caller is named where nothing authenticated, because the surface it reached configures no credential.</summary>
    /// <remarks>The one caller this deployment cannot tell apart from any other, so the word says exactly that rather than borrowing a name no entry carries. No administrator may be configured under it, for the same reason.</remarks>
    internal const string AnonymousCaller = "anonymous";

    /// <summary>The claim type carrying the name of the administrator a credential on the administrative surface admitted.</summary>
    /// <remarks>A private claim type rather than a registered one: the value is a name this deployment's configuration gave a person or a system, and it means nothing outside it.</remarks>
    internal const string AdministratorClaimType = "urn:mailfathom:administrator";

    /// <summary>Names the caller a validated credential produced.</summary>
    /// <param name="caller">The principal an authentication scheme produced.</param>
    /// <returns>The configured name, or <see langword="null" /> when nothing authenticated.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="caller" /> is <see langword="null" />.</exception>
    /// <remarks>The administrator claim and then the API key claim are read ahead of the name claim rather than instead of it, so a scheme that stops naming its identity by either is still reported by the claim it writes.</remarks>
    internal static string? NameOf(ClaimsPrincipal caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        return caller.FindFirstValue(AdministratorClaimType)
            ?? caller.FindFirstValue(ApiKeyAuthentication.ApiKeyNameClaimType)
            ?? caller.Identity.Name;
    }
}
