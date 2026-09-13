// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Domain.Folders;

/// <summary>The name of one level of a local folder hierarchy, as a person typed it or a source folder supplied it.</summary>
/// <remarks>
/// <para>
/// A name is one level and never a path: the local hierarchy delimiter is fixed, so a name carrying it would be two
/// levels that only look like one. No source path is ever derived from a name either — a local folder reaches a server
/// only through a mapping an operator wrote — so a name carrying some server's own delimiter is harmless here.
/// </para>
/// <para>
/// Names compare without regard to case through <see cref="ComparisonKey" />, which is the form the database's
/// uniqueness among siblings is written over, so the rule and the constraint cannot disagree.
/// </para>
/// </remarks>
public readonly record struct LocalMailFolderName
{
    /// <summary>The longest name accepted, in UTF-16 code units.</summary>
    public const int MaximumLength = 255;

    /// <summary>The delimiter between levels of the local hierarchy, which no name may contain.</summary>
    public const char HierarchyDelimiter = '/';

    private const string InboxName = "INBOX";

    private LocalMailFolderName(string value) => this.Value = value;

    /// <summary>Gets the name, trimmed.</summary>
    public string Value { get; }

    /// <summary>Gets the form two names are compared in.</summary>
    public string ComparisonKey => this.Value.ToUpperInvariant();

    /// <summary>Gets whether this is the name only the protected inbox may carry at the top of a hierarchy.</summary>
    public bool IsReservedAtTopLevel => string.Equals(this.ComparisonKey, InboxName, StringComparison.Ordinal);

    /// <summary>Gets the name the inbox carries.</summary>
    public static LocalMailFolderName Inbox { get; } = new(InboxName);

    /// <summary>Reads a name, refusing one that is empty, too long, or carries a control character, a format character, or the hierarchy delimiter.</summary>
    /// <remarks>
    /// Format characters — zero-width spaces and joiners, bidirectional overrides — are refused because a name may come
    /// from a source server rather than a person, and such a character would make a name look blank, render the rest of
    /// a row reversed, or make two siblings look identical while comparing as different.
    /// </remarks>
    /// <param name="value">The name as supplied.</param>
    /// <param name="name">The name, when it is one.</param>
    /// <returns>Whether <paramref name="value" /> is a name.</returns>
    public static bool TryCreate(string? value, out LocalMailFolderName name)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed)
            || trimmed.Length > MaximumLength
            || trimmed.Any(static character =>
                char.IsControl(character)
                || char.GetUnicodeCategory(character) == UnicodeCategory.Format
                || character == HierarchyDelimiter))
        {
            name = default;

            return false;
        }

        name = new LocalMailFolderName(trimmed);

        return true;
    }

    /// <summary>Reads a name that is known to be valid.</summary>
    /// <param name="value">The name.</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is not a name.</exception>
    public static LocalMailFolderName Create(string value) =>
        TryCreate(value, out var name)
            ? name
            : throw new ArgumentException(
                $"A local folder name is non-empty, at most {MaximumLength} characters, and carries no control or format character and no '{HierarchyDelimiter}'.",
                nameof(value));

    /// <summary>Gets whether two names name the same folder among siblings.</summary>
    /// <param name="other">The other name.</param>
    /// <returns>Whether the two differ only in case, or not at all.</returns>
    public bool NamesSameFolderAs(LocalMailFolderName other) =>
        string.Equals(this.ComparisonKey, other.ComparisonKey, StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
