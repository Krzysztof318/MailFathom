// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the contact book against real PostgreSQL, where its constraints, its order, and its erasure live.</summary>
/// <remarks>
/// <para>
/// Five claims, none of which a substitute can settle. That erasing a person removes every row derived from them runs
/// along a foreign key the schema declares. That one address stays in one person's hands is a unique index, and losing
/// that race has to reach a caller as a conflict rather than as a provider failure. That a walk of the book serves every
/// contact once depends on PostgreSQL comparing the same two columns the index is ordered by, which is also the one part
/// of the read that has to survive being translated into SQL at all. That resolving whole names answers each with one
/// person or with an exact count turns on that same collation, on counts the database groups rather than pages this read
/// holds, and on the one translation that grouping is. And that an amendment which drops an address frees it is the
/// interaction between the replacement's deletes and
/// that same index.
/// </para>
/// <para>
/// Every test owns its own domain of addresses, because the index they turn on is unique across the whole book and the
/// suite shares one database. The walk is the only test writing collected contacts, so filtering by that origin is what
/// keeps its assertion about order an assertion about its own rows.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedContactBookTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The instant a directly constructed contact is stamped with, fixed so no test here reads a clock.</summary>
    private static readonly DateTimeOffset RecordedAt = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The names the walk lists, written out of order so the order it reads them in is the book's rather than the insertion's.</summary>
    /// <remarks>
    /// One of them is lower-cased, so ordering by the name as written and ordering by the stored comparison form are two
    /// different sequences and the walk's assertion can tell them apart.
    /// </remarks>
    private static readonly string[] WalkedNames =
    [
        "Walk Delta",
        "walk alpha",
        "Walk Echo",
        "Walk Charlie",
        "Walk Bravo",
    ];

    /// <summary>
    /// A person's addresses are found whichever way they are written, an export carries everything held, and erasing
    /// them takes every address row with them and frees those addresses for somebody else.
    /// </summary>
    [Fact]
    public async Task Erasure_AContactWithSeveralAddresses_RemovesEveryRowDerivedFromThemAndFreesTheirAddresses()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var recorded = await RecordAsync(
            services,
            "Erasure Subject",
            ["anna@erasure.contacts.test", "anna.kowalska@erasure.contacts.test"],
            ContactOrigin.Asserted,
            cancellationToken);

        var contactId = recorded.Contact!.Id;

        // Act
        var foundByAnotherCasing = await FindByAddressAsync(
            services,
            "ANNA.KOWALSKA@Erasure.Contacts.Test",
            cancellationToken);

        var export = await AsOperatorAsync(
            services,
            (book, token) => book.ExportAsync(contactId, token),
            cancellationToken);

        var erasure = await AsOperatorAsync(
            services,
            (book, token) => book.EraseAsync(contactId, token),
            cancellationToken);

        var addressRowsLeft = await CountAddressRowsAsync(services, contactId, cancellationToken);
        var afterErasure = await FindByAddressAsync(services, "anna@erasure.contacts.test", cancellationToken);

        // The freed address is claimed by a different person, which is the observable half of the erasure having
        // reached the index rather than only the row a caller reads.
        var reclaimed = await RecordAsync(
            services,
            "Erasure Successor",
            ["anna@erasure.contacts.test"],
            ContactOrigin.Asserted,
            cancellationToken);

        // Assert
        Assert.Equal(contactId, foundByAnotherCasing?.Id);
        Assert.Equal(2, export?.Contact.Addresses.Count);
        Assert.Equal("Erasure Subject", export?.Contact.DisplayName.Value);
        Assert.Equal(new ContactErasure(contactId, WasHeld: true, AddressesErased: 2), erasure);
        Assert.Equal(0, addressRowsLeft);
        Assert.Null(afterErasure);
        Assert.Equal(ContactWriteOutcome.Written, reclaimed.Outcome);
    }

    /// <summary>Promotion changes a column an amendment never touches, so only a re-read from the database proves it landed.</summary>
    /// <remarks>
    /// The in-memory double a unit test writes through replaces the whole record, which would report a promotion as
    /// written whatever the adapter copied onto the tracked row. What that cannot establish is the one thing this write
    /// is for: that the origin column moved, and that asking again therefore says there is nothing left to do.
    /// </remarks>
    [Fact]
    public async Task PromoteAsync_ACollectedContact_WritesTheOriginAndAnsweredAgainSaysNothingIsLeftToDo()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var recorded = await RecordAsync(
            services,
            "Promotion Subject",
            ["anna@promotion.contacts.test"],
            ContactOrigin.Collected,
            cancellationToken);

        var contactId = recorded.Contact!.Id;

        // Act
        var promoted = await AsOperatorAsync(
            services,
            (book, token) => book.PromoteAsync(contactId, ContactOrigin.Asserted, token),
            cancellationToken);

        var reread = await AsOperatorAsync(
            services,
            (book, token) => book.ExportAsync(contactId, token),
            cancellationToken);

        var again = await AsOperatorAsync(
            services,
            (book, token) => book.PromoteAsync(contactId, ContactOrigin.Asserted, token),
            cancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, promoted.Outcome);
        Assert.Equal(ContactOrigin.Asserted, reread!.Contact.Origin);
        Assert.Equal(ContactWriteOutcome.AlreadyAsserted, again.Outcome);
    }

    /// <summary>Two writers claiming one address both read nothing, so only the index closes that window — as a conflict.</summary>
    /// <remarks>
    /// Written through the store rather than through the book, because the book reads first and would answer the second
    /// caller without ever reaching the database. What has to be proven here is the arrangement that read cannot cover:
    /// two overlapping transactions, neither able to see the other's staged row.
    /// </remarks>
    [Fact]
    public async Task ContactAddresses_TwoOverlappingWritersClaimingOneAddress_LeaveTheLoserWithAConflict()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var winning = ContactOf("Race Winner", ["contested@race.contacts.test"], ContactOrigin.Asserted);
        var losing = ContactOf("Race Loser", ["CONTESTED@race.contacts.test"], ContactOrigin.Asserted);

        // Act
        var (winningCommit, losingCommit) = await services.InScopeAsync(
            async (losingScope, token) =>
            {
                await using var losingSession = await losingScope
                    .GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                await losingScope.GetRequiredService<IContactStore>().AddAsync(losingSession, losing, token);

                var committedFirst = await services.CommitAsync(
                    (winningScope, winningSession, winningToken) => winningScope
                        .GetRequiredService<IContactStore>()
                        .AddAsync(winningSession, winning, winningToken),
                    token);

                return (committedFirst, await losingSession.CommitAsync(token));
            },
            cancellationToken);

        var held = await FindByAddressAsync(services, "contested@race.contacts.test", cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, winningCommit);
        Assert.Equal(PersistenceCommitResult.ConcurrencyConflict, losingCommit);
        Assert.Equal("Race Winner", held?.DisplayName.Value);
    }

    /// <summary>A walk of the book serves every contact once, in the order the index is built in.</summary>
    [Fact]
    public async Task ReadPageAsync_AWalkOfTheBookInPages_ServesEveryContactOnceInNameOrder()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        foreach (var (name, position) in WalkedNames.Select((name, position) => (name, position)))
        {
            await RecordAsync(
                services,
                name,
                [$"walked-{position}@walk.contacts.test"],
                ContactOrigin.Collected,
                cancellationToken);
        }

        // Act
        var walked = new List<Contact>();
        ContactCursor? cursor = null;

        do
        {
            var page = await ReadPageAsync(services, cursor, pageSize: 2, cancellationToken);
            walked.AddRange(page.Contacts);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        // Assert
        // Expected by the comparison form rather than by the name as written, and one of the names is lower-cased so
        // those two orders differ: a walk that ordered by the displayed value would pass against the wrong sequence
        // otherwise, which is exactly the collation independence the stored key exists to provide.
        string[] byComparisonForm = [.. WalkedNames.OrderBy(name => name.ToUpperInvariant(), StringComparer.Ordinal)];

        Assert.NotEqual([.. WalkedNames.Order(StringComparer.Ordinal)], byComparisonForm);
        Assert.Equal(byComparisonForm, walked.Select(contact => contact.DisplayName.Value));
        Assert.Equal(walked.Count, walked.Select(contact => contact.Id).Distinct().Count());
    }

    /// <summary>The search is the one read no index answers, and the only part of the query that reaches two tables at once.</summary>
    /// <remarks>
    /// A substitute settles what the comparison form is; what it cannot settle is that the predicate translates into SQL
    /// at all — it reaches a stored key on the contact and a normalized address on a row joined to it, and a provider
    /// that could not translate the second half would throw rather than serve fewer people. The names are chosen so the
    /// matched pair is ordered against the insertion order and against one contact whose name carries the text in a
    /// different casing from the one the caller wrote.
    /// </remarks>
    [Fact]
    public async Task ReadPageAsync_ASearch_ServesTheContactsWhoseNameOrAddressCarriesTheText()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        await RecordAsync(
            services,
            "Zephyr Fairweather",
            ["zephyr@search.contacts.test"],
            ContactOrigin.Asserted,
            cancellationToken);

        await RecordAsync(
            services,
            "Ingrid Nordahl",
            ["fairweather.ingrid@search.contacts.test"],
            ContactOrigin.Asserted,
            cancellationToken);

        await RecordAsync(
            services,
            "Ingrid Sorensen",
            ["sorensen@search.contacts.test"],
            ContactOrigin.Asserted,
            cancellationToken);

        // Act
        var matched = await SearchAsync(services, "fairweather", cancellationToken);
        var matchedNothing = await SearchAsync(services, "nobody-writes-this@search.contacts.test", cancellationToken);

        // Assert
        Assert.Equal(
            ["Ingrid Nordahl", "Zephyr Fairweather"],
            matched.Contacts.Select(contact => contact.DisplayName.Value));
        Assert.Empty(matchedNothing.Contacts);
        Assert.Null(matchedNothing.NextCursor);
    }

    /// <summary>
    /// Addressing a message by naming somebody turns on this lookup answering with one person or with a count, and every
    /// half of it is PostgreSQL's: the equality is on a column pinned to the <c>C</c> collation, the counts are grouped by
    /// the database rather than read as pages this query never holds, and one statement answers however many names a
    /// message named.
    /// </summary>
    [Fact]
    public async Task MatchDisplayNamesAsync_NamesOnePersonCarriesAndSeveralDo_AnswerWithThePeopleAndWithTheCounts()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var unique = await RecordAsync(
            services,
            "Solveig Lindqvist",
            ["solveig@named.contacts.test"],
            ContactOrigin.Asserted,
            cancellationToken);

        await RecordAsync(
            services,
            "Namesake Halvorsen",
            ["namesake.one@named.contacts.test"],
            ContactOrigin.Asserted,
            cancellationToken);

        await RecordAsync(
            services,
            "Namesake Halvorsen",
            ["namesake.two@named.contacts.test"],
            ContactOrigin.Asserted,
            cancellationToken);

        // Act
        // Every name a message named, in one read. The casing is the author's rather than the book's, which is the whole
        // reason the comparison form is stored.
        var matches = await MatchDisplayNamesAsync(
            services,
            ["solveig lindqvist", "Namesake Halvorsen", "Solveig"],
            cancellationToken);

        var held = await FindAllAsync(
            services,
            [unique.Contact!.Id, ContactId.Create(Guid.CreateVersion7())],
            cancellationToken);

        // Assert
        var byName = matches[ContactDisplayName.Create("solveig lindqvist")];
        var shared = matches[ContactDisplayName.Create("Namesake Halvorsen")];
        var carriedByNobody = matches[ContactDisplayName.Create("Solveig")];

        Assert.Equal(1, byName.MatchCount);
        Assert.Equal(unique.Contact.Id, byName.OnlyMatch?.Id);
        Assert.Equal("solveig@named.contacts.test", byName.OnlyMatch?.PreferredAddress.Address);

        Assert.Equal(2, shared.MatchCount);
        Assert.Null(shared.OnlyMatch);

        // Part of a name is not a person being named, which is what separates this lookup from a search.
        Assert.Equal(0, carriedByNobody.MatchCount);
        Assert.Null(carriedByNobody.OnlyMatch);

        // The identity lookup answers the same way for a set: the people the book holds, and nothing standing in for the
        // one it does not.
        Assert.Equal([unique.Contact.Id], held.Keys);
        Assert.Equal("solveig@named.contacts.test", held[unique.Contact.Id].PreferredAddress.Address);
    }

    /// <summary>An amendment is the whole record: what it stops naming is removed, and the address it releases is free.</summary>
    [Fact]
    public async Task AmendAsync_DroppingAnAddress_RemovesItsRowAndReleasesItToAnotherContact()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var recorded = await RecordAsync(
            services,
            "Amendment Subject",
            ["kept@amendment.contacts.test", "dropped@amendment.contacts.test"],
            ContactOrigin.Asserted,
            cancellationToken);

        var contactId = recorded.Contact!.Id;

        // Act
        var amended = await AsOperatorAsync(
            services,
            (book, token) => book.AmendAsync(
                new ContactAmendment
                {
                    ContactId = contactId,
                    Writer = ContactOrigin.Asserted,
                    DisplayName = ContactDisplayName.Create("Amendment Subject"),
                    Addresses = [Address("kept@amendment.contacts.test"), Address("added@amendment.contacts.test")],
                    PreferredAddress = Address("added@amendment.contacts.test"),
                    Note = ContactNote.Create("Changed address in March."),
                },
                token),
            cancellationToken);

        var addressRows = await CountAddressRowsAsync(services, contactId, cancellationToken);
        var successor = await RecordAsync(
            services,
            "Amendment Successor",
            ["dropped@amendment.contacts.test"],
            ContactOrigin.Asserted,
            cancellationToken);

        var reread = await AsOperatorAsync(
            services,
            (book, token) => book.ExportAsync(contactId, token),
            cancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, amended.Outcome);
        Assert.Equal(2, addressRows);
        Assert.Equal(ContactWriteOutcome.Written, successor.Outcome);
        Assert.Equal(
            ["ADDED@AMENDMENT.CONTACTS.TEST", "KEPT@AMENDMENT.CONTACTS.TEST"],
            reread!.Contact.Addresses.Select(address => address.NormalizedAddress).Order(StringComparer.Ordinal));
        Assert.Equal("ADDED@AMENDMENT.CONTACTS.TEST", reread.Contact.PreferredAddress.NormalizedAddress);
        Assert.Equal("Changed address in March.", reread.Contact.Note?.Value);
    }

    private static Contact ContactOf(string displayName, IReadOnlyList<string> addresses, ContactOrigin origin)
    {
        var recordedAt = RecordedAt;

        return Contact.Create(
            ContactId.Create(Guid.CreateVersion7(recordedAt)),
            ContactDisplayName.Create(displayName),
            [.. addresses.Select(Address)],
            Address(addresses[0]),
            note: null,
            origin,
            recordedAt,
            recordedAt);
    }

    private static EmailAddress Address(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return emailAddress;
    }

    private static Task<ContactWriteResult> RecordAsync(
        OrchestratedMailFathomServices services,
        string displayName,
        IReadOnlyList<string> addresses,
        ContactOrigin origin,
        CancellationToken cancellationToken) => AsOperatorAsync(
            services,
            (book, token) => book.RecordAsync(
                new NewContact
                {
                    DisplayName = ContactDisplayName.Create(displayName),
                    Addresses = [.. addresses.Select(Address)],
                    PreferredAddress = Address(addresses[0]),
                    Origin = origin,
                },
                token),
            cancellationToken);

    private static Task<Contact?> FindByAddressAsync(
        OrchestratedMailFathomServices services,
        string address,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().FindByAddressAsync(Address(address), token),
            cancellationToken);

    private static Task<ContactPage> ReadPageAsync(
        OrchestratedMailFathomServices services,
        ContactCursor? cursor,
        int pageSize,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().ReadPageAsync(
                ContactQuery.Create(ContactOrigin.Collected, search: null, pageSize, cursor),
                token),
            cancellationToken);

    private static Task<IReadOnlyDictionary<ContactDisplayName, ContactMatch>> MatchDisplayNamesAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyCollection<string> displayNames,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().MatchDisplayNamesAsync(
                [.. displayNames.Select(ContactDisplayName.Create)],
                token),
            cancellationToken);

    private static Task<IReadOnlyDictionary<ContactId, Contact>> FindAllAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyCollection<ContactId> contactIds,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().FindAllAsync(contactIds, token),
            cancellationToken);

    private static Task<ContactPage> SearchAsync(
        OrchestratedMailFathomServices services,
        string search,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IContactDirectory>().ReadPageAsync(
                ContactQuery.Create(origin: null, ContactSearch.Create(search), pageSize: 20, cursor: null),
                token),
            cancellationToken);

    private static Task<int> CountAddressRowsAsync(
        OrchestratedMailFathomServices services,
        ContactId contactId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .ContactAddresses
                .AsNoTracking()
                .Where(address => address.ContactId == contactId.Value)
                .CountAsync(token),
            cancellationToken);

    /// <summary>Reaches the book as the operator, which is the only principal every act asserted here is admitted to.</summary>
    /// <remarks>
    /// Every method this class drives — recording, amending, promoting, exporting, erasing — is published to a caller
    /// rather than to MailFathom's own identity, so a scope stating none is refused before it reaches the database and
    /// the class would prove nothing about PostgreSQL at all. The grant is the administrative surface an operator holds,
    /// which is the whole of what these acts are published under; collection's own two methods are the process
    /// identity's and are exercised where collection is, not here.
    /// </remarks>
    private static Task<TResult> AsOperatorAsync<TResult>(
        OrchestratedMailFathomServices services,
        Func<ContactBook, CancellationToken, Task<TResult>> read,
        CancellationToken cancellationToken) => services.AsCallerInScopeAsync(
            (scope, token) => read(scope.GetRequiredService<ContactBook>(), token),
            [
                MailFathomPermission.AdminOperate,
                MailFathomPermission.AdminAuditRead,
                MailFathomPermission.AdminErase,
            ],
            cancellationToken);
}
