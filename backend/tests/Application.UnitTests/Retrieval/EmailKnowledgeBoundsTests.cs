// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval;

/// <summary>Covers the ceiling on what one retrieval may hand a model.</summary>
public sealed class EmailKnowledgeBoundsTests
{
    /// <summary>
    /// A run reaching fewer messages per lookup than one search window holds answers worse than the search it was meant
    /// to spare the caller, and does so on exactly the questions a search already handles. Matching the window is the
    /// whole reason the default is what it is, so it is asserted against that constant rather than against a literal.
    /// </summary>
    [Fact]
    public void Default_TheLookupCount_MatchesTheWindowASearchReturns()
    {
        // Act
        var maximumPassages = EmailKnowledgeBounds.Default.MaximumPassages;

        // Assert
        Assert.Equal(EmailSearchResultLimit.DefaultValue, maximumPassages);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(EmailSearchResultLimit.MaximumValue)]
    public void Create_APassageCountInsideWhatOneSearchRanks_IsAccepted(int maximumPassages)
    {
        // Arrange, Act
        var bounds = EmailKnowledgeBounds.Create(maximumPassages, maximumCharactersPerPassage: 100);

        // Assert
        Assert.Equal(maximumPassages, bounds.MaximumPassages);
    }

    /// <summary>A retrieval is answered from a search window, so a bound beyond one states something no run could reach.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(EmailSearchResultLimit.MaximumValue + 1)]
    public void Create_APassageCountNoSearchCouldFill_IsRefused(int maximumPassages)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EmailKnowledgeBounds.Create(maximumPassages, maximumCharactersPerPassage: 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_APassageSizeThatCarriesNothing_IsRefused(int maximumCharactersPerPassage)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EmailKnowledgeBounds.Create(maximumPassages: 4, maximumCharactersPerPassage));
    }

    [Fact]
    public void ToString_ABound_NamesBothHalvesOfIt()
    {
        // Arrange
        var bounds = EmailKnowledgeBounds.Create(maximumPassages: 4, maximumCharactersPerPassage: 900);

        // Act
        var described = bounds.ToString();

        // Assert
        Assert.Equal("4 passages of at most 900 characters", described);
    }
}
