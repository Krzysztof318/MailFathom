// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>Which of the two mail-serving endpoints one user may be served on.</summary>
/// <param name="McpEndpoint">Whether a credential of this user is admitted on the MCP endpoint.</param>
/// <param name="ClientEndpoint">Whether a credential or a session of this user is admitted on the client endpoint.</param>
/// <remarks>
/// <para>
/// The switches are the user's rather than a credential's, which is the whole of the decision: a person kept off a
/// surface is refused there whichever credential they present, so no credential can be provisioned around it. What a
/// credential admitted on a surface may then do is still its grant, and neither switch widens that.
/// </para>
/// <para>
/// A user is recorded reaching both, which is <see cref="Everywhere" />. The struct default reaches neither, so a value
/// something forgot to read refuses rather than admits.
/// </para>
/// </remarks>
public readonly record struct UserEndpointAccess(bool McpEndpoint, bool ClientEndpoint)
{
    /// <summary>Gets the access a user is recorded with: both endpoints.</summary>
    public static UserEndpointAccess Everywhere { get; } = new(McpEndpoint: true, ClientEndpoint: true);
}
