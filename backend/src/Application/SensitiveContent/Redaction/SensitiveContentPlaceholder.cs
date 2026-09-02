// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Application.SensitiveContent.Redaction;

/// <summary>The text that replaces a redacted region, which is the same text in every consumer.</summary>
/// <remarks>
/// <para>
/// One scheme rather than one per consumer, because two consumers disagreeing about how the same message reads is the
/// failure this feature cannot have: a citation drawn from a redacted chunk has to land on the same redacted text when
/// the reader opens the message, or the citation reads as wrong.
/// </para>
/// <para>
/// The placeholder names the category and carries no part of what was found — not a prefix, not a length, not a masked
/// remainder. A length alone narrows a credential's search space, and a preserved prefix names the service the
/// credential belongs to, so neither is worth the readability it would buy.
/// </para>
/// <para>
/// What it does keep is coherence. A reader and a model both meet a marker that says a credential of a named kind stood
/// here, which is what stops a redacted message from reading as a message with a hole in it.
/// </para>
/// </remarks>
public static class SensitiveContentPlaceholder
{
    /// <summary>The opening delimiter, chosen so a placeholder is visibly not prose and is stable under chunking.</summary>
    private const string Opening = "[redacted:";

    /// <summary>The closing delimiter.</summary>
    private const string Closing = "]";

    /// <summary>Produces the placeholder that replaces a region of one category.</summary>
    /// <param name="category">The kind of material that stood there.</param>
    /// <returns>The replacement text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="category" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The category's own grammar admits no bracket, no whitespace, and no newline, so the result cannot be made to
    /// close early or to span a line by anything a rule corpus declares.
    /// </remarks>
    public static string For(SensitiveContentCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return string.Concat(Opening, category.Name, Closing);
    }

    /// <summary>Produces the placeholder that replaces the region a finding covers.</summary>
    /// <param name="finding">The finding being redacted.</param>
    /// <returns>The replacement text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="finding" /> is <see langword="null" />.</exception>
    public static string For(SensitiveContentFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return For(finding.Category);
    }
}
