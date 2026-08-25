// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Failures;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Mcp.Tools.Contacts;
using MailFathom.Mcp.UnitTests.TestDoubles;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Contacts;

/// <summary>Covers what <c>update_contact</c> states to the book, and what each way an amendment can end reads as.</summary>
public sealed class UpdateContactToolTests
{
    /// <summary>An amendment states the whole record, so what a caller sent is what the book holds afterwards.</summary>
    [Fact]
    public async Task UpdateContactAsync_ARecordTheBookAccepts_PublishesTheContactAsStated()
    {
        // Arrange
        var held = StubContactBook.ContactOf("Anna Kowalska", "anna@example.test");
        var book = new StubContactBook();
        book.Directory.FindAsync(Arg.Any<MailOwnerId>(), held.Id, Arg.Any<CancellationToken>()).Returns(held);

        var tool = new UpdateContactTool(book.Writer);

        // Act
        var result = await tool.UpdateContactAsync(
            held.Id.ToString(),
            "Anna Nowak",
            ["anna.nowak@example.test"],
            "anna.nowak@example.test",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteState.Written, result.State);
        Assert.Equal("Anna Nowak", result.Contact?.DisplayName);
        Assert.Equal(["anna.nowak@example.test"], result.Contact?.Addresses);
    }

    /// <summary>An omitted note clears the one held, which is what stating the whole record means.</summary>
    [Fact]
    public async Task UpdateContactAsync_NoNote_LeavesTheContactHoldingNone()
    {
        // Arrange
        var held = Contact.Create(
            ContactId.Create(Guid.CreateVersion7(StubContactBook.Now)),
            ContactDisplayName.Create("Anna Kowalska"),
            [StubContactBook.Address("anna@example.test")],
            StubContactBook.Address("anna@example.test"),
            ContactNote.Create("Chartering broker."),
            ContactOrigin.Asserted,
            StubContactBook.Now,
            StubContactBook.Now);

        var book = new StubContactBook();
        book.Directory.FindAsync(Arg.Any<MailOwnerId>(), held.Id, Arg.Any<CancellationToken>()).Returns(held);

        var tool = new UpdateContactTool(book.Writer);

        // Act
        var result = await tool.UpdateContactAsync(
            held.Id.ToString(),
            "Anna Kowalska",
            ["anna@example.test"],
            "anna@example.test",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteState.Written, result.State);
        Assert.Null(result.Contact?.Note);
    }

    /// <summary>A note sent back with the record is the note the book holds afterwards, which is how one survives an amendment.</summary>
    [Fact]
    public async Task UpdateContactAsync_ANoteSentWithTheRecord_LeavesTheContactHoldingIt()
    {
        // Arrange
        var held = StubContactBook.ContactOf("Anna Kowalska", "anna@example.test");
        var book = new StubContactBook();
        book.Directory.FindAsync(Arg.Any<MailOwnerId>(), held.Id, Arg.Any<CancellationToken>()).Returns(held);

        var tool = new UpdateContactTool(book.Writer);

        // Act
        var result = await tool.UpdateContactAsync(
            held.Id.ToString(),
            "Anna Kowalska",
            ["anna@example.test"],
            "anna@example.test",
            "Chartering broker.",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteState.Written, result.State);
        Assert.Equal("Chartering broker.", result.Contact?.Note);
    }

    /// <summary>A record this deployment collected is the operator's to take on, and the state says which rule refused it.</summary>
    [Fact]
    public async Task UpdateContactAsync_AContactTheDeploymentCollected_PublishesTheRefusalWithoutTheRecord()
    {
        // Arrange
        var collected = StubContactBook.ContactOf("Anna Kowalska", "anna@example.test", ContactOrigin.Collected);
        var book = new StubContactBook();
        book.Directory.FindAsync(Arg.Any<MailOwnerId>(), collected.Id, Arg.Any<CancellationToken>()).Returns(collected);

        var tool = new UpdateContactTool(book.Writer);

        // Act
        var result = await tool.UpdateContactAsync(
            collected.Id.ToString(),
            "Anna Nowak",
            ["anna@example.test"],
            "anna@example.test",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteState.ContactWasCollected, result.State);
        Assert.Null(result.Contact);
    }

    /// <summary>A contact the book does not hold is an answered call rather than a failed one, and it publishes no record.</summary>
    [Fact]
    public async Task UpdateContactAsync_AContactTheBookDoesNotHold_PublishesThatItFoundNobody()
    {
        // Arrange
        var book = new StubContactBook();
        var tool = new UpdateContactTool(book.Writer);

        // Act
        var result = await tool.UpdateContactAsync(
            ContactId.Create(Guid.CreateVersion7(StubContactBook.Now)).ToString(),
            "Anna Kowalska",
            ["anna@example.test"],
            "anna@example.test",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteState.NotFound, result.State);
        Assert.Null(result.Contact);
    }

    /// <summary>Text naming no identity this system issued is refused before the book is read.</summary>
    [Fact]
    public async Task UpdateContactAsync_TextThatIsNoIdentifier_IsRefusedWithoutALookup()
    {
        // Arrange
        var book = new StubContactBook();
        var tool = new UpdateContactTool(book.Writer);

        // Act, Assert
        await Assert.ThrowsAsync<ContactIdentifierMalformedException>(() => tool.UpdateContactAsync(
            "not-a-uuid",
            "Anna Kowalska",
            ["anna@example.test"],
            "anna@example.test",
            cancellationToken: TestContext.Current.CancellationToken));

        await book.Directory.DidNotReceiveWithAnyArgs()
            .FindAsync(default, default, TestContext.Current.CancellationToken);
    }
}
