// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Client.UnitTests.Strings;

namespace MailFathom.Client.UnitTests;

/// <summary>
/// Covers what the shipped application declares about the languages it can be read in.
/// </summary>
/// <remarks>
/// The declaration is the embedded <c>appsettings.json</c> every head reads at startup, which is what
/// <c>ILocalizationService</c> offers a person and therefore what a screen's picker is filled from. What that list has
/// to agree with is asserted beside the tables it names, in <see cref="ResourceTableTests"/>; what is asserted here is
/// the list itself, which is a product decision rather than a consistency one.
/// </remarks>
public sealed class ClientLocalizationTests
{
    /// <summary>English and Polish, and neither more nor fewer: the two languages this release is readable in.</summary>
    [Fact]
    public void Cultures_TheEmbeddedConfiguration_NamesTheLanguagesTheClientCarries()
    {
        // Act
        var cultures = DeclaredLanguages.Offered();

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
        var cultures = DeclaredLanguages.Offered().Select(CultureInfo.GetCultureInfo);

        // Assert
        Assert.All(cultures, culture => Assert.True(culture.IsNeutralCulture, culture.Name));
    }
}
