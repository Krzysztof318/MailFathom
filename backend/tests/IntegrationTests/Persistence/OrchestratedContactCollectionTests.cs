// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Collection;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the two halves of contact collection that only PostgreSQL can settle.</summary>
/// <remarks>
/// <para>
/// What collection decides — which header a folder contributes, what an exclusion keeps out, when a run's bound is
/// reached — is settled by <c>MailContactCollector</c> against substitutes and stays there. Two things underneath it
/// cannot be. The threshold is answered by counting an account's stored mail by its sender's comparison form, in a query
/// with two halves and a distinct over a nullable column, and a provider that could not translate either would throw at
/// runtime rather than count wrong. And erasing everything one origin produced is two set-based deletes, the first
/// narrowed by a subquery over the other table, which is exactly the shape a change tracker never sees.
/// </para>
/// <para>
/// The erasure reaches the whole book rather than rows this class wrote, because that is what the operator's command
/// does. It is stated in one test that both writes and erases, so no collected record of this class's outlives it — the
/// walk in <c>OrchestratedContactBookTests</c> asserts over every collected contact in the database, and a class that
/// left some behind would decide that test's result.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedContactCollectionTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "contact-collection-tally";

    /// <summary>The first UID of the block this class seeds, which is what tells its rows from another class's.</summary>
    private const uint FirstSeededUid = 8100;

    /// <summary>The address the seeded correspondence is written by, in the casing a sender would have used.</summary>
    private const string CorrespondentAddress = "Marek.Nowak@tally.contacts.test";

    /// <summary>How many messages that address wrote, more than any ceiling this test asks for.</summary>
    private const int AuthoredMessageCount = 4;

    /// <summary>
    /// The threshold reads what one address wrote out of the mail already stored, stops at the ceiling it was asked
    /// for, and counts one message stored in two folders once.
    /// </summary>
    /// <remarks>
    /// The ceiling is the whole point of the two-half query: a prolific correspondent must cost the threshold rather
    /// than their mailbox, and a message that reached two folders of one account is one message however many rows it
    /// left. Both are properties of the SQL rather than of the caller, so both are asserted here.
    /// </remarks>
    [Fact]
    public async Task CountMessagesAuthoredByAsync_MailOneAddressWrote_CountsDistinctMessagesUpToTheCeiling()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);

        await EnsureSeededAsync(services, binding, cancellationToken);

        // Act
        var withinReach = await CountAsync(services, CorrespondentAddress, ceiling: 10, cancellationToken);
        var atTheCeiling = await CountAsync(services, CorrespondentAddress, ceiling: 2, cancellationToken);
        var byAnotherCasing = await CountAsync(
            services,
            "MAREK.NOWAK@TALLY.CONTACTS.TEST",
            ceiling: 10,
            cancellationToken);
        var wroteNothing = await CountAsync(
            services,
            "nobody@tally.contacts.test",
            ceiling: 10,
            cancellationToken);

        // Assert
        Assert.Equal(AuthoredMessageCount, withinReach);
        Assert.Equal(2, atTheCeiling);
        Assert.Equal(AuthoredMessageCount, byAnotherCasing);
        Assert.Equal(0, wroteNothing);
    }

    /// <summary>Erasing what an instance collected takes every record of that origin and every address row with it.</summary>
    /// <remarks>
    /// The address rows go by a delete narrowed on the contacts about to be deleted, so the two statements have to agree
    /// about which rows those are; the schema's foreign key would refuse the second otherwise, and that refusal is what
    /// this proves does not happen. What the owner asserted is untouched, which is the whole claim the command makes.
    /// </remarks>
    [Fact]
    public async Task EraseCollectedAsync_ABookOfBothOrigins_RemovesEveryCollectedRecordAndLeavesTheAssertedOnes()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var asserted = await RecordAsync(
            services,
            "Erasure Keeper",
            ["kept@collected.contacts.test"],
            ContactOrigin.Asserted,
            cancellationToken);

        var collected = await RecordAsync(
            services,
            "Erasure Collected",
            ["picked-up@collected.contacts.test", "picked-up.too@collected.contacts.test"],
            ContactOrigin.Collected,
            cancellationToken);

        var assertedId = asserted.Contact!.Id;
        var collectedId = collected.Contact!.Id;

        // Act
        var erasure = await AsOperatorAsync(
            services,
            (book, token) => book.EraseCollectedAsync(token),
            cancellationToken);

        var collectedRowsLeft = await CountCollectedRowsAsync(services, cancellationToken);
        var collectedAddressRowsLeft = await CountAddressRowsAsync(services, collectedId, cancellationToken);
        var keptAddressRows = await CountAddressRowsAsync(services, assertedId, cancellationToken);
        var kept = await FindByAddressAsync(services, "kept@collected.contacts.test", cancellationToken);
        var gone = await FindByAddressAsync(services, "picked-up@collected.contacts.test", cancellationToken);

        // Assert
        // At least this test's own record and its two addresses: the served owner's book is shared by every class in
        // this collection, so another class's collected rows are erased by the same statement and counting them exactly
        // would assert about somebody else's arrangement. The counts below are scoped to that owner rather than to the
        // table, because the erasure is scoped to it and a foreign owner's collected rows are outside what it promises.
        Assert.True(erasure.ContactsErased >= 1);
        Assert.True(erasure.AddressesErased >= 2);
        Assert.Equal(0, collectedRowsLeft);
        Assert.Equal(0, collectedAddressRowsLeft);
        Assert.Equal(1, keptAddressRows);
        Assert.Equal(assertedId, kept?.Id);
        Assert.Null(gone);
    }

    private static EmailAddress Address(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return emailAddress;
    }

    private static async Task EnsureSeededAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var repository = scope.GetRequiredService<IEmailMetadataRepository>();

                foreach (var seeded in SeededEmails(binding))
                {
                    await repository.UpsertMetadataAsync(
                        session,
                        seeded.RemoteMetadata,
                        seeded.Extraction,
                        StoredEmailContentAvailability.Available,
                        token);
                }
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    /// <summary>Describes what the tally counts: four messages from one address, one of them stored twice, and one from another.</summary>
    /// <remarks>
    /// The repeated message carries the identifier its sender wrote and a UID of its own, which is a message this account
    /// holds in two folders — the case the identified half of the query exists to collapse. The message from another
    /// address is what makes a query that ignored the sender fail rather than pass with a larger number.
    /// </remarks>
    private static IEnumerable<SeededEmail> SeededEmails(MailFolderResolution binding)
    {
        var authored = Enumerable.Range(0, AuthoredMessageCount).Select(index => SeededEmail.Of(
            binding,
            FirstSeededUid + (uint)index,
            $"tally-{index:D2}",
            CorrespondentAddress));

        return
        [
            .. authored,
            SeededEmail.Of(binding, FirstSeededUid + 90, "tally-00", CorrespondentAddress),
            SeededEmail.Of(binding, FirstSeededUid + 91, "tally-other", "someone.else@tally.contacts.test"),
        ];
    }

    private static Task<int> CountAsync(
        OrchestratedMailFathomServices services,
        string author,
        int ceiling,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IAuthoredMailTally>().CountMessagesAuthoredByAsync(
                SyntheticMailAccount.AccountId,
                Address(author),
                ceiling,
                token),
            cancellationToken);

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
            (scope, token) => scope.GetRequiredService<IContactDirectory>().FindByAddressAsync(
                services.ServedOwner,
                Address(address),
                token),
            cancellationToken);

    private static Task<int> CountCollectedRowsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .Contacts
                .AsNoTracking()
                .Where(contact =>
                    contact.OwnerId == services.ServedOwner.Value && contact.Origin == ContactOrigin.Collected)
                .CountAsync(token),
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

    /// <summary>Reaches the book as the operator, which is what admits the two acts this class asks of it.</summary>
    /// <remarks>
    /// Recording a person and erasing the collected origin are both published to a caller. What collection itself
    /// performs is published to MailFathom's own identity instead and is reached through the account run rather than
    /// through this helper, which is why the collecting half of this class states no caller at all.
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

    /// <summary>One message this class seeds, named by the sender the tally is asked about.</summary>
    private sealed record SeededEmail(RemoteEmailMetadata RemoteMetadata, ExtractedEmailMetadata Extraction)
    {
        internal static SeededEmail Of(MailFolderResolution binding, uint uid, string subject, string senderAddress)
        {
            var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);

            return new(
                SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                SyntheticEmail.ExtractionFrom(
                    occurrenceId,
                    subject,
                    SyntheticEmail.BodyTextContaining(subject, wordCount: 12),
                    senderAddress,
                    SyntheticEmail.ReceivedAt,
                    "owner@tally.contacts.test"));
        }
    }
}
