// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Domain.Emails;

namespace MailFathom.Application.EmailContent;

/// <summary>One representation of a message body, together with what was left out of it.</summary>
/// <param name="Text">The representation as it is returned, already bounded.</param>
/// <param name="OriginalCharacterCount">How many characters the source of this representation held before the bound was applied.</param>
/// <param name="WasTruncated">Whether the bound removed anything.</param>
/// <remarks>
/// <para>
/// Truncation is part of the value rather than a flag beside it, because a body and the fact that it is incomplete are
/// never useful apart: a caller handed only the text would have to guess whether it read a whole message, which is
/// exactly what the specification forbids. Both representations a reader can receive — the plain text and the sanitized
/// HTML — carry their own copy, since a message can exceed the bound in one and not in the other.
/// </para>
/// <para>
/// <paramref name="WasTruncated" /> is stated rather than derived from the two lengths, because the HTML representation
/// is bounded on its source markup and then re-serialized: the returned text can be shorter than the source it was cut
/// from, or slightly longer once the parser closes what the cut left open, and neither difference is truncation.
/// </para>
/// </remarks>
public sealed record EmailBodyRepresentation(string Text, int OriginalCharacterCount, bool WasTruncated)
{
    /// <summary>Gets the representation of a body that displayed nothing.</summary>
    public static EmailBodyRepresentation Empty { get; } = new(string.Empty, OriginalCharacterCount: 0, WasTruncated: false);

    /// <summary>Bounds one text to the characters a reader may be handed.</summary>
    /// <param name="text">The text as the message wrote it.</param>
    /// <param name="maxCharacters">The greatest number of characters the representation may hold.</param>
    /// <returns>The bounded representation, which records the original length whether or not it had to cut.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxCharacters" /> is negative.</exception>
    /// <remarks>
    /// The cut falls on a text-element boundary, so a body ending in an emoji or a combining sequence cannot be handed
    /// over as a lone surrogate that a JSON writer would replace and PostgreSQL would reject.
    /// </remarks>
    public static EmailBodyRepresentation Bounded(string text, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);

        var boundedText = MailTextBounds.TruncateAtTextElementBoundary(text, maxCharacters);

        return new EmailBodyRepresentation(boundedText, text.Length, boundedText.Length < text.Length);
    }
}
