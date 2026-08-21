// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent;

/// <summary>What one switched-on scanner looks for, after configuration has been resolved against its catalog.</summary>
/// <remarks>
/// <para>
/// The categories here are the ones that will actually be looked for, spelled the way the scanner declares them rather
/// than the way an operator capitalized them. That is what makes redaction reproducible: the same deployment produces
/// the same placeholders whatever the configuration file looked like.
/// </para>
/// <para>
/// A suppressed rule is always inside one of those categories. A suppression can therefore silence part of a category
/// that stays on, and can never switch a category on, which is the asymmetry that keeps the category the unit of
/// configuration.
/// </para>
/// </remarks>
public sealed record SensitiveContentScannerPlan
{
    private SensitiveContentScannerPlan(
        SensitiveContentScannerKind scanner,
        IReadOnlyList<SensitiveContentCategory> categories,
        IReadOnlyList<SensitiveContentRule> suppressedRules)
    {
        this.Scanner = scanner;
        this.Categories = categories;
        this.SuppressedRules = suppressedRules;
    }

    /// <summary>Gets which of the two switches this plan belongs to.</summary>
    public SensitiveContentScannerKind Scanner { get; }

    /// <summary>Gets every category this scanner looks for.</summary>
    public IReadOnlyList<SensitiveContentCategory> Categories { get; }

    /// <summary>Gets every rule that stays silent inside a category that is otherwise looked for.</summary>
    public IReadOnlyList<SensitiveContentRule> SuppressedRules { get; }

    /// <summary>Composes the plan for one scanner.</summary>
    /// <param name="scanner">Which of the two switches the plan belongs to.</param>
    /// <param name="categories">The categories the scanner looks for.</param>
    /// <param name="suppressedRules">The rules that stay silent inside those categories.</param>
    /// <returns>The validated plan.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no category is named, a category is named twice, or a suppression falls outside the named categories.</exception>
    /// <remarks>
    /// A plan naming no category is refused rather than accepted as a scanner that finds nothing, because that is
    /// indistinguishable at run time from a scanner that is working and would leave an operator reading a switch that is
    /// on as protection that is in force.
    /// </remarks>
    public static SensitiveContentScannerPlan Create(
        SensitiveContentScannerKind scanner,
        IReadOnlyList<SensitiveContentCategory> categories,
        IReadOnlyList<SensitiveContentRule> suppressedRules)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(suppressedRules);

        if (categories.Count == 0)
        {
            throw new ArgumentException(
                $"The {scanner} scanner is switched on and names no category, so it would run and find nothing.",
                nameof(categories));
        }

        var duplicated = categories
            .GroupBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(sameName => sameName.Count() > 1);

        if (duplicated is not null)
        {
            throw new ArgumentException(
                $"The {scanner} scanner names the category '{duplicated.Key}' more than once.",
                nameof(categories));
        }

        var stray = suppressedRules.FirstOrDefault(rule => !categories.Contains(rule.Category));

        if (stray is not null)
        {
            throw new ArgumentException(
                $"The {scanner} scanner suppresses '{stray}', which is not inside a category it looks for.",
                nameof(suppressedRules));
        }

        return new SensitiveContentScannerPlan(scanner, [.. categories], [.. suppressedRules]);
    }

    /// <summary>Reports whether a rule stays silent under this plan.</summary>
    /// <param name="rule">The rule a scanner is about to apply.</param>
    /// <returns><see langword="true" /> when the rule is suppressed; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule" /> is <see langword="null" />.</exception>
    public bool Suppresses(SensitiveContentRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return this.SuppressedRules.Contains(rule);
    }
}
