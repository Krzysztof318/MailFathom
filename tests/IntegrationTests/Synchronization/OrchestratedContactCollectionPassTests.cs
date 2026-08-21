// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Collection;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Contacts.Collection;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.SyntheticMail.Generation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>Runs a whole synchronization over seeded mail and reads back who ended up in the collected origin.</summary>
/// <remarks>
/// <para>
/// Every rule collection applies is settled against substitutes in the unit suite, and none of that is repeated here.
/// What this class establishes is the one claim no substitute reaches: that the rules are actually wired into the pass
/// that stores mail, in the order that makes them mean anything — a real message is fetched from a real server, its
/// headers are read by the real MIME reader, the automation claim it carries survives extraction, the threshold is
/// answered by counting real rows, and what comes out of the far end is a book holding exactly the people the folder
/// says the owner corresponds with. A defect anywhere along that chain shows up as a missing contact or an extra one
/// rather than as a failure, which is precisely the kind this suite exists for.
/// </para>
/// <para>
/// <b>Every address here is invented</b>, under the reserved <c>.test</c> domain, and each test owns a suffix of its
/// own so what it asserts about is its own seeding rather than the shared book's whole contents. The second test is the
/// control for the first: without it, a composition that never reached collection at all would satisfy every negative
/// assertion above by collecting nobody.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedContactCollectionPassTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How many messages an address must have written before the pass records the person behind it.</summary>
    private const int MinimumMessagesFromSender = 2;

    private const string CollectingFolderName = "ContactsCollected";
    private const string IgnoringFolderName = "ContactsIgnored";

    /// <summary>The suffix every address the collecting test seeds ends in, in the book's own comparison form.</summary>
    private const string CollectingSuffix = "SIGHTED.CONTACTS.TEST";

    /// <summary>The same, for the test that proves a switched-off account writes nothing.</summary>
    private const string IgnoringSuffix = "UNWATCHED.CONTACTS.TEST";

    /// <summary>The domain an owner excluded, which is a real correspondent they would rather not have written down.</summary>
    private const string ExcludedDomain = "bulletins.sighted.contacts.test";

    private static readonly MailFolderMapping CollectingFolder = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create("contacts-collected"),
        RemoteFolderPath.Create(CollectingFolderName, hierarchyDelimiter: '.'));

    private static readonly MailFolderMapping IgnoringFolder = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create("contacts-ignored"),
        RemoteFolderPath.Create(IgnoringFolderName, hierarchyDelimiter: '.'));

    /// <summary>The one person the seeded folder says the owner corresponds with.</summary>
    private static readonly SyntheticParticipant Correspondent =
        new("Zofia Kowalska", "Zofia.Kowalska@sighted.contacts.test");

    /// <summary>Somebody who wrote once, which is not yet correspondence at the threshold this test states.</summary>
    private static readonly SyntheticParticipant WroteOnce =
        new("Henryk Lis", "Henryk.Lis@sighted.contacts.test");

    /// <summary>A mailbox no person reads, refused by its name whatever it writes.</summary>
    private static readonly SyntheticParticipant AutomatedMailbox =
        new("Sighted Notifications", "no-reply@sighted.contacts.test");

    /// <summary>A person whose messages announce themselves as bulk, which is not somebody writing to somebody.</summary>
    private static readonly SyntheticParticipant BulkSender =
        new("Ewa Zielińska", "Ewa.Zielinska@sighted.contacts.test");

    /// <summary>A real person on the domain the owner excluded.</summary>
    private static readonly SyntheticParticipant OnTheExcludedDomain =
        new("Marta Wrona", $"Marta.Wrona@{ExcludedDomain}");

    /// <summary>The only correspondent the switched-off test seeds, who must still end up nowhere.</summary>
    private static readonly SyntheticParticipant Unwatched =
        new("Paweł Adamczyk", "Pawel.Adamczyk@unwatched.contacts.test");

    /// <summary>
    /// A pass over synchronized mail records the people the folder says the owner corresponds with, and nobody else.
    /// </summary>
    /// <remarks>
    /// Five senders, of which exactly one is a correspondent, and the four refusals are each of a different kind: one
    /// has not written often enough yet, one is a mailbox nobody reads, one announced its messages as bulk in a header
    /// only the real MIME reader produces, and one is on a domain the owner excluded. Asserting the whole set rather
    /// than each member is what makes "and no others" a claim: an extra contact fails the comparison instead of passing
    /// four separate lookups.
    /// </remarks>
    [Fact]
    public async Task SynchronizeAsync_MailFromCorrespondentsAndFromNobody_CollectsTheCorrespondentsAndNobodyElse()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        await mailbox.RecreateFolderAsync(CollectingFolderName, cancellationToken);
        await SeedAsync(mailbox, CollectingFolderName, "sighted-correspondent", Correspondent, 2, cancellationToken);
        await SeedAsync(mailbox, CollectingFolderName, "sighted-wrote-once", WroteOnce, 1, cancellationToken);
        await SeedAsync(mailbox, CollectingFolderName, "sighted-automated", AutomatedMailbox, 2, cancellationToken);
        await SeedAsync(mailbox, CollectingFolderName, "sighted-excluded", OnTheExcludedDomain, 2, cancellationToken);
        await SeedAsync(
            mailbox,
            CollectingFolderName,
            "sighted-bulk",
            BulkSender,
            2,
            cancellationToken,
            ("Precedence", "bulk"));

        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            contactCollection: CollectionSwitchedOn());

        // Act
        var result = await SynchronizeAsync(services, CollectingFolder, cancellationToken);

        // Assert
        Assert.Equal(MailboxSynchronizationOutcome.Synchronized, result.Outcome);
        Assert.Equal(9, result.StoredEmailCount);

        var collected = await ReadCollectedAddressesAsync(services, CollectingSuffix, cancellationToken);
        Assert.Equal(new[] { Correspondent.Address.ToUpperInvariant() }, collected);

        var recorded = await ReadCollectedContactAsync(services, Correspondent.Address, cancellationToken);
        Assert.Equal(Correspondent.DisplayName, recorded?.DisplayName.Value);
    }

    /// <summary>An account that never switched collection on writes nothing into the collected origin.</summary>
    /// <remarks>
    /// The control the test above rests on. Its four refusals are all absences, and a composition that resolved a
    /// collector which never ran — a settings reader nobody consulted, a folder role nothing mapped, a call the
    /// synchronizer stopped making — would satisfy every one of them. Seeding a correspondent who clears the same
    /// threshold and asserting that this account records nobody is what tells the two apart.
    /// </remarks>
    [Fact]
    public async Task SynchronizeAsync_AnAccountThatCollectsNothing_LeavesTheCollectedOriginEmpty()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        await mailbox.RecreateFolderAsync(IgnoringFolderName, cancellationToken);
        await SeedAsync(mailbox, IgnoringFolderName, "unwatched-correspondent", Unwatched, 3, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var result = await SynchronizeAsync(services, IgnoringFolder, cancellationToken);

        // Assert
        Assert.Equal(MailboxSynchronizationOutcome.Synchronized, result.Outcome);
        Assert.Equal(3, result.StoredEmailCount);
        Assert.Empty(await ReadCollectedAddressesAsync(services, IgnoringSuffix, cancellationToken));
    }

    /// <summary>The settings an owner who switched collection on for this account would have written.</summary>
    private static ContactCollectionSettings CollectionSwitchedOn()
    {
        Assert.True(ContactCollectionExclusion.TryCreateForDomain(
            ExcludedDomain,
            includeSubdomains: false,
            out var exclusion));

        return new ContactCollectionSettings
        {
            IsEnabled = true,
            MinimumMessagesFromSender = MinimumMessagesFromSender,
            MaxContactsPerRun = 50,
            Policy = ContactCollectionPolicy.Create([exclusion], []),
        };
    }

    /// <summary>Appends a run of messages from one author, each with an identifier and a subject of its own.</summary>
    private static async Task SeedAsync(
        OrchestratedMailbox mailbox,
        string folderName,
        string subjectPrefix,
        SyntheticParticipant author,
        int messageCount,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] headers)
    {
        foreach (var index in Enumerable.Range(0, messageCount))
        {
            await mailbox.AppendAsync(
                folderName,
                $"{subjectPrefix}-{index:D2}",
                author,
                headers,
                cancellationToken);
        }
    }

    private static Task<MailboxSynchronizationResult> SynchronizeAsync(
        OrchestratedMailFathomServices services,
        MailFolderMapping folder,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailboxSynchronizer>().SynchronizeAsync(
                SyntheticMailAccount.AccountId,
                folder,
                token),
            cancellationToken);

    /// <summary>Reads every collected address under one test's own suffix, in the book's comparison form.</summary>
    /// <remarks>
    /// Narrowed to the suffix rather than read whole, because the book is shared with every other class in this
    /// collection and a claim over all of it would be decided by whatever they seeded.
    /// </remarks>
    private static Task<IReadOnlyList<string>> ReadCollectedAddressesAsync(
        OrchestratedMailFathomServices services,
        string suffix,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) => (IReadOnlyList<string>)await scope.GetRequiredService<MailFathomDbContext>()
                .Contacts
                .AsNoTracking()
                .Where(contact => contact.Origin == ContactOrigin.Collected)
                .SelectMany(contact => contact.Addresses)
                .Select(address => address.NormalizedAddress)
                .Where(address => address.EndsWith(suffix))
                .OrderBy(address => address)
                .ToArrayAsync(token),
            cancellationToken);

    private static Task<Contact?> ReadCollectedContactAsync(
        OrchestratedMailFathomServices services,
        string address,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) =>
            {
                if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
                {
                    throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
                }

                return scope.GetRequiredService<IContactDirectory>().FindByAddressAsync(emailAddress, token);
            },
            cancellationToken);
}
