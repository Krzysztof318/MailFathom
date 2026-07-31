// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Mcp.Tools;

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

    /// <summary>The body arrived inside a cryptographic envelope, so MailMcp cannot read it.</summary>
    EncryptedNotReadableLocally = 1,

    /// <summary>The message exceeded the configured size limit, so its content was never stored locally.</summary>
    NotStoredExceededSizeLimit = 2,
}
