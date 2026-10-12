// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>One of the two halves the published permission vocabulary is divided into.</summary>
/// <remarks>
/// <para>
/// A permission belongs to exactly one surface, and its name says which: the domain after <c>mailfathom.</c> is
/// <c>admin</c> for <see cref="Administration" /> and anything else for <see cref="Mail" />. The two halves are
/// disjoint: no permission covers an operation on both.
/// </para>
/// <para>
/// It is a plain enum because a member is nothing but a name the process reads. What a caller may do is carried by
/// <see cref="MailFathomPermission" />, and where a surface is served — which listener, which routes, which
/// authentication schemes — is the host's own question and is answered there.
/// </para>
/// </remarks>
public enum ProtectedSurface
{
    /// <summary>The surface serving one person: their mailbox, and the records this deployment keeps beside it for them — contacts, tasks, the calendar, notifications, and their own profile.</summary>
    /// <remarks>It keeps the name of what it was first built to serve; the MCP endpoint and the client endpoint both draw on it.</remarks>
    Mail = 0,

    /// <summary>The surface serving the deployment's own administration.</summary>
    Administration = 1,
}
