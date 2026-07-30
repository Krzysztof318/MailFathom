// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.EmailContent;

/// <summary>Names what is wrong with the locally stored content of one email.</summary>
/// <remarks>
/// The four values stay distinct because they say different things to whoever repairs them: three of them mean the
/// stored bytes have to be fetched again, while <see cref="Unreadable" /> means bytes that arrived intact cannot be
/// parsed, which a second fetch of the same message may well reproduce. Collapsing them into one "broken" marker would
/// hide a message that will never become readable behind a queue of ones that will.
/// </remarks>
public enum EmailContentDefect
{
    /// <summary>The email is recorded as having its content stored, and no content is stored for it.</summary>
    Missing = 0,

    /// <summary>The stored payload is not as long as the length recorded beside it when it was written.</summary>
    ByteLengthMismatch = 1,

    /// <summary>The stored payload does not hash to the digest recorded beside it when it was written.</summary>
    HashMismatch = 2,

    /// <summary>The stored payload matches what was recorded for it and still yields no message a reader can render.</summary>
    Unreadable = 3,
}
