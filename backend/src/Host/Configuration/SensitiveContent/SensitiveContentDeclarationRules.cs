// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>Judges a scanning configuration against what the registered scanners actually declare.</summary>
/// <remarks>
/// <para>
/// This is the part that cannot be left to the binder. A list item the binder cannot bind is dropped and the host starts
/// anyway, so a mistyped category name would leave that category off while an operator reads their own configuration as
/// proof that it is on — precisely the quiet failure this feature exists to prevent. Every configured name is therefore
/// matched against a catalog, and one that matches nothing names itself in a startup failure.
/// </para>
/// <para>
/// Every problem is reported rather than the first, so an operator who wrote two mistakes reads both. Nothing here
/// judges a switch that is off: a category list left behind under a scanner nobody runs describes no protection, so
/// refusing to start over it would be refusing over a comment.
/// </para>
/// </remarks>
internal static class SensitiveContentDeclarationRules
{
    /// <summary>Finds everything wrong with a scanning configuration.</summary>
    /// <param name="settings">The bound section.</param>
    /// <param name="catalogs">Every catalog the registered scanners declare.</param>
    /// <returns>One result per problem, and none when the configuration is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static IReadOnlyList<ValidationResult> FindDeclarationErrors(
        SensitiveContentOptions settings,
        IEnumerable<ISensitiveContentCatalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalogs);

        var declared = catalogs as IReadOnlyList<ISensitiveContentCatalog> ?? [.. catalogs];

        return
        [
            .. Enum.GetValues<SensitiveContentScannerKind>()
                .Where(scanner => settings.For(scanner).Enabled)
                .SelectMany(scanner => FindScannerErrors(scanner, settings.For(scanner), declared)),
        ];
    }

    private static IEnumerable<ValidationResult> FindScannerErrors(
        SensitiveContentScannerKind scanner,
        SensitiveContentScannerOptions settings,
        IReadOnlyList<ISensitiveContentCatalog> catalogs)
    {
        var registered = SensitiveContentCatalogResolution.CatalogsFor(catalogs, scanner);

        if (registered.Count == 0)
        {
            yield return Error(
                scanner,
                string.Empty,
                "is switched on and this deployment registers no detector for it. A scanner that cannot run must not start, because the switch would otherwise report a protection that is not in force.");

            yield break;
        }

        if (registered.Count > 1)
        {
            yield return Error(
                scanner,
                string.Empty,
                $"is switched on and this deployment registers {registered.Count} detectors for it, so which categories it looks for is undecidable.");

            yield break;
        }

        var catalog = registered[0];

        foreach (var result in FindCategoryErrors(scanner, settings, catalog))
        {
            yield return result;
        }

        foreach (var result in FindSuppressionErrors(scanner, settings, catalog))
        {
            yield return result;
        }
    }

    private static IEnumerable<ValidationResult> FindCategoryErrors(
        SensitiveContentScannerKind scanner,
        SensitiveContentScannerOptions settings,
        ISensitiveContentCatalog catalog)
    {
        foreach (var (configured, position) in settings.Categories.Select((name, position) => (name, position)))
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                yield return Error(scanner, $":Categories:{position}", "names no category.");

                continue;
            }

            if (!catalog.Categories.Any(definition => definition.Category.HasName(configured)))
            {
                yield return Error(
                    scanner,
                    $":Categories:{position}",
                    $"is '{configured}', which this scanner does not detect. It detects: {DetectedCategories(catalog)}.");
            }
        }

        var duplicated = settings.Categories
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(sameName => sameName.Count() > 1);

        if (duplicated is not null)
        {
            yield return Error(
                scanner,
                ":Categories",
                $"names '{duplicated.Key}' {duplicated.Count()} times.");
        }

        if (SensitiveContentCatalogResolution.ResolveCategories(settings, catalog).Count == 0)
        {
            yield return settings.Categories.Count == 0
                ? Error(
                    scanner,
                    string.Empty,
                    "is switched on and this scanner declares no category by default, so it would run and find nothing.")
                : Error(
                    scanner,
                    ":Categories",
                    "resolves to no category at all, so the scanner would run and find nothing.");
        }
    }

    private static IEnumerable<ValidationResult> FindSuppressionErrors(
        SensitiveContentScannerKind scanner,
        SensitiveContentScannerOptions settings,
        ISensitiveContentCatalog catalog)
    {
        foreach (var (suppression, position) in settings.Suppressions.Select((entry, position) => (entry, position)))
        {
            if (string.IsNullOrWhiteSpace(suppression.Category) || string.IsNullOrWhiteSpace(suppression.Rule))
            {
                yield return Error(
                    scanner,
                    $":Suppressions:{position}",
                    "must name both a category and a rule inside it.");

                continue;
            }

            var definition = catalog.Categories
                .FirstOrDefault(candidate => candidate.Category.HasName(suppression.Category));

            if (definition is null)
            {
                yield return Error(
                    scanner,
                    $":Suppressions:{position}:Category",
                    $"is '{suppression.Category}', which this scanner does not detect. It detects: {DetectedCategories(catalog)}.");

                continue;
            }

            if (!definition.Rules.Any(rule => rule.HasName(suppression.Rule)))
            {
                yield return Error(
                    scanner,
                    $":Suppressions:{position}:Rule",
                    $"is '{suppression.Rule}', which the category '{definition.Category}' does not hold.");
            }
        }
    }

    private static string DetectedCategories(ISensitiveContentCatalog catalog) =>
        string.Join(", ", catalog.Categories.Select(definition => $"'{definition.Category}'"));

    private static ValidationResult Error(
        SensitiveContentScannerKind scanner,
        string keySuffix,
        string problem) => new(
        $"{SensitiveContentOptions.SectionName}:{scanner}{keySuffix} {problem}",
        [scanner.ToString()]);
}
