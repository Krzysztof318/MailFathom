// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.Emails.AttachmentText;
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

    /// <summary>A format this deployment reads is one an operator may name, which is what makes a narrowed list a choice.</summary>
    [Theory]
    [InlineData(AttachmentDocumentFormat.PlainText)]
    [InlineData(AttachmentDocumentFormat.Markdown)]
    [InlineData(AttachmentDocumentFormat.Csv)]
    public void Validate_AFormatMailFathomReads_IsAccepted(AttachmentDocumentFormat format)
    {
        // Arrange
        var settings = new AttachmentTextOptions();
        settings.Formats.Add(format);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.DoesNotContain(errors, error => error.MemberNames.Contains(nameof(AttachmentTextOptions.Formats)));
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
    [InlineData(nameof(AttachmentTextOptions.MaxAttachmentsPerEmail), 0L)]
    [InlineData(nameof(AttachmentTextOptions.MaxAttachmentsPerEmail), 10_000L)]
    [InlineData(nameof(AttachmentTextOptions.MaxInputOctetsPerEmail), 512L)]
    [InlineData(nameof(AttachmentTextOptions.MaxInputOctetsPerEmail), 16L * 1024 * 1024 * 1024)]
    [InlineData(nameof(AttachmentTextOptions.MaxInputOctetsPerAccountRun), 512L)]
    [InlineData(nameof(AttachmentTextOptions.MaxInputOctetsPerAccountRun), 4096L * 1024 * 1024 * 1024)]
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

    /// <summary>
    /// The switch is what an operator turns on, so what it maps onto has to be the switch and the three numbers the
    /// pass spends — a block bound correctly and then read as disabled would leave a deployment configured and silent.
    /// </summary>
    [Fact]
    public void ToAttachmentTextBounds_ABlockWritingTheMessageAndRunCeilings_CarriesEachOfThemOntoThePass()
    {
        // Arrange
        var settings = new AttachmentTextOptions
        {
            Enabled = true,
            MaxAttachmentsPerEmail = 5,
            MaxInputOctetsPerEmail = 32L * 1024 * 1024,
            MaxInputOctetsPerAccountRun = 512L * 1024 * 1024,
        };

        // Act
        var bounds = settings.ToAttachmentTextBounds();

        // Assert
        Assert.True(bounds.IsEnabled);
        Assert.Equal(5, bounds.MaxAttachmentsPerEmail);
        Assert.Equal(32L * 1024 * 1024, bounds.MaxInputOctetsPerEmail);
        Assert.Equal(512L * 1024 * 1024, bounds.MaxInputOctetsPerAccountRun);
    }

    /// <summary>A deployment that wrote nothing reads no attachment, which is what ADR 0029 decides for an upgrade.</summary>
    [Fact]
    public void ToAttachmentTextBounds_ABlockNobodyWrote_ReadsNoAttachmentAtAll()
    {
        // Act
        var bounds = new AttachmentTextOptions().ToAttachmentTextBounds();

        // Assert
        Assert.False(bounds.IsEnabled);
    }

    /// <summary>
    /// The three octet ceilings are nested rather than independent. An attachment larger than what its message may
    /// spend, or a message larger than what its run may, is refused by every run for ever — a deployment that looks
    /// configured and reads nothing — so the order is refused at startup rather than found as mail nobody can search.
    /// </summary>
    [Theory]
    [InlineData(nameof(AttachmentTextOptions.MaxInputOctetsPerEmail))]
    [InlineData(nameof(AttachmentTextOptions.MaxInputOctetsPerAccountRun))]
    public void Validate_ACeilingSmallerThanTheOneItHasToContain_IsRefused(string key)
    {
        // Arrange
        var settings = new AttachmentTextOptions
        {
            MaxInputOctets = 16L * 1024 * 1024,
            MaxInputOctetsPerEmail = key == nameof(AttachmentTextOptions.MaxInputOctetsPerEmail)
                ? 8L * 1024 * 1024
                : 32L * 1024 * 1024,
            MaxInputOctetsPerAccountRun = key == nameof(AttachmentTextOptions.MaxInputOctetsPerAccountRun)
                ? 16L * 1024 * 1024
                : 64L * 1024 * 1024,
        };

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(key));
    }

    /// <summary>The three written equal is the tightest arrangement that still reads, so it is accepted.</summary>
    [Fact]
    public void Validate_TheThreeOctetCeilingsWrittenEqual_IsAccepted()
    {
        // Arrange
        var settings = new AttachmentTextOptions
        {
            MaxInputOctets = 16L * 1024 * 1024,
            MaxInputOctetsPerEmail = 16L * 1024 * 1024,
            MaxInputOctetsPerAccountRun = 16L * 1024 * 1024,
        };

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A negative period ceiling is refused, because a period admitting less than nothing is not a budget.</summary>
    [Theory]
    [InlineData(nameof(AttachmentTextOptions.MaxInputOctetsPerPeriod))]
    [InlineData(nameof(AttachmentTextOptions.MaxInputOctetsPerPeriodPerOwner))]
    public void Validate_ANegativePeriodCeiling_IsRefused(string key)
    {
        // Arrange
        AttachmentTextOptions settings = new();

        WritePeriodCeiling(settings, key, -1);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(key));
    }

    /// <summary>
    /// A period ceiling below what one message may read is a deployment that could never read a message at all, which
    /// is a configuration to refuse at start rather than a mailbox that quietly never advances.
    /// </summary>
    [Theory]
    [InlineData(nameof(AttachmentTextOptions.MaxInputOctetsPerPeriod))]
    [InlineData(nameof(AttachmentTextOptions.MaxInputOctetsPerPeriodPerOwner))]
    public void Validate_APeriodCeilingBelowOneMessage_IsRefused(string key)
    {
        // Arrange
        AttachmentTextOptions settings = new();

        WritePeriodCeiling(settings, key, EmailAttachmentTextBounds.DefaultMaxInputOctetsPerEmail - 1);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(key));
    }

    /// <summary>A per-owner ceiling above the deployment's own bounds nothing, so it is refused rather than ignored.</summary>
    [Fact]
    public void Validate_APerOwnerCeilingAboveTheDeploymentCeiling_IsRefused()
    {
        // Arrange
        AttachmentTextOptions settings = new()
        {
            MaxInputOctetsPerPeriod = 128L * 1024 * 1024,
            MaxInputOctetsPerPeriodPerOwner = 256L * 1024 * 1024,
        };

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(
            errors,
            error => error.MemberNames.Contains(nameof(AttachmentTextOptions.MaxInputOctetsPerPeriodPerOwner)));
    }

    /// <summary>Zero declares no ceiling at all, which is the default a deployment starts on and is never refused.</summary>
    [Fact]
    public void Validate_PeriodCeilingsLeftAtZero_DeclareNoCeilingAndAreAccepted()
    {
        // Arrange
        AttachmentTextOptions settings = new();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Equal(0, settings.MaxInputOctetsPerPeriod);
        Assert.Equal(0, settings.MaxInputOctetsPerPeriodPerOwner);
        Assert.DoesNotContain(
            errors,
            error => error.MemberNames.Contains(nameof(AttachmentTextOptions.MaxInputOctetsPerPeriod))
                || error.MemberNames.Contains(nameof(AttachmentTextOptions.MaxInputOctetsPerPeriodPerOwner)));
    }

    private static void WritePeriodCeiling(AttachmentTextOptions settings, string key, long value)
    {
        if (key == nameof(AttachmentTextOptions.MaxInputOctetsPerPeriod))
        {
            settings.MaxInputOctetsPerPeriod = value;

            return;
        }

        settings.MaxInputOctetsPerPeriodPerOwner = value;
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

            case nameof(AttachmentTextOptions.MaxAttachmentsPerEmail):
                settings.MaxAttachmentsPerEmail = (int)value;
                break;

            // The two per-scope octet ceilings are written with everything beneath them, so the value under test is the
            // only thing the ordering check could complain about — and it cannot, because writing them down satisfies
            // it. Without that, a value below the floor also inverts the order, the order error names the same member
            // the range error would, and deleting the floor from the validator leaves the assertion green.
            case nameof(AttachmentTextOptions.MaxInputOctetsPerEmail):
                settings.MaxInputOctets = Math.Min(settings.MaxInputOctets, value);
                settings.MaxInputOctetsPerEmail = value;
                break;

            case nameof(AttachmentTextOptions.MaxInputOctetsPerAccountRun):
                settings.MaxInputOctets = Math.Min(settings.MaxInputOctets, value);
                settings.MaxInputOctetsPerEmail = Math.Min(settings.MaxInputOctetsPerEmail, value);
                settings.MaxInputOctetsPerAccountRun = value;
                break;

            default:
                settings.MaxElementDepth = (int)value;
                break;
        }
    }
}
