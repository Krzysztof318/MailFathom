// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Failures;
using MailFathom.Domain.Contacts;
using MailFathom.Mcp.Tools.Contacts;
using MailFathom.Mcp.UnitTests.TestDoubles;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Contacts;

/// <summary>Covers the one crossing between the two halves of the book, and what each way it can end reads as.</summary>
public sealed class PromoteContactToolTests
{
    /// <summary>A record the deployment collected becomes one the owner asserted, and the answer says so.</summary>
    [Fact]
    public async Task PromoteContactAsync_AContactTheDeploymentCollected_PublishesItAsAsserted()
    {
        // Arrange
        var collected = StubContactBook.ContactOf("Anna Kowalska", "anna@example.test", ContactOrigin.Collected);
        var book = new StubContactBook();
        book.Directory.FindAsync(collected.Id, Arg.Any<CancellationToken>()).Returns(collected);

        var tool = new PromoteContactTool(book.Writer);

        // Act
        var result = await tool.PromoteContactAsync(
            collected.Id.ToString(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteState.Written, result.State);
        Assert.Equal(PublishedContactOrigin.Asserted, result.Contact?.Origin);
        Assert.Equal("Anna Kowalska", result.Contact?.DisplayName);
    }

    /// <summary>Asking twice is asking once, so the second call answers with the state the first left the record in.</summary>
    [Fact]
    public async Task PromoteContactAsync_AContactTheOwnerAlreadyAsserted_AnswersThatNothingWasLeftToDo()
    {
        // Arrange
        var asserted = StubContactBook.ContactOf("Anna Kowalska", "anna@example.test");
        var book = new StubContactBook();
        book.Directory.FindAsync(asserted.Id, Arg.Any<CancellationToken>()).Returns(asserted);

        var tool = new PromoteContactTool(book.Writer);

        // Act
        var result = await tool.PromoteContactAsync(
            asserted.Id.ToString(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteState.AlreadyAsserted, result.State);
        Assert.Null(result.Contact);
    }

    /// <summary>A refusal publishes an identity and never a record, so a promotion of nobody says only that.</summary>
    [Fact]
    public async Task PromoteContactAsync_AContactTheBookDoesNotHold_AnswersThatItFoundNone()
    {
        // Arrange
        var book = new StubContactBook();
        book.Directory
            .FindAsync(Arg.Any<ContactId>(), Arg.Any<CancellationToken>())
            .Returns((Contact?)null);

        var tool = new PromoteContactTool(book.Writer);

        // Act
        var result = await tool.PromoteContactAsync(
            Guid.CreateVersion7(StubContactBook.Now).ToString(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteState.NotFound, result.State);
        Assert.Null(result.Contact);
    }

    /// <summary>Text that names no contact this system issued is a caller's mistake rather than an answer about the book.</summary>
    [Fact]
    public async Task PromoteContactAsync_AnIdentifierThisSystemNeverIssued_IsRefused()
    {
        // Arrange
        var tool = new PromoteContactTool(new StubContactBook().Writer);

        // Act & Assert
        await Assert.ThrowsAsync<ContactIdentifierMalformedException>(() =>
            tool.PromoteContactAsync("not-a-contact", TestContext.Current.CancellationToken));
    }
}
