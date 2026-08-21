// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Failures;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Mcp.Tools.Contacts;
using MailFathom.Mcp.UnitTests.TestDoubles;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Contacts;

/// <summary>Covers what <c>create_contact</c> hands the book, and what a caller reads back from each way the write can end.</summary>
public sealed class CreateContactToolTests
{
    /// <summary>A record a caller stated is what the book holds, published back as the contact it now is.</summary>
    [Fact]
    public async Task CreateContactAsync_ARecordTheBookAccepts_PublishesThePersonAsWritten()
    {
        // Arrange
        var book = new StubContactBook();
        var tool = new CreateContactTool(book.Writer);

        // Act
        var result = await tool.CreateContactAsync(
            "Anna Kowalska",
            ["anna@example.test", "a.kowalska@example.test"],
            "anna@example.test",
            "Chartering broker.",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteState.Written, result.State);
        Assert.Equal("Anna Kowalska", result.Contact?.DisplayName);
        Assert.Equal(["anna@example.test", "a.kowalska@example.test"], result.Contact?.Addresses);
        Assert.Equal("anna@example.test", result.Contact?.PreferredAddress);
        Assert.Equal("Chartering broker.", result.Contact?.Note);
        Assert.Null(result.AddressHolderContactId);
    }

    /// <summary>A caller writing for the owner writes somebody down, so the record is asserted rather than collected.</summary>
    [Fact]
    public async Task CreateContactAsync_ARecordTheBookAccepts_PublishesItAsAsserted()
    {
        // Arrange
        var book = new StubContactBook();
        var tool = new CreateContactTool(book.Writer);

        // Act
        var result = await tool.CreateContactAsync(
            "Anna Kowalska",
            ["anna@example.test"],
            "anna@example.test",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PublishedContactOrigin.Asserted, result.Contact?.Origin);
        Assert.Null(result.Contact?.Note);
    }

    /// <summary>One address belongs to one contact, so a second record claiming it is answered with who holds it and nothing else.</summary>
    [Fact]
    public async Task CreateContactAsync_AnAddressAnotherContactHolds_PublishesThatIdentityAndNoRecord()
    {
        // Arrange
        var holder = ContactId.Create(Guid.CreateVersion7(StubContactBook.Now));
        var book = new StubContactBook();
        book.Directory
            .FindHoldersOfAsync(Arg.Any<IReadOnlyCollection<EmailAddress>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<EmailAddress, ContactId>
            {
                [StubContactBook.Address("anna@example.test")] = holder,
            });

        var tool = new CreateContactTool(book.Writer);

        // Act
        var result = await tool.CreateContactAsync(
            "Anna Kowalska",
            ["anna@example.test"],
            "anna@example.test",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteState.AddressHeldByAnotherContact, result.State);
        Assert.Equal(holder.ToString(), result.AddressHolderContactId);
        Assert.Null(result.Contact);
    }

    /// <summary>A record the book refuses stops at the use case, so nothing is staged and the refusal names no personal data.</summary>
    [Fact]
    public async Task CreateContactAsync_ARecordTheBookRefuses_ReachesNoWriteAndNamesNothingSupplied()
    {
        // Arrange
        var book = new StubContactBook();
        var tool = new CreateContactTool(book.Writer);

        // Act
        var refusal = await Assert.ThrowsAsync<ContactRecordInvalidException>(() => tool.CreateContactAsync(
            "Anna Kowalska",
            ["anna@example.test"],
            "someone.else@example.test",
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain("@", refusal.Message, StringComparison.Ordinal);
        await book.Store.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default!, TestContext.Current.CancellationToken);
    }
}
