// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Security.Mcp;

/// <summary>The client application a request's connection certificate identified.</summary>
/// <remarks>
/// <para>
/// Carried as a request feature rather than as a claim, because the certificate is judged before authentication runs and
/// <c>UseAuthentication</c> replaces the principal with the one its ticket carries — a claim set beforehand would be
/// gone by the time anything downstream looked for it. A feature is the seam the pipeline already has for a fact one
/// middleware established and a later one needs.
/// </para>
/// <para>
/// It is present only when a profile actually matched a presented certificate. A request served because every profile
/// was content without one carries no feature, because no client application was identified — the two are different
/// answers and only the first names anybody.
/// </para>
/// </remarks>
internal sealed class McpClientCertificateIdentity
{
    /// <summary>Records the profile whose client the connection certificate identified.</summary>
    /// <param name="profileName">The matching profile's configured name.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="profileName" /> names no profile.</exception>
    internal McpClientCertificateIdentity(string profileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileName);

        this.ProfileName = profileName;
    }

    /// <summary>Gets the configured name of the profile the certificate matched.</summary>
    /// <remarks>The operator's own name for a client application, never anything the certificate itself carried.</remarks>
    internal string ProfileName { get; }
}
