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

/// <summary>Covers the two ways <c>get_contact</c> names one person, and what it does when a call names neither or both.</summary>
public sealed class GetContactToolTests
{
    /// <summary>The question an agent asks after reading an address out of mail, answered from the address index.</summary>
    [Fact]
    public async Task GetContactAsync_AnAddress_ResolvesThePersonUsingIt()
    {
        // Arrange
        var contact = StubContactBook.ContactOf("Anna Kowalska", "anna@example.test");
        var book = new StubContactBook();
        book.Directory
            .FindByAddressAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>())
            .Returns(contact);

        var tool = new GetContactTool(book.Reader);

        // Act
        var result = await tool.GetContactAsync(
            address: "anna@example.test",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(contact.Id.ToString(), result.Contact?.ContactId);
        await book.Directory.DidNotReceiveWithAnyArgs()
            .FindAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>The identifier form is what a caller reaches for once a listing or a write has handed it one.</summary>
    [Fact]
    public async Task GetContactAsync_AnIdentifier_ResolvesThroughTheIdentityLookup()
    {
        // Arrange
        var contact = StubContactBook.ContactOf("Anna Kowalska", "anna@example.test");
        var book = new StubContactBook();
        book.Directory.FindAsync(contact.Id, Arg.Any<CancellationToken>()).Returns(contact);

        var tool = new GetContactTool(book.Reader);

        // Act
        var result = await tool.GetContactAsync(
            contact.Id.ToString(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Anna Kowalska", result.Contact?.DisplayName);
        await book.Directory.DidNotReceiveWithAnyArgs()
            .FindByAddressAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>A person this deployment has no record of is an answered question rather than a failed call.</summary>
    [Fact]
    public async Task GetContactAsync_APersonTheBookDoesNotHold_AnswersWithNobody()
    {
        // Arrange
        var book = new StubContactBook();
        var tool = new GetContactTool(book.Reader);

        // Act
        var result = await tool.GetContactAsync(
            address: "nobody@example.test",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Contact);
    }

    /// <summary>Naming neither asks nothing, and naming both can name two people whose answers a caller could not tell apart.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "   ")]
    [InlineData("2c3f0f2e-0000-7000-8000-000000000001", "anna@example.test")]
    public async Task GetContactAsync_ACallNamingNeitherOrBoth_IsRefused(string? contactId, string? address)
    {
        // Arrange
        var book = new StubContactBook();
        var tool = new GetContactTool(book.Reader);

        // Act, Assert
        await Assert.ThrowsAsync<ContactIdentifierMalformedException>(() =>
            tool.GetContactAsync(contactId, address, TestContext.Current.CancellationToken));
    }

    /// <summary>Text that names no identity this system issues is refused before anything is looked up.</summary>
    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetContactAsync_TextThatIsNoIdentifier_IsRefusedWithoutALookup(string contactId)
    {
        // Arrange
        var book = new StubContactBook();
        var tool = new GetContactTool(book.Reader);

        // Act, Assert
        await Assert.ThrowsAsync<ContactIdentifierMalformedException>(() =>
            tool.GetContactAsync(contactId, cancellationToken: TestContext.Current.CancellationToken));

        await book.Directory.DidNotReceiveWithAnyArgs()
            .FindAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>Text that is no address the book could hold is refused the same way, and names nothing about the text.</summary>
    [Fact]
    public async Task GetContactAsync_TextThatIsNoAddress_IsRefusedWithoutNamingIt()
    {
        // Arrange
        var book = new StubContactBook();
        var tool = new GetContactTool(book.Reader);

        // Act
        var refusal = await Assert.ThrowsAsync<ContactIdentifierMalformedException>(() =>
            tool.GetContactAsync(
                address: "not-an-address",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain("not-an-address", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A header copied whole is not an address, and reading one leniently here would leave the write tools refusing what this one accepted.</summary>
    /// <remarks>
    /// The brackets alone are the case worth pinning: the display name carries whitespace no local part admits, so the
    /// first form was already refused, while <c>&lt;anna@example.test&gt;</c> splits into two halves that each read as
    /// usable and would otherwise resolve nobody while looking like a lookup that found nobody.
    /// </remarks>
    [Theory]
    [InlineData("Anna Kowalska <anna@example.test>")]
    [InlineData("<anna@example.test>")]
    public async Task GetContactAsync_AnAddressWrittenAsAHeaderWould_IsRefused(string address)
    {
        // Arrange
        var book = new StubContactBook();
        var tool = new GetContactTool(book.Reader);

        // Act, Assert
        await Assert.ThrowsAsync<ContactIdentifierMalformedException>(() => tool.GetContactAsync(
            address: address,
            cancellationToken: TestContext.Current.CancellationToken));

        await book.Directory.DidNotReceiveWithAnyArgs()
            .FindByAddressAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>Casing is not part of an address's identity, so the lookup uses the comparison form the book indexes on.</summary>
    [Fact]
    public async Task GetContactAsync_AnAddressWrittenInAnotherCase_ReachesTheBookAsTheSameAddress()
    {
        // Arrange
        var contact = StubContactBook.ContactOf("Anna Kowalska", "anna@example.test");
        var book = new StubContactBook();
        book.Directory
            .FindByAddressAsync(StubContactBook.Address("anna@example.test"), Arg.Any<CancellationToken>())
            .Returns(contact);

        var tool = new GetContactTool(book.Reader);

        // Act
        var result = await tool.GetContactAsync(
            address: "  Anna@Example.TEST  ",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(contact.Id.ToString(), result.Contact?.ContactId);
    }

    /// <summary>The note travels with the person, because withholding it would decide for an owner which of their own words an agent may read.</summary>
    [Fact]
    public async Task GetContactAsync_AContactCarryingANote_PublishesItWithTheRecord()
    {
        // Arrange
        var contact = Contact.Create(
            ContactId.Create(Guid.CreateVersion7(StubContactBook.Now)),
            ContactDisplayName.Create("Anna Kowalska"),
            [StubContactBook.Address("anna@example.test")],
            StubContactBook.Address("anna@example.test"),
            ContactNote.Create("Chartering broker."),
            ContactOrigin.Collected,
            StubContactBook.Now,
            StubContactBook.Now);

        var book = new StubContactBook();
        book.Directory.FindAsync(contact.Id, Arg.Any<CancellationToken>()).Returns(contact);

        var tool = new GetContactTool(book.Reader);

        // Act
        var result = await tool.GetContactAsync(
            contact.Id.ToString(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Chartering broker.", result.Contact?.Note);
        Assert.Equal(PublishedContactOrigin.Collected, result.Contact?.Origin);
    }
}
