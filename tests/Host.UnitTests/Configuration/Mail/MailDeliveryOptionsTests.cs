// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Domain.Delivery;
using MailFathom.Host.Configuration.Mail;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>
/// Covers how large a message this deployment is willing to compose: that the defaults are usable correspondence
/// bounds, and that a combination which could never compose anything is refused at startup.
/// </summary>
public sealed class MailDeliveryOptionsTests
{
    /// <summary>An operator who writes no section gets bounds that send ordinary correspondence and refuse a mailing list.</summary>
    [Fact]
    public void Defaults_UnconfiguredSection_AreUsableCorrespondenceBounds()
    {
        // Act
        var options = new MailDeliveryOptions();

        // Assert
        Assert.InRange(options.MaxRecipientCount, 1, OutgoingEmailRequest.MaximumRecipientCount);
        Assert.True(options.MaxAttachmentBytes < options.MaxMessageBytes);
        Assert.Empty(Validate(options));
    }

    /// <summary>
    /// A per-file bound above the whole-message bound describes a file that could never be sent, and the refusal an
    /// operator would meet is the one about the message rather than the one they configured.
    /// </summary>
    [Fact]
    public void Validate_AttachmentBoundAboveTheMessageBound_IsRefused()
    {
        // Arrange
        var options = new MailDeliveryOptions { MaxAttachmentBytes = 32 * 1024 * 1024, MaxMessageBytes = 1024 * 1024 };

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Contains(nameof(MailDeliveryOptions.MaxAttachmentBytes), result.MemberNames);
    }

    /// <summary>A deployment that attaches nothing is not judged against a bound it can never reach.</summary>
    [Fact]
    public void Validate_DeploymentAttachingNoFiles_IgnoresThePerFileBound()
    {
        // Arrange
        var options = new MailDeliveryOptions
        {
            MaxAttachmentCount = 0,
            MaxAttachmentBytes = 32 * 1024 * 1024,
            MaxMessageBytes = 1024 * 1024,
        };

        // Act and assert
        Assert.Empty(Validate(options));
    }

    /// <summary>More recipients than an outgoing record can hold would compose a message no send could be written for.</summary>
    [Fact]
    public void Validate_MoreRecipientsThanARecordHolds_IsRefused()
    {
        // Arrange
        var options = new MailDeliveryOptions
        {
            MaxRecipientCount = OutgoingEmailRequest.MaximumRecipientCount + 1,
        };

        // Act
        var results = ValidateWithDataAnnotations(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Contains(nameof(MailDeliveryOptions.MaxRecipientCount), result.MemberNames);
    }

    /// <summary>A body bound outside what the reference publishes is refused rather than composed against.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(10_000_001)]
    public void Validate_BodyLengthOutsideTheDocumentedRange_IsRefused(int maxBodyCharacters)
    {
        // Arrange
        var options = new MailDeliveryOptions { MaxBodyCharacters = maxBodyCharacters };

        // Act
        var results = ValidateWithDataAnnotations(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(MailDeliveryOptions.MaxBodyCharacters)));
    }

    /// <summary>Attaching nothing is a deployment's choice; attaching a negative number of files is not a bound.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_AttachmentCountOutsideTheDocumentedRange_IsRefused(int maxAttachmentCount)
    {
        // Arrange
        var options = new MailDeliveryOptions { MaxAttachmentCount = maxAttachmentCount };

        // Act
        var results = ValidateWithDataAnnotations(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(MailDeliveryOptions.MaxAttachmentCount)));
    }

    /// <summary>A per-file bound outside what the reference publishes is refused wherever the whole-message bound sits.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(104_857_601L)]
    public void Validate_AttachmentSizeOutsideTheDocumentedRange_IsRefused(long maxAttachmentBytes)
    {
        // Arrange
        var options = new MailDeliveryOptions
        {
            MaxAttachmentBytes = maxAttachmentBytes,
            MaxMessageBytes = 200L * 1024 * 1024,
        };

        // Act
        var results = ValidateWithDataAnnotations(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(MailDeliveryOptions.MaxAttachmentBytes)));
    }

    /// <summary>A whole-message bound outside what the reference publishes is refused rather than composed against.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(209_715_201L)]
    public void Validate_MessageSizeOutsideTheDocumentedRange_IsRefused(long maxMessageBytes)
    {
        // Arrange
        var options = new MailDeliveryOptions { MaxAttachmentCount = 0, MaxMessageBytes = maxMessageBytes };

        // Act
        var results = ValidateWithDataAnnotations(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(MailDeliveryOptions.MaxMessageBytes)));
    }

    private static IReadOnlyList<ValidationResult> Validate(MailDeliveryOptions options) =>
        [.. options.Validate(new ValidationContext(options))];

    private static List<ValidationResult> ValidateWithDataAnnotations(MailDeliveryOptions options)
    {
        List<ValidationResult> results = [];
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }
}
