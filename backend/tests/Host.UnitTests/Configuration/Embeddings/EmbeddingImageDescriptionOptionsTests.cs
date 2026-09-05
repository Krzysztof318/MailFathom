// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Embeddings;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Embeddings;

/// <summary>Covers the grid ceiling's own rule, which is read while the host composes rather than under the options pipeline.</summary>
/// <remarks>
/// The block is nested inside the embedding section, and <c>ValidateDataAnnotations</c> validates the bound root's own
/// properties without descending into a child object — so an attribute here would read as a bound and enforce nothing.
/// These tests are what makes the range a rule rather than a sentence in the documentation.
/// </remarks>
public sealed class EmbeddingImageDescriptionOptionsTests
{
    [Fact]
    public void FindDeclarationErrors_TheDefaultBlock_ReportsNothing()
    {
        // Act
        var errors = new EmbeddingImageDescriptionOptions().FindDeclarationErrors();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Both ends of the documented range are enforced, and the upper one is what keeps a mistyped ceiling from admitting whatever a hostile header declares.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(EmbeddingImageDescriptionOptions.GreatestMaxPixels + 1)]
    public void FindDeclarationErrors_AGridCeilingOutsideTheRange_NamesTheKeyAnOperatorEdits(long maxPixels)
    {
        // Arrange
        var settings = new EmbeddingImageDescriptionOptions { MaxPixels = maxPixels };

        // Act
        var errors = settings.FindDeclarationErrors();

        // Assert
        Assert.Contains(
            errors,
            error => error.StartsWith("Embeddings:ImageDescription:MaxPixels", StringComparison.Ordinal));
    }

    /// <summary>The rule is about the value rather than about the switch, so a ceiling nothing will read yet is still refused.</summary>
    [Fact]
    public void FindDeclarationErrors_AGridCeilingOutsideTheRangeWithDescriptionOff_IsStillRefused()
    {
        // Arrange
        var settings = new EmbeddingImageDescriptionOptions { Enabled = false, MaxPixels = 0 };

        // Act
        var errors = settings.FindDeclarationErrors();

        // Assert
        Assert.NotEmpty(errors);
    }

    /// <summary>Both ends of the range are admitted, so the rule bounds the declaration rather than narrowing it.</summary>
    [Theory]
    [InlineData(1L)]
    [InlineData(EmbeddingImageDescriptionOptions.GreatestMaxPixels)]
    public void FindDeclarationErrors_AGridCeilingAtEitherEndOfTheRange_ReportsNothing(long maxPixels)
    {
        // Arrange
        var settings = new EmbeddingImageDescriptionOptions { MaxPixels = maxPixels };

        // Act
        var errors = settings.FindDeclarationErrors();

        // Assert
        Assert.Empty(errors);
    }
}
