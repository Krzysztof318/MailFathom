// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation;

/// <summary>Covers how a theme becomes the offer a person picks from.</summary>
public sealed class AppThemeOptionTests
{
    /// <summary>The words come from the string table, and the enum stays what the theme service is handed.</summary>
    [Fact]
    public void Named_AThemeTheTableHolds_CarriesItsWordsAndItsTheme()
    {
        // Arrange
        var localizer = new StubStringLocalizer(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AppThemeOption.ResourceKeyFor(AppTheme.Dark)] = "Ciemny",
        });

        // Act
        var option = AppThemeOption.Named(AppTheme.Dark, localizer);

        // Assert
        Assert.Equal(AppTheme.Dark, option.Theme);
        Assert.Equal("Ciemny", option.Name);
    }

    [Fact]
    public void Named_NoStringTable_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => AppThemeOption.Named(AppTheme.Light, null!));
    }
}
