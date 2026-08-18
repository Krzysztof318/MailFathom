// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts;

/// <summary>Covers what a caller-facing read of the contact book checks before the store is reached.</summary>
/// <remarks>
/// The use case owns two things the store does not: the grant the caller has to hold, and every bound a page is served
/// under. Both are asserted with the transport absent, because the transport is what an entrypoint added later would
/// arrive without.
/// </remarks>
public sealed class ContactBookReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The reading grant is the authority, so a caller granted everything else reaches nothing here.</summary>
    [Fact]
    public async Task ReadPageAsync_ACallerWithoutTheReadingGrant_IsRefusedBeforeTheStoreIsReached()
    {
        // Arrange
        var directory = Substitute.For<IContactDirectory>();
        var reader = ReaderOver(directory, MailFathomPermission.MailContactsWrite);

        // Act, Assert
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken));

        Assert.Equal(MailFathomPermission.MailContactsRead, refusal.RequiredPermission);
        await directory.DidNotReceiveWithAnyArgs().ReadPageAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>Work no caller requested holds no permission, so it is refused rather than admitted as a caller with everything.</summary>
    [Fact]
    public async Task FindAsync_TheProcessIdentity_IsRefused()
    {
        // Arrange
        var directory = Substitute.For<IContactDirectory>();
        var reader = new ContactBookReader(
            directory,
            AuthorizationOf(AuthorizedPrincipal.Process));

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.FindAsync(AContactId(), TestContext.Current.CancellationToken));
    }

    /// <summary>An entrypoint that never stated what admitted it fails rather than defaulting to permitted.</summary>
    [Fact]
    public async Task FindByAddressAsync_NoPrincipalAtAll_IsRefused()
    {
        // Arrange
        var directory = Substitute.For<IContactDirectory>();
        var reader = new ContactBookReader(
            directory,
            AuthorizationOf(principal: null));

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.FindByAddressAsync(Address("anna@example.test"), TestContext.Current.CancellationToken));
    }

    /// <summary>A caller that asks for nothing is served the book's default page rather than the whole book.</summary>
    [Fact]
    public async Task ReadPageAsync_ARequestThatNarrowsNothing_ReadsTheDefaultPageOfTheWholeBook()
    {
        // Arrange
        var directory = DirectoryAnswering(new ContactPage([], NextCursor: null));
        var reader = ReaderOver(directory);

        // Act
        await reader.ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);

        // Assert
        var query = QueryReadBy(directory);

        Assert.Equal(ContactQuery.DefaultPageSize, query.PageSize);
        Assert.Null(query.Origin);
        Assert.Null(query.Search);
        Assert.Null(query.Cursor);
    }

    /// <summary>A page size inside the bound is the caller's, so the book reads exactly what was asked for.</summary>
    [Fact]
    public async Task ReadPageAsync_APageSizeInsideTheBound_ReadsThatManyRatherThanTheDefault()
    {
        // Arrange
        var directory = DirectoryAnswering(new ContactPage([], NextCursor: null));
        var reader = ReaderOver(directory);
        var pageSize = ContactQuery.DefaultPageSize + 1;

        // Act
        await reader.ReadPageAsync(
            new ContactPageRequest { PageSize = pageSize },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(pageSize, QueryReadBy(directory).PageSize);
    }

    /// <summary>The ceiling is refused rather than clamped, so a short page never reads as the end of the book.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ContactQuery.MaximumPageSize + 1)]
    public async Task ReadPageAsync_APageSizeOutsideTheBound_IsRefused(int pageSize)
    {
        // Arrange
        var directory = Substitute.For<IContactDirectory>();
        var reader = ReaderOver(directory);

        // Act, Assert
        await Assert.ThrowsAsync<ContactQueryInvalidException>(() =>
            reader.ReadPageAsync(
                new ContactPageRequest { PageSize = pageSize },
                TestContext.Current.CancellationToken));

        await directory.DidNotReceiveWithAnyArgs().ReadPageAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>An origin nothing declares would narrow the page to a half of the book that does not exist.</summary>
    [Fact]
    public async Task ReadPageAsync_AnOriginNothingDeclares_IsRefused()
    {
        // Arrange
        var reader = ReaderOver(Substitute.For<IContactDirectory>());

        // Act, Assert
        await Assert.ThrowsAsync<ContactQueryInvalidException>(() =>
            reader.ReadPageAsync(
                new ContactPageRequest { Origin = (ContactOrigin)42 },
                TestContext.Current.CancellationToken));
    }

    /// <summary>The search is matched against the two comparison forms the book stores, so it is derived once here.</summary>
    [Fact]
    public async Task ReadPageAsync_ASearch_ReachesTheStoreInItsComparisonForm()
    {
        // Arrange
        var directory = DirectoryAnswering(new ContactPage([], NextCursor: null));
        var reader = ReaderOver(directory);

        // Act
        await reader.ReadPageAsync(
            new ContactPageRequest { Search = "  Kowalska " },
            TestContext.Current.CancellationToken);

        // Assert
        var search = Assert.IsType<ContactSearch>(QueryReadBy(directory).Search);

        Assert.Equal("Kowalska", search.Text);
        Assert.Equal("KOWALSKA", search.ComparisonForm);
    }

    /// <summary>A caller writing an empty argument asked for no narrowing, not for somebody whose name is empty.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReadPageAsync_ABlankSearch_NarrowsNothing(string search)
    {
        // Arrange
        var directory = DirectoryAnswering(new ContactPage([], NextCursor: null));
        var reader = ReaderOver(directory);

        // Act
        await reader.ReadPageAsync(new ContactPageRequest { Search = search }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(QueryReadBy(directory).Search);
    }

    /// <summary>Search text the book cannot look anybody up by is refused rather than run as a filter matching nothing.</summary>
    [Fact]
    public async Task ReadPageAsync_ASearchTheBookCannotUse_IsRefused()
    {
        // Arrange
        var reader = ReaderOver(Substitute.For<IContactDirectory>());

        // Act, Assert
        await Assert.ThrowsAsync<ContactQueryInvalidException>(() =>
            reader.ReadPageAsync(
                new ContactPageRequest { Search = new string('a', ContactSearch.MaximumLength + 1) },
                TestContext.Current.CancellationToken));
    }

    /// <summary>A cursor nothing here issued would serve a page from a boundary this deployment never chose.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorThisSystemDidNotIssue_IsRefused()
    {
        // Arrange
        var directory = Substitute.For<IContactDirectory>();
        var reader = ReaderOver(directory);

        // Act, Assert
        await Assert.ThrowsAsync<ContactCursorMalformedException>(() =>
            reader.ReadPageAsync(
                new ContactPageRequest { Cursor = "not-a-cursor-this-system-issued" },
                TestContext.Current.CancellationToken));

        await directory.DidNotReceiveWithAnyArgs().ReadPageAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>A cursor this deployment issued names the boundary the next page reads beyond, and reaches the store unchanged.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorThisSystemIssued_ReachesTheStoreAsTheBoundaryItNames()
    {
        // Arrange
        var directory = DirectoryAnswering(new ContactPage([], NextCursor: null));
        var reader = ReaderOver(directory);
        var cursor = ContactCursor.After(ContactDisplayName.Create("Anna Kowalska"), AContactId());

        // Act
        await reader.ReadPageAsync(
            new ContactPageRequest { Cursor = cursor.Encode() },
            TestContext.Current.CancellationToken);

        // Assert
        var read = QueryReadBy(directory).Cursor;

        Assert.Equal("ANNA KOWALSKA", read?.DisplayNameSortKey);
        Assert.Equal(cursor.ContactId, read?.ContactId);
    }

    /// <summary>The lookup an agent reaches for once it has an address is answered from the address index rather than from a search.</summary>
    [Fact]
    public async Task FindByAddressAsync_AGrantedCaller_ResolvesThroughTheAddressLookup()
    {
        // Arrange
        var directory = Substitute.For<IContactDirectory>();
        var address = Address("anna@example.test");
        var contact = ContactOf("Anna Kowalska", "anna@example.test");
        directory.FindByAddressAsync(address, Arg.Any<CancellationToken>()).Returns(contact);

        var reader = ReaderOver(directory);

        // Act
        var held = await reader.FindByAddressAsync(address, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(contact, held);
    }

    /// <summary>Answers for whoever reached the use case, which is the whole of what the transport tells this layer.</summary>
    private static AccessAuthorization AuthorizationOf(AuthorizedPrincipal? principal)
    {
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(principal);

        return new AccessAuthorization(principals);
    }

    private static ContactBookReader ReaderOver(
        IContactDirectory directory,
        params IEnumerable<MailFathomPermission> permissions)
    {
        var granted = permissions.ToArray();

        return new ContactBookReader(
            directory,
            AuthorizationOf(AuthorizedPrincipal.Caller(
                "a-caller",
                granted.Length == 0 ? [MailFathomPermission.MailContactsRead] : granted)));
    }

    private static IContactDirectory DirectoryAnswering(ContactPage page)
    {
        var directory = Substitute.For<IContactDirectory>();
        directory.ReadPageAsync(Arg.Any<ContactQuery>(), Arg.Any<CancellationToken>()).Returns(page);

        return directory;
    }

    /// <summary>Reads the query the use case composed, which is what every bound above is asserted against.</summary>
    private static ContactQuery QueryReadBy(IContactDirectory directory) =>
        (ContactQuery)directory.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IContactDirectory.ReadPageAsync)).GetArguments()[0]!;

    private static ContactId AContactId() => ContactId.Create(Guid.CreateVersion7(Now));

    private static Contact ContactOf(string displayName, string address) => Contact.Create(
        AContactId(),
        ContactDisplayName.Create(displayName),
        [Address(address)],
        Address(address),
        note: null,
        ContactOrigin.Asserted,
        Now,
        Now);

    private static EmailAddress Address(string address)
    {
        EmailAddress.TryCreate(displayName: null, address, out var emailAddress);

        return emailAddress;
    }
}
