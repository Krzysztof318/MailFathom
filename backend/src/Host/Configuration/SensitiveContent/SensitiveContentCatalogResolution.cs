// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>Matches what an operator wrote against what a scanner declares.</summary>
/// <remarks>
/// One place answers this for both readers of it. <see cref="SensitiveContentDeclarationRules" /> reports what did not
/// match and <see cref="SensitiveContentPlanMapper" /> composes what did, so a validated configuration and the plan
/// built from it cannot disagree about which categories a scanner ends up looking for.
/// </remarks>
internal static class SensitiveContentCatalogResolution
{
    /// <summary>Finds the categories a scanner will look for.</summary>
    /// <param name="settings">The switch's configuration.</param>
    /// <param name="catalog">What that scanner declares.</param>
    /// <returns>The declared categories, in the order the catalog declares them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A configured name that matches nothing is left out rather than carried, because startup has already refused such
    /// a configuration and the only path that reaches this with one is the pass composing that refusal. The declared
    /// spelling is what survives a match, so the placeholder a category produces does not depend on how an operator
    /// capitalized it.
    /// </remarks>
    public static IReadOnlyList<SensitiveContentCategory> ResolveCategories(
        SensitiveContentScannerOptions settings,
        ISensitiveContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);

        return settings.Categories.Count == 0
            ? [.. catalog.Categories.Where(definition => definition.DetectedByDefault).Select(definition => definition.Category)]
            : [.. catalog.Categories
                .Where(definition => settings.Categories.Any(configured => definition.Category.HasName(configured)))
                .Select(definition => definition.Category)];
    }

    /// <summary>Finds the rules a scanner will stay silent about.</summary>
    /// <param name="settings">The switch's configuration.</param>
    /// <param name="catalog">What that scanner declares.</param>
    /// <param name="categories">The categories that scanner will look for.</param>
    /// <returns>The declared rules the suppressions name, inside categories that are being looked for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A suppression naming a category this deployment does not look for resolves to nothing, which is what makes a
    /// suppression unable to switch a category on. It is not an error either: a category switched off with its
    /// suppression left in place says nothing untrue about what is being scanned for.
    /// </remarks>
    public static IReadOnlyList<SensitiveContentRule> ResolveSuppressions(
        SensitiveContentScannerOptions settings,
        ISensitiveContentCatalog catalog,
        IReadOnlyList<SensitiveContentCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(categories);

        return
        [
            .. catalog.Categories
                .Where(definition => categories.Contains(definition.Category))
                .SelectMany(definition => definition.Rules)
                .Where(rule => settings.Suppressions.Any(suppression =>
                    rule.Category.HasName(suppression.Category) && rule.HasName(suppression.Rule)))
                .Distinct(),
        ];
    }

    /// <summary>Finds the catalogs one scanner registered.</summary>
    /// <param name="catalogs">Every registered catalog.</param>
    /// <param name="scanner">The scanner to look up.</param>
    /// <returns>The catalogs declaring themselves that scanner's, which is ordinarily exactly one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="catalogs" /> is <see langword="null" />.</exception>
    public static IReadOnlyList<ISensitiveContentCatalog> CatalogsFor(
        IEnumerable<ISensitiveContentCatalog> catalogs,
        SensitiveContentScannerKind scanner)
    {
        ArgumentNullException.ThrowIfNull(catalogs);

        return [.. catalogs.Where(catalog => catalog.Scanner == scanner)];
    }
}
