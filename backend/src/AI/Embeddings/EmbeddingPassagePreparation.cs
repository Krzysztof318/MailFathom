// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;

namespace MailFathom.AI.Embeddings;

/// <summary>Turns a passage into what the model is actually shown.</summary>
/// <remarks>
/// This is the step that decides what a vector means, so it is derived from the profile's own
/// <see cref="EmbeddingInputPreparation" /> and from nothing else. A second rule able to cut a passage — a batch
/// setting, a caller's own truncation, a provider default — would put points from two different spaces under one
/// profile identifier, which is the failure
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// separates preparation from ceilings to prevent.
/// </remarks>
internal static class EmbeddingPassagePreparation
{
    /// <summary>Applies the declared preparation to one passage.</summary>
    /// <param name="passage">The passage as the caller supplied it.</param>
    /// <param name="preparation">What the profile records about how a passage is prepared.</param>
    /// <returns>The text to send.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The instruction is applied before the cut rather than after it, because a prefix appended to an
    /// already-full passage would push the model's own limit over and a prefix that survived the cut on short passages
    /// but not long ones would prepare two passages differently under one identity.
    /// </remarks>
    public static string Prepare(string passage, EmbeddingInputPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(passage);
        ArgumentNullException.ThrowIfNull(preparation);

        var prepared = preparation.PassageInstruction is { } instruction
            ? instruction + passage
            : passage;

        // The beginning is kept and the end is lost, in reading order, because a passage's opening is what the rest of
        // it elaborates. The direction is the profile's, not a caller's choice.
        return prepared.Length > preparation.InputCharacterLimit
            ? prepared[..preparation.InputCharacterLimit]
            : prepared;
    }
}
