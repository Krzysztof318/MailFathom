// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>What is done to a passage between reading it and sending it, and therefore what the model actually saw.</summary>
/// <remarks>
/// <para>
/// Every member here changes the point a passage lands on, which is why this is part of an embedding profile's identity
/// rather than a setting beside it: cutting a long passage at a different width, prefixing it with a different
/// instruction, or normalizing one vector and not another produces vectors that cannot be compared, and nothing about
/// the resulting distance says so.
/// </para>
/// <para>
/// This is not the input ceiling an instance configures to bound what it spends on one message. The two are easy to
/// confuse because both are a limit on text, and the distinction is what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// draws: preparation decides what a vector means, a ceiling decides how many vectors exist.
/// </para>
/// <para>
/// Deliberately not a record. Two preparations are never compared directly — an identity is compared through the
/// fingerprint computed over it, which is what the unique index on the profile table is over.
/// </para>
/// </remarks>
public sealed class EmbeddingInputPreparation
{
    /// <summary>The greatest number of characters an instruction or prefix template may hold.</summary>
    public const int MaximumPassageInstructionLength = 512;

    private EmbeddingInputPreparation(
        int inputCharacterLimit,
        string? passageInstruction,
        bool normalizesVector)
    {
        this.InputCharacterLimit = inputCharacterLimit;
        this.PassageInstruction = passageInstruction;
        this.NormalizesVector = normalizesVector;
    }

    /// <summary>Gets the width a passage is cut to before it is sent.</summary>
    /// <remarks>
    /// A passage longer than this keeps its beginning and loses its end, because a passage was cut in reading order and
    /// its opening is what the rest of it elaborates. The direction is fixed rather than configured: it is not a choice
    /// an operator makes, and a column offering both would be a second way to produce two incomparable spaces.
    /// </remarks>
    public int InputCharacterLimit { get; }

    /// <summary>Gets the instruction or prefix a model requires of a passage, or <see langword="null" /> where it requires none.</summary>
    /// <remarks>
    /// Null is the model asking for nothing rather than an unrecorded value, which is why a blank instruction is refused
    /// rather than stored: a prefix of spaces is a misconfigured declaration, and accepting it would register a second
    /// profile for a space identical to one already registered.
    /// </remarks>
    public string? PassageInstruction { get; }

    /// <summary>Gets whether the vector is normalized to unit length before it is stored.</summary>
    public bool NormalizesVector { get; }

    /// <summary>Counts the characters one passage occupies once this preparation has been applied to it.</summary>
    /// <param name="passage">The passage as a caller supplied it.</param>
    /// <returns>The length of the text a provider would be sent for it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passage" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// What a spend ceiling is counted in. It answers without preparing the passage, because the ceiling is consulted
    /// before a batch is assembled and building the text to measure it would allocate a copy of every passage for a
    /// number the lengths already give. The instruction is added before the cut for the reason the preparation applies
    /// it there, so the two agree by construction rather than by coincidence.
    /// </remarks>
    public int CountBilledCharacters(string passage)
    {
        ArgumentNullException.ThrowIfNull(passage);

        var preparedLength = passage.Length + (this.PassageInstruction?.Length ?? 0);

        return Math.Min(preparedLength, this.InputCharacterLimit);
    }

    /// <summary>Builds a preparation, refusing one that could not produce a passage to send.</summary>
    /// <param name="inputCharacterLimit">The width a passage is cut to before it is sent.</param>
    /// <param name="passageInstruction">The instruction a model requires of a passage, or <see langword="null" />.</param>
    /// <param name="normalizesVector">Whether the vector is normalized to unit length.</param>
    /// <returns>The input preparation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the character limit is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when the instruction is blank, or longer than <see cref="MaximumPassageInstructionLength" />.</exception>
    public static EmbeddingInputPreparation Create(
        int inputCharacterLimit,
        string? passageInstruction,
        bool normalizesVector)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputCharacterLimit);

        if (passageInstruction is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(passageInstruction);

            if (passageInstruction.Length > MaximumPassageInstructionLength)
            {
                throw new ArgumentException(
                    $"A passage instruction is at most {MaximumPassageInstructionLength} characters.",
                    nameof(passageInstruction));
            }
        }

        return new EmbeddingInputPreparation(inputCharacterLimit, passageInstruction, normalizesVector);
    }
}
