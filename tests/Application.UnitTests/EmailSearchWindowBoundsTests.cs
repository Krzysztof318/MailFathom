// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests;

/// <summary>Covers the two bounds that decide how much a search may return: the window and the snippets inside it.</summary>
public sealed class EmailSearchWindowBoundsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(EmailSearchResultLimit.DefaultValue)]
    [InlineData(EmailSearchResultLimit.MaximumValue)]
    public void ResultLimit_CountInsideTheAcceptedRange_IsAccepted(int requested)
    {
        // Act
        var limit = EmailSearchResultLimit.Create(requested);

        // Assert
        Assert.Equal(requested, limit.Value);
        Assert.True(limit.IsSpecified);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(EmailSearchResultLimit.MaximumValue + 1)]
    public void ResultLimit_CountOutsideTheAcceptedRange_IsRejected(int requested)
    {
        // Act
        var failure = Assert.Throws<EmailSearchResultLimitOutOfRangeException>(() =>
            EmailSearchResultLimit.Create(requested));

        // Assert
        Assert.Equal(requested, failure.RequestedResultLimit);
        Assert.Equal(EmailSearchResultLimit.MaximumValue, failure.MaximumResultLimit);
    }

    [Fact]
    public void ResultLimit_NoCountNamed_TakesTheDefault()
    {
        // Act
        var limit = EmailSearchResultLimit.FromRequested(null);

        // Assert
        Assert.Equal(EmailSearchResultLimit.DefaultValue, limit.Value);
    }

    /// <summary>The struct default is reachable and names no window, which is what <c>IsSpecified</c> exists to report.</summary>
    [Fact]
    public void ResultLimit_StructDefault_NamesNoWindow()
    {
        // Act
        var limit = default(EmailSearchResultLimit);

        // Assert
        Assert.False(limit.IsSpecified);
        Assert.Equal(0, limit.Value);
    }

    /// <summary>A ranked window costs more per result than a listed page, so it is bounded lower.</summary>
    [Fact]
    public void ResultLimit_Maximum_IsBelowTheListingPageSizeMaximum()
    {
        // Assert
        Assert.True(EmailSearchResultLimit.MaximumValue < MailboxQueryPageSize.MaximumValue);
    }

    [Fact]
    public void SnippetBounds_ValuesInsideTheAcceptedRange_AreAccepted()
    {
        // Act
        var bounds = EmailSearchSnippetBounds.Create(snippetsPerEmail: 2, wordsPerSnippet: 12);

        // Assert
        Assert.Equal(2, bounds.SnippetsPerEmail);
        Assert.Equal(12, bounds.WordsPerSnippet);
    }

    /// <summary>The bounds are the privacy control, so nothing constructs them without meeting the range.</summary>
    [Theory]
    [InlineData(0, 12)]
    [InlineData(EmailSearchSnippetBounds.MaximumSnippetsPerEmail + 1, 12)]
    [InlineData(2, EmailSearchSnippetBounds.MinimumWordsPerSnippet - 1)]
    [InlineData(2, EmailSearchSnippetBounds.MaximumWordsPerSnippet + 1)]
    public void SnippetBounds_ValuesOutsideTheAcceptedRange_AreRejected(int snippetsPerEmail, int wordsPerSnippet)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EmailSearchSnippetBounds.Create(snippetsPerEmail, wordsPerSnippet));
    }

    /// <summary>A word count is not a size: one unbroken token can satisfy it while carrying most of a message body.</summary>
    [Theory]
    [InlineData(EmailSearchSnippetBounds.MinimumWordsPerSnippet)]
    [InlineData(24)]
    [InlineData(EmailSearchSnippetBounds.MaximumWordsPerSnippet)]
    public void SnippetBounds_CharacterCeiling_IsDerivedFromTheWordBound(int wordsPerSnippet)
    {
        // Act
        var bounds = EmailSearchSnippetBounds.Create(snippetsPerEmail: 1, wordsPerSnippet);

        // Assert
        Assert.Equal(wordsPerSnippet * EmailSearchSnippetBounds.MaximumCharactersPerWord, bounds.MaximumCharacters);
    }

    [Fact]
    public void SnippetBounds_Default_IsInsideTheAcceptedRange()
    {
        // Act
        var reconstructed = EmailSearchSnippetBounds.Create(
            EmailSearchSnippetBounds.Default.SnippetsPerEmail,
            EmailSearchSnippetBounds.Default.WordsPerSnippet);

        // Assert
        Assert.Equal(EmailSearchSnippetBounds.Default, reconstructed);
    }
}
