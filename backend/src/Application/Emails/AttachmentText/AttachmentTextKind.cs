// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>Names where an attachment's words came from: the file itself, or a machine's account of a picture.</summary>
/// <remarks>
/// The distinction is not a detail of provenance, it is what decides where the words are indexed.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// rules that a description reaches the vector index and nothing else, so a
/// <see cref="ImageDescription" /> row is embedded and never written into the lexical index, while a
/// <see cref="Document" /> row joins both. Searching for a word by the letters it is written in should never return a
/// file in which nobody wrote it and a model guessed it.
/// </remarks>
public enum AttachmentTextKind
{
    /// <summary>The words are the file's own, read out of it by a parser.</summary>
    Document = 0,

    /// <summary>The words are a model's description of what a picture shows, which nobody wrote.</summary>
    ImageDescription = 1,
}
