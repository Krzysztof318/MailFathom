// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Xml.Linq;
using MailFathom.Client.Presentation;

namespace MailFathom.Client.UnitTests.Strings;

/// <summary>
/// Reads the string tables under <c>Strings/</c> and holds them to each other.
/// </summary>
/// <remarks>
/// A running head resolves a <c>x:Uid</c> or an <see cref="Microsoft.Extensions.Localization.IStringLocalizer"/>
/// lookup against a compiled resource map, which this host has none of, so the suite reads the authored files the
/// project links into its output. What is worth asserting of them is not what any one word says — that is a
/// translator's judgement — but that the two tables carry the same keys: a key present in one and missing from the
/// other is a word somebody reading the other language never sees, and nothing in the build reports it.
/// </remarks>
public sealed class ResourceTableTests
{
    /// <summary>English and Polish name the same things, so neither language is missing a word the other has.</summary>
    [Fact]
    public void Tables_TheLanguagesTheClientOffers_HoldTheSameKeys()
    {
        // Act
        var english = KeysOf("en");
        var polish = KeysOf("pl");

        // Assert
        Assert.Equal(english, polish);
    }

    /// <summary>A key with nothing behind it reaches a screen as a blank, which is worse than an untranslated word.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("pl")]
    public void Tables_EveryStringTheClientShows_HasWordsBehindIt(string culture)
    {
        // Act
        var table = TableOf(culture);

        // Assert
        Assert.NotEmpty(table);
        Assert.All(table, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), entry.Key));
    }

    /// <summary>
    /// The theme offers are named in code rather than by a <c>x:Uid</c>, so the keys the model builds are the ones
    /// this asserts the tables hold — the one place a typo would reach a reader as the key itself.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("pl")]
    public void Tables_TheThemesTheClientOffers_AreNamedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = AppThemeOption.Offered.Select(AppThemeOption.ResourceKeyFor);

        // Act
        var table = TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    private static Dictionary<string, string> TableOf(string culture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Strings", culture, "Resources.resw");
        Assert.True(File.Exists(path), path);

        // Everything above the first authored entry is the ResX schema and the four resheaders, neither of which is a
        // `data` element — the sample entries the format's preamble shows are inside an XML comment.
        return XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(
                entry => entry.Attribute("name")!.Value,
                entry => entry.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static IEnumerable<string> KeysOf(string culture) =>
        TableOf(culture).Keys.Order(StringComparer.Ordinal);
}
