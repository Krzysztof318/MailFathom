// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Security.Claims;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.ApiKeys;

namespace MailFathom.Host.Security.Transport;

/// <summary>What a validated credential turned out to name.</summary>
/// <remarks>
/// <para>
/// Every scheme sets its identity's name claim to something this deployment authorized — a credential's own identifier,
/// or the issuer and subject a token was checked against — so one reading covers all of them and none discloses
/// credential material. On the administrative surface a caller is reported by <see cref="OfAdministrator" /> instead,
/// which names the user and the credential together: an administrative act is attributed to a person, and the
/// credential beside them is what an operator revokes when the act was not theirs. The token case carries a host name and that server's own
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
    /// <remarks>The one caller this deployment cannot tell apart from any other, so the word says exactly that rather than borrowing a name no entry carries.</remarks>
    internal const string AnonymousCaller = "anonymous";

    /// <summary>Names the caller a validated credential produced.</summary>
    /// <param name="caller">The principal an authentication scheme produced.</param>
    /// <returns>The name the scheme gave the caller, or <see langword="null" /> when nothing authenticated.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="caller" /> is <see langword="null" />.</exception>
    /// <remarks>The API key claim is read ahead of the name claim rather than instead of it, so a scheme that stops naming its identity by it is still reported by the claim it writes.</remarks>
    internal static string? NameOf(ClaimsPrincipal caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        return caller.FindFirstValue(ApiKeyAuthentication.ApiKeyNameClaimType)
            ?? caller.Identity.Name;
    }

    /// <summary>Names a caller on the administrative surface by the user acting and the credential they acted through.</summary>
    /// <param name="user">The administrator.</param>
    /// <param name="credential">The credential that admitted them, or <see langword="null" /> where the surface authenticates nobody and serves its callers as the default administrator.</param>
    /// <returns>The name every administrative act, log scope, and audit record carries.</returns>
    /// <remarks>Identifiers alone, never a username or a subject, so the name is safe in any record and still leads an operator to both rows.</remarks>
    internal static string OfAdministrator(UserId user, Guid? credential) => credential is { } presented
        ? string.Create(CultureInfo.InvariantCulture, $"user {user} credential {presented:D}")
        : string.Create(CultureInfo.InvariantCulture, $"user {user} credential none");
}
