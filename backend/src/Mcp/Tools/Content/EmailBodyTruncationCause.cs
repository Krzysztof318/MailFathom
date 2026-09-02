// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Content;

/// <summary>The published values naming which bound cut a body representation short.</summary>
/// <remarks>
/// The member names are the wire values, camel-cased by the one serialization policy every tool registration is given.
/// The type is this boundary's own rather than the application enumeration describing the same states, so a rename
/// inside the application is not a silent change to the protocol.
/// </remarks>
internal enum EmailBodyTruncationCause
{
    /// <summary>Nothing was removed: the text is the whole of what the email displayed in this representation.</summary>
    None = 0,

    /// <summary>The per-body character bound cut it, so this email alone is longer than any single call returns.</summary>
    BodyCharacterLimit = 1,

    /// <summary>The call's total character budget cut it, because the emails named before it had already spent the budget.</summary>
    ReadCharacterBudget = 2,

    /// <summary>The sensitive-content scan's analyzed ceiling cut it, so the remainder is withheld from every call rather than from this one.</summary>
    SensitiveContentScanCeiling = 3,
}
