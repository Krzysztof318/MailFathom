// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Mail;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers the bounds the extraction backfill walks stored mail under, and what the sensitive-content section asks of it.</summary>
public sealed class MailExtractionBackfillOptionsTests
{
    /// <summary>Both keys one walk is bounded by reach the bounds the walk stops at.</summary>
    [Fact]
    public void ToBackfillOptions_ConfiguredSection_CarriesBothKeysTheWalkStopsAt()
    {
        // Arrange
        var settings = new MailExtractionBackfillOptions { BatchSize = 12, MaxBatchesPerRun = 5 };

        // Act
        var bounds = settings.ToBackfillOptions(rebuildsStaleDerivedData: false);

        // Assert
        Assert.Equal(12, bounds.BatchSize);
        Assert.Equal(5, bounds.MaxBatchesPerRun);
    }

    /// <summary>Whether a stale derived copy is rebuilt is the other section's answer, so it arrives as an argument rather than as a key here.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToBackfillOptions_RebuildDecidedElsewhere_CarriesWhatThatSectionAnswered(bool rebuildsStaleDerivedData)
    {
        // Arrange
        var settings = new MailExtractionBackfillOptions();

        // Act
        var bounds = settings.ToBackfillOptions(rebuildsStaleDerivedData);

        // Assert
        Assert.Equal(rebuildsStaleDerivedData, bounds.RebuildsStaleDerivedData);
    }
}
