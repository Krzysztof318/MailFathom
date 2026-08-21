// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Infrastructure.SensitiveContent.PersonalData;

namespace MailFathom.IntegrationTests.SensitiveContent;

/// <summary>Composes the plans the analyzer tests run against, the way the host's mapper composes them.</summary>
/// <remarks>
/// Built through the real catalog rather than from category names written here, so a test asks for a category the scanner
/// actually declares and a renamed one fails the test that reads it rather than silently scanning for nothing.
/// </remarks>
internal static class PersonalDataScanningPlan
{
    /// <summary>The plan a deployment that names no categories of its own receives.</summary>
    public static SensitiveContentPlan Default { get; } = For(
    [
        .. Declared().Where(definition => definition.DetectedByDefault).Select(definition => definition.Category),
    ]);

    /// <summary>Every category the scanner declares, whether or not it is detected by default.</summary>
    public static IReadOnlyList<SensitiveContentCategory> EveryDeclaredCategory() =>
        [.. Declared().Select(definition => definition.Category)];

    /// <summary>The category one of the scanner's declared names identifies.</summary>
    /// <param name="name">The declared category name.</param>
    /// <returns>The category.</returns>
    public static SensitiveContentCategory Category(string name) =>
        Declared().First(definition => definition.Category.HasName(name)).Category;

    /// <summary>Composes a plan over named categories, with no rule suppressed.</summary>
    /// <param name="categories">The categories the scanner looks for.</param>
    /// <returns>The plan.</returns>
    public static SensitiveContentPlan For(IReadOnlyList<SensitiveContentCategory> categories) =>
        SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [SensitiveContentScannerPlan.Create(SensitiveContentScannerKind.Pii, categories, [])]);

    private static IReadOnlyList<SensitiveContentCategoryDefinition> Declared() =>
        new PersonalDataContentCatalog().Categories;
}
