// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Planning;

/// <summary>Covers the catalogue of question kinds and the block each one opens a result with.</summary>
public sealed class DiscoveryIntentTests
{
    /// <summary>The whole of the product's mapping, asserted as the table it is rather than one case at a time.</summary>
    [Theory]
    [InlineData(DiscoveryIntent.FindFactIdentity, PresentationBlockType.AnswerIdentity)]
    [InlineData(DiscoveryIntent.TrackChangeIdentity, PresentationBlockType.TimelineIdentity)]
    [InlineData(DiscoveryIntent.CompareTermsIdentity, PresentationBlockType.FactTableIdentity)]
    [InlineData(DiscoveryIntent.FindDocumentsIdentity, PresentationBlockType.AttachmentGalleryIdentity)]
    [InlineData(DiscoveryIntent.UnclassifiedIdentity, PresentationBlockType.AnswerIdentity)]
    public void OpensWith_EachKindOfQuestion_IsTheBlockTheProductNames(string intentIdentity, string blockIdentity)
    {
        // Arrange
        Assert.True(DiscoveryIntent.TryParse(intentIdentity, out var intent));

        // Act
        var opensWith = intent.OpensWith;

        // Assert
        Assert.Equal(blockIdentity, opensWith.Identity);
    }

    /// <summary>Every member is reachable by the name it is written under, so the catalogue and the parser cannot drift.</summary>
    [Fact]
    public void TryParse_EveryMemberOfTheCatalogue_ReadsBackAsItself()
    {
        // Act
        var readBack = DiscoveryIntent.All
            .Select(intent => DiscoveryIntent.TryParse(intent.Identity, out var parsed) ? parsed : default)
            .ToArray();

        // Assert
        Assert.Equal(DiscoveryIntent.All, readBack);
    }

    /// <summary>A model answers in whatever case it likes, and the name is the same name however it wrote it.</summary>
    [Theory]
    [InlineData("FINDFACT")]
    [InlineData("  findFact  ")]
    public void TryParse_TheNameWrittenDifferently_ReadsTheSameIntent(string identity)
    {
        // Act
        var read = DiscoveryIntent.TryParse(identity, out var intent);

        // Assert
        Assert.True(read);
        Assert.Equal(DiscoveryIntent.FindFact, intent);
    }

    /// <summary>A name nobody defined is an ordinary answer the reading falls back on, never a value that half exists.</summary>
    [Theory]
    [InlineData("summarise")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_ANameThisCatalogueDoesNotHold_NamesNothing(string? identity)
    {
        // Act
        var read = DiscoveryIntent.TryParse(identity, out var intent);

        // Assert
        Assert.False(read);
        Assert.False(intent.IsSpecified);
    }

    [Fact]
    public void OpensWith_TheDefaultOfTheStruct_SaysItNamesNothing()
    {
        // Arrange
        var unspecified = default(DiscoveryIntent);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => unspecified.OpensWith);

        // Assert
        Assert.Contains("opens with no block", failure.Message, StringComparison.Ordinal);
    }
}
