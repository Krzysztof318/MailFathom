// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Domain.Contacts;
using MailFathom.Mcp.Tools.Contacts;
using MailFathom.Mcp.UnitTests.TestDoubles;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Contacts;

/// <summary>Covers what <c>list_contacts</c> converts in each direction.</summary>
/// <remarks>
/// The bounds a page is served under belong to the use case and are covered there. What is asserted here is the
/// conversion the use case cannot do for the boundary: the published origin into the one the book records, and a page
/// into the shape a client reads.
/// </remarks>
public sealed class ListContactsToolTests
{
    /// <summary>The published names are the wire values, so each has to reach the book as the origin it means.</summary>
    /// <remarks>
    /// Written as two facts rather than as a theory over the published names, because a theory's parameters are part of a
    /// public signature and the published enumeration is internal to the boundary that publishes it.
    /// </remarks>
    [Fact]
    public Task ListContactsAsync_TheAssertedOrigin_ReachesTheBookAsAsserted() =>
        AssertOriginNarrows(PublishedContactOrigin.Asserted, ContactOrigin.Asserted);

    /// <summary>The other half of the same conversion, kept apart for the reason the remark above gives.</summary>
    [Fact]
    public Task ListContactsAsync_TheCollectedOrigin_ReachesTheBookAsCollected() =>
        AssertOriginNarrows(PublishedContactOrigin.Collected, ContactOrigin.Collected);

    /// <summary>A caller that narrows nothing asks for the whole book, which the use case then bounds for it.</summary>
    [Fact]
    public async Task ListContactsAsync_NoArguments_NarrowsNothing()
    {
        // Arrange
        var book = AnEmptyBook();
        var tool = new ListContactsTool(book.Reader);

        // Act
        await tool.ListContactsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var query = QueryReadBy(book);

        Assert.Null(query.Origin);
        Assert.Null(query.Search);
        Assert.Null(query.Cursor);
    }

    /// <summary>A page is published as the contacts a client reads and the cursor it continues from.</summary>
    [Fact]
    public async Task ListContactsAsync_APageTheBookAnswered_PublishesTheContactsAndTheCursor()
    {
        // Arrange
        var contact = StubContactBook.ContactOf("Anna Kowalska", "anna@example.test");
        var cursor = ContactCursor.After(contact.DisplayName, contact.Id);
        var book = new StubContactBook();
        book.Directory
            .ReadPageAsync(Arg.Any<ContactQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ContactPage([contact], cursor));

        var tool = new ListContactsTool(book.Reader);

        // Act
        var result = await tool.ListContactsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var published = Assert.Single(result.Contacts);

        Assert.Equal(contact.Id.ToString(), published.ContactId);
        Assert.Equal("Anna Kowalska", published.DisplayName);
        Assert.Equal(["anna@example.test"], published.Addresses);
        Assert.Equal("anna@example.test", published.PreferredAddress);
        Assert.Equal(PublishedContactOrigin.Asserted, published.Origin);
        Assert.Equal(cursor.Encode(), result.NextCursor);
    }

    /// <summary>A page that ended the walk says so by carrying no cursor, so a caller stops without spending a request.</summary>
    [Fact]
    public async Task ListContactsAsync_ThePageThatEndedTheWalk_PublishesNoCursor()
    {
        // Arrange
        var book = AnEmptyBook();
        var tool = new ListContactsTool(book.Reader);

        // Act
        var result = await tool.ListContactsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Contacts);
        Assert.Null(result.NextCursor);
    }

    private static async Task AssertOriginNarrows(PublishedContactOrigin published, ContactOrigin expected)
    {
        // Arrange
        var book = AnEmptyBook();
        var tool = new ListContactsTool(book.Reader);

        // Act
        await tool.ListContactsAsync(
            origin: published,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, QueryReadBy(book).Origin);
    }

    private static StubContactBook AnEmptyBook()
    {
        var book = new StubContactBook();
        book.Directory
            .ReadPageAsync(Arg.Any<ContactQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ContactPage([], NextCursor: null));

        return book;
    }

    private static ContactQuery QueryReadBy(StubContactBook book) => (ContactQuery)book.Directory
        .ReceivedCalls()
        .Single(call => call.GetMethodInfo().Name == nameof(IContactDirectory.ReadPageAsync))
        .GetArguments()[0]!;
}
