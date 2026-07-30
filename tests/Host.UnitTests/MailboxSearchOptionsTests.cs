// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel.DataAnnotations;
using MailMcp.Application.Emails;
using MailMcp.Host.Configuration;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers the bounds a deployment may configure on what a search result shows.</summary>
public sealed class MailboxSearchOptionsTests
{
    /// <summary>A deployment that configures nothing gets the bounds the application defines.</summary>
    [Fact]
    public void Validate_UnconfiguredDeployment_MatchesTheApplicationDefaults()
    {
        // Arrange
        var options = new MailboxSearchOptions();

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
        Assert.Equal(EmailSearchSnippetBounds.Default.SnippetsPerEmail, options.SnippetsPerEmail);
        Assert.Equal(EmailSearchSnippetBounds.Default.WordsPerSnippet, options.WordsPerSnippet);
    }

    /// <summary>The bound is the privacy control, so a value outside the range fails startup rather than being clamped.</summary>
    [Theory]
    [InlineData(0, EmailSearchSnippetBounds.MinimumWordsPerSnippet, nameof(MailboxSearchOptions.SnippetsPerEmail))]
    [InlineData(EmailSearchSnippetBounds.MaximumSnippetsPerEmail + 1, 24, nameof(MailboxSearchOptions.SnippetsPerEmail))]
    [InlineData(1, EmailSearchSnippetBounds.MinimumWordsPerSnippet - 1, nameof(MailboxSearchOptions.WordsPerSnippet))]
    [InlineData(1, EmailSearchSnippetBounds.MaximumWordsPerSnippet + 1, nameof(MailboxSearchOptions.WordsPerSnippet))]
    public void Validate_BoundOutsideTheAcceptedRange_FailsStartupNamingTheSetting(
        int snippetsPerEmail,
        int wordsPerSnippet,
        string expectedMemberName)
    {
        // Arrange
        var options = new MailboxSearchOptions
        {
            SnippetsPerEmail = snippetsPerEmail,
            WordsPerSnippet = wordsPerSnippet,
        };

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal([expectedMemberName], result.MemberNames);
    }

    /// <summary>Whatever the options accept, the application value object accepts, so no configured bound fails later.</summary>
    [Theory]
    [InlineData(1, EmailSearchSnippetBounds.MinimumWordsPerSnippet)]
    [InlineData(EmailSearchSnippetBounds.MaximumSnippetsPerEmail, EmailSearchSnippetBounds.MaximumWordsPerSnippet)]
    public void Validate_BoundAtEitherEndOfTheRange_IsAcceptedByTheApplicationValue(
        int snippetsPerEmail,
        int wordsPerSnippet)
    {
        // Arrange
        var options = new MailboxSearchOptions
        {
            SnippetsPerEmail = snippetsPerEmail,
            WordsPerSnippet = wordsPerSnippet,
        };

        // Act
        var results = Validate(options);
        var bounds = EmailSearchSnippetBounds.Create(options.SnippetsPerEmail, options.WordsPerSnippet);

        // Assert
        Assert.Empty(results);
        Assert.Equal(snippetsPerEmail, bounds.SnippetsPerEmail);
        Assert.Equal(wordsPerSnippet, bounds.WordsPerSnippet);
    }

    private static ValidationResult[] Validate(MailboxSearchOptions options)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return [.. results];
    }
}
