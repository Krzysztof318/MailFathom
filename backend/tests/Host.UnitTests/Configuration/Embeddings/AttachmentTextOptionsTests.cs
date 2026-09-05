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

    /// <summary>
    /// Every ceiling is checked here rather than by an attribute, because this block is a complex property the options
    /// framework's own validator does not descend into — so an attribute would publish a constraint nothing applied.
    /// </summary>
    [Theory]
    [InlineData(nameof(AttachmentTextOptions.MaxInputOctets), 1023L)]
    [InlineData(nameof(AttachmentTextOptions.MaxInputOctets), 1024L * 1024 * 1024)]
    [InlineData(nameof(AttachmentTextOptions.MaxExtractedTextCharacters), 999L)]
    [InlineData(nameof(AttachmentTextOptions.MaxExtractedTextCharacters), 20_000_000L)]
    [InlineData(nameof(AttachmentTextOptions.MaxDecompressedOctets), 512L)]
    [InlineData(nameof(AttachmentTextOptions.MaxDecompressedOctets), 999_999_999_999L)]
    [InlineData(nameof(AttachmentTextOptions.MaxDecompressionRatio), 0L)]
    [InlineData(nameof(AttachmentTextOptions.MaxDecompressionRatio), 20_000L)]
    [InlineData(nameof(AttachmentTextOptions.MaxContainerParts), 0L)]
    [InlineData(nameof(AttachmentTextOptions.MaxContainerParts), 200_000L)]
    [InlineData(nameof(AttachmentTextOptions.MaxElementDepth), 1L)]
    [InlineData(nameof(AttachmentTextOptions.MaxElementDepth), 20_000L)]
    public void Validate_ACeilingOutsideTheRangeItIsMeaningfulIn_IsRefused(string key, long value)
    {
        // Arrange
        var settings = new AttachmentTextOptions();

        WriteCeiling(settings, key, value);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(key));
    }

    /// <summary>A block written entirely at its extremes is still a block a deployment may run, or the range is wrong.</summary>
    [Fact]
    public void Validate_ABlockWrittenAtEveryRangeEnd_IsAccepted()
    {
        // Arrange
        var settings = new AttachmentTextOptions
        {
            MaxInputOctets = 1024,
            MaxExtractedTextCharacters = 1_000,
            MaxDecompressedOctets = 2L * 1024 * 1024 * 1024,
            MaxDecompressionRatio = 10_000,
            MaxContainerParts = 1,
            MaxElementDepth = 2,
            Timeout = TimeSpan.FromHours(1),
        };

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
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

    /// <summary>
    /// A <c>TimeSpan</c> bound from a bare number is that many days, which is the ordinary way an operator meaning
    /// thirty seconds writes thirty — and a deadline built from one past the platform timer's own maximum throws out of
    /// a port whose whole contract is that it answers instead.
    /// </summary>
    [Fact]
    public void Validate_ATimeoutPastWhatADeadlineCanBeBuiltFrom_IsRefused()
    {
        // Arrange
        var settings = new AttachmentTextOptions { Timeout = TimeSpan.FromDays(30) };

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(AttachmentTextOptions.Timeout)));
    }

    private static IReadOnlyList<ValidationResult> Validate(AttachmentTextOptions settings) =>
        [.. settings.Validate(new ValidationContext(settings))];

    private static void WriteCeiling(AttachmentTextOptions settings, string key, long value)
    {
        switch (key)
        {
            case nameof(AttachmentTextOptions.MaxInputOctets):
                settings.MaxInputOctets = value;
                break;

            case nameof(AttachmentTextOptions.MaxExtractedTextCharacters):
                settings.MaxExtractedTextCharacters = (int)value;
                break;

            case nameof(AttachmentTextOptions.MaxDecompressedOctets):
                settings.MaxDecompressedOctets = value;
                break;

            case nameof(AttachmentTextOptions.MaxDecompressionRatio):
                settings.MaxDecompressionRatio = (int)value;
                break;

            case nameof(AttachmentTextOptions.MaxContainerParts):
                settings.MaxContainerParts = (int)value;
                break;

            default:
                settings.MaxElementDepth = (int)value;
                break;
        }
    }
}
