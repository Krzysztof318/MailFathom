// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;

namespace MailFathom.Client.UnitTests;

/// <summary>
/// Covers what the shipped application declares about the languages it can be read in.
/// </summary>
/// <remarks>
/// The declaration is the embedded <c>appsettings.json</c> every head reads at startup, which is what
/// <c>ILocalizationService</c> offers a person and therefore what a screen's picker is filled from. A culture named
/// there with no string table under <c>Strings/</c> beside it would reach somebody as a screen with no words on it, so
/// the list is asserted rather than left to a reviewer to notice.
/// </remarks>
public sealed class ClientLocalizationTests
{
    private const string CulturesSection = "LocalizationConfiguration";

    /// <summary>English and Polish, and neither more nor fewer: the two the client carries a string table for.</summary>
    [Fact]
    public void Cultures_TheEmbeddedConfiguration_NamesTheLanguagesTheClientCarries()
    {
        // Act
        var cultures = DeclaredCultures();

        // Assert
        Assert.Equal(["en", "pl"], cultures);
    }

    /// <summary>
    /// Neutral cultures rather than regional variants. A regional variant arrives when something actually differs
    /// between two regions, and nothing does yet — while a variant named here would ask for a string table per region.
    /// </summary>
    [Fact]
    public void Cultures_TheEmbeddedConfiguration_NamesNeutralCultures()
    {
        // Act
        var cultures = DeclaredCultures().Select(CultureInfo.GetCultureInfo);

        // Assert
        Assert.All(cultures, culture => Assert.True(culture.IsNeutralCulture, culture.Name));
    }

    private static string[] DeclaredCultures()
    {
        var assembly = typeof(App).Assembly;
        var name = Array.Find(
            assembly.GetManifestResourceNames(),
            resource => resource.EndsWith("appsettings.json", StringComparison.Ordinal));

        Assert.NotNull(name);

        using var settings = assembly.GetManifestResourceStream(name)!;

        // The file is written for a reader rather than for a parser, so it carries comments the JSON grammar does not.
        using var document = JsonDocument.Parse(
            settings,
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        return [.. document.RootElement
            .GetProperty(CulturesSection)
            .GetProperty("Cultures")
            .EnumerateArray()
            .Select(culture => culture.GetString()!)];
    }
}
