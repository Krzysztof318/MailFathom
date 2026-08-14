// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;

namespace MailFathom.Application.SensitiveContent;

/// <summary>One kind of sensitive material a scanner can look for, which is the unit an operator configures.</summary>
/// <remarks>
/// <para>
/// A category rather than a rule is the unit of configuration because a rule corpus carries hundreds of entries, and an
/// operator picking among them individually is maintaining a fork of that corpus. A single rule that misfires on one
/// mailbox is suppressed by name inside a category that stays on.
/// </para>
/// <para>
/// The name is written into a placeholder that replaces detected text, and from there into chunks, embeddings, and
/// whatever a reader is served, so the accepted grammar is narrow rather than merely careful: a name that could carry a
/// bracket, a newline, or a quotation mark would let a rule corpus decide how the surrounding text parses.
/// </para>
/// <para>
/// Equality is ordinal, and a configured name is matched against a declared one case-insensitively where the two meet.
/// The declared spelling is the one that survives that match, which is what keeps a placeholder identical however an
/// operator capitalized the category in their own file.
/// </para>
/// </remarks>
public sealed partial record SensitiveContentCategory
{
    private SensitiveContentCategory(string name) => this.Name = name;

    /// <summary>Gets the category's name, as the scanner that declares it spells it.</summary>
    public string Name { get; }

    /// <summary>Creates a category from a declared or configured name.</summary>
    /// <param name="name">The name to validate.</param>
    /// <returns>The validated category.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name" /> is not an acceptable category name.</exception>
    public static SensitiveContentCategory Create(string name)
    {
        if (name is null || !AcceptedName.IsMatch(name))
        {
            throw new ArgumentException(
                $"'{name}' is not an acceptable sensitive-content category name. It may carry up to 64 letters, digits, dots, dashes, and underscores, and must begin with a letter.",
                nameof(name));
        }

        return new SensitiveContentCategory(name);
    }

    /// <summary>Reports whether a configured name is this category's, ignoring how it was capitalized.</summary>
    /// <param name="name">The name an operator configured.</param>
    /// <returns><see langword="true" /> when the two name the same category; otherwise <see langword="false" />.</returns>
    public bool HasName(string name) => string.Equals(this.Name, name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override string ToString() => this.Name;

    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex AcceptedName { get; }
}
