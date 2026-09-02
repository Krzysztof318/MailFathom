// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Detection;

/// <summary>One category a scanner can look for, the rules inside it, and whether it is on when nothing names it.</summary>
/// <remarks>
/// The default set is the product's opinion rather than a starting point an operator has to assemble, which is why the
/// answer lives with the scanner that declares the category rather than in a deployment's configuration. Naming
/// categories in configuration replaces that opinion outright instead of adding to it.
/// </remarks>
public sealed record SensitiveContentCategoryDefinition
{
    private SensitiveContentCategoryDefinition(
        SensitiveContentCategory category,
        bool detectedByDefault,
        IReadOnlyList<SensitiveContentRule> rules)
    {
        this.Category = category;
        this.DetectedByDefault = detectedByDefault;
        this.Rules = rules;
    }

    /// <summary>Gets the category being declared.</summary>
    public SensitiveContentCategory Category { get; }

    /// <summary>Gets whether this category is looked for by a deployment that names no categories of its own.</summary>
    public bool DetectedByDefault { get; }

    /// <summary>Gets every rule inside the category, which are the names a suppression may reach.</summary>
    public IReadOnlyList<SensitiveContentRule> Rules { get; }

    /// <summary>Declares a category.</summary>
    /// <param name="category">The category being declared.</param>
    /// <param name="detectedByDefault">Whether it is looked for when a deployment names no categories.</param>
    /// <param name="rules">Every rule inside the category.</param>
    /// <returns>The validated declaration.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the rules are empty, name the same rule twice, or belong to another category.</exception>
    public static SensitiveContentCategoryDefinition Create(
        SensitiveContentCategory category,
        bool detectedByDefault,
        IReadOnlyList<SensitiveContentRule> rules)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.Count == 0)
        {
            throw new ArgumentException(
                $"The category '{category}' declares no rule, so nothing in it could ever match.",
                nameof(rules));
        }

        if (rules.Any(rule => rule.Category != category))
        {
            throw new ArgumentException(
                $"The category '{category}' declares a rule belonging to another category, which would make a suppression name a rule this category does not hold.",
                nameof(rules));
        }

        var duplicated = rules
            .GroupBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(sameName => sameName.Count() > 1);

        if (duplicated is not null)
        {
            throw new ArgumentException(
                $"The category '{category}' declares the rule '{duplicated.Key}' more than once, so a suppression naming it would be ambiguous.",
                nameof(rules));
        }

        return new SensitiveContentCategoryDefinition(category, detectedByDefault, [.. rules]);
    }
}
