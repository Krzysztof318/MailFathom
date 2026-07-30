// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel.DataAnnotations;
using MailMcp.Host.Configuration;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers the bound a deployment may configure on what one read of a message body returns.</summary>
public sealed class EmailContentOptionsTests
{
    /// <summary>A deployment that configures nothing reads mail under a bound rather than under none.</summary>
    [Fact]
    public void Validate_UnconfiguredDeployment_AcceptsTheDefaultBound()
    {
        // Arrange
        var options = new EmailContentOptions();

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
        Assert.Equal(100_000, options.MaxBodyCharacters);
    }

    /// <summary>A bound outside the range fails startup, because neither end of it produces a usable response.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    [InlineData(1_000_001)]
    public void Validate_BoundOutsideTheAcceptedRange_FailsStartup(int maxBodyCharacters)
    {
        // Arrange
        var options = new EmailContentOptions { MaxBodyCharacters = maxBodyCharacters };

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal([nameof(EmailContentOptions.MaxBodyCharacters)], result.MemberNames);
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(1_000_000)]
    public void Validate_BoundAtEitherEndOfTheRange_IsAccepted(int maxBodyCharacters)
    {
        // Arrange
        var options = new EmailContentOptions { MaxBodyCharacters = maxBodyCharacters };

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
