// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
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
/// Four claims, none of which a substitute can settle. That erasing a person removes every row derived from them runs
/// along a foreign key the schema declares. That one address stays in one person's hands is a unique index, and losing
/// that race has to reach a caller as a conflict rather than as a provider failure. That a walk of the book serves every
/// contact once depends on PostgreSQL comparing the same two columns the index is ordered by, which is also the one part
/// of the read that has to survive being translated into SQL at all. And that an amendment which drops an address frees
/// it is the interaction between the replacement's deletes and that same index.
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
    /// <summary>The names the walk lists, written out of order so the order it reads them in is the book's rather than the insertion's.</summary>
    /// <summary>The instant a directly constructed contact is stamped with, fixed so no test here reads a clock.</summary>
    private static readonly DateTimeOffset RecordedAt = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

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

        var export = await InScopeAsync(
            services,
            (book, token) => book.ExportAsync(contactId, token),
            cancellationToken);

        var erasure = await InScopeAsync(
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
        var losingCommit = await services.InScopeAsync(
            async (losingScope, token) =>
            {
                await using var losingSession = await losingScope
                    .GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                await losingScope.GetRequiredService<IContactStore>().AddAsync(losingSession, losing, token);

                var winningCommit = await services.CommitAsync(
                    (winningScope, winningSession, winningToken) => winningScope
                        .GetRequiredService<IContactStore>()
                        .AddAsync(winningSession, winning, winningToken),
                    token);
                Assert.Equal(PersistenceCommitResult.Committed, winningCommit);

                return await losingSession.CommitAsync(token);
            },
            cancellationToken);

        var held = await FindByAddressAsync(services, "contested@race.contacts.test", cancellationToken);

        // Assert
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
        var amended = await InScopeAsync(
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

        var reread = await InScopeAsync(
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
        EmailAddress.TryCreate(displayName: null, address, out var emailAddress);

        return emailAddress;
    }

    private static Task<ContactWriteResult> RecordAsync(
        OrchestratedMailFathomServices services,
        string displayName,
        IReadOnlyList<string> addresses,
        ContactOrigin origin,
        CancellationToken cancellationToken) => InScopeAsync(
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
                ContactQuery.Create(ContactOrigin.Collected, pageSize, cursor),
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

    private static Task<TResult> InScopeAsync<TResult>(
        OrchestratedMailFathomServices services,
        Func<ContactBook, CancellationToken, Task<TResult>> read,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => read(scope.GetRequiredService<ContactBook>(), token),
            cancellationToken);
}
