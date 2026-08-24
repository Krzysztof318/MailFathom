// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Xml.Linq;

namespace MailFathom.Client.UnitTests.Styles;

/// <summary>
/// Reads <c>Styles/ColorPaletteOverride.xaml</c> and holds it to what a reader depends on.
/// </summary>
/// <remarks>
/// <para>
/// The palette is the one place a colour value is written in this application, and a screen written against it can
/// only be as legible as the pairs it resolves. Contrast is therefore measured here rather than asserted in a pull
/// request: a value edited to look better and a role repointed at a different tone both arrive as a failing test
/// naming the pair, which is the only reading of it that survives the next change.
/// </para>
/// <para>
/// The file is read rather than the running application's resources, because parsing XAML into a resource dictionary
/// needs a visual tree and this suite deliberately has none — <c>frontend/tests/AGENTS.md</c> states which cases that
/// rules out. What is asserted here is the source of every brush, which is what the question is about.
/// </para>
/// </remarks>
public sealed class ColorPaletteOverrideTests
{
    private const double TextContrast = 4.5;
    private const double NonTextContrast = 3.0;

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Themes = ReadPalette();

    /// <summary>
    /// The pairs a reader or a control actually depends on, with the ratio WCAG AA asks of each. Text takes 4.5:1; an
    /// outline and a control state are non-text contrast and take 3:1.
    /// </summary>
    public static TheoryData<string, string, string, double> ContrastObligations()
    {
        var obligations = new TheoryData<string, string, string, double>();
        (string Foreground, string Background, double Minimum)[] pairs =
        [
            ("OnBackgroundColor", "BackgroundColor", TextContrast),
            ("OnSurfaceColor", "SurfaceColor", TextContrast),
            ("OnSurfaceVariantColor", "SurfaceColor", TextContrast),
            ("OnSurfaceVariantColor", "SurfaceVariantColor", TextContrast),
            ("OnPrimaryColor", "PrimaryColor", TextContrast),
            ("OnPrimaryContainerColor", "PrimaryContainerColor", TextContrast),
            ("OnSecondaryColor", "SecondaryColor", TextContrast),
            ("OnSecondaryContainerColor", "SecondaryContainerColor", TextContrast),
            ("OnTertiaryColor", "TertiaryColor", TextContrast),
            ("OnTertiaryContainerColor", "TertiaryContainerColor", TextContrast),
            ("OnErrorColor", "ErrorColor", TextContrast),
            ("OnErrorContainerColor", "ErrorContainerColor", TextContrast),
            ("OnSurfaceInverseColor", "SurfaceInverseColor", TextContrast),
            ("OutlineColor", "SurfaceColor", NonTextContrast),
            ("OutlineColor", "BackgroundColor", NonTextContrast),
            ("PrimaryColor", "SurfaceColor", NonTextContrast),
            ("PrimaryColor", "BackgroundColor", NonTextContrast),
            ("ErrorColor", "SurfaceColor", NonTextContrast),
            ("PrimaryInverseColor", "SurfaceInverseColor", NonTextContrast),
        ];

        foreach (var theme in Themes.Keys)
        {
            foreach (var (foreground, background, minimum) in pairs)
            {
                obligations.Add(theme, foreground, background, minimum);
            }
        }

        return obligations;
    }

    /// <summary>A key present in one theme and missing from the other is a role that stops resolving when the theme flips.</summary>
    [Fact]
    public void ThemeDictionaries_TheTwoThemes_DeclareTheSameRoles()
    {
        // Arrange
        var light = Themes["Light"].Keys.Order(StringComparer.Ordinal);
        var dark = Themes["Dark"].Keys.Order(StringComparer.Ordinal);

        // Act & Assert
        Assert.Equal(light, dark);
    }

    /// <summary>Every value has to be a colour a XAML parser accepts, in the one notation this file writes them in.</summary>
    [Fact]
    public void ThemeDictionaries_EveryDeclaredValue_IsAColourLiteral()
    {
        // Arrange
        var values = Themes.SelectMany(theme => theme.Value.Select(entry => (theme.Key, entry.Key, entry.Value)));

        // Act & Assert
        foreach (var (theme, key, value) in values)
        {
            Assert.True(
                TryParseColour(value, out _),
                $"{theme}/{key} is '{value}', which is neither #RRGGBB nor #AARRGGBB.");
        }
    }

    /// <summary>
    /// The Uno template's violet is what this palette replaced, so its two signature values reappearing means the file
    /// was regenerated from the template rather than edited.
    /// </summary>
    [Theory]
    [InlineData("#5946D2")]
    [InlineData("#C7BFFF")]
    public void ThemeDictionaries_TheTemplatePalette_SurvivesInNeitherTheme(string templateValue)
    {
        // Arrange
        var values = Themes.SelectMany(theme => theme.Value.Values);

        // Act & Assert
        Assert.DoesNotContain(templateValue, values, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Each pair a reader depends on, measured against the ratio WCAG AA asks of it.</summary>
    [Theory]
    [MemberData(nameof(ContrastObligations))]
    public void ThemeDictionaries_APairAReaderDependsOn_MeetsWcagAa(
        string theme,
        string foreground,
        string background,
        double minimum)
    {
        // Arrange
        var values = Themes[theme];
        Assert.True(values.ContainsKey(foreground), $"{theme} declares no {foreground}.");
        Assert.True(values.ContainsKey(background), $"{theme} declares no {background}.");

        // Act
        var ratio = ContrastRatio(values[foreground], values[background]);

        // Assert
        Assert.True(
            ratio >= minimum,
            FormattableString.Invariant(
                $"{theme}: {foreground} {values[foreground]} on {background} {values[background]} is {ratio:F2}:1, below the {minimum:F1}:1 WCAG AA asks."));
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> ReadPalette()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Styles", "ColorPaletteOverride.xaml");
        var document = XDocument.Load(path);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        return document
            .Descendants(presentation + "ResourceDictionary")
            .Where(dictionary => dictionary.Attribute(xaml + "Key") is not null)
            .ToDictionary(
                dictionary => dictionary.Attribute(xaml + "Key")!.Value,
                dictionary => (IReadOnlyDictionary<string, string>)dictionary
                    .Elements(presentation + "Color")
                    .ToDictionary(
                        colour => colour.Attribute(xaml + "Key")!.Value,
                        colour => colour.Value.Trim(),
                        StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static bool TryParseColour(string value, out (byte Red, byte Green, byte Blue) colour)
    {
        colour = default;
        if (value.Length is not (7 or 9) || value[0] is not '#')
        {
            return false;
        }

        // An eight-digit literal is #AARRGGBB, so the alpha is dropped: contrast is measured against what a reader
        // sees, and every colour behind a translucent one here is opaque.
        var digits = value.Length is 9 ? value[3..] : value[1..];
        if (!int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            return false;
        }

        colour = ((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
        return true;
    }

    private static double ContrastRatio(string foreground, string background)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string value)
    {
        Assert.True(TryParseColour(value, out var colour), $"'{value}' is not a colour literal.");
        return (0.2126 * Channel(colour.Red)) + (0.7152 * Channel(colour.Green)) + (0.0722 * Channel(colour.Blue));

        static double Channel(byte component)
        {
            var normalized = component / 255.0;
            return normalized <= 0.03928 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }
    }
}
