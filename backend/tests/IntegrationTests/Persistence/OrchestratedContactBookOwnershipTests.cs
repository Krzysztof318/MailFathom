// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that a book belongs to one holder and that a reader reads several of them, where the rules that make it so actually live.</summary>
/// <remarks>
/// <para>
/// Every claim here is PostgreSQL's rather than the application's. That two books may each hold a contact for one
/// address is the unique index being over the book and the address rather than over the address, which nothing but a
/// second insert against a real index can establish — a substitute would report whatever rule it was written with.
/// Which of two books answers for an address they both hold is a correlated read over the ordered list of books the
/// scope carries, translated to <c>array_position</c>, so it is the database that applies the precedence. That
/// emptying one account's collected book leaves another's is a set-based statement no change tracker sees. And that
/// reading a book does not scan the whole table is a query plan, which is a statement only the planner can make and
/// only over enough rows that, without the holder leading the index, a sequential scan would have been the cheaper
/// plan.
/// </para>
/// <para>
/// The second user is provisioned by this class and erased by it, including on a failure, because a deployment whose
/// mail accounts still come from configuration holds exactly one user record and every folder binding a later class
/// arranges is resolved against that. Erasing it takes the seeded book with it through the same cascade
/// <see cref="OrchestratedUserErasureTests" /> asserts, so nothing here cleans up a contact by hand.
/// </para>
/// <para>
/// Every address belongs to a domain no other class writes into, because the index the claims turn on is unique within
/// a book and this suite shares one database.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedContactBookOwnershipTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The instant a directly constructed contact is stamped with, fixed so no test here reads a clock.</summary>
    private static readonly DateTimeOffset RecordedAt = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Enough contacts in one book for a sequential scan to be the more expensive plan, so a listing that reaches for
    /// the index is doing so because the index helps rather than because the table is too small for the choice to
    /// matter.
    /// </summary>
    private const int SeededContactCount = 600;

    private const int PageSize = 50;

    /// <summary>The address both users hold, which is the whole point of the pair of writes it is used by.</summary>
    private const string SharedAddress = "shared@ownership.contacts.test";

    /// <summary>The name a foreign user writes down and the served user asks about, which nothing of theirs answers.</summary>
    private const string ForeignOnlyDisplayName = "Ownership Namesake";

    /// <summary>The address beneath that person, asked about the same way and by the same reads.</summary>
    private const string ForeignOnlyAddress = "namesake@ownership.contacts.test";

    /// <summary>The collected person each account holds, which is what the set-based erasure is aimed at one book of.</summary>
    private const string CollectedOnFirstAddress = "collected-first@ownership.contacts.test";

    private const string CollectedOnSecondAddress = "collected-second@ownership.contacts.test";

    /// <summary>The address a shared mailbox collected and one of its users then wrote down under a name of their own.</summary>
    private const string SharedCollectedAddress = "shared-mailbox@ownership.contacts.test";

    private const string SharedCollectedDisplayName = "Ownership Collected Shared";

    private const string SharedWrittenDisplayName = "Ownership Written Shared";

    /// <summary>
    /// Two mail accounts this class alone collects into. Each is provisioned as a <c>mailbox_accounts</c> row before
    /// anything is collected into it, because a collected book keys onto the account the way a folder and a thread do.
    /// </summary>
    private static readonly MailAccountId FirstAccount = MailAccountId.Create("ownership-contacts-account-one");

    private static readonly MailAccountId SecondAccount = MailAccountId.Create("ownership-contacts-account-two");

    /// <summary>A reader whose own book nothing ever writes into, so a scope built on it answers from the account alone.</summary>
    private static readonly MailUserId NobodysBook = MailUserId.Create(Guid.CreateVersion7());

    /// <summary>Reads one page of one user's book in the order the listing index declares.</summary>
    private const string FirstListingPageSql =
        """
        SELECT "Id"
        FROM contacts
        WHERE "BookHolderId" = @bookHolderId
        ORDER BY "DisplayNameSortKey", "Id"
        LIMIT @pageSize
        """;

    /// <summary>Resolves the person one address belongs to, in one book.</summary>
    private const string AddressLookupSql =
        """
        SELECT "ContactId"
        FROM contact_addresses
        WHERE "BookHolderId" = @bookHolderId AND "NormalizedAddress" = @normalizedAddress
        """;

    /// <summary>
    /// One address is one person's within one book and says nothing about anybody else's, so two users each recording
    /// the same correspondent both succeed and each resolves the address to their own record.
    /// </summary>
    [Fact]
    public async Task ContactAddresses_OneAddressInEachOfTwoUsersBooks_AreBothHeldAndResolveToEachUsersOwn()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var servedUser = services.ServedUser;
        var foreignUserId = Guid.CreateVersion7();
        var foreignUser = MailUserId.Create(foreignUserId);
        Contact? ours = null;

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, foreignUserId, cancellationToken));

            ours = ContactOf("Ownership Ours", SharedAddress);
            var theirs = ContactOf("Ownership Theirs", SharedAddress);

            // Act
            var written = ours;
            var oursCommit = await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IContactStore>()
                    .AddAsync(session, ContactBookHolder.Of(servedUser), written, token),
                cancellationToken);

            var theirsCommit = await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IContactStore>()
                    .AddAsync(session, ContactBookHolder.Of(foreignUser), theirs, token),
                cancellationToken);

            var heldByUs = await FindByAddressAsync(services, OwnBookOf(servedUser), SharedAddress, cancellationToken);
            var heldByThem = await FindByAddressAsync(services, OwnBookOf(foreignUser), SharedAddress, cancellationToken);

            // Assert
            Assert.Equal(PersistenceCommitResult.Committed, oursCommit);
            Assert.Equal(PersistenceCommitResult.Committed, theirsCommit);
            Assert.Equal(ours.Id, heldByUs?.Id);
            Assert.Equal(theirs.Id, heldByThem?.Id);
        }
        finally
        {
            // The provisioned user goes first, because the erasure below is asserted and an assertion that fails is a
            // user left behind: ReadSoleUserAsync reads the sole user with SingleAsync, so a second settings_accounts
            // row makes every later start in this collection throw in classes that never touched a contact.
            try
            {
                // The one erasure this class performs by hand. The served user is not taken by the foreign-user erasure, so a
                // contact left behind would make the next run against this database fail at the first AddAsync on the
                // address uniqueness rather than at the assertion that actually broke.
                if (ours is not null)
                {
                    Assert.Equal(
                        PersistenceCommitResult.Committed,
                        await services.CommitAsync(
                            (scope, session, token) => scope.GetRequiredService<IContactStore>()
                                .EraseAsync(session, OwnBookOf(servedUser), ours.Id, token),
                            cancellationToken));
                }
            }
            finally
            {
                await OrchestratedForeignUser.EraseAsync(services, foreignUserId);
            }
        }
    }

    /// <summary>
    /// The four reads answered in batches are scoped by the same user the two indexed reads are, against the real
    /// LINQ and the real database: a person only another user wrote down is nobody by name, nobody by address, and
    /// nobody by identity.
    /// </summary>
    /// <remarks>
    /// These four gain their user predicate as an ordinary <c>Where</c> clause rather than as an index the planner has
    /// to choose, which is exactly why a substitute settles nothing about them — the fake would report whatever rule it
    /// was written with. Losing the predicate on any of them is the weakness the issue names, reachable again: a
    /// namesake in another book makes a name lookup report two carriers, and a resolved recipient becomes somebody
    /// else's correspondent.
    /// </remarks>
    [Fact]
    public async Task ContactDirectory_APersonOnlyAnotherUserWroteDown_IsAnsweredForByNoneOfTheBatchedReads()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var servedUser = services.ServedUser;
        var foreignUserId = Guid.CreateVersion7();
        var foreignUser = MailUserId.Create(foreignUserId);

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, foreignUserId, cancellationToken));

            var theirs = ContactOf(ForeignOnlyDisplayName, ForeignOnlyAddress);

            Assert.Equal(
                PersistenceCommitResult.Committed,
                await services.CommitAsync(
                    (scope, session, token) => scope.GetRequiredService<IContactStore>()
                        .AddAsync(session, ContactBookHolder.Of(foreignUser), theirs, token),
                    cancellationToken));

            // Act
            var matchedForUs = await MatchDisplayNamesAsync(services, OwnBookOf(servedUser), cancellationToken);
            var heldForUs = await FindHoldersOfAsync(services, ContactBookHolder.Of(servedUser), cancellationToken);
            var byIdentityForUs = await FindAsync(services, OwnBookOf(servedUser), theirs.Id, cancellationToken);
            var allByIdentityForUs = await FindAllAsync(services, OwnBookOf(servedUser), theirs.Id, cancellationToken);

            var matchedForThem = await MatchDisplayNamesAsync(services, OwnBookOf(foreignUser), cancellationToken);
            var heldForThem = await FindHoldersOfAsync(services, ContactBookHolder.Of(foreignUser), cancellationToken);
            var byIdentityForThem = await FindAsync(services, OwnBookOf(foreignUser), theirs.Id, cancellationToken);
            var allByIdentityForThem = await FindAllAsync(services, OwnBookOf(foreignUser), theirs.Id, cancellationToken);

            // Assert
            Assert.Equal(0, matchedForUs[ContactDisplayName.Create(ForeignOnlyDisplayName)].MatchCount);
            Assert.Empty(heldForUs);
            Assert.Null(byIdentityForUs);
            Assert.Empty(allByIdentityForUs);

            // The control the absences above rest on: the same four reads under the user who does hold the person
            // answer with them, so an observation channel that silently reported nothing would fail here instead of
            // passing everything.
            Assert.Equal(1, matchedForThem[ContactDisplayName.Create(ForeignOnlyDisplayName)].MatchCount);
            Assert.Equal(theirs.Id, heldForThem[Address(ForeignOnlyAddress)]);
            Assert.Equal(theirs.Id, byIdentityForThem?.Id);
            Assert.Equal(theirs.Id, Assert.Single(allByIdentityForThem).Value.Id);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, foreignUserId);
        }
    }

    /// <summary>
    /// Giving up on collection gives up on one mail account's book. It is the one statement over the books whose
    /// blast radius would be the whole table, so it is asked with a second account's collected rows present.
    /// </summary>
    /// <remarks>
    /// Nothing a substitute can settle: this is a set-based delete no change tracker sees, and its predicate is the
    /// only thing standing between one mailbox's collection being switched off and every mailbox's collected contacts
    /// going with it.
    /// </remarks>
    [Fact]
    public async Task EraseCollectedAsync_ACollectedPersonInEachOfTwoAccountsBooks_TakesOnlyTheOneItWasAskedOf()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        try
        {
            await OrchestratedMailboxAccountRows.HoldAsync(services, [FirstAccount, SecondAccount], cancellationToken);

            var onTheFirst = CollectedContactOf("Ownership Collected First", CollectedOnFirstAddress);
            var onTheSecond = CollectedContactOf("Ownership Collected Second", CollectedOnSecondAddress);

            Assert.Equal(
                PersistenceCommitResult.Committed,
                await services.CommitAsync(
                    (scope, session, token) => scope.GetRequiredService<IContactStore>()
                        .AddAsync(session, ContactBookHolder.Of(FirstAccount), onTheFirst, token),
                    cancellationToken));

            Assert.Equal(
                PersistenceCommitResult.Committed,
                await services.CommitAsync(
                    (scope, session, token) => scope.GetRequiredService<IContactStore>()
                        .AddAsync(session, ContactBookHolder.Of(SecondAccount), onTheSecond, token),
                    cancellationToken));

            // Act
            var erasure = await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IContactStore>()
                    .EraseCollectedAsync(session, FirstAccount, token),
                cancellationToken);

            var firstAfter = await FindByAddressAsync(
                services,
                CollectedBookOf(FirstAccount),
                CollectedOnFirstAddress,
                cancellationToken);
            var secondAfter = await FindByAddressAsync(
                services,
                CollectedBookOf(SecondAccount),
                CollectedOnSecondAddress,
                cancellationToken);

            // Assert
            Assert.Equal(PersistenceCommitResult.Committed, erasure);
            Assert.Null(firstAfter);

            // The control the absence rests on, and the claim the predicate is actually about: the other mailbox's
            // collected person and the address row beneath them are untouched, which FindByAddressAsync reads through.
            Assert.Equal(onTheSecond.Id, secondAfter?.Id);
            Assert.Equal(ContactOrigin.Collected, secondAfter?.Origin);
        }
        finally
        {
            await EraseCollectedBooksAsync(services, cancellationToken);
        }
    }

    /// <summary>
    /// A mailbox two people are assigned holds one collected record that both of them read, and a record one of them
    /// writes down hides it for that one alone.
    /// </summary>
    /// <remarks>
    /// The hiding is a correlated read over the ordered list of books the scope carries, translated as
    /// <c>array_position</c> against the holder column, so it is the database that decides which of two records a page
    /// serves. A substitute would report whatever precedence it was written with, and the thing that would break here
    /// silently — a page whose two books both answer for one address — is a person served twice under two names.
    /// </remarks>
    [Fact]
    public async Task ReadPageAsync_AnAccountTwoUsersShare_ServesOneRecordToEachAndLetsAUsersOwnHideIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var servedUser = services.ServedUser;

        // A second reader of the same mailbox. Nothing is written into their own book, so they need no user record:
        // what the claim is about is the account's book being in both scopes.
        var otherUser = MailUserId.Create(Guid.CreateVersion7());
        var collected = CollectedContactOf(SharedCollectedDisplayName, SharedCollectedAddress);
        Contact? written = null;

        try
        {
            await OrchestratedMailboxAccountRows.HoldAsync(services, [FirstAccount, SecondAccount], cancellationToken);

            Assert.Equal(
                PersistenceCommitResult.Committed,
                await services.CommitAsync(
                    (scope, session, token) => scope.GetRequiredService<IContactStore>()
                        .AddAsync(session, ContactBookHolder.Of(FirstAccount), collected, token),
                    cancellationToken));

            // Act
            var beforeForUs = await FindByAddressAsync(
                services,
                ContactBookScope.Of(servedUser, [FirstAccount]),
                SharedCollectedAddress,
                cancellationToken);
            var beforeForThem = await FindByAddressAsync(
                services,
                ContactBookScope.Of(otherUser, [FirstAccount]),
                SharedCollectedAddress,
                cancellationToken);

            written = ContactOf(SharedWrittenDisplayName, SharedCollectedAddress);
            var ours = written;

            Assert.Equal(
                PersistenceCommitResult.Committed,
                await services.CommitAsync(
                    (scope, session, token) => scope.GetRequiredService<IContactStore>()
                        .AddAsync(session, ContactBookHolder.Of(servedUser), ours, token),
                    cancellationToken));

            var afterForUs = await FindByAddressAsync(
                services,
                ContactBookScope.Of(servedUser, [FirstAccount]),
                SharedCollectedAddress,
                cancellationToken);
            var afterForThem = await FindByAddressAsync(
                services,
                ContactBookScope.Of(otherUser, [FirstAccount]),
                SharedCollectedAddress,
                cancellationToken);

            var pageForUs = await ReadPageAsync(
                services,
                ContactBookScope.Of(servedUser, [FirstAccount]),
                cancellationToken);

            // Assert
            Assert.Equal(collected.Id, beforeForUs?.Id);
            Assert.Equal(collected.Id, beforeForThem?.Id);

            // The user who wrote the person down reads their own record; the other reader of the same mailbox goes on
            // reading the collected one, which is what makes this a precedence rather than an edit.
            Assert.Equal(ours.Id, afterForUs?.Id);
            Assert.Equal(collected.Id, afterForThem?.Id);

            // And a listing over both books answers for the address once rather than serving one person twice.
            Assert.Equal(
                [SharedWrittenDisplayName],
                pageForUs.Contacts
                    .Where(contact => contact.Holds(Address(SharedCollectedAddress)))
                    .Select(contact => contact.DisplayName.Value));
        }
        finally
        {
            try
            {
                if (written is not null)
                {
                    await services.CommitAsync(
                        (scope, session, token) => scope.GetRequiredService<IContactStore>()
                            .EraseAsync(session, OwnBookOf(servedUser), written.Id, token),
                        cancellationToken);
                }
            }
            finally
            {
                await EraseCollectedBooksAsync(services, cancellationToken);
            }
        }
    }

    /// <summary>A read of one book reads that book, which is the index leading with the user and a plan that says so.</summary>
    /// <remarks>
    /// The queries are written here rather than taken from the read model, for the reason the same claim about the mail
    /// timeline is: what is asserted is that the schema can serve the read from an index, which is a property of the
    /// schema. That the read model produces this shape is what the walk in <see cref="OrchestratedContactBookTests" />
    /// establishes, over the same two columns in the same order.
    /// </remarks>
    [Fact]
    public async Task ReadPageAsync_ABookOfSixHundredUnderAnotherUser_ServesNoneOfItAndIsPlannedThroughTheUsersIndexes()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var foreignUserId = Guid.CreateVersion7();
        var foreignUser = MailUserId.Create(foreignUserId);

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, foreignUserId, cancellationToken));
            await SeedBookAsync(services, foreignUserId, cancellationToken);

            // Act
            var theirPage = await ReadPageAsync(services, OwnBookOf(foreignUser), cancellationToken);
            var ourPage = await ReadPageAsync(services, OwnBookOf(services.ServedUser), cancellationToken);

            var listingPlan = await OrchestratedQueryPlans.ReadAsync(
                services,
                FirstListingPageSql,
                [BookParameter(ContactBookHolder.Of(foreignUser).Key), PageSizeParameter(PageSize)],
                cancellationToken);

            var addressPlan = await OrchestratedQueryPlans.ReadAsync(
                services,
                AddressLookupSql,
                [
                    BookParameter(ContactBookHolder.Of(foreignUser).Key),
                    NormalizedAddressParameter(SeededAddress(0).ToUpperInvariant()),
                ],
                cancellationToken);

            // Assert
            // Their book is served whole and in the index's order, which is what makes the absence below an absence
            // rather than a read that found nothing at all.
            Assert.Equal(PageSize, theirPage.Contacts.Count);
            Assert.Equal(
                theirPage.Contacts.Select(contact => contact.DisplayName.Value).Order(StringComparer.Ordinal),
                theirPage.Contacts.Select(contact => contact.DisplayName.Value));
            Assert.All(
                theirPage.Contacts,
                contact => Assert.StartsWith(SeededNamePrefix, contact.DisplayName.Value, StringComparison.Ordinal));

            // The same read as the user this deployment serves reaches none of it, whatever else that book holds from
            // the classes that ran before this one.
            Assert.DoesNotContain(
                ourPage.Contacts,
                contact => contact.DisplayName.Value.StartsWith(SeededNamePrefix, StringComparison.Ordinal));

            Assert.Contains(
                PersistenceConstraintNames.ContactListingIndexName,
                listingPlan,
                StringComparison.Ordinal);
            Assert.Contains(
                PersistenceConstraintNames.ContactAddressUniqueIndexName,
                addressPlan,
                StringComparison.Ordinal);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, foreignUserId);
        }
    }

    /// <summary>The name every seeded contact carries, which is what makes them recognizable in somebody else's page.</summary>
    private const string SeededNamePrefix = "Ownership Seeded ";

    private static string SeededName(int position) => $"{SeededNamePrefix}{position:D4}";

    private static string SeededAddress(int position) => $"seeded-{position:D4}@ownership.contacts.test";

    /// <summary>Writes a book large enough that reading a page of it is a choice the planner has to make.</summary>
    private static async Task SeedBookAsync(
        OrchestratedMailFathomServices services,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            async (_, session, token) =>
            {
                var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                foreach (var position in Enumerable.Range(0, SeededContactCount))
                {
                    var contact = ContactOf(SeededName(position), SeededAddress(position));

                    context.Contacts.Add(ContactRowOf(userId, contact));
                    context.ContactAddresses.Add(AddressRowOf(userId, contact));
                }

                await context.SaveChangesAsync(token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        // Without it the planner is reading whatever the last automatic pass left, which on a table this suite has
        // filled a few hundred rows into within one run is usually nothing at all.
        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database
                .ExecuteSqlRawAsync("ANALYZE contacts, contact_addresses", token),
            cancellationToken);
    }

    private static ContactEntity ContactRowOf(Guid userId, Contact contact) => new()
    {
        Id = contact.Id.Value,
        BookHolderId = userId.ToString("D"),
        UserId = userId,
        DisplayName = contact.DisplayName.Value,
        DisplayNameSortKey = contact.DisplayName.SortKey,
        PreferredNormalizedAddress = contact.PreferredAddress.NormalizedAddress,
        Origin = contact.Origin,
        RecordedAt = contact.RecordedAt,
        AmendedAt = contact.AmendedAt,
    };

    private static ContactAddressEntity AddressRowOf(Guid userId, Contact contact) => new()
    {
        Id = Guid.CreateVersion7(RecordedAt),
        ContactId = contact.Id.Value,
        BookHolderId = userId.ToString("D"),
        Address = contact.PreferredAddress.Address,
        NormalizedAddress = contact.PreferredAddress.NormalizedAddress,
    };

    private static Contact CollectedContactOf(string displayName, string address) =>
        ContactOf(displayName, address, ContactOrigin.Collected);

    private static Contact ContactOf(string displayName, string address) =>
        ContactOf(displayName, address, ContactOrigin.Asserted);

    private static Contact ContactOf(string displayName, string address, ContactOrigin origin) => Contact.Create(
        ContactId.Create(Guid.CreateVersion7(RecordedAt)),
        ContactDisplayName.Create(displayName),
        [Address(address)],
        Address(address),
        note: null,
        origin,
        RecordedAt,
        RecordedAt);

    private static EmailAddress Address(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return emailAddress;
    }

    private static Task<ContactPage> ReadPageAsync(
        OrchestratedMailFathomServices services,
        ContactBookScope books,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().ReadPageAsync(
                books,
                ContactQuery.Create(origin: null, search: null, PageSize, cursor: null),
                token),
            cancellationToken);

    private static Task<Contact?> FindByAddressAsync(
        OrchestratedMailFathomServices services,
        ContactBookScope books,
        string address,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>()
                .FindByAddressAsync(books, Address(address), token),
            cancellationToken);

    private static Task<IReadOnlyDictionary<ContactDisplayName, ContactMatch>> MatchDisplayNamesAsync(
        OrchestratedMailFathomServices services,
        ContactBookScope books,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().MatchDisplayNamesAsync(
                books,
                [ContactDisplayName.Create(ForeignOnlyDisplayName)],
                token),
            cancellationToken);

    private static Task<IReadOnlyDictionary<EmailAddress, ContactId>> FindHoldersOfAsync(
        OrchestratedMailFathomServices services,
        ContactBookHolder book,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().FindHoldersOfAsync(
                book,
                [Address(ForeignOnlyAddress)],
                token),
            cancellationToken);

    private static Task<Contact?> FindAsync(
        OrchestratedMailFathomServices services,
        ContactBookScope books,
        ContactId contactId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().FindAsync(books, contactId, token),
            cancellationToken);

    private static Task<IReadOnlyDictionary<ContactId, Contact>> FindAllAsync(
        OrchestratedMailFathomServices services,
        ContactBookScope books,
        ContactId contactId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().FindAllAsync(books, [contactId], token),
            cancellationToken);

    /// <summary>Names one user's own book, which is the whole of what a caller assigned no mailbox reads.</summary>
    private static ContactBookScope OwnBookOf(MailUserId user) => ContactBookScope.OfOwnBookAlone(user);

    /// <summary>Names one mail account's collected book beside an empty own book, for reading the account's back directly.</summary>
    /// <remarks>A scope is always one user's, so the reader named here is one nothing ever writes for — their own book is empty and the account's is the whole answer.</remarks>
    private static ContactBookScope CollectedBookOf(MailAccountId account) =>
        ContactBookScope.Of(NobodysBook, [account]);

    /// <summary>Empties both of this class's collected books, so nothing it wrote outlives it.</summary>
    private static async Task EraseCollectedBooksAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        foreach (var account in (MailAccountId[])[FirstAccount, SecondAccount])
        {
            await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IContactStore>()
                    .EraseCollectedAsync(session, account, token),
                cancellationToken);
        }
    }

    private static NpgsqlParameter BookParameter(string bookHolderId) => new("bookHolderId", bookHolderId);

    private static NpgsqlParameter PageSizeParameter(int pageSize) => new("pageSize", pageSize);

    private static NpgsqlParameter NormalizedAddressParameter(string normalizedAddress) =>
        new("normalizedAddress", normalizedAddress);
}
