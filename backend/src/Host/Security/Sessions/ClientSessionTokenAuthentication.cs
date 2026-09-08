// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security.ApiKeys;

namespace MailFathom.Host.Security.Sessions;

/// <summary>The names the session-token scheme publishes.</summary>
/// <remarks>
/// There is no challenge of its own here, and that is the method rather than an omission: a session token is set
/// deliberately by a client that already holds one, so what a refusal owes it is the bare bearer challenge every method
/// on the surface produces. What a person needs to be told to sign in — the password challenge — belongs to the method
/// that reads a password, and the surface answers a request carrying nothing under that scheme rather than this one.
/// </remarks>
internal static class ClientSessionTokenAuthentication
{
    /// <summary>The claim type carrying the identifier of the credential the exchange behind this session authenticated.</summary>
    /// <remarks>
    /// The credential rather than the session, for the reason <see cref="Basic.BasicAuthentication.CredentialIdClaimType" />
    /// names the credential rather than the username: it is MailFathom's own handle for the row, it is what an audit
    /// record already correlates on, and it means nothing outside this deployment. Naming the session instead would put
    /// half of a live credential into every diagnostic that renders a principal.
    /// </remarks>
    internal const string CredentialIdClaimType = "urn:mailfathom:client-session-credential-id";

    /// <summary>The claim type a role check reads on a session identity, which nothing ever issues.</summary>
    /// <remarks>Named rather than left empty for the reason <see cref="ApiKeyAuthentication.RoleClaimType" /> is: an identity given an empty role type silently reverts to the framework's default.</remarks>
    internal const string RoleClaimType = "urn:mailfathom:client-session-role";
}
