// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Preferences;
using Xunit;

namespace MailFathom.Application.UnitTests.Preferences;

/// <summary>
/// Covers the theme choice as the published identity it is. The name is what a client sends, what a response answers
/// with, and what the stored row holds, so what these hold is that the set is exactly the three the client offers, that
/// a name outside it is refused rather than reconstructed, and that the unusable struct default reaches neither a
/// response nor a document.
/// </summary>
public sealed class ClientThemeChoiceTests
{
    /// <summary>The names the client's own <c>themeChoice.ts</c> offers, stated here so a rename on either side fails.</summary>
    private static readonly string[] PublishedNames = ["system", "light", "dark"];

    [Fact]
    public void All_ThePublishedSet_IsTheThreeChoicesTheClientOffers()
    {
        // Act
        var names = ClientThemeChoice.All.Select(choice => choice.Name);

        // Assert
        Assert.Equal(PublishedNames, names);
    }

    [Fact]
    public void All_ThePublishedSet_CarriesNoNameTwice()
    {
        // Act
        var distinct = ClientThemeChoice.All.Select(choice => choice.Name).Distinct(StringComparer.Ordinal);

        // Assert
        Assert.Equal(ClientThemeChoice.All.Count, distinct.Count());
    }

    [Fact]
    public void Unset_TheThemeAPersonWhoChoseNothingReads_FollowsTheMachine()
    {
        // Assert
        Assert.Equal(ClientThemeChoice.System, ClientPreferences.Unset.Theme);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("light")]
    [InlineData("dark")]
    public void TryParse_ANameThisBuildPublishes_ReportsTheChoiceItNames(string name)
    {
        // Act
        var parsed = ClientThemeChoice.TryParse(name, out var choice);

        // Assert
        Assert.True(parsed);
        Assert.Equal(name, choice.Name);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("solarized")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_ANameNothingPublishes_ReportsTheUnspecifiedDefault(string? name)
    {
        // Act
        var parsed = ClientThemeChoice.TryParse(name, out var choice);

        // Assert
        Assert.False(parsed);
        Assert.False(choice.IsSpecified);
    }

    [Fact]
    public void IsSpecified_TheStructDefault_ReportsThatItNamesNoChoice()
    {
        // Assert
        Assert.False(default(ClientThemeChoice).IsSpecified);
    }

    [Fact]
    public void Name_TheStructDefault_RefusesToAnswerForOne()
    {
        // Assert
        Assert.Throws<InvalidOperationException>(() => default(ClientThemeChoice).Name);
    }

    [Fact]
    public void ToString_TheStructDefault_ReadsAsUnspecifiedRatherThanAsEmptyText()
    {
        // Assert
        Assert.Equal("(unspecified)", default(ClientThemeChoice).ToString());
    }

    [Fact]
    public void ToString_APublishedChoice_ReadsAsItsName()
    {
        // Assert
        Assert.Equal("dark", ClientThemeChoice.Dark.ToString());
    }

    [Fact]
    public void Write_APublishedChoice_SerializesTheNameRatherThanAnOrdinal()
    {
        // Act
        var json = JsonSerializer.Serialize(ClientThemeChoice.Light);

        // Assert
        Assert.Equal("\"light\"", json);
    }

    [Fact]
    public void Read_ASerializedChoice_ReadsBackTheChoiceThatWasWritten()
    {
        // Act
        var read = JsonSerializer.Deserialize<ClientThemeChoice>(JsonSerializer.Serialize(ClientThemeChoice.Dark));

        // Assert
        Assert.Equal(ClientThemeChoice.Dark, read);
    }

    [Fact]
    public void Read_ANameNothingPublishes_RefusesRatherThanReconstructingOne()
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientThemeChoice>("\"solarized\""));
    }

    [Fact]
    public void Read_ATokenThatIsNotAName_Refuses()
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientThemeChoice>("7"));
    }

    [Fact]
    public void Write_TheStructDefault_RefusesRatherThanWritingAnEmptyValue()
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(ClientThemeChoice)));
    }

    [Fact]
    public void WriteAsPropertyName_APublishedChoice_KeysTheObjectByItsName()
    {
        // Arrange
        var keyed = new Dictionary<ClientThemeChoice, int> { [ClientThemeChoice.System] = 1 };

        // Act
        var json = JsonSerializer.Serialize(keyed);

        // Assert
        Assert.Equal("""{"system":1}""", json);
    }

    [Fact]
    public void ReadAsPropertyName_ASerializedKey_ReadsBackTheChoiceThatKeyedIt()
    {
        // Act
        var read = JsonSerializer.Deserialize<Dictionary<ClientThemeChoice, int>>("""{"light":1}""");

        // Assert
        Assert.Equal(ClientThemeChoice.Light, Assert.Single(read!).Key);
    }

    [Fact]
    public void ReadAsPropertyName_AKeyNothingPublishes_Refuses()
    {
        // Assert
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<Dictionary<ClientThemeChoice, int>>("""{"solarized":1}"""));
    }

    [Fact]
    public void WriteAsPropertyName_TheStructDefault_Refuses()
    {
        // Arrange
        var keyed = new Dictionary<ClientThemeChoice, int> { [default] = 1 };

        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(keyed));
    }
}
