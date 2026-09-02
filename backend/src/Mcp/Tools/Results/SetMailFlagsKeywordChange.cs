// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Selects what a flag change does with the keywords it names, as the protocol spells it.</summary>
/// <remarks>
/// The transport carries its own enumeration for the reason <see cref="ListEmailsDirection" /> does: the member names
/// are the wire values — they are serialized camel-cased — so a rename inside the application would otherwise be a
/// silent change to the published tool contract.
/// </remarks>
internal enum SetMailFlagsKeywordChange
{
    /// <summary>Puts the listed keywords on beside whatever the email already carries.</summary>
    Add = 0,

    /// <summary>Takes the listed keywords off and leaves the rest.</summary>
    Remove = 1,

    /// <summary>Makes the email's keywords exactly the listed ones.</summary>
    Replace = 2,
}
