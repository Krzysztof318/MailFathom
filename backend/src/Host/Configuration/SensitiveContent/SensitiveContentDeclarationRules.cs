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
/// Every problem is reported rather than the first, so an operator who wrote two mistakes reads both.
/// </para>
/// <para>
/// Two questions are asked of different sets, and the difference is who can turn a scanner on. What the operator
/// <em>wrote</em> under a scanner — its categories and its suppressions — is judged for every scanner this deployment
/// provides, switched on or not, because an owner's own record may switch a provided scanner on for their own mail and
/// no roster exists while this runs: a mistyped category under a switch that is off would otherwise pass a start and
/// then throw out of the posture composition the moment somebody opted in, taking every scanning path on the
/// deployment with it. A scanner this section switched on is judged too, whether or not the deployment can provide it,
/// so a switch on with nothing behind it is still answered here rather than by the endpoint rule alone. Whether a
/// scanner has a detector behind it at all is asked only of the ones this section switched on, because that is a
/// question about this process's own registrations rather than about what an operator wrote, and asking it of a
/// scanner nobody runs would refuse a start over a service graph nothing was going to use.
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
                .Where(scanner => settings.For(scanner).Enabled || settings.ProvidedScanners.Contains(scanner))
                .SelectMany(scanner => FindScannerErrors(
                    scanner,
                    settings.For(scanner),
                    declared,
                    settings.For(scanner).Enabled)),
        ];
    }

    /// <summary>Finds everything wrong with one scanner's declaration, and with the detector behind it where it runs.</summary>
    /// <param name="scanner">The scanner being judged.</param>
    /// <param name="settings">What the deployment wrote under it.</param>
    /// <param name="catalogs">Every catalog the registered scanners declare.</param>
    /// <param name="switchedOn">Whether this deployment's own section runs it, which is what the registration questions are asked of.</param>
    private static IEnumerable<ValidationResult> FindScannerErrors(
        SensitiveContentScannerKind scanner,
        SensitiveContentScannerOptions settings,
        IReadOnlyList<ISensitiveContentCatalog> catalogs,
        bool switchedOn)
    {
        var registered = SensitiveContentCatalogResolution.CatalogsFor(catalogs, scanner);

        if (registered.Count != 1)
        {
            // Reported only where this section runs the scanner. A deployment that runs none registers no catalog
            // either — a validator judging a candidate configuration is handed exactly what the process holds — so
            // asking this of a scanner nobody runs would refuse every such start over a detector nothing wanted.
            if (switchedOn)
            {
                yield return registered.Count == 0
                    ? Error(
                        scanner,
                        string.Empty,
                        "is switched on and this deployment registers no detector for it. A scanner that cannot run must not start, because the switch would otherwise report a protection that is not in force.")
                    : Error(
                        scanner,
                        string.Empty,
                        $"is switched on and this deployment registers {registered.Count} detectors for it, so which categories it looks for is undecidable.");
            }

            yield break;
        }

        var catalog = registered[0];

        foreach (var result in FindCategoryErrors(scanner, settings, catalog, switchedOn))
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
        ISensitiveContentCatalog catalog,
        bool switchedOn)
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
            // A written list that resolves to nothing is what the operator wrote, so it is refused whether or not this
            // section runs the scanner: an owner switching that scanner on for their own mail would otherwise be the
            // first thing to meet it, out of the posture composition rather than out of a start. An empty list is a
            // property of the catalog instead, and is judged only where this section runs it.
            if (settings.Categories.Count > 0)
            {
                yield return Error(
                    scanner,
                    ":Categories",
                    "resolves to no category at all, so the scanner would run and find nothing.");
            }
            else if (switchedOn)
            {
                yield return Error(
                    scanner,
                    string.Empty,
                    "is switched on and this scanner declares no category by default, so it would run and find nothing.");
            }
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
