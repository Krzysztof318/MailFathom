// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
    /// <summary>Cuts extracted text into chunks, in reading order.</summary>
    /// <param name="text">The text extraction derived from one message's body.</param>
    /// <param name="rules">The boundaries to cut along.</param>
    /// <returns>The chunks in reading order, or an empty list when the message yielded no text to cut.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> or <paramref name="rules" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The same text and the same rules produce the same chunks and the same hashes on every call, on any machine, and
    /// in any order, which is what lets a caller decide by hash alone whether anything downstream has to be re-done.
    /// </remarks>
    IReadOnlyList<EmailTextChunk> DeriveChunks(ExtractedEmailText text, EmailChunkingRules rules);
}
