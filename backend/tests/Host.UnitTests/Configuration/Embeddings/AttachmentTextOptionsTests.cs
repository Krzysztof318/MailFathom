// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Host.Configuration.Embeddings;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Embeddings;

/// <summary>Covers what startup refuses about the attachment-extraction block, and what the block maps onto.</summary>
public sealed class AttachmentTextOptionsTests
{
    /// <summary>Writing nothing is a deployment reading every format MailFathom parses, under the ceilings it ships with.</summary>
    [Fact]
    public void ToExtractionOptions_ABlockNobodyWrote_ReadsEveryFormatUnderTheShippedCeilings()
    {
        // Arrange
        var settings = new AttachmentTextOptions();

        // Act
        var bounds = settings.ToExtractionOptions();

        // Assert
        Assert.Equal(AttachmentDocumentFormats.Extracted, bounds.Formats);
        Assert.Equal(AttachmentTextExtractionOptions.DefaultMaxInputOctets, bounds.MaxInputOctets);
        Assert.Equal(AttachmentTextExtractionOptions.DefaultMaxDecompressedOctets, bounds.MaxDecompressedOctets);
        Assert.Equal(AttachmentTextExtractionOptions.DefaultTimeout, bounds.Timeout);
    }

    /// <summary>Naming one format narrows to exactly that one, which is what a deployment reducing its surface asks for.</summary>
    [Fact]
    public void ToExtractionOptions_ABlockNamingOneFormat_NarrowsToExactlyThatFormat()
    {
        // Arrange
        var settings = new AttachmentTextOptions();
        settings.Formats.Add(AttachmentDocumentFormat.Pdf);

        // Act
        var bounds = settings.ToExtractionOptions();

        // Assert
        Assert.Equal([AttachmentDocumentFormat.Pdf], bounds.Formats);
    }

    /// <summary>Every ceiling an operator writes is the one the port applies, or the block would be a set of keys nobody reads.</summary>
    [Fact]
    public void ToExtractionOptions_ABlockWritingEveryCeiling_CarriesEachOfThemOntoThePort()
    {
        // Arrange
        var settings = new AttachmentTextOptions
        {
            MaxInputOctets = 2048,
            MaxExtractedTextCharacters = 3000,
            MaxDecompressedOctets = 4096,
            MaxDecompressionRatio = 7,
            MaxContainerParts = 11,
            MaxElementDepth = 13,
            Timeout = TimeSpan.FromSeconds(17),
        };

        // Act
        var bounds = settings.ToExtractionOptions();

        // Assert
        Assert.Equal(2048, bounds.MaxInputOctets);
        Assert.Equal(3000, bounds.MaxExtractedTextCharacters);
        Assert.Equal(4096, bounds.MaxDecompressedOctets);
        Assert.Equal(7, bounds.MaxDecompressionRatio);
        Assert.Equal(11, bounds.MaxContainerParts);
        Assert.Equal(13, bounds.MaxElementDepth);
        Assert.Equal(TimeSpan.FromSeconds(17), bounds.Timeout);
    }

    /// <summary>An unbounded extraction would hold a parser open over a sender's bytes for as long as it kept working.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Validate_ATimeoutThatCouldNotBoundAnything_IsRefused(int seconds)
    {
        // Arrange
        var settings = new AttachmentTextOptions { Timeout = TimeSpan.FromSeconds(seconds) };

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(AttachmentTextOptions.Timeout)));
    }

    /// <summary>Naming a format nothing reads would read as a deployment that extracts it, which is the misreading this refuses.</summary>
    [Theory]
    [InlineData(AttachmentDocumentFormat.LegacyWord)]
    [InlineData(AttachmentDocumentFormat.LegacySpreadsheet)]
    [InlineData(AttachmentDocumentFormat.LegacyPresentation)]
    public void Validate_AFormatMailFathomRecognizesAndDoesNotRead_IsRefused(AttachmentDocumentFormat format)
    {
        // Arrange
        var settings = new AttachmentTextOptions();
        settings.Formats.Add(format);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(AttachmentTextOptions.Formats)));
    }

    /// <summary>The block is validated whether or not a chain was declared, because the port applies it either way.</summary>
    [Fact]
    public void Validate_AnEmbeddingSectionWithNoChainAndABrokenAttachmentBlock_IsStillRefused()
    {
        // Arrange
        var settings = new EmbeddingOptions();
        settings.AttachmentText.Timeout = TimeSpan.Zero;

        // Act
        var errors = settings.Validate(new ValidationContext(settings)).ToList();

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(AttachmentTextOptions.Timeout)));
    }

    private static IReadOnlyList<ValidationResult> Validate(AttachmentTextOptions settings) =>
        [.. settings.Validate(new ValidationContext(settings))];
}
