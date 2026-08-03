// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>One representation of a message body, together with what was left out of it.</summary>
/// <param name="Text">The representation as it is returned, already bounded.</param>
/// <param name="OriginalCharacterCount">How many characters the source of this representation held before the bound was applied.</param>
/// <param name="Truncation">Which bound removed something, or that none did.</param>
/// <remarks>
/// <para>
/// Truncation is part of the value rather than a flag beside it, because a body and the fact that it is incomplete are
/// never useful apart: a caller handed only the text would have to guess whether it read a whole message, which is
/// exactly what the specification forbids. Both representations a reader can receive — the plain text and the sanitized
/// HTML — carry their own copy, since a message can exceed the bound in one and not in the other.
/// </para>
/// <para>
/// <paramref name="Truncation" /> names the bound rather than merely reporting that there was one, because a read of
/// several emails is subject to two of them and the answer to "ask for less at once" differs from the answer to "this
/// message is longer than any single call returns".
/// </para>
/// <para>
/// It is stated rather than derived from the two lengths, because the HTML representation is bounded on its source
/// markup and then re-serialized: the returned text can be shorter than the source it was cut from, or slightly longer
/// once the parser closes what the cut left open, and neither difference is truncation.
/// </para>
/// </remarks>
public sealed record EmailBodyRepresentation(string Text, int OriginalCharacterCount, EmailBodyTruncation Truncation)
{
    /// <summary>Gets the representation of a body that displayed nothing.</summary>
    public static EmailBodyRepresentation Empty { get; } = new(
        string.Empty,
        OriginalCharacterCount: 0,
        EmailBodyTruncation.None);

    /// <summary>Gets whether any bound removed something from this representation.</summary>
    public bool WasTruncated => this.Truncation is not EmailBodyTruncation.None;

    /// <summary>Bounds one text to the characters a reader may be handed.</summary>
    /// <param name="text">The text as the message wrote it.</param>
    /// <param name="allowance">How many characters the representation may hold, and which bound to name when it cuts.</param>
    /// <returns>The bounded representation, which records the original length whether or not it had to cut.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the allowance is negative.</exception>
    /// <remarks>
    /// The cut falls on a text-element boundary, so a body ending in an emoji or a combining sequence cannot be handed
    /// over as a lone surrogate that a JSON writer would replace and PostgreSQL would reject.
    /// </remarks>
    public static EmailBodyRepresentation Bounded(string text, EmailBodyCharacterAllowance allowance)
    {
        ArgumentNullException.ThrowIfNull(text);

        var boundedText = MailTextBounds.TruncateAtTextElementBoundary(text, allowance.MaxCharacters);

        return new EmailBodyRepresentation(
            boundedText,
            text.Length,
            boundedText.Length < text.Length ? allowance.TruncationWhenCut : EmailBodyTruncation.None);
    }
}
