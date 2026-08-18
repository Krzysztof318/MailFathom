// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts;

/// <summary>Covers what a caller-facing write to the contact book checks, and the origin it acts under.</summary>
/// <remarks>
/// The book's own acts are covered where the book is; what is asserted here is the layer above them — the grant, the
/// rules a record obeys, and the fact that a caller writes as somebody who wrote the person down rather than as
/// collection.
/// </remarks>
public sealed class ContactBookWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A note holding somebody's address, so a refusal that repeated what it read would be visible.</summary>
    private const string PrivateNote = "reachable at anna@example.test";

    /// <summary>The writing grant is the authority, so holding the reading half reaches nothing here.</summary>
    [Fact]
    public async Task RecordAsync_ACallerWithoutTheWritingGrant_IsRefusedBeforeTheBookIsReached()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var writer = WriterOver(book, MailFathomPermission.MailContactsRead);

        // Act, Assert
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            writer.RecordAsync(DraftOf("Anna Kowalska", "anna@example.test"), TestContext.Current.CancellationToken));

        Assert.Equal(MailFathomPermission.MailContactsWrite, refusal.RequiredPermission);
        Assert.Equal(0, book.ContactCount);
    }

    /// <summary>Erasure is behind the writing grant, because a grant that cannot edit the book cannot take somebody out of it either.</summary>
    [Fact]
    public async Task EraseAsync_ACallerWithoutTheWritingGrant_IsRefused()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        book.Hold(ContactOf("Anna Kowalska", "anna@example.test", ContactOrigin.Asserted));

        var writer = WriterOver(book, MailFathomPermission.MailContactsRead);

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            writer.EraseAsync(AContactId(), TestContext.Current.CancellationToken));

        Assert.Equal(1, book.ContactCount);
    }

    /// <summary>An amendment rewrites a person's name, addresses, and note, so it is behind the same grant the other two are.</summary>
    [Fact]
    public async Task AmendAsync_ACallerWithoutTheWritingGrant_IsRefusedAndLeavesTheRecordAsItWas()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var held = ContactOf("Anna Kowalska", "anna@example.test", ContactOrigin.Asserted);
        book.Hold(held);

        var writer = WriterOver(book, MailFathomPermission.MailContactsRead);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => writer.AmendAsync(
            held.Id,
            DraftOf("Someone Else", "someone.else@example.test"),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailContactsWrite, refusal.RequiredPermission);
        var untouched = await book.FindAsync(held.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Anna Kowalska", untouched?.DisplayName.Value);
    }

    /// <summary>A caller granted the book writes for the owner, so what it records is a person somebody wrote down.</summary>
    [Fact]
    public async Task RecordAsync_AGrantedCaller_RecordsThePersonAsAsserted()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var writer = WriterOver(book);

        // Act
        var result = await writer.RecordAsync(
            DraftOf("Anna Kowalska", "anna@example.test"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, result.Outcome);
        Assert.Equal(ContactOrigin.Asserted, result.Contact?.Origin);
    }

    /// <summary>A record collected from arriving mail is not a caller's to edit in place, and the outcome says which rule refused it.</summary>
    [Fact]
    public async Task AmendAsync_AContactTheDeploymentCollected_IsRefusedByItsOrigin()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var collected = ContactOf("Anna Kowalska", "anna@example.test", ContactOrigin.Collected);
        book.Hold(collected);

        var writer = WriterOver(book);

        // Act
        var result = await writer.AmendAsync(
            collected.Id,
            DraftOf("Anna K.", "anna@example.test"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.OriginRefusesWriter, result.Outcome);
    }

    /// <summary>An amendment states the whole record, so the second identical call leaves the same one.</summary>
    [Fact]
    public async Task AmendAsync_TheSameRecordTwice_LeavesTheBookHoldingIt()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var held = ContactOf("Anna Kowalska", "anna@example.test", ContactOrigin.Asserted);
        book.Hold(held);

        var writer = WriterOver(book);
        var draft = DraftOf("Anna Nowak", "anna@example.test");

        // Act
        await writer.AmendAsync(held.Id, draft, TestContext.Current.CancellationToken);
        var second = await writer.AmendAsync(held.Id, draft, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, second.Outcome);
        Assert.Equal("Anna Nowak", second.Contact?.DisplayName.Value);
        Assert.Equal(1, book.ContactCount);
    }

    /// <summary>Somebody asking to be taken out of a book is not answered with which half of it they are in.</summary>
    [Fact]
    public async Task EraseAsync_AContactTheDeploymentCollected_IsErasedAllTheSame()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var collected = ContactOf("Anna Kowalska", "anna@example.test", ContactOrigin.Collected);
        book.Hold(collected);

        var writer = WriterOver(book);

        // Act
        var erasure = await writer.EraseAsync(collected.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(erasure.WasHeld);
        Assert.Equal(0, book.ContactCount);
    }

    /// <summary>Every rule the book holds is checked before it is reached, and each refusal names the rule rather than the value.</summary>
    [Theory]
    [MemberData(nameof(RecordsTheBookRefuses))]
    public async Task RecordAsync_ARecordTheBookRefuses_IsRefusedWithoutNamingTheValue(ContactRecordDraft draft)
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var writer = WriterOver(book);

        // Act
        var refusal = await Assert.ThrowsAsync<ContactRecordInvalidException>(() =>
            writer.RecordAsync(draft, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(0, book.ContactCount);
        Assert.DoesNotContain("@", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The records the rules above refuse, one per rule a caller can break.</summary>
    /// <remarks>
    /// Every draft carries a note naming somebody's address, and each draft whose broken rule is the name carries one
    /// holding an at-sign too, so the assertion above is one the refusal can fail wherever it echoes what it read. A
    /// refusal repeating a name a caller supplied publishes a person to the client and to the log exactly as one
    /// repeating an address does, and a theory whose names carried no at-sign would have stayed green through it.
    /// </remarks>
    public static TheoryData<ContactRecordDraft> RecordsTheBookRefuses() =>
    [
        new ContactRecordDraft
        {
            Addresses = ["anna@example.test"],
            PreferredAddress = "anna@example.test",
            Note = PrivateNote,
        },
        new ContactRecordDraft
        {
            DisplayName = "anna@example.test " + new string('n', ContactDisplayName.MaximumLength),
            Addresses = ["anna@example.test"],
            PreferredAddress = "anna@example.test",
            Note = PrivateNote,
        },
        new ContactRecordDraft { DisplayName = "Anna Kowalska", Addresses = [], Note = PrivateNote },
        new ContactRecordDraft
        {
            DisplayName = "Anna Kowalska",
            Addresses = [.. Enumerable.Range(0, Contact.MaximumAddressCount + 1).Select(index => $"anna{index}@example.test")],
            PreferredAddress = "anna0@example.test",
            Note = PrivateNote,
        },
        new ContactRecordDraft
        {
            DisplayName = "Anna Kowalska",
            Addresses = ["not-an-address"],
            PreferredAddress = "not-an-address",
            Note = PrivateNote,
        },
        new ContactRecordDraft
        {
            DisplayName = "Anna Kowalska",
            Addresses = ["<anna@example.test>"],
            PreferredAddress = "<anna@example.test>",
            Note = PrivateNote,
        },
        new ContactRecordDraft
        {
            DisplayName = "Anna Kowalska",
            Addresses = ["anna@example.test"],
            PreferredAddress = "someone.else@example.test",
            Note = PrivateNote,
        },
        new ContactRecordDraft
        {
            DisplayName = "Anna Kowalska",
            Addresses = ["anna@example.test"],
            PreferredAddress = "anna@example.test",
            Note = PrivateNote + new string('n', ContactNote.MaximumLength),
        },
    ];

    /// <summary>The ceiling counts the mailboxes a person is reachable at, so two spellings of one address spend one place.</summary>
    /// <remarks>
    /// What every published description promises is a number of addresses, and a caller repeating one — or writing it in
    /// another case — supplied no further mailbox. Counting the spellings instead would refuse a record the book holds
    /// happily.
    /// </remarks>
    [Fact]
    public async Task RecordAsync_TheSameAddressWrittenTwice_IsRecordedAsOneMailbox()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var writer = WriterOver(book);
        var draft = new ContactRecordDraft
        {
            DisplayName = "Anna Kowalska",
            Addresses =
            [
                .. Enumerable.Range(0, Contact.MaximumAddressCount - 1).Select(index => $"anna{index}@example.test"),
                "ANNA0@EXAMPLE.TEST",
            ],
            PreferredAddress = "anna0@example.test",
        };

        // Act
        var result = await writer.RecordAsync(draft, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, result.Outcome);
        Assert.Equal(Contact.MaximumAddressCount - 1, result.Contact?.Addresses.Count);
    }

    /// <summary>The same ceiling bounds the spellings a caller sends, so what one record costs to read is not the caller's to choose.</summary>
    /// <remarks>
    /// A ceiling on distinct mailboxes alone would admit a hundred thousand copies of one address: each is trimmed,
    /// length-checked, and parsed before the duplicate is dropped, and the record then written holds one address and
    /// refuses nothing. So the bound is applied to the values before any of them is read, which is also the number the
    /// administrative reader applies to the same two things.
    /// </remarks>
    [Fact]
    public async Task RecordAsync_MoreSpellingsThanTheCeilingAllNamingOneMailbox_IsRefused()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var writer = WriterOver(book);
        var draft = new ContactRecordDraft
        {
            DisplayName = "Anna Kowalska",
            Addresses = [.. Enumerable.Repeat("anna@example.test", Contact.MaximumAddressCount + 1)],
            PreferredAddress = "anna@example.test",
        };

        // Act, Assert
        await Assert.ThrowsAsync<ContactRecordInvalidException>(() =>
            writer.RecordAsync(draft, TestContext.Current.CancellationToken));

        Assert.Equal(0, book.ContactCount);
    }

    /// <summary>A contact without a note holds none, so blank text is the absence of one rather than an empty one.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RecordAsync_ABlankNote_RecordsNoNoteAtAll(string? note)
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var writer = WriterOver(book);
        var draft = DraftOf("Anna Kowalska", "anna@example.test") with { Note = note };

        // Act
        var result = await writer.RecordAsync(draft, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Contact?.Note);
    }

    private static ContactBookWriter WriterOver(
        InMemoryContactBookStore book,
        params IEnumerable<MailFathomPermission> permissions)
    {
        var granted = permissions.ToArray();
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(AuthorizedPrincipal.Caller(
            "a-caller",
            granted.Length == 0 ? [MailFathomPermission.MailContactsWrite] : granted));

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var timeProvider = new FakeTimeProvider(Now);

        return new ContactBookWriter(
            new ContactBook(
                book,
                book,
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), timeProvider),
                timeProvider,
                new AccessAuthorization(principals)),
            new AccessAuthorization(principals));
    }

    private static ContactRecordDraft DraftOf(string displayName, string address) => new()
    {
        DisplayName = displayName,
        Addresses = [address],
        PreferredAddress = address,
    };

    private static ContactId AContactId() => ContactId.Create(Guid.CreateVersion7(Now));

    private static Contact ContactOf(string displayName, string address, ContactOrigin origin) => Contact.Create(
        AContactId(),
        ContactDisplayName.Create(displayName),
        [Address(address)],
        Address(address),
        note: null,
        origin,
        Now,
        Now);

    private static EmailAddress Address(string address)
    {
        EmailAddress.TryCreate(displayName: null, address, out var emailAddress);

        return emailAddress;
    }

    /// <summary>A session that commits, because nothing here is about a conflict the policy has to retry.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
