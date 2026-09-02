// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Contacts;

/// <summary>The text a caller is looking somebody up by, in the form the book compares its own values in.</summary>
/// <remarks>
/// <para>
/// One value matched against two things — a name and an address — so it is derived here rather than per reader. The
/// comparison form is the upper-cased text, which is exactly what <see cref="ContactDisplayName.SortKey" /> and an
/// address's normalized form already are, so a search means the same thing in memory, in a query, and in a database
/// whose collation MailFathom does not control.
/// </para>
/// <para>
/// It is bounded and refuses the characters a name refuses, for the reason those values do: the text reaches a database
/// predicate, and a caller decides how long it is and what it carries. Nothing here is a pattern — every character
/// matches itself, wildcards included — so a caller cannot widen a search into a scan of the whole book by writing one.
/// </para>
/// <para>
/// The text is somebody's name or address and is therefore personal data. It is never logged and never written into a
/// failure message; what a refusal names is the rule the text broke.
/// </para>
/// </remarks>
public readonly record struct ContactSearch
{
    /// <summary>The greatest length a search text may carry.</summary>
    /// <remarks>
    /// The longer of the two values it is matched against, which is an address at
    /// <see cref="Contact.MaximumAddressLength" /> characters: text beyond it can match nothing the book holds, so it is
    /// refused rather than run.
    /// </remarks>
    public const int MaximumLength = Contact.MaximumAddressLength;

    private ContactSearch(string text, string comparisonForm)
    {
        this.Text = text;
        this.ComparisonForm = comparisonForm;
    }

    /// <summary>Gets the text as the caller wrote it, trimmed.</summary>
    public string Text { get; }

    /// <summary>Gets the form a name's sort key and an address's normalized form are matched against.</summary>
    public string ComparisonForm { get; }

    /// <summary>Creates a search from text a caller supplied.</summary>
    /// <param name="value">The text to look somebody up by.</param>
    /// <returns>The validated search.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank, longer than <see cref="MaximumLength" />, or carries a character that renders as nothing.</exception>
    /// <remarks>
    /// Blank text is refused rather than read as the whole book, because a caller that wants the whole book asks for it
    /// by naming no search at all: reading a blank one as "everything" would turn a mistyped lookup into a walk of every
    /// person this deployment holds.
    /// </remarks>
    public static ContactSearch Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();

        if (trimmed.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"A contact search cannot be longer than {MaximumLength} characters.",
                nameof(value));
        }

        if (!ContactText.IsWellFormed(trimmed) || trimmed.EnumerateRunes().Any(ContactText.IsUnprintable))
        {
            throw new ArgumentException(
                "A contact search cannot contain characters that carry no glyph of their own.",
                nameof(value));
        }

        return new ContactSearch(trimmed, trimmed.ToUpperInvariant());
    }

    /// <inheritdoc />
    public override string ToString() => this.Text;
}
