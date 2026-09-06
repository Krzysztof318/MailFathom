// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction;

namespace MailFathom.Application.Emails.Chunking;

/// <summary>Cuts one message's extracted text into the passages retrieval works on.</summary>
/// <remarks>
/// <para>
/// The operation is a pure derivation and the contract says so: it is synchronous and takes no cancellation token,
/// because it reaches no provider, opens no connection, and reads nothing but the text it is handed. That is what lets
/// re-cutting a mailbox be a local cost rather than a provider bill, and what lets every rule below be proven by a test
/// with no substitute in it.
/// </para>
/// <para>
/// The port exists because chunking belongs to the AI boundary while persistence writes the chunks in the same session
/// as the message they derive from, and persistence may not reference that boundary. It is also the seam a rule set
/// that measures tokens rather than characters would arrive through.
/// </para>
/// </remarks>
public interface IEmailTextChunker
{
    /// <summary>Cuts extracted text into chunks, in reading order, stopping at what one message may cost.</summary>
    /// <param name="text">The text extraction derived from one message's body.</param>
    /// <param name="rules">The boundaries to cut along.</param>
    /// <param name="bound">How much of the text to cut, beyond which the message's remainder yields no passage.</param>
    /// <returns>The chunks in reading order and what the ceiling left out, or an empty result when the message yielded no text to cut.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The same text, the same rules, and the same bound produce the same chunks and the same hashes on every call, on
    /// any machine, and in any order, which is what lets a caller decide by hash alone whether anything downstream has
    /// to be re-done. The bound is a parameter rather than a member of the rules deliberately: the rules are covered by
    /// each chunk's content hash because they decide what a passage says, while the bound decides only how many
    /// passages there are and must not make an unchanged passage look like a different one.
    /// </remarks>
    EmailChunkingResult DeriveChunks(
        ExtractedEmailText text,
        EmailChunkingRules rules,
        EmbeddingInputBound bound);

    /// <summary>Cuts the text one attachment yielded into chunks, in reading order, stopping at what one attachment may cost.</summary>
    /// <param name="text">The text an extraction or a description derived from one attachment.</param>
    /// <param name="rules">The boundaries to cut along, which are the message body's boundaries and not a second set.</param>
    /// <param name="bound">How much of the text to cut, beyond which the attachment's remainder yields no passage.</param>
    /// <param name="source">The attachment the passages are spans of.</param>
    /// <returns>The chunks in reading order and what the ceiling left out, or an empty result when the attachment yielded no text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The same walk, the same rules, and the same determinism as the body cut, which is the whole of what
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0029-what-an-embedding-is-derived-from-and-whether-attachment-text-joins-it.md">ADR 0029</see>
    /// means by one kind of passage: a passage out of a contract and a passage out of the note that carried it are
    /// comparable because they were cut the same way and are embedded into the same space. What differs is the identity
    /// each passage is stored under, which <paramref name="source" /> supplies, and the text being cut — the ordinals
    /// and offsets returned here index this attachment's text and nothing else.
    /// </remarks>
    EmailChunkingResult DeriveAttachmentChunks(
        string text,
        EmailChunkingRules rules,
        EmbeddingInputBound bound,
        EmailChunkAttachmentSource source);
}
