// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools.Categories;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Categories;

/// <summary>Covers the reading of a header a caller writes, which is untrusted input and is treated as such.</summary>
/// <remarks>
/// Every shape a caller can write that this surface cannot act on comes back as no categories at all, which leaves the
/// deployment's own selection in force. That is the answer under test throughout: a header is a narrowing convenience,
/// so a malformed one must not narrow an endpoint to silence and must not fail a request either.
/// </remarks>
public sealed class McpToolCategoryHeaderTests
{
    /// <summary>A name colliding with the transport's own, with an authentication header, or with a proxy's would be read by two readers with different ideas of what it means.</summary>
    [Fact]
    public void Name_IsMailFathomsOwnRatherThanOneSomethingElseOnThePathAlreadyReads()
    {
        // Assert
        Assert.Equal("MailFathom-Tool-Categories", McpToolCategoryHeader.Name);
    }

    [Fact]
    public void CategoriesNamedBy_NoRequestAtAll_NamesNothing()
    {
        // Assert
        Assert.Empty(McpToolCategoryHeader.CategoriesNamedBy(request: null));
    }

    [Fact]
    public void CategoriesNamedBy_ARequestWithoutTheHeader_NamesNothing()
    {
        // Assert
        Assert.Empty(McpToolCategoryHeader.CategoriesNamedBy(new DefaultHttpContext().Request));
    }

    [Fact]
    public void CategoriesNamedBy_AListOfPublishedNames_NamesEveryOneOfThem()
    {
        // Act
        var named = McpToolCategoryHeader.CategoriesNamedBy(RequestCarrying("mailbox, contacts"));

        // Assert
        Assert.Equal(2, named.Count);
        Assert.Contains(McpToolCategory.Mailbox, named);
        Assert.Contains(McpToolCategory.Contacts, named);
    }

    /// <summary>An HTTP list header may arrive as one line or as several, and a client that writes it either way asked for the same thing.</summary>
    [Fact]
    public void CategoriesNamedBy_TheHeaderWrittenTwice_NamesWhatBothOccurrencesCarry()
    {
        // Arrange
        var request = new DefaultHttpContext().Request;
        request.Headers[McpToolCategoryHeader.Name] = new[] { "mailbox", "sending" };

        // Act
        var named = McpToolCategoryHeader.CategoriesNamedBy(request);

        // Assert
        Assert.Equal(2, named.Count);
        Assert.Contains(McpToolCategory.Mailbox, named);
        Assert.Contains(McpToolCategory.Sending, named);
    }

    /// <summary>An unknown value is dropped rather than failing the request, because a client asking for something this endpoint has never heard of has asked for nothing it can act on.</summary>
    [Fact]
    public void CategoriesNamedBy_AnUnknownNameBesideAPublishedOne_NamesThePublishedOneAlone()
    {
        // Act
        var named = McpToolCategoryHeader.CategoriesNamedBy(RequestCarrying("mailbox, rules, "));

        // Assert
        Assert.Equal([McpToolCategory.Mailbox], named);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,,")]
    [InlineData("rules")]
    [InlineData("<script>alert(1)</script>")]
    public void CategoriesNamedBy_AHeaderNamingNothingUsable_NamesNothing(string written)
    {
        // Assert
        Assert.Empty(McpToolCategoryHeader.CategoriesNamedBy(RequestCarrying(written)));
    }

    /// <summary>A value longer than the bound is dropped whole, because half a list is a selection nobody asked for.</summary>
    [Fact]
    public void CategoriesNamedBy_AHeaderLongerThanTheBound_IsIgnoredEntirely()
    {
        // Arrange
        var overlong = "mailbox," + new string('a', McpToolCategoryHeader.MaximumLength);

        // Assert
        Assert.Empty(McpToolCategoryHeader.CategoriesNamedBy(RequestCarrying(overlong)));
    }

    /// <summary>Reading past the bound is what a caller could spend this surface's time on, and stopping at it can only narrow further.</summary>
    [Fact]
    public void CategoriesNamedBy_MoreNamesThanTheBound_ReadsNoFurtherThanTheBound()
    {
        // Arrange
        var padding = string.Join(",", Enumerable.Repeat("rules", McpToolCategoryHeader.MaximumNamedCategories));

        // Act
        var named = McpToolCategoryHeader.CategoriesNamedBy(RequestCarrying($"{padding},mailbox"));

        // Assert
        Assert.Empty(named);
    }

    private static HttpRequest RequestCarrying(string written)
    {
        var request = new DefaultHttpContext().Request;
        request.Headers[McpToolCategoryHeader.Name] = written;

        return request;
    }
}
