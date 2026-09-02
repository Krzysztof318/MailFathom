// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

    /// <summary>Composes the plan one set of switched-on scanners describes.</summary>
    /// <param name="settings">The bound section, already judged by <see cref="SensitiveContentDeclarationRules" />.</param>
    /// <param name="catalogs">Every catalog the registered scanners declare.</param>
    /// <param name="switchedOn">Which scanners this plan runs, which is one owner's answer rather than the section's.</param>
    /// <returns>The plan, or <see langword="null" /> when nothing is switched on.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Nothing switched on is not a failure. It is the default state of the product, and returning nothing is what lets
    /// a posture hold no redaction rather than one that would fail at first use.
    /// </para>
    /// <para>
    /// Which scanners run is an argument while what each of them looks for is read from the section, and the split is
    /// the whole of what an owner may decide: a scanner switched on for one owner looks for the categories the
    /// deployment named, because what a scanner detects is one answer for the machine it runs on.
    /// </para>
    /// </remarks>
    public static SensitiveContentPlan? Map(
        SensitiveContentOptions settings,
        IEnumerable<ISensitiveContentCatalog> catalogs,
        IReadOnlyCollection<SensitiveContentScannerKind> switchedOn)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(switchedOn);

        if (switchedOn.Count == 0)
        {
            return null;
        }

        var declared = catalogs as IReadOnlyList<ISensitiveContentCatalog> ?? [.. catalogs];

        var scanners = Enum.GetValues<SensitiveContentScannerKind>()
            .Where(switchedOn.Contains)
            .Select(scanner => MapScanner(scanner, settings.For(scanner), declared))
            .ToArray();

        return SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Create(
                settings.MaximumAnalyzedCharacters,
                settings.ScanTimeout,
                settings.MaximumConcurrentScans),
            scanners);
    }

    /// <summary>Reads which scanners stop an outgoing message on a deployment that named none.</summary>
    /// <param name="settings">The bound section, already judged by <see cref="SensitiveContentOptions.Validate" />.</param>
    /// <returns>The scanners the deployment screens every owner's outgoing mail for, which may be none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Published rather than folded into the composition below, because two things ask it: the policy an act is judged
    /// by, and the rule that refuses an owner naming fewer scanners in their own record than the deployment requires.
    /// An entry that names no scanner is unreachable here, because startup validation refuses it before anything reads
    /// this; one arriving anyway is dropped rather than guessed at.
    /// </remarks>
    public static IReadOnlyList<SensitiveContentScannerKind> ScreeningScannersOf(SensitiveContentOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.ScreenOutgoingMailFor is { } configured
            ? [.. configured
                .Select(scanner => Enum.TryParse<SensitiveContentScannerKind>(scanner, ignoreCase: true, out var kind)
                    ? kind
                    : (SensitiveContentScannerKind?)null)
                .Where(kind => kind is not null)
                .Select(kind => kind!.Value)]
            : DefaultScreeningScanners;
    }

    /// <summary>Composes what one owner's outgoing message may not carry out of this deployment.</summary>
    /// <param name="plan">The posture's plan, which decides which categories a named scanner covers.</param>
    /// <param name="screeningScanners">The scanners whose findings stop the act, deployment and owner composed.</param>
    /// <returns>The policy every screened act is judged by, which screens nothing where nothing was named.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// An absent key takes the default and a written empty list takes nothing, which is the distinction both keys are
    /// typed as arrays to preserve. What arrives here is already the union of the two answers, so a scanner the
    /// deployment named cannot be missing from one owner's policy.
    /// </remarks>
    public static SensitiveContentScreeningPolicy MapScreeningPolicy(
        SensitiveContentPlan plan,
        IReadOnlyList<SensitiveContentScannerKind> screeningScanners)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(screeningScanners);

        return SensitiveContentScreeningPolicy.Create(plan, screeningScanners);
    }

    /// <summary>Composes the profile the personal-data analyzer is reached under.</summary>
    /// <param name="settings">The bound section, already judged by <see cref="SensitiveContentOptions.Validate" />.</param>
    /// <returns>The profile the scanner and the readiness probe both read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the configuration names no analyzer address.</exception>
    /// <remarks>
    /// Called only where the deployment stated an address, which is what makes the personal-data scanner available at
    /// all — to the deployment itself and to any owner who switches it on for their own mail. An absent address here is
    /// therefore a defect rather than a configuration error to report: the composition root registers nothing that
    /// reaches this without one, and reaching it with none means the two passes disagree.
    /// </remarks>
    public static PersonalDataAnalyzerProfile MapAnalyzerProfile(SensitiveContentOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!Uri.TryCreate(settings.PersonalDataAnalyzer.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException(
                "The personal-data analyzer profile was composed for a deployment whose validated configuration carries no analyzer address.");
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
