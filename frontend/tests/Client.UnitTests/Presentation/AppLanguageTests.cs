// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Client.Presentation;

namespace MailFathom.Client.UnitTests.Presentation;

/// <summary>Covers how a culture becomes the language offer a person picks from.</summary>
public sealed class AppLanguageTests
{
    /// <summary>A language is named in its own words, because that is what somebody who cannot read the current one is scanning for.</summary>
    [Theory]
    [InlineData("en", "English")]
    [InlineData("pl", "Polski")]
    public void FromCulture_ACultureTheClientOffers_IsNamedInItsOwnLanguage(string tag, string expected)
    {
        // Arrange
        var culture = CultureInfo.GetCultureInfo(tag);

        // Act
        var language = AppLanguage.FromCulture(culture);

        // Assert
        Assert.Equal(tag, language.Tag);
        Assert.Equal(expected, language.Name);
    }

    /// <summary>The tag is the identity, which is what lets the picker match a selection against the offered list.</summary>
    [Fact]
    public void FromCulture_TheSameCultureTwice_IsTheSameOffer()
    {
        // Act
        var first = AppLanguage.FromCulture(CultureInfo.GetCultureInfo("pl"));
        var second = AppLanguage.FromCulture(CultureInfo.GetCultureInfo("pl"));

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void FromCulture_NoCulture_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => AppLanguage.FromCulture(null!));
    }
}
