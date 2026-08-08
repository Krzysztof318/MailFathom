// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Limits;

/// <summary>How much of one message's extracted text is cut into passages, and therefore what one message may cost.</summary>
/// <remarks>
/// <para>
/// Per-item cost is not uniform: raw MIME is bounded at tens of megabytes, so a single message can carry more text than
/// an ordinary mailbox does in a month, and every character of it would otherwise become a passage and a paid vector.
/// This is the ceiling that makes one message's cost bounded rather than proportional to whatever a sender attached.
/// </para>
/// <para>
/// It bounds rather than refuses. A message beyond the ceiling is still cut, still embedded, and still retrievable on
/// its opening — which is the part the rest of a message elaborates — and the length its text had is recorded on the
/// message so that what was left out is a stored fact rather than something inferred later from a chunk count.
/// </para>
/// <para>
/// It is not the input preparation an embedding profile records, although both are limits on text.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// draws the line: preparation decides what one passage means and belongs to the profile's identity, while this decides
/// how many passages exist and changes the meaning of none of them. Raising it therefore cuts new passages for the
/// messages it reaches and leaves every stored vector exactly as comparable as it was.
/// </para>
/// </remarks>
public sealed class EmbeddingInputBound
{
    /// <summary>The characters of one message that are cut into passages where a deployment declares nothing.</summary>
    /// <remarks>
    /// Two hundred thousand characters is a long report with its quoted history, and far beyond what ordinary
    /// correspondence reaches; what it excludes is the log dump, the exported table, and the machine-generated
    /// attachment transcript, none of which a person asks a question about.
    /// </remarks>
    public const int DefaultMaximumCharacterCount = 200_000;

    private EmbeddingInputBound(int maximumCharacterCount) =>
        this.MaximumCharacterCount = maximumCharacterCount;

    /// <summary>Gets the bound a deployment that declared none is cut to.</summary>
    public static EmbeddingInputBound Default { get; } = new(DefaultMaximumCharacterCount);

    /// <summary>Gets the characters of one message's text that are cut into passages.</summary>
    public int MaximumCharacterCount { get; }

    /// <summary>Builds a bound from what a deployment declared.</summary>
    /// <param name="maximumCharacterCount">The characters of one message's text to cut into passages.</param>
    /// <returns>The bound.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is not positive.</exception>
    public static EmbeddingInputBound Create(int maximumCharacterCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacterCount);

        return new EmbeddingInputBound(maximumCharacterCount);
    }
}
