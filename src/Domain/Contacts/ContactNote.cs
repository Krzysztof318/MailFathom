// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Contacts;

/// <summary>Holds what an owner wrote about a person beyond their name and their addresses.</summary>
/// <remarks>
/// <para>
/// A note is the freest field the book has, so it is also the one most likely to hold something an owner would not want
/// read back to them by an agent: where somebody works, what was agreed, why they are being avoided. It is therefore
/// personal data of the most sensitive kind this record carries and is treated as such everywhere — never logged, never
/// a metric dimension, never in a failure message, and erased with the contact it belongs to.
/// </para>
/// <para>
/// Line breaks are kept because a note is written to be read as somebody wrote it, and a note about a person genuinely
/// runs to more than one line. Every other character that carries no glyph of its own is refused, for the reason a name
/// refuses all of them; <see cref="ContactText" /> holds which ones those are.
/// </para>
/// </remarks>
public readonly record struct ContactNote
{
    /// <summary>The greatest length a note may carry.</summary>
    /// <remarks>
    /// Long enough for a paragraph somebody would actually write about a person, and bounded because the value is
    /// unstructured text an owner controls: without a limit the book would become a place to keep documents, with the
    /// retention and export obligations of one and none of the handling.
    /// </remarks>
    public const int MaximumLength = 4_000;

    private ContactNote(string value) => this.Value = value;

    /// <summary>Gets the note as the owner wrote it, trimmed of surrounding whitespace.</summary>
    public string Value { get; }

    /// <summary>Creates a note from text an owner supplied.</summary>
    /// <param name="value">The note to record.</param>
    /// <returns>A validated note, trimmed and otherwise as written.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank, longer than <see cref="MaximumLength" />, or carries a character that does not render as part of the note, other than a line break or a tab.</exception>
    /// <remarks>
    /// Blank text is refused rather than stored as an empty note, because a contact without a note holds no note at all:
    /// two ways to say the same absence would leave every reader deciding which one it was looking at.
    /// </remarks>
    public static ContactNote Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();

        if (trimmed.Length > MaximumLength)
        {
            throw new ArgumentException($"A contact note cannot be longer than {MaximumLength} characters.", nameof(value));
        }

        if (!ContactText.IsWellFormed(trimmed)
            || trimmed.EnumerateRunes().Any(scalar => ContactText.IsUnprintable(scalar) && !ContactText.IsLayout(scalar)))
        {
            throw new ArgumentException("A contact note cannot contain characters that carry no glyph of their own, other than line breaks and tabs.", nameof(value));
        }

        return new ContactNote(trimmed);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
