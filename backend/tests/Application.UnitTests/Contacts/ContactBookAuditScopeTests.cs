// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts;

/// <summary>Covers which of the books a user reads an administrator scoped below the deployment is shown.</summary>
/// <remarks>
/// The directory is substituted, because what is asserted is the scope the book hands it: the user's own book whenever
/// the caller's scope covers the user, and each collected book only where the caller's scope also covers that mailbox.
/// </remarks>
public sealed class ContactBookAuditScopeTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("0198f0aa-0000-7000-8000-00000000f0cc");

    private static readonly ContactBookScope HoldersBooks = ContactBookScope.Of(AccessAuthorizations.ScopedHolder, [Account]);

    private readonly IContactDirectory directory = Substitute.For<IContactDirectory>();

    public static TheoryData<AssignmentScope> ScopesCoveringTheUser => [.. AccessAuthorizations.ScopesCoveringTheirTarget];

    public static TheoryData<AssignmentScope> ScopesOutsideTheUser => [.. AccessAuthorizations.ScopesOutsideTheirTarget];

    [Theory]
    [MemberData(nameof(ScopesCoveringTheUser))]
    public async Task ReadPageAsync_AnAdministratorScopedOverTheUserAndTheirMailbox_ReadsEveryBook(AssignmentScope scope)
    {
        // Arrange
        var book = this.BookFor(AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminAuditRead));

        // Act
        await book.ReadPageAsync(HoldersBooks, WholeBook(), TestContext.Current.CancellationToken);

        // Assert
        await this.directory.Received(1).ReadPageAsync(
            Arg.Is<ContactBookScope>(read => read!.Keys.SequenceEqual(HoldersBooks.Keys)),
            Arg.Any<ContactQuery>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(ScopesOutsideTheUser))]
    public async Task FindByAddressAsync_AnAdministratorScopedElsewhere_IsRefusedBeforeTheBooksAreRead(AssignmentScope scope)
    {
        // Arrange
        var book = this.BookFor(AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminAuditRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => book.FindByAddressAsync(
            HoldersBooks,
            SomeoneElse(),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminAuditRead, refusal.RequiredPermission);
        Assert.Empty(this.directory.ReceivedCalls());
    }

    /// <summary>A mailbox another person reads too is not one user's to administer, so a scope naming the user keeps their own book and leaves that collected book out.</summary>
    [Fact]
    public async Task FindAsync_AUserScopeOverSomebodyWhoseMailboxIsShared_ReadsTheirOwnBookAlone()
    {
        // Arrange
        var targets = Substitute.For<IAdministrativeTargets>();
        targets.PlaceUserAsync(AccessAuthorizations.ScopedHolder, Arg.Any<CancellationToken>())
            .Returns(AdministrativeTarget.User(AccessAuthorizations.ScopedHolder, organization: null));
        targets.PlaceMailAccountAsync(Account, Arg.Any<CancellationToken>())
            .Returns(AdministrativeTarget.MailAccount(
                organization: null,
                [AccessAuthorizations.ScopedHolder, UserId.Create(new Guid("0198f0aa-0000-7000-8000-00000000f0cd"))]));
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(AuthorizedPrincipal.Caller(
            "test-administrator",
            ScopedGrant.Of([(MailFathomPermission.AdminAuditRead, AssignmentScope.User(AccessAuthorizations.ScopedHolder))])));
        var book = this.BookFor(new AccessAuthorization(principals, targets));

        // Act
        await book.FindAsync(
            HoldersBooks,
            ContactId.Create(new Guid("0198f0aa-0000-7000-8000-00000000f0ce")),
            TestContext.Current.CancellationToken);

        // Assert
        await this.directory.Received(1).FindAsync(
            Arg.Is<ContactBookScope>(read => read!.Keys.SequenceEqual(new[] { HoldersBooks.OwnBook.Key })),
            Arg.Any<ContactId>(),
            Arg.Any<CancellationToken>());
    }

    private static EmailAddress SomeoneElse() =>
        EmailAddress.TryCreate(displayName: null, "someone@example.org", out var address)
            ? address
            : throw new InvalidOperationException("The test address names no mailbox.");

    private static ContactQuery WholeBook() => ContactQuery.Create(origin: null, search: null, pageSize: null, cursor: null);

    private ContactBook BookFor(AccessAuthorization authorization)
    {
        var timeProvider = new FakeTimeProvider();

        return new ContactBook(
            Substitute.For<IContactStore>(),
            this.directory,
            new OptimisticConcurrencyRetryPolicy(
                Substitute.For<IPersistenceSessionFactory>(),
                new PersistenceConcurrencyOptions(),
                timeProvider),
            timeProvider,
            authorization);
    }
}
