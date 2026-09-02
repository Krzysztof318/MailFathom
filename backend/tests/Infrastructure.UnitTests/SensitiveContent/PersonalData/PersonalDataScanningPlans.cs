// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Infrastructure.SensitiveContent.PersonalData;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.PersonalData;

/// <summary>Composes the plans the personal-data tests run against, the way the host's mapper composes them.</summary>
/// <remarks>
/// Built through the real <see cref="PersonalDataContentCatalog" /> rather than from category names written here, so a
/// test asks for a category the scanner actually declares and a renamed one fails the test that reads it rather than
/// silently scanning for nothing.
/// </remarks>
internal static class PersonalDataScanningPlans
{
    /// <summary>The plan a deployment that names no categories of its own receives.</summary>
    public static SensitiveContentPlan Default { get; } = For(DefaultCategories());

    /// <summary>The confidence floor the tests state, distinct from any default so a request carrying one proves where it came from.</summary>
    public const double MinimumConfidence = 0.42;

    /// <summary>The profile the tests reach an analyzer under, at an address nothing resolves.</summary>
    /// <remarks>The tests answer through a scripted handler, so the address is only ever composed against and never dialled.</remarks>
    public static PersonalDataAnalyzerProfile Profile { get; } =
        PersonalDataAnalyzerProfile.Create(new Uri("http://analyzer.invalid:3000"), ["en"], MinimumConfidence);

    /// <summary>The profile of a mixed mailbox, which asks the analyzer once per language over the same text.</summary>
    public static PersonalDataAnalyzerProfile MultilingualProfile { get; } =
        PersonalDataAnalyzerProfile.Create(new Uri("http://analyzer.invalid:3000"), ["pl", "en"], MinimumConfidence);

    /// <summary>Composes a plan over named categories, with no rule suppressed.</summary>
    /// <param name="categories">The categories the scanner looks for.</param>
    /// <returns>The plan.</returns>
    public static SensitiveContentPlan For(IReadOnlyList<SensitiveContentCategory> categories) =>
        For(categories, []);

    /// <summary>Composes a plan over named categories with rules suppressed inside them.</summary>
    /// <param name="categories">The categories the scanner looks for.</param>
    /// <param name="suppressedRules">The rules that stay silent inside them.</param>
    /// <returns>The plan.</returns>
    public static SensitiveContentPlan For(
        IReadOnlyList<SensitiveContentCategory> categories,
        IReadOnlyList<SensitiveContentRule> suppressedRules) => SensitiveContentPlan.Create(
        SensitiveContentScanBounds.Default,
        [SensitiveContentScannerPlan.Create(SensitiveContentScannerKind.Pii, categories, suppressedRules)]);

    /// <summary>The categories the scanner declares as detected when a deployment names none.</summary>
    public static IReadOnlyList<SensitiveContentCategory> DefaultCategories() =>
    [
        .. new PersonalDataContentCatalog().Categories
            .Where(definition => definition.DetectedByDefault)
            .Select(definition => definition.Category),
    ];

    /// <summary>The category one of the scanner's declared names identifies.</summary>
    /// <param name="name">The declared category name.</param>
    /// <returns>The category.</returns>
    public static SensitiveContentCategory Category(string name) =>
        new PersonalDataContentCatalog().Categories.First(definition => definition.Category.HasName(name)).Category;

    /// <summary>The rule one declared entity name identifies inside a declared category.</summary>
    /// <param name="category">The declared category name.</param>
    /// <param name="rule">The analyzer entity name the category holds.</param>
    /// <returns>The rule.</returns>
    public static SensitiveContentRule Rule(string category, string rule) =>
        new PersonalDataContentCatalog().Categories
            .First(definition => definition.Category.HasName(category))
            .Rules
            .First(candidate => candidate.HasName(rule));
}
