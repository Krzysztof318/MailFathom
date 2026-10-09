// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Access.Grants;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Security.Transport;

/// <summary>Decides whether a credential one method resolved is admitted on the surface that judged it.</summary>
/// <remarks>
/// <para>
/// Every method that resolves a user — a password, an API key, a client assertion, an OAuth subject — asks this after
/// the credential matched and before an identity is built, so the answer is the one a credential nobody holds receives:
/// a <c>401</c> rather than a <c>403</c> telling a caller that what they presented is good somewhere.
/// </para>
/// <para>
/// Three things are asked, in the order a refusal is cheapest. The credential lists the surface and the user is served
/// there, which <see cref="TransportSurface.Admits" /> reads. The request arrived from a network the credential is
/// restricted to, read from the peer address the forwarded-headers policy left, which is the client's behind a proxy
/// this deployment named. And on the administrative surface, the user's grant narrowed by the credential still holds at
/// least one administrative permission at some scope, which is what makes a user an administrator at all: the
/// credential is the way in, and the assignment is what there is to come in for. A credential narrowed to mail names
/// alone, or to the empty list, is therefore refused there however much its user holds.
/// </para>
/// </remarks>
internal static class UserCredentialAdmission
{
    /// <summary>Reports why a resolved credential is refused on a surface, or nothing where it is admitted.</summary>
    /// <param name="context">The request the credential arrived with.</param>
    /// <param name="surface">The surface that judged it.</param>
    /// <param name="admitted">What the credential resolved to.</param>
    /// <returns>The reason the framework records beside the refusal, or <see langword="null" /> where the credential is admitted.</returns>
    internal static async Task<string?> FindRefusalAsync(
        HttpContext context,
        TransportSurface surface,
        AdmittedUserCredential admitted)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(admitted);

        if (!surface.Admits(admitted))
        {
            return "The credential is not presented on this endpoint, or its user is kept off it.";
        }

        if (!admitted.Reach.AdmitsSource(context.Connection.RemoteIpAddress))
        {
            return "The credential is not accepted from the network this request arrived from.";
        }

        if (surface != TransportSurface.Admin)
        {
            return null;
        }

        var held = await context.RequestServices
            .GetRequiredService<UserGrantResolver>()
            .ResolveAsync(admitted.User, context.RequestAborted);

        return held
            .NarrowedTo(MailFathomPermission.PublishedFor(ProtectedSurface.Administration))
            .NarrowedTo(admitted.Permissions)
            .Permissions.Count == 0
            ? "The credential's narrowed grant holds no administrative permission."
            : null;
    }
}
