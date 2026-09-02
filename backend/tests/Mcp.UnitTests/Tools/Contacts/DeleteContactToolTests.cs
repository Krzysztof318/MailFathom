// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Mcp.Tools.Contacts;
using MailFathom.Mcp.UnitTests.TestDoubles;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Contacts;

/// <summary>Covers what <c>delete_contact</c> reports about an erasure, and what it reports about a person it never held.</summary>
public sealed class DeleteContactToolTests
{
    /// <summary>Whoever asked for an erasure is entitled to what it removed rather than to a call that did not complain.</summary>
    [Fact]
    public async Task DeleteContactAsync_APersonTheBookHeld_PublishesWhatWentWithThem()
    {
        // Arrange
        var erased = ContactId.Create(Guid.CreateVersion7(StubContactBook.Now));
        var book = new StubContactBook();
        book.Store
            .EraseAsync(Arg.Any<IPersistenceSession>(), Arg.Any<MailOwnerId>(), erased, Arg.Any<CancellationToken>())
            .Returns(new ContactErasure(erased, WasHeld: true, AddressesErased: 3));

        var tool = new DeleteContactTool(book.Writer);

        // Act
        var result = await tool.DeleteContactAsync(erased.ToString(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(erased.ToString(), result.ContactId);
        Assert.True(result.WasHeld);
        Assert.Equal(3, result.AddressesErased);
    }

    /// <summary>Erasing somebody the book does not hold leaves it in the state the caller asked for, so the call repeats safely.</summary>
    [Fact]
    public async Task DeleteContactAsync_APersonTheBookDoesNotHold_PublishesACompletedErasure()
    {
        // Arrange
        var absent = ContactId.Create(Guid.CreateVersion7(StubContactBook.Now));
        var book = new StubContactBook();
        book.Store
            .EraseAsync(Arg.Any<IPersistenceSession>(), Arg.Any<MailOwnerId>(), absent, Arg.Any<CancellationToken>())
            .Returns(new ContactErasure(absent, WasHeld: false, AddressesErased: 0));

        var tool = new DeleteContactTool(book.Writer);

        // Act
        var result = await tool.DeleteContactAsync(absent.ToString(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.WasHeld);
        Assert.Equal(0, result.AddressesErased);
    }

    /// <summary>Text naming no identity this system issued is refused before anything is erased.</summary>
    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task DeleteContactAsync_TextThatIsNoIdentifier_IsRefusedWithoutAnErasure(string contactId)
    {
        // Arrange
        var book = new StubContactBook();
        var tool = new DeleteContactTool(book.Writer);

        // Act, Assert
        await Assert.ThrowsAsync<ContactIdentifierMalformedException>(() =>
            tool.DeleteContactAsync(contactId, TestContext.Current.CancellationToken));

        await book.Store.DidNotReceiveWithAnyArgs()
            .EraseAsync(default!, default, default, TestContext.Current.CancellationToken);
    }
}
