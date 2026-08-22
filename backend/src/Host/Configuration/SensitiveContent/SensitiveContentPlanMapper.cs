// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
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
    /// <summary>The scanners that stop an outgoing message where a deployment named none, which is most deployments.</summary>
    /// <remarks>
    /// One rather than both, for the reason <see cref="SensitiveContentScannerKind" /> gives for keeping the two
    /// switches apart at all: a credential identifies itself and was never meant to be in a message somebody is
    /// sending, while personal data is what correspondence is made of. Screening for the second by default would
    /// refuse most ordinary mail on a deployment that switched it on to protect its AI egress.
    /// </remarks>
    private static readonly SensitiveContentScannerKind[] DefaultScreeningScanners =
        [SensitiveContentScannerKind.Secrets];

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

    /// <summary>Composes what a deployment refuses to let leave in a message it is about to send or file.</summary>
    /// <param name="settings">The bound section, already judged by <see cref="SensitiveContentOptions.Validate" />.</param>
    /// <param name="plan">The plan the same settings composed, which decides which categories a named scanner covers.</param>
    /// <returns>The policy every screened act is judged by, which screens nothing where nothing was named.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// An absent key takes the default and a written empty list takes nothing, which is the distinction the key is
    /// typed as an array to preserve. An entry that names no scanner is unreachable here, because startup validation
    /// refuses it before anything is composed; one reaching this anyway is dropped rather than guessed at.
    /// </remarks>
    public static SensitiveContentScreeningPolicy MapScreeningPolicy(
        SensitiveContentOptions settings,
        SensitiveContentPlan plan)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(plan);

        var named = settings.ScreenOutgoingMailFor is { } configured
            ? configured
                .Select(scanner => Enum.TryParse<SensitiveContentScannerKind>(scanner, ignoreCase: true, out var kind)
                    ? kind
                    : (SensitiveContentScannerKind?)null)
                .Where(kind => kind is not null)
                .Select(kind => kind!.Value)
                .ToArray()
            : DefaultScreeningScanners;

        return SensitiveContentScreeningPolicy.Create(plan, named);
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
            LanguagesOf(settings.PersonalDataAnalyzer),
            settings.PersonalDataAnalyzer.MinimumConfidence);
    }

    /// <summary>Resolves the languages the analyzer is asked in, which a deployment that named none leaves to the default.</summary>
    /// <remarks>
    /// An unnamed list means the language the shipped analyzer image serves, exactly as an unnamed category list means the
    /// scanner's default categories. The profile refuses an empty set outright, which is what makes this the one place the
    /// default is applied rather than a fallback something further down repeats.
    /// </remarks>
    private static IReadOnlyList<string> LanguagesOf(PersonalDataAnalyzerOptions settings) =>
        settings.Languages.Count == 0
            ? [PersonalDataAnalyzerOptions.DefaultLanguage]
            : [.. settings.Languages];

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
