// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;

namespace MailFathom.Application.SensitiveContent;

/// <summary>One detection rule inside a category, which is the finest thing an operator can name.</summary>
/// <remarks>
/// <para>
/// A rule is always carried with the category it belongs to, because the same rule name may exist under two categories
/// and a suppression that named only the rule would silence both. Carrying the pair is also what makes a suppression
/// unable to switch a category on: it names something inside a category rather than the category itself.
/// </para>
/// <para>
/// A rule is never the unit a scanner is configured by. It exists so a single entry of a corpus that misfires on one
/// deployment's mail can be silenced without that deployment giving up the category around it.
/// </para>
/// </remarks>
public sealed partial record SensitiveContentRule
{
    private SensitiveContentRule(SensitiveContentCategory category, string name)
    {
        this.Category = category;
        this.Name = name;
    }

    /// <summary>Gets the category this rule belongs to.</summary>
    public SensitiveContentCategory Category { get; }

    /// <summary>Gets the rule's name, as the scanner that declares it spells it.</summary>
    public string Name { get; }

    /// <summary>Creates a rule from a declared or configured name inside a category.</summary>
    /// <param name="category">The category the rule belongs to.</param>
    /// <param name="name">The rule name to validate.</param>
    /// <returns>The validated rule.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="category" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name" /> is not an acceptable rule name.</exception>
    public static SensitiveContentRule Create(SensitiveContentCategory category, string name)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (name is null || !AcceptedName.IsMatch(name))
        {
            throw new ArgumentException(
                $"'{name}' is not an acceptable sensitive-content rule name. It may carry up to 128 letters, digits, dots, dashes, and underscores, and must begin with a letter or a digit.",
                nameof(name));
        }

        return new SensitiveContentRule(category, name);
    }

    /// <summary>Reports whether a configured name is this rule's, ignoring how it was capitalized.</summary>
    /// <param name="name">The name an operator configured.</param>
    /// <returns><see langword="true" /> when the two name the same rule; otherwise <see langword="false" />.</returns>
    public bool HasName(string name) => string.Equals(this.Name, name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override string ToString() => $"{this.Category}:{this.Name}";

    /// <remarks>
    /// Wider than a category's grammar in length and in its first character, because a rule name is carried across from
    /// a third-party corpus whose entries read as <c>aws-access-token</c> or <c>1password-secret-key</c>, and narrowing
    /// it further would mean renaming corpus entries rather than adopting them. It stays as narrow in what characters it
    /// admits, because a rule name reaches a log line and a validation message.
    /// </remarks>
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex AcceptedName { get; }
}
