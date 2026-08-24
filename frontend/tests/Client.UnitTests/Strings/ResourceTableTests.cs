// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation;

namespace MailFathom.Client.UnitTests.Strings;

/// <summary>
/// Holds the string tables under <c>Strings/</c> against each other and against the configuration that offers them.
/// </summary>
/// <remarks>
/// A running head resolves a <c>x:Uid</c> or an <see cref="Microsoft.Extensions.Localization.IStringLocalizer"/>
/// lookup against a compiled resource map, which this host has none of, so the suite reads the authored files the
/// project links into its output. What is worth asserting of them is not what any one word says — that is a
/// translator's judgement — but that a language is declared in both places and says the same things in each: an
/// offered culture with no table behind it is a screen with no words on it, and a key present in one table and
/// missing from the other is a word somebody reading that language never sees. Neither is reported by a build.
/// </remarks>
public sealed class ResourceTableTests
{
    /// <summary>Every language the client is readable in, derived rather than named here.</summary>
    public static TheoryData<string> Languages => [.. DeclaredLanguages.Offered()];

    /// <summary>
    /// A language exists for a reader only where the configuration offers it and a table answers it, so the two lists
    /// are the same list. Naming one without the other is the failure this is here for, in either direction.
    /// </summary>
    [Fact]
    public void Tables_TheCulturesTheConfigurationOffers_AreExactlyTheOnesAuthored()
    {
        // Act
        var offered = DeclaredLanguages.Offered().Order(StringComparer.Ordinal);
        var tabled = DeclaredLanguages.Tabled().Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal(offered, tabled);
    }

    /// <summary>Every language names the same things, so none of them is missing a word the others have.</summary>
    [Fact]
    public void Tables_TheLanguagesTheClientOffers_HoldTheSameKeys()
    {
        // Arrange
        var languages = DeclaredLanguages.Offered();
        var first = KeysOf(languages[0]);

        // Act, Assert
        Assert.All(
            languages.Skip(1),
            language => Assert.Equal(first, KeysOf(language)));
    }

    /// <summary>A key with nothing behind it reaches a screen as a blank, which is worse than an untranslated word.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_EveryStringTheClientShows_HasWordsBehindIt(string culture)
    {
        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.NotEmpty(table);
        Assert.All(table, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), entry.Key));
    }

    /// <summary>
    /// The theme offers are named in code rather than by a <c>x:Uid</c>, so the keys the model builds are the ones
    /// this asserts the tables hold — the one place a typo would reach a reader as the key itself.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_TheThemesTheClientOffers_AreNamedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = AppThemeOption.Offered.Select(AppThemeOption.ResourceKeyFor);

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    private static IEnumerable<string> KeysOf(string culture) =>
        DeclaredLanguages.TableOf(culture).Keys.Order(StringComparer.Ordinal);
}
