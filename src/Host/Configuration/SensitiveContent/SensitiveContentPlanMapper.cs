// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Infrastructure.SensitiveContent.PersonalData;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>Turns the bound <c>SensitiveContent</c> section into the values the scanners and the redactor run on.</summary>
/// <remarks>
/// The mapping is separate from the options type for the reason every mapper in this directory is: the bound object is
/// mutable, binder-shaped, and full of empty lists that mean "the default", while the plan is the resolved value the
/// rest of the system is allowed to assume. Keeping the two apart is what lets a scanner hold no defaulting logic at
/// all.
/// </remarks>
internal static class SensitiveContentPlanMapper
{
    /// <summary>Composes the plan a configuration describes.</summary>
    /// <param name="settings">The bound section, already judged by <see cref="SensitiveContentDeclarationRules" />.</param>
    /// <param name="catalogs">Every catalog the registered scanners declare.</param>
    /// <returns>The plan, or <see langword="null" /> when both switches are off.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Nothing switched on is not a failure. It is the default state of the product, and returning nothing is what lets
    /// the composition root register no redactor rather than one that would fail at first use.
    /// </remarks>
    public static SensitiveContentPlan? Map(
        SensitiveContentOptions settings,
        IEnumerable<ISensitiveContentCatalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalogs);

        if (!settings.IsAnyScannerEnabled)
        {
            return null;
        }

        var declared = catalogs as IReadOnlyList<ISensitiveContentCatalog> ?? [.. catalogs];

        var scanners = Enum.GetValues<SensitiveContentScannerKind>()
            .Where(scanner => settings.For(scanner).Enabled)
            .Select(scanner => MapScanner(scanner, settings.For(scanner), declared))
            .ToArray();

        return SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Create(
                settings.MaximumAnalyzedCharacters,
                settings.ScanTimeout,
                settings.MaximumConcurrentScans),
            scanners);
    }

    /// <summary>Composes the profile the personal-data analyzer is reached under.</summary>
    /// <param name="settings">The bound section, already judged by <see cref="SensitiveContentOptions.Validate" />.</param>
    /// <returns>The profile the scanner and the readiness probe both read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the personal-data scanner is not switched on, or names no analyzer address.</exception>
    /// <remarks>
    /// Called only where that scanner is switched on, so an absent address here is a defect rather than a configuration
    /// error to report: startup validation refuses that combination before anything is composed, and reaching this with one
    /// means the two passes disagree.
    /// </remarks>
    public static PersonalDataAnalyzerProfile MapAnalyzerProfile(SensitiveContentOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Pii.Enabled)
        {
            throw new InvalidOperationException(
                "The personal-data analyzer profile was composed for a deployment whose configuration does not switch that scanner on.");
        }

        if (!Uri.TryCreate(settings.PersonalDataAnalyzer.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException(
                "The personal-data scanner is switched on and the validated configuration carries no analyzer address, which startup validation refuses.");
        }

        return PersonalDataAnalyzerProfile.Create(
            endpoint,
            settings.PersonalDataAnalyzer.Language,
            settings.PersonalDataAnalyzer.MinimumConfidence);
    }

    private static SensitiveContentScannerPlan MapScanner(
        SensitiveContentScannerKind scanner,
        SensitiveContentScannerOptions settings,
        IReadOnlyList<ISensitiveContentCatalog> catalogs)
    {
        var registered = SensitiveContentCatalogResolution.CatalogsFor(catalogs, scanner);

        // Startup already refuses a switch with no catalog behind it, so this path is only reached when something
        // unregistered a scanner after that refusal ran. Composing a plan around the absence would replace a named
        // configuration failure with an empty scan that reports nothing wrong.
        var catalog = registered.Count == 1
            ? registered[0]
            : throw new InvalidOperationException(
                $"The {scanner} scanner was switched on at registration and {registered.Count} catalogs answer for it.");

        var categories = SensitiveContentCatalogResolution.ResolveCategories(settings, catalog);

        return SensitiveContentScannerPlan.Create(
            scanner,
            categories,
            SensitiveContentCatalogResolution.ResolveSuppressions(settings, catalog, categories));
    }
}
