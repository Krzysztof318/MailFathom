// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools.Categories;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Categories;

/// <summary>Covers the closed set of categories: the names it publishes, and what it does with a name it does not.</summary>
/// <remarks>
/// The names are what an operator writes in configuration and what a client writes in a header, so they are asserted
/// against <see cref="McpToolCategory.All" /> rather than reflected over: a member that stopped being listed there
/// would stop being parsable while the property that names it still compiled.
/// </remarks>
public sealed class McpToolCategoryTests
{
    /// <summary>Two categories sharing a name would leave a configured value naming whichever the parser reached first.</summary>
    [Fact]
    public void All_TheNamesTheSetPublishes_AreUnique()
    {
        // Act
        var names = McpToolCategory.All.Select(static category => category.Name).ToArray();

        // Assert
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The name travels in a header and in a configuration value, so it stays a bare lowercase token rather than anything either would have to escape.</summary>
    [Fact]
    public void All_EveryPublishedName_IsALowercaseTokenWithoutSeparators()
    {
        // Act
        var malformed = McpToolCategory.All
            .Select(static category => category.Name)
            .Where(static name => !name.All(char.IsAsciiLetterLower))
            .ToArray();

        // Assert
        Assert.Empty(malformed);
    }

    [Fact]
    public void TryParse_APublishedName_IsTheCategoryItNames()
    {
        // Act
        var parsed = McpToolCategory.TryParse("contacts", out var category);

        // Assert
        Assert.True(parsed);
        Assert.Equal(McpToolCategory.Contacts, category);
    }

    /// <summary>An operator writing a configuration value and a client writing a header both spell a name the way their own file reads, and neither spelling means anything else anywhere.</summary>
    [Theory]
    [InlineData("Mailbox")]
    [InlineData("MAILBOX")]
    [InlineData("  mailbox  ")]
    public void TryParse_AName_IgnoresCaseAndSurroundingWhitespace(string written)
    {
        // Act
        var parsed = McpToolCategory.TryParse(written, out var category);

        // Assert
        Assert.True(parsed);
        Assert.Equal(McpToolCategory.Mailbox, category);
    }

    /// <summary>A name nothing publishes is unknown rather than new, so it yields the value a caller already receives on failure.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mailboxes")]
    [InlineData("rules")]
    public void TryParse_ANameNothingPublishes_IsRefusedAsTheUnspecifiedDefault(string? written)
    {
        // Act
        var parsed = McpToolCategory.TryParse(written, out var category);

        // Assert
        Assert.False(parsed);
        Assert.False(category.IsSpecified);
    }

    [Fact]
    public void IsSpecified_TheStructDefault_NamesNoCategory()
    {
        // Arrange
        McpToolCategory unspecified = default;

        // Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => unspecified.Name);
    }

    [Fact]
    public void ToString_TheStructDefault_ReadsAsUnspecifiedRatherThanAsEmptiness()
    {
        // Assert
        Assert.Equal("(unspecified)", default(McpToolCategory).ToString());
        Assert.Equal("mailbox", McpToolCategory.Mailbox.ToString());
    }

    /// <summary>A refusal has to say what is accepted, and what is accepted is the whole published set in the order it is declared.</summary>
    [Fact]
    public void PublishedNames_ReportsEveryCategoryInDeclarationOrder()
    {
        // Assert
        Assert.Equal(
            string.Join(", ", McpToolCategory.All.Select(static category => category.Name)),
            McpToolCategory.PublishedNames());
    }
}
