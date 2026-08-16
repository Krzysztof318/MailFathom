// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>One of the two halves the published permission vocabulary is divided into.</summary>
/// <remarks>
/// <para>
/// A permission belongs to exactly one surface and says so in its own name, which is what lets a grant written on the
/// wrong surface be refused at startup instead of sitting there granting nothing. The two halves are disjoint: no
/// permission covers an operation on both.
/// </para>
/// <para>
/// It is a plain enum because a member is nothing but a name the process reads. What a caller may do is carried by
/// <see cref="MailFathomPermission" />, and where a surface is served — which listener, which routes, which
/// authentication schemes — is the host's own question and is answered there.
/// </para>
/// </remarks>
public enum ProtectedSurface
{
    /// <summary>The surface serving the mailbox: the MCP tools that read the local copy and the one that answers from it.</summary>
    Mail = 0,

    /// <summary>The surface serving the deployment's own administration.</summary>
    Administration = 1,
}
