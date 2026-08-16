// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Contacts;

/// <summary>Names the person a contact is about, the way the owner who wrote them down recognizes them.</summary>
/// <remarks>
/// <para>
/// The owner's casing is kept, because the value exists to be read and <c>Anna Kowalska</c> is not
/// <c>ANNA KOWALSKA</c> to the person who wrote it. What a listing is ordered by is <see cref="SortKey" /> instead, a
/// comparison form derived here so the order a page is walked in is decided by one rule rather than by the collation of
/// a database MailFathom does not control.
/// </para>
/// <para>
/// This is personal data. Nothing logs it, records it as a metric dimension, or writes it into a failure message;
/// <see cref="ContactId" /> is what a failure names.
/// </para>
/// </remarks>
public readonly record struct ContactDisplayName
{
    /// <summary>The greatest length a contact's name may carry.</summary>
    /// <remarks>
    /// Generous against any name written to be read at a glance, and short enough that the field cannot become a way to
    /// keep a paragraph about somebody in a column meant to identify them. Notes are what
    /// <see cref="ContactNote" /> is for, and they are bounded separately.
    /// </remarks>
    public const int MaximumLength = 256;

    private ContactDisplayName(string value, string sortKey)
    {
        this.Value = value;
        this.SortKey = sortKey;
    }

    /// <summary>Gets the name as the owner wrote it, trimmed.</summary>
    public string Value { get; }

    /// <summary>Gets the comparison form a listing is ordered and paginated by.</summary>
    /// <remarks>
    /// Upper-cased for the reason a folder alias is: upper case round-trips in every culture, so the key means the same
    /// thing in memory, in a query, and in a database whose collation MailFathom does not control. It is never displayed;
    /// <see cref="Value" /> is what a reader is shown.
    /// </remarks>
    public string SortKey { get; }

    /// <summary>Creates a contact name from text an owner supplied.</summary>
    /// <param name="value">The name to record.</param>
    /// <returns>A validated name, trimmed and otherwise as written.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank, longer than <see cref="MaximumLength" />, or carries a character that does not render as part of the name.</exception>
    /// <remarks>
    /// Characters carrying no glyph of their own are refused because the value is published in every answer that names the
    /// contact: a newline in it would let one record write arbitrary lines into a listing of the others, and a
    /// bidirectional override would let one render as a name it does not contain. <see cref="ContactText" /> holds which
    /// characters those are and why two of them are admitted.
    /// </remarks>
    public static ContactDisplayName Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();

        if (trimmed.Length > MaximumLength)
        {
            throw new ArgumentException($"A contact name cannot be longer than {MaximumLength} characters.", nameof(value));
        }

        if (trimmed.EnumerateRunes().Any(ContactText.IsUnprintable))
        {
            throw new ArgumentException("A contact name cannot contain characters that carry no glyph of their own.", nameof(value));
        }

        return new ContactDisplayName(trimmed, trimmed.ToUpperInvariant());
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
