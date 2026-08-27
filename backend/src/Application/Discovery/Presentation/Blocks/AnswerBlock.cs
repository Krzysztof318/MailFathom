// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>One synthesized answer, in the words the run wrote it in.</summary>
/// <remarks>
/// The block for a question that has an answer rather than a shape — "did they ever agree to it", "what was the figure".
/// It is prose, so it is the block most able to say something the correspondence does not: the confidence beside it
/// says how much of it the sources settle, and the evidence beside that says whether they settle anything at all.
/// </remarks>
public sealed record AnswerBlock : PresentationBlock
{
    /// <summary>Initializes one synthesized answer.</summary>
    /// <param name="evidence">What the correspondence does for the answer.</param>
    /// <param name="text">The answer.</param>
    /// <param name="confidence">How far the answer is worth trusting beyond the sources it names.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text" /> is the unspecified default.</exception>
    public AnswerBlock(
        PresentationEvidence evidence,
        PresentationText text,
        PresentationConfidence confidence)
        : base(PresentationBlockType.Answer, evidence)
    {
        PresentationRequirement.Specified(text, nameof(text));

        this.Text = text;
        this.Confidence = confidence;
    }

    /// <summary>Gets the answer.</summary>
    public PresentationText Text { get; }

    /// <summary>Gets how far the answer is worth trusting beyond the sources it names.</summary>
    public PresentationConfidence Confidence { get; }
}
