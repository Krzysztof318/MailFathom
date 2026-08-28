// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Http.Headers;
using System.Text;

namespace MailFathom.Client.Backend.Authorization;

/// <summary>Composes the <c>Authorization: Basic</c> header a credential is presented in, and reads a challenge for it.</summary>
/// <remarks>
/// <para>
/// RFC 7617 carries both halves in one Base64 field separated by the first colon, encoded as UTF-8 — which is the one
/// value the specification's <c>charset</c> parameter permits and the one MailFathom's own challenge names. Encoding
/// any other way would make a password containing anything outside US-ASCII arrive as different characters than were
/// typed.
/// </para>
/// <para>
/// Composed per request rather than stored ready-made. The client needs the username on its own anyway, and a single
/// stored value that has to be split back apart is worse than two that are already named.
/// </para>
/// </remarks>
internal static class BasicCredentialHeader
{
    /// <summary>The HTTP authentication scheme this composes, and the one a challenge is looked for under.</summary>
    /// <remarks>
    /// This end of the same agreement <c>backend/src/Host/Security/Basic/</c> states at the other. Written here as a
    /// literal for the reason <see cref="DeploymentRoutes" /> gives about the paths beside it: two ends stating one
    /// contract rather than a constant shared across the two stacks.
    /// </remarks>
    internal const string HttpAuthenticationScheme = "Basic";

    /// <summary>Composes the header value one credential is presented as.</summary>
    /// <param name="credential">The credential to present.</param>
    /// <returns>The scheme and the encoded field.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="credential" /> is <see langword="null" />.</exception>
    internal static AuthenticationHeaderValue ComposedFrom(OwnerCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        var field = Encoding.UTF8.GetBytes($"{credential.Username}:{credential.Password}");

        return new AuthenticationHeaderValue(HttpAuthenticationScheme, Convert.ToBase64String(field));
    }

    /// <summary>Reports whether a refusal invited a password at all.</summary>
    /// <param name="refusal">The answer whose <c>WWW-Authenticate</c> header is read.</param>
    /// <returns><see langword="true" /> where the deployment named this scheme among the ones it accepts.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The one thing that separates a password the deployment did not accept from a deployment that accepts no
    /// password. RFC 7235 has a server list every scheme it offers in that header, and a MailFathom surface with the
    /// password method configured names <c>Basic</c> beside its bearer challenge on every refusal — so a refusal
    /// naming only <c>Bearer</c> is a deployment whose operator has not enabled password sign-in, which is something to
    /// tell somebody rather than to let them read as a wrong password.
    /// </remarks>
    internal static bool InvitesAPassword(HttpResponseMessage refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        return refusal.Headers.WwwAuthenticate.Any(challenge =>
            string.Equals(challenge.Scheme, HttpAuthenticationScheme, StringComparison.OrdinalIgnoreCase));
    }
}
