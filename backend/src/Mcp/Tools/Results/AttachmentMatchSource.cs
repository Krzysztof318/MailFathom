// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Reports who wrote the words an attachment contributed to a result, as the protocol spells it.</summary>
/// <remarks>
/// <para>
/// The distinction is what a caller weighs the extract by, not a detail of provenance. A document's words are the
/// sender's own and are worth what the file is worth; a description is a sentence a model composed out of an image, and
/// nobody wrote it — so a claim resting on one is a claim resting on a guess about a picture.
/// </para>
/// <para>
/// The transport carries its own enumeration for the reason <see cref="EmailRetrievalMode" /> does: the member names are
/// the published wire values, so they belong to the boundary that publishes them.
/// </para>
/// </remarks>
internal enum AttachmentMatchSource
{
    /// <summary>The words are the file's own, read out of it by a parser.</summary>
    /// <remarks>Searchable by the words themselves as well as by meaning, exactly as a message body is.</remarks>
    Document = 0,

    /// <summary>The words are a model's description of what a picture shows, which nobody wrote.</summary>
    /// <remarks>Reachable by meaning alone, and never by matching a word: a search for a word never returns a picture because a model guessed that word into it.</remarks>
    ImageDescription = 1,
}
