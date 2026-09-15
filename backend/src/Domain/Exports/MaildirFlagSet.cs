// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Exports;

/// <summary>The four flags a Maildir file name has a letter for.</summary>
/// <remarks>
/// Maildir standardizes exactly these, which is why the keywords a message carries beside them are written in a
/// document of their own at the archive root rather than in any server's private flag form.
/// </remarks>
[Flags]
public enum MaildirFlagSet
{
    /// <summary>The message carries none of the four.</summary>
    None = 0,

    /// <summary>The message is read, which Maildir writes as <c>S</c>.</summary>
    Seen = 1,

    /// <summary>The message is starred, which Maildir writes as <c>F</c>.</summary>
    Flagged = 2,

    /// <summary>The message has been replied to, which Maildir writes as <c>R</c>.</summary>
    Answered = 4,

    /// <summary>The message is a draft, which Maildir writes as <c>D</c>.</summary>
    Draft = 8,
}
