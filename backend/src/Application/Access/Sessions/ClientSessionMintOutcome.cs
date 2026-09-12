// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Sessions;

/// <summary>What writing a session row did, or why it wrote none.</summary>
/// <remarks>
/// The two refusals are separate because a client does two different things with them: one signs in again, and the
/// other is tried again in a moment. Collapsing them would tell somebody whose credential an operator disabled to wait
/// and retry forever.
/// </remarks>
public enum ClientSessionMintOutcome
{
    /// <summary>The deployment is holding the session.</summary>
    Minted = 0,

    /// <summary>The user or the credential the session would have named no longer admits one, because an operator erased, deleted, or disabled it.</summary>
    NoLongerAdmitted = 1,

    /// <summary>The deployment is already holding as many live sessions as it will hold.</summary>
    BoundReached = 2,
}
