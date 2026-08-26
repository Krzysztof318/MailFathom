// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
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

/// <summary>Proves that a contact book belongs to one owner, where the two rules that make it so actually live.</summary>
/// <remarks>
/// <para>
/// Both claims here are PostgreSQL's rather than the application's. That two owners may each hold a contact for one
/// address is the unique index being over the owner and the address rather than over the address, which nothing but a
/// second insert against a real index can establish — a substitute would report whatever rule it was written with. And
/// that reading one book does not scan the whole table is a query plan, which is a statement only the planner can make
/// and only over enough rows that, without the owner leading the index, a sequential scan would have been the cheaper
/// plan.
/// </para>
/// <para>
/// The second owner is provisioned by this class and erased by it, including on a failure, because a deployment whose
/// mail accounts still come from configuration holds exactly one owner record and every folder binding a later class
/// arranges is resolved against that. Erasing it takes the seeded book with it through the same cascade
/// <see cref="OrchestratedOwnerErasureTests" /> asserts, so nothing here cleans up a contact by hand.
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

    /// <summary>The address both owners hold, which is the whole point of the pair of writes it is used by.</summary>
    private const string SharedAddress = "shared@ownership.contacts.test";

    /// <summary>The name a foreign owner writes down and the served owner asks about, which nothing of theirs answers.</summary>
    private const string ForeignOnlyDisplayName = "Ownership Namesake";

    /// <summary>The address beneath that person, asked about the same way and by the same reads.</summary>
    private const string ForeignOnlyAddress = "namesake@ownership.contacts.test";

    /// <summary>The collected person each owner holds, which is what the set-based erasure is aimed at one book of.</summary>
    private const string CollectedOursAddress = "collected-ours@ownership.contacts.test";

    private const string CollectedTheirsAddress = "collected-theirs@ownership.contacts.test";

    /// <summary>Reads one page of one owner's book in the order the listing index declares.</summary>
    private const string FirstListingPageSql =
        """
        SELECT "Id"
        FROM contacts
        WHERE "OwnerId" = @ownerId
        ORDER BY "DisplayNameSortKey", "Id"
        LIMIT @pageSize
        """;

    /// <summary>Resolves the person one address belongs to, in one book.</summary>
    private const string AddressLookupSql =
        """
        SELECT "ContactId"
        FROM contact_addresses
        WHERE "OwnerId" = @ownerId AND "NormalizedAddress" = @normalizedAddress
        """;

    /// <summary>
    /// One address is one person's within one book and says nothing about anybody else's, so two owners each recording
    /// the same correspondent both succeed and each resolves the address to their own record.
    /// </summary>
    [Fact]
    public async Task ContactAddresses_OneAddressInEachOfTwoOwnersBooks_AreBothHeldAndResolveToEachOwnersOwn()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var servedOwner = services.ServedOwner;
        var foreignOwnerId = Guid.CreateVersion7();
        var foreignOwner = MailOwnerId.Create(foreignOwnerId);
        Contact? ours = null;

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignOwner.ProvisionAsync(services, foreignOwnerId, cancellationToken));

            ours = ContactOf("Ownership Ours", SharedAddress);
            var theirs = ContactOf("Ownership Theirs", SharedAddress);

            // Act
            var written = ours;
            var oursCommit = await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IContactStore>()
                    .AddAsync(session, servedOwner, written, token),
                cancellationToken);

            var theirsCommit = await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IContactStore>()
                    .AddAsync(session, foreignOwner, theirs, token),
                cancellationToken);

            var heldByUs = await FindByAddressAsync(services, servedOwner, SharedAddress, cancellationToken);
            var heldByThem = await FindByAddressAsync(services, foreignOwner, SharedAddress, cancellationToken);

            // Assert
            Assert.Equal(PersistenceCommitResult.Committed, oursCommit);
            Assert.Equal(PersistenceCommitResult.Committed, theirsCommit);
            Assert.Equal(ours.Id, heldByUs?.Id);
            Assert.Equal(theirs.Id, heldByThem?.Id);
        }
        finally
        {
            // The provisioned owner goes first, because the erasure below is asserted and an assertion that fails is an
            // owner left behind: ReadSoleOwnerAsync reads the sole owner with SingleAsync, so a second settings_accounts
            // row makes every later start in this collection throw in classes that never touched a contact.
            try
            {
                // The one erasure this class performs by hand. The served owner is not taken by the foreign-owner erasure, so a
                // contact left behind would make the next run against this database fail at the first AddAsync on the
                // address uniqueness rather than at the assertion that actually broke.
                if (ours is not null)
                {
                    Assert.Equal(
                        PersistenceCommitResult.Committed,
                        await services.CommitAsync(
                            (scope, session, token) => scope.GetRequiredService<IContactStore>()
                                .EraseAsync(session, servedOwner, ours.Id, token),
                            cancellationToken));
                }
            }
            finally
            {
                await OrchestratedForeignOwner.EraseAsync(services, foreignOwnerId);
            }
        }
    }

    /// <summary>
    /// The four reads answered in batches are scoped by the same owner the two indexed reads are, against the real
    /// LINQ and the real database: a person only another owner wrote down is nobody by name, nobody by address, and
    /// nobody by identity.
    /// </summary>
    /// <remarks>
    /// These four gain their owner predicate as an ordinary <c>Where</c> clause rather than as an index the planner has
    /// to choose, which is exactly why a substitute settles nothing about them — the fake would report whatever rule it
    /// was written with. Losing the predicate on any of them is the weakness the issue names, reachable again: a
    /// namesake in another book makes a name lookup report two carriers, and a resolved recipient becomes somebody
    /// else's correspondent.
    /// </remarks>
    [Fact]
    public async Task ContactDirectory_APersonOnlyAnotherOwnerWroteDown_IsAnsweredForByNoneOfTheBatchedReads()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var servedOwner = services.ServedOwner;
        var foreignOwnerId = Guid.CreateVersion7();
        var foreignOwner = MailOwnerId.Create(foreignOwnerId);

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignOwner.ProvisionAsync(services, foreignOwnerId, cancellationToken));

            var theirs = ContactOf(ForeignOnlyDisplayName, ForeignOnlyAddress);

            Assert.Equal(
                PersistenceCommitResult.Committed,
                await services.CommitAsync(
                    (scope, session, token) => scope.GetRequiredService<IContactStore>()
                        .AddAsync(session, foreignOwner, theirs, token),
                    cancellationToken));

            // Act
            var matchedForUs = await MatchDisplayNamesAsync(services, servedOwner, cancellationToken);
            var heldForUs = await FindHoldersOfAsync(services, servedOwner, cancellationToken);
            var byIdentityForUs = await FindAsync(services, servedOwner, theirs.Id, cancellationToken);
            var allByIdentityForUs = await FindAllAsync(services, servedOwner, theirs.Id, cancellationToken);

            var matchedForThem = await MatchDisplayNamesAsync(services, foreignOwner, cancellationToken);
            var heldForThem = await FindHoldersOfAsync(services, foreignOwner, cancellationToken);
            var byIdentityForThem = await FindAsync(services, foreignOwner, theirs.Id, cancellationToken);
            var allByIdentityForThem = await FindAllAsync(services, foreignOwner, theirs.Id, cancellationToken);

            // Assert
            Assert.Equal(0, matchedForUs[ContactDisplayName.Create(ForeignOnlyDisplayName)].MatchCount);
            Assert.Empty(heldForUs);
            Assert.Null(byIdentityForUs);
            Assert.Empty(allByIdentityForUs);

            // The control the absences above rest on: the same four reads under the owner who does hold the person
            // answer with them, so an observation channel that silently reported nothing would fail here instead of
            // passing everything.
            Assert.Equal(1, matchedForThem[ContactDisplayName.Create(ForeignOnlyDisplayName)].MatchCount);
            Assert.Equal(theirs.Id, heldForThem[Address(ForeignOnlyAddress)]);
            Assert.Equal(theirs.Id, byIdentityForThem?.Id);
            Assert.Equal(theirs.Id, Assert.Single(allByIdentityForThem).Value.Id);
        }
        finally
        {
            await OrchestratedForeignOwner.EraseAsync(services, foreignOwnerId);
        }
    }

    /// <summary>
    /// Giving up on collection gives up on one book's collected half. It is the one statement over the book whose
    /// blast radius would be the whole table, so it is asked with a second owner's collected rows present.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The erasure is aimed at the foreign owner rather than at the served one deliberately. Both directions would
    /// prove the predicate, and only this one leaves every other class's arrangement alone: the served owner's book is
    /// shared by the whole collection, so erasing its collected half here would take whatever a class that has not run
    /// yet was relying on.
    /// </para>
    /// <para>
    /// Nothing a substitute can settle: this is a set-based delete no change tracker sees, and its predicate is the
    /// only thing standing between one owner switching collection off and every owner's collected contacts going with
    /// it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EraseCollectedAsync_ACollectedPersonInEachOfTwoBooks_TakesOnlyTheOneBookItWasAskedOf()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var servedOwner = services.ServedOwner;
        var foreignOwnerId = Guid.CreateVersion7();
        var foreignOwner = MailOwnerId.Create(foreignOwnerId);
        Contact? ours = null;

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignOwner.ProvisionAsync(services, foreignOwnerId, cancellationToken));

            ours = CollectedContactOf("Ownership Collected Ours", CollectedOursAddress);
            var theirs = CollectedContactOf("Ownership Collected Theirs", CollectedTheirsAddress);
            var written = ours;

            Assert.Equal(
                PersistenceCommitResult.Committed,
                await services.CommitAsync(
                    (scope, session, token) => scope.GetRequiredService<IContactStore>()
                        .AddAsync(session, servedOwner, written, token),
                    cancellationToken));

            Assert.Equal(
                PersistenceCommitResult.Committed,
                await services.CommitAsync(
                    (scope, session, token) => scope.GetRequiredService<IContactStore>()
                        .AddAsync(session, foreignOwner, theirs, token),
                    cancellationToken));

            // Act
            var erasure = await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IContactStore>()
                    .EraseCollectedAsync(session, foreignOwner, token),
                cancellationToken);

            var theirsAfter = await FindByAddressAsync(services, foreignOwner, CollectedTheirsAddress, cancellationToken);
            var oursAfter = await FindByAddressAsync(services, servedOwner, CollectedOursAddress, cancellationToken);

            // Assert
            Assert.Equal(PersistenceCommitResult.Committed, erasure);
            Assert.Null(theirsAfter);

            // The control the absence rests on, and the claim the predicate is actually about: the other owner's
            // collected person and the address row beneath them are untouched, which FindByAddressAsync reads through.
            Assert.Equal(ours.Id, oursAfter?.Id);
            Assert.Equal(ContactOrigin.Collected, oursAfter?.Origin);
        }
        finally
        {
            // The provisioned owner goes first for the reason the first test in this class states: a commit that throws
            // here would otherwise leave a second settings_accounts row behind and break every later start.
            try
            {
                if (ours is not null)
                {
                    await services.CommitAsync(
                        (scope, session, token) => scope.GetRequiredService<IContactStore>()
                            .EraseAsync(session, servedOwner, ours.Id, token),
                        cancellationToken);
                }
            }
            finally
            {
                await OrchestratedForeignOwner.EraseAsync(services, foreignOwnerId);
            }
        }
    }

    /// <summary>A read of one book reads that book, which is the index leading with the owner and a plan that says so.</summary>
    /// <remarks>
    /// The queries are written here rather than taken from the read model, for the reason the same claim about the mail
    /// timeline is: what is asserted is that the schema can serve the read from an index, which is a property of the
    /// schema. That the read model produces this shape is what the walk in <see cref="OrchestratedContactBookTests" />
    /// establishes, over the same two columns in the same order.
    /// </remarks>
    [Fact]
    public async Task ReadPageAsync_ABookOfSixHundredUnderAnotherOwner_ServesNoneOfItAndIsPlannedThroughTheOwnersIndexes()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var foreignOwnerId = Guid.CreateVersion7();
        var foreignOwner = MailOwnerId.Create(foreignOwnerId);

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignOwner.ProvisionAsync(services, foreignOwnerId, cancellationToken));
            await SeedBookAsync(services, foreignOwnerId, cancellationToken);

            // Act
            var theirPage = await ReadPageAsync(services, foreignOwner, cancellationToken);
            var ourPage = await ReadPageAsync(services, services.ServedOwner, cancellationToken);

            var listingPlan = await OrchestratedQueryPlans.ReadAsync(
                services,
                FirstListingPageSql,
                [OwnerParameter(foreignOwnerId), PageSizeParameter(PageSize)],
                cancellationToken);

            var addressPlan = await OrchestratedQueryPlans.ReadAsync(
                services,
                AddressLookupSql,
                [OwnerParameter(foreignOwnerId), NormalizedAddressParameter(SeededAddress(0).ToUpperInvariant())],
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

            // The same read as the owner this deployment serves reaches none of it, whatever else that book holds from
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
            await OrchestratedForeignOwner.EraseAsync(services, foreignOwnerId);
        }
    }

    /// <summary>The name every seeded contact carries, which is what makes them recognizable in somebody else's page.</summary>
    private const string SeededNamePrefix = "Ownership Seeded ";

    private static string SeededName(int position) => $"{SeededNamePrefix}{position:D4}";

    private static string SeededAddress(int position) => $"seeded-{position:D4}@ownership.contacts.test";

    /// <summary>Writes a book large enough that reading a page of it is a choice the planner has to make.</summary>
    private static async Task SeedBookAsync(
        OrchestratedMailFathomServices services,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            async (_, session, token) =>
            {
                var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                foreach (var position in Enumerable.Range(0, SeededContactCount))
                {
                    var contact = ContactOf(SeededName(position), SeededAddress(position));

                    context.Contacts.Add(ContactRowOf(ownerId, contact));
                    context.ContactAddresses.Add(AddressRowOf(ownerId, contact));
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

    private static ContactEntity ContactRowOf(Guid ownerId, Contact contact) => new()
    {
        Id = contact.Id.Value,
        OwnerId = ownerId,
        DisplayName = contact.DisplayName.Value,
        DisplayNameSortKey = contact.DisplayName.SortKey,
        PreferredNormalizedAddress = contact.PreferredAddress.NormalizedAddress,
        Origin = contact.Origin,
        RecordedAt = contact.RecordedAt,
        AmendedAt = contact.AmendedAt,
    };

    private static ContactAddressEntity AddressRowOf(Guid ownerId, Contact contact) => new()
    {
        Id = Guid.CreateVersion7(RecordedAt),
        ContactId = contact.Id.Value,
        OwnerId = ownerId,
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
        MailOwnerId owner,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().ReadPageAsync(
                owner,
                ContactQuery.Create(origin: null, search: null, PageSize, cursor: null),
                token),
            cancellationToken);

    private static Task<Contact?> FindByAddressAsync(
        OrchestratedMailFathomServices services,
        MailOwnerId owner,
        string address,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>()
                .FindByAddressAsync(owner, Address(address), token),
            cancellationToken);

    private static Task<IReadOnlyDictionary<ContactDisplayName, ContactMatch>> MatchDisplayNamesAsync(
        OrchestratedMailFathomServices services,
        MailOwnerId owner,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().MatchDisplayNamesAsync(
                owner,
                [ContactDisplayName.Create(ForeignOnlyDisplayName)],
                token),
            cancellationToken);

    private static Task<IReadOnlyDictionary<EmailAddress, ContactId>> FindHoldersOfAsync(
        OrchestratedMailFathomServices services,
        MailOwnerId owner,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().FindHoldersOfAsync(
                owner,
                [Address(ForeignOnlyAddress)],
                token),
            cancellationToken);

    private static Task<Contact?> FindAsync(
        OrchestratedMailFathomServices services,
        MailOwnerId owner,
        ContactId contactId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().FindAsync(owner, contactId, token),
            cancellationToken);

    private static Task<IReadOnlyDictionary<ContactId, Contact>> FindAllAsync(
        OrchestratedMailFathomServices services,
        MailOwnerId owner,
        ContactId contactId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().FindAllAsync(owner, [contactId], token),
            cancellationToken);

    private static NpgsqlParameter OwnerParameter(Guid ownerId) => new("ownerId", ownerId);

    private static NpgsqlParameter PageSizeParameter(int pageSize) => new("pageSize", pageSize);

    private static NpgsqlParameter NormalizedAddressParameter(string normalizedAddress) =>
        new("normalizedAddress", normalizedAddress);
}
