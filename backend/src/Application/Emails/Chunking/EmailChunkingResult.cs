// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Chunking;

/// <summary>The passages one message's text was cut into, and how much of that text the cut reached.</summary>
/// <param name="Chunks">The passages in reading order, empty for a message whose body yielded no text.</param>
/// <param name="TruncatedFromCharacterCount">
/// The length the text had when the per-message ceiling stopped the cut short of its end, or <see langword="null" />
/// when the whole text was cut.
/// </param>
/// <remarks>
/// The truncation travels with the chunks rather than being inferred from them, because a chunk count says nothing
/// about what is missing: a message cut whole and one cut to a ceiling look identical from the passages alone, and the
/// difference is exactly what a reader deciding whether an answer could have been in the part nobody embedded needs.
/// </remarks>
public sealed record EmailChunkingResult(
    IReadOnlyList<EmailTextChunk> Chunks,
    int? TruncatedFromCharacterCount)
{
    /// <summary>The result of cutting a message whose body yielded no text at all.</summary>
    public static EmailChunkingResult NoText { get; } = new([], TruncatedFromCharacterCount: null);
}
