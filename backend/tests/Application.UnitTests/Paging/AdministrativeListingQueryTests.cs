// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;
using Xunit;

namespace MailFathom.Application.UnitTests.Paging;

/// <summary>Covers which pages an administrative listing may be asked for, and how a read of one row past a page becomes the page.</summary>
public sealed class AdministrativeListingQueryTests
{
    private static readonly Guid[] Rows =
    [
        new("00000000-0000-7000-8000-000000000001"),
        new("00000000-0000-7000-8000-000000000002"),
        new("00000000-0000-7000-8000-000000000003"),
    ];

    /// <summary>A first page with more following holds the page alone and continues after its last row, never after the extra one.</summary>
    [Fact]
    public void PageOf_ARowPastThePage_HoldsThePageAndContinuesAfterItsLastRow()
    {
        // Arrange
        var query = AdministrativeListingQuery.Create(pageSize: 2, after: null)!;

        // Act
        var page = query.PageOf(Rows, row => row);

        // Assert
        Assert.Equal(Rows[..2], page.Entries);
        Assert.Equal(Rows[1], page.ContinuesAfter);
    }

    /// <summary>A following page that filled exactly and found nothing past it is the last page.</summary>
    [Fact]
    public void PageOf_NoRowPastThePage_IsTheLastPage()
    {
        // Arrange
        var query = AdministrativeListingQuery.Create(pageSize: 3, after: Rows[0])!;

        // Act
        var page = query.PageOf(Rows, row => row);

        // Assert
        Assert.Equal(Rows, page.Entries);
        Assert.Null(page.ContinuesAfter);
    }

    /// <summary>A cursor past every row reads nothing, and an empty page is the last.</summary>
    [Fact]
    public void PageOf_NoRows_IsAnEmptyLastPage()
    {
        // Arrange
        var query = AdministrativeListingQuery.Create(pageSize: 2, after: Rows[^1])!;

        // Act
        var page = query.PageOf(Array.Empty<Guid>(), row => row);

        // Assert
        Assert.Empty(page.Entries);
        Assert.Null(page.ContinuesAfter);
    }

    [Fact]
    public void Create_NoPageSize_AsksForTheDefault()
    {
        // Act
        var query = AdministrativeListingQuery.Create(pageSize: null, after: null);

        // Assert
        Assert.Equal(AdministrativeListingQuery.DefaultPageSize, query!.PageSize);
        Assert.Null(query.After);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(AdministrativeListingQuery.MaximumPageSize)]
    public void Create_APageSizeWithinTheRange_IsAccepted(int pageSize)
    {
        // Arrange
        var after = new Guid("019893e5-6ad0-7bd0-9f11-6c3a1d5e4b2f");

        // Act
        var query = AdministrativeListingQuery.Create(pageSize, after);

        // Assert
        Assert.Equal(pageSize, query!.PageSize);
        Assert.Equal(after, query.After);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AdministrativeListingQuery.MaximumPageSize + 1)]
    public void Create_APageSizeOutsideTheRange_IsRefused(int pageSize)
    {
        // Act
        var query = AdministrativeListingQuery.Create(pageSize, after: null);

        // Assert
        Assert.Null(query);
    }
}
