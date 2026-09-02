// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Persistence;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Persistence;

/// <summary>Covers the two bounds a deployment may configure on what one read of message content returns, and the download block beside them.</summary>
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

    /// <summary>The download block has a default of its own, so a deployment that configures nothing still has one.</summary>
    [Fact]
    public void Validate_UnconfiguredDeployment_AcceptsTheDefaultAttachmentDownloadWindow()
    {
        // Arrange
        var options = new EmailContentOptions();

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
        Assert.Equal(TimeSpan.FromMinutes(10), options.AttachmentDownloads.LinkLifetime);
    }

    /// <summary>
    /// The nested block is judged from here, because the options framework validates the annotations of the type it was
    /// handed and never descends into a property's own object. Without this, an unusable download block would reach a
    /// running deployment.
    /// </summary>
    [Fact]
    public void Validate_UnusableAttachmentDownloadBlock_FailsStartupNamingThatBlock()
    {
        // Arrange
        var options = new EmailContentOptions
        {
            AttachmentDownloads = new AttachmentDownloadOptions { LinkLifetime = TimeSpan.FromHours(4) },
        };

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal([nameof(EmailContentOptions.AttachmentDownloads)], result.MemberNames);
        Assert.Contains("LinkLifetime", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>Both keys one read is bounded by reach the bounds the read truncates at.</summary>
    [Fact]
    public void ToReadOptions_ConfiguredSection_CarriesBothKeysTheReadTruncatesAt()
    {
        // Arrange
        var options = new EmailContentOptions { MaxBodyCharacters = 40_000, MaxCharactersPerRead = 90_000 };

        // Act
        var bounds = options.ToReadOptions();

        // Assert
        Assert.Equal(40_000, bounds.MaxBodyCharacters);
        Assert.Equal(90_000, bounds.MaxCharactersPerRead);
    }

    private static ValidationResult[] Validate(EmailContentOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return [.. results];
    }
}
