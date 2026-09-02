// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Authorship;

/// <summary>Reports how much an email's own text read as machine written, as the protocol spells it.</summary>
/// <remarks>
/// The transport carries its own enumeration for the reason every other published one does: the member names are the
/// wire values, so they belong to the boundary that publishes them rather than to the domain that happens to describe
/// the same states today.
/// </remarks>
internal enum MachineAuthorshipState
{
    /// <summary>Nothing read this email's text, so nothing is claimed either way.</summary>
    NotAssessed = 0,

    /// <summary>The text was read and carries little or nothing of what machine-written text carries.</summary>
    Unlikely = 1,

    /// <summary>The text carries some of it, in a combination a person writing by hand also reaches.</summary>
    Possible = 2,

    /// <summary>The text carries enough of it that a person typing it is the less likely reading.</summary>
    Likely = 3,
}
