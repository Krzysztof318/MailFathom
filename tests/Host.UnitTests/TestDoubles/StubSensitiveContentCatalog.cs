// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>A catalog declaring exactly what a configuration test needs a scanner to know about.</summary>
/// <remarks>
/// Hand-written rather than substituted because every test here reads the same two or three declarations and a factory
/// that builds them by name keeps each test's arrangement to the part that differs.
/// </remarks>
internal sealed class StubSensitiveContentCatalog(
    SensitiveContentScannerKind scanner,
    IReadOnlyList<SensitiveContentCategoryDefinition> categories) : ISensitiveContentCatalog
{
    /// <inheritdoc />
    public SensitiveContentScannerKind Scanner { get; } = scanner;

    /// <inheritdoc />
    public IReadOnlyList<SensitiveContentCategoryDefinition> Categories { get; } = categories;

    /// <summary>Declares a category holding the rules named, and says whether it is looked for by default.</summary>
    /// <param name="category">The category name.</param>
    /// <param name="detectedByDefault">Whether a deployment naming no category receives it.</param>
    /// <param name="rules">The rule names inside it.</param>
    /// <returns>The declaration.</returns>
    public static SensitiveContentCategoryDefinition Declare(
        string category,
        bool detectedByDefault,
        params string[] rules)
    {
        var declared = SensitiveContentCategory.Create(category);

        return SensitiveContentCategoryDefinition.Create(
            declared,
            detectedByDefault,
            [.. rules.Select(rule => SensitiveContentRule.Create(declared, rule))]);
    }
}
