// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Session;

/// <summary>Something the client's interface can put in front of a person, named so that one place decides whether it is offered.</summary>
/// <remarks>
/// <para>
/// The three spaces the shell holds, rather than every gesture inside one. A capability is worth naming here when the
/// interface would otherwise offer a whole surface the deployment will refuse; what a space does with a narrower grant
/// once it has content is that space's own decision, taken against the same session.
/// </para>
/// <para>
/// Each is answered against the grant the deployment reports, and <see cref="SessionStanding" /> holds which published
/// permission each one asks for. That table is the client's end of a vocabulary
/// <c>docs/operations/permissions.md</c> owns — the client reads the mail an agent reads, so it draws on the same half
/// of the published set rather than on names of its own.
/// </para>
/// </remarks>
public enum ClientCapability
{
    /// <summary>Asking a question of the mailbox and being answered from it, which is what the Discover space is.</summary>
    Discover = 0,

    /// <summary>Reading correspondence, which is what the Mail space is.</summary>
    Mail = 1,

    /// <summary>Following a thread of work assembled from correspondence, which is what the Cases space is.</summary>
    Cases = 2,
}
