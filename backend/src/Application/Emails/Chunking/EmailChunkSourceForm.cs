// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Chunking;

/// <summary>Names which of the two readings of a message body chunks are cut from.</summary>
/// <remarks>
/// Extraction keeps both: the text a body carried and the text that remained after quoted history and signatures were
/// removed. Which one chunking derives from is a boundary rule rather than an implementation detail, because it decides
/// what a retrieved passage will turn out to be — a paragraph somebody wrote, or the same paragraph inside a thread's
/// worth of repeated history.
/// </remarks>
public enum EmailChunkSourceForm
{
    /// <summary>The form quoted history and signatures were removed from, which is what a reader of the message means.</summary>
    TrimmedText = 0,

    /// <summary>The form as extracted, including the quoted history trimming removed.</summary>
    OriginalText = 1,
}
