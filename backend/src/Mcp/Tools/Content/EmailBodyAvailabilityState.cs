// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools.Summaries;

namespace MailFathom.Mcp.Tools.Content;

/// <summary>Reports whether a reader was given the message body, or why it could not be, as the protocol spells it.</summary>
/// <remarks>
/// The transport carries its own enumeration for the reason <see cref="ListedEmailContentAvailability" /> does: the
/// member names are the published wire values, so a rename inside the application would otherwise become a silent
/// change to the contract a client reads.
/// </remarks>
internal enum EmailBodyAvailabilityState
{
    /// <summary>The body was read from the stored message, and an empty one means the message displayed nothing.</summary>
    Readable = 0,

    /// <summary>The body arrived inside a cryptographic envelope, so MailFathom cannot read it.</summary>
    EncryptedNotReadableLocally = 1,

    /// <summary>The message exceeded the configured size limit, so its content was never stored locally.</summary>
    NotStoredExceededSizeLimit = 2,

    /// <summary>Local content storage was at its ceiling when the message arrived, so its content is not stored yet.</summary>
    NotStoredAwaitingStorageHeadroom = 3,
}
