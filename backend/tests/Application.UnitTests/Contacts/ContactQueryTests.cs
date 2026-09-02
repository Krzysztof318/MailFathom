// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Domain.Contacts;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts;

public sealed class ContactQueryTests
{
    /// <summary>A caller that asks for nothing is served the first page of the whole book rather than all of it.</summary>
    [Fact]
    public void Create_NoPageSizeAndNoFilter_ReadsTheFirstPageOfTheWholeBook()
    {
        // Act
        var query = ContactQuery.Create(origin: null, search: null, pageSize: null, cursor: null);

        // Assert
        Assert.Equal(ContactQuery.DefaultPageSize, query.PageSize);
        Assert.Null(query.Origin);
        Assert.Null(query.Cursor);
    }

    /// <summary>The ceiling is what every surface over the book inherits, so a request past it is refused rather than clamped.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ContactQuery.MaximumPageSize + 1)]
    public void Create_APageSizeOutsideTheBound_IsRefused(int pageSize)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ContactQuery.Create(origin: null, search: null, pageSize, cursor: null));
    }

    /// <summary>The greatest page a caller may ask for is served rather than reduced.</summary>
    [Fact]
    public void Create_ThePageSizeAtTheBound_IsAccepted()
    {
        // Act
        var query = ContactQuery.Create(origin: null, search: null, ContactQuery.MaximumPageSize, cursor: null);

        // Assert
        Assert.Equal(ContactQuery.MaximumPageSize, query.PageSize);
    }

    /// <summary>An origin nothing declares would narrow the listing to a half of the book that does not exist.</summary>
    [Fact]
    public void Create_AnOriginNothingDeclares_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContactQuery.Create((ContactOrigin)42, search: null, pageSize: null, cursor: null));
    }

    /// <summary>A cursor names the boundary in the order the walk is taken on, and is carried through unchanged.</summary>
    [Fact]
    public void Create_ACursorAndAnOrigin_AreCarriedToTheRead()
    {
        // Arrange
        var cursor = ContactCursor.After(
            ContactDisplayName.Create("Anna Kowalska"),
            ContactId.Create(Guid.CreateVersion7(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero))));

        // Act
        var query = ContactQuery.Create(ContactOrigin.Collected, search: null, pageSize: 10, cursor);

        // Assert
        Assert.Equal(ContactOrigin.Collected, query.Origin);
        Assert.Equal("ANNA KOWALSKA", query.Cursor?.DisplayNameSortKey);
        Assert.Equal(cursor.ContactId, query.Cursor?.ContactId);
    }
}
