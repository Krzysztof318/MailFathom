// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Persistence;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Persistence;

/// <summary>Covers the four bounds a deployment may configure on what one read of message content returns.</summary>
public sealed class EmailContentOptionsTests
{
    /// <summary>A deployment that configures nothing reads mail under bounds rather than under none.</summary>
    [Fact]
    public void Validate_UnconfiguredDeployment_AcceptsTheDefaultBounds()
    {
        // Arrange
        var options = new EmailContentOptions();

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
        Assert.Equal(100_000, options.MaxBodyCharacters);
        Assert.Equal(200_000, options.MaxCharactersPerRead);
    }

    /// <summary>A bound outside the range fails startup, because neither end of it produces a usable response.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    [InlineData(1_000_001)]
    public void Validate_BodyBoundOutsideTheAcceptedRange_FailsStartup(int maxBodyCharacters)
    {
        // Arrange
        var options = new EmailContentOptions
        {
            MaxBodyCharacters = maxBodyCharacters,
            MaxCharactersPerRead = 2_000_000,
        };

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal([nameof(EmailContentOptions.MaxBodyCharacters)], result.MemberNames);
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(1_000_000)]
    public void Validate_BodyBoundAtEitherEndOfTheRange_IsAccepted(int maxBodyCharacters)
    {
        // Arrange
        var options = new EmailContentOptions
        {
            MaxBodyCharacters = maxBodyCharacters,
            MaxCharactersPerRead = 2 * maxBodyCharacters,
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    /// <summary>The budget has a range of its own, because it bounds a call rather than an email.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_999)]
    [InlineData(2_000_001)]
    public void Validate_ReadBudgetOutsideTheAcceptedRange_FailsStartup(int maxCharactersPerRead)
    {
        // Arrange
        var options = new EmailContentOptions
        {
            MaxBodyCharacters = 1_000,
            MaxCharactersPerRead = maxCharactersPerRead,
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(EmailContentOptions.MaxCharactersPerRead)));
    }

    /// <summary>
    /// One email asking for both representations may return the per-body bound twice, so a budget below that would cut a
    /// one-email call by a limit that exists for calls naming several — and tell the caller to split what it cannot.
    /// </summary>
    [Fact]
    public void Validate_ReadBudgetBelowTwiceTheBodyBound_FailsStartup()
    {
        // Arrange
        var options = new EmailContentOptions
        {
            MaxBodyCharacters = 100_000,
            MaxCharactersPerRead = 199_999,
        };

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal([nameof(EmailContentOptions.MaxCharactersPerRead)], result.MemberNames);
        Assert.Contains("200000", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>Exactly twice the per-body bound is what a single email can return, so it is accepted rather than refused.</summary>
    [Fact]
    public void Validate_ReadBudgetOfExactlyTwiceTheBodyBound_IsAccepted()
    {
        // Arrange
        var options = new EmailContentOptions
        {
            MaxBodyCharacters = 100_000,
            MaxCharactersPerRead = 200_000,
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    /// <summary>The attachment bounds have defaults of their own, so a deployment that configures nothing still has them.</summary>
    [Fact]
    public void Validate_UnconfiguredDeployment_AcceptsTheDefaultAttachmentBounds()
    {
        // Arrange
        var options = new EmailContentOptions();

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
        Assert.Equal(5 * 1024 * 1024, options.MaxAttachmentBytes);
        Assert.Equal(10 * 1024 * 1024, options.MaxAttachmentBytesPerRead);
    }

    /// <summary>An attachment cannot be larger than the message carrying it, and a negative bound is nothing at all.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData((25 * 1024 * 1024) + 1)]
    public void Validate_AttachmentBoundOutsideTheAcceptedRange_FailsStartup(int maxAttachmentBytes)
    {
        // Arrange
        var options = new EmailContentOptions
        {
            MaxAttachmentBytes = maxAttachmentBytes,
            MaxAttachmentBytesPerRead = 100 * 1024 * 1024,
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(EmailContentOptions.MaxAttachmentBytes)));
    }

    /// <summary>Zero is how a deployment says attachments are described and never handed over, so it is a setting rather than a defect.</summary>
    [Fact]
    public void Validate_AttachmentBoundsOfZero_AreAccepted()
    {
        // Arrange
        var options = new EmailContentOptions
        {
            MaxAttachmentBytes = 0,
            MaxAttachmentBytesPerRead = 0,
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    /// <summary>
    /// A budget that cannot carry one permitted attachment would withhold a file the other bound was set to allow, in
    /// every call including one naming a single email.
    /// </summary>
    [Fact]
    public void Validate_AttachmentBudgetBelowThePerAttachmentBound_FailsStartup()
    {
        // Arrange
        var options = new EmailContentOptions
        {
            MaxAttachmentBytes = 5 * 1024 * 1024,
            MaxAttachmentBytesPerRead = (5 * 1024 * 1024) - 1,
        };

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal([nameof(EmailContentOptions.MaxAttachmentBytesPerRead)], result.MemberNames);
        Assert.Contains("5242880", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>A budget of exactly one permitted attachment serves a one-email call in full, so it is accepted.</summary>
    [Fact]
    public void Validate_AttachmentBudgetOfExactlyThePerAttachmentBound_IsAccepted()
    {
        // Arrange
        var options = new EmailContentOptions
        {
            MaxAttachmentBytes = 5 * 1024 * 1024,
            MaxAttachmentBytesPerRead = 5 * 1024 * 1024,
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    private static ValidationResult[] Validate(EmailContentOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return [.. results];
    }
}
