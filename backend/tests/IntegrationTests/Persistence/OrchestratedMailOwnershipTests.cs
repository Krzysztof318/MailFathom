// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves a stored message belongs to the owner of the account it names, and to nobody else's read.</summary>
/// <remarks>
/// <para>
/// Three claims live here that no substitute can make. That the owner reaches the row at all is what a real
/// <c>INSERT</c> leaves behind, and a column left at its default would be reported by nothing else. That a read
/// narrowed by owner returns none of another owner's mail is a predicate PostgreSQL evaluates, stated here as the two
/// counts of one identifier under two owners so the owner column is the only thing separating them. And that a
/// timeline naming one account is served from the index built for it is a plan, which needs enough rows under one
/// owner for the account term to stop being the whole of the selectivity.
/// </para>
/// <para>
/// The second owner is provisioned here and erased in a <c>finally</c>, for the reason
/// <see cref="OrchestratedForeignOwner" /> gives. Two accounts are hung on them rather than one, because a corpus in
/// which the owner and the account narrow to the same rows would let a plan chosen for either look like a plan chosen
/// for the account.
/// </para>
/// <para>
/// The identifiers are this class's own. <c>mailbox_accounts</c> still keeps a single-column key, so an identifier one
/// owner holds is an identifier no other owner may hold — which is why the strongest form of this claim, two owners
/// each calling an account <c>work</c>, belongs to the change that takes the key rather than to this one.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailOwnershipTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The alias every account in this class binds, so what separates their mail is whose it is.</summary>
    private const string FolderAlias = "mail-ownership";

    private const string FirstForeignAccount = "mail-ownership-first";

    private const string SecondForeignAccount = "mail-ownership-second";

    /// <summary>
    /// Enough mail under one owner for a sequential scan to be the more expensive plan, split over two accounts so the
    /// account predicate carries half the selectivity rather than all of it.
    /// </summary>
    private const int SeededEmailsPerForeignAccount = 300;

    /// <summary>The first UID of the block this class writes, which is what tells its rows from another class's.</summary>
    private const uint FirstForeignUid = 60_000;

    /// <summary>The UID of the one message the served owner stores here, which is this class's control.</summary>
    /// <remarks>
    /// An absence proves nothing unless the same observation reports a presence, and the observation below is the
    /// served owner's own timeline read over this alias. Without a message of theirs in it, a read that returned
    /// nothing because the folder was empty would look exactly like a read that returned nothing because the mail
    /// belonged to somebody else.
    /// </remarks>
    private const uint ServedOwnerControlUid = 60_900;

    private const int PageSize = 50;

    /// <summary>The subject prefix every seeded foreign message carries, which is how one is recognized in a page.</summary>
    private const string ForeignSubjectPrefix = "mail-ownership-foreign-";

    private const string ServedOwnerControlSubject = "mail-ownership-served-control";

    /// <summary>Counts one account's stored mail under one owner, which is the narrowing the claim turns on.</summary>
    private const string OwnedMailCountSql =
        """
        SELECT count(*)
        FROM stored_emails
        WHERE "OwnerId" = @ownerId AND "MailboxAccountId" = @accountId
        """;

    /// <summary>Reads one page of one account's timeline in the order the account timeline index declares.</summary>
    /// <remarks>
    /// Written here rather than taken from the read model, for the reason <see cref="OrchestratedStoredEmailIndexTests" />
    /// gives: the ordering states an explicit <c>NULLS LAST</c>, which is what the index declares and what EF Core
    /// publishes no way to write. What the read model does over the same rows is asserted by the reader's own tests.
    /// </remarks>
    private const string AccountTimelinePageSql =
        """
        SELECT "Id", "ReceivedAt"
        FROM stored_emails
        WHERE "OwnerId" = @ownerId AND "MailboxAccountId" = @accountId
        ORDER BY "ReceivedAt" DESC NULLS LAST, "Id" DESC
        LIMIT @pageSize
        """;

    [Fact]
    public async Task StoredEmails_MailUnderASecondOwner_HangOnThatOwnerAndAreServedToNoReadOfAnothers()
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

            await SeedControlMessageAsync(services, cancellationToken);

            foreach (var accountId in ForeignAccountIds())
            {
                await SeedForeignMailAsync(services, foreignOwner, accountId, cancellationToken);
            }

            await AnalyzeAsync(services, cancellationToken);

            // Act
            var storedRows = await ReadForeignRowOwnersAsync(services, cancellationToken);
            var accountRows = await ReadForeignAccountOwnersAsync(services, cancellationToken);
            var folderRows = await ReadForeignFolderOwnersAsync(services, cancellationToken);

            var underTheirOwner = await CountAsync(services, foreignOwnerId, FirstForeignAccount, cancellationToken);
            var underOurs = await CountAsync(services, servedOwner.Value, FirstForeignAccount, cancellationToken);

            var ourPage = await ReadServedOwnerPageAsync(services, cancellationToken);

            var timelinePlan = await OrchestratedQueryPlans.ReadAsync(
                services,
                AccountTimelinePageSql,
                [OwnerParameter(foreignOwnerId), AccountParameter(FirstForeignAccount), PageSizeParameter()],
                cancellationToken);

            // Assert
            // Every row the write path left behind names the owner of the account it was stored under, on the three
            // tables one binding and one upsert touch.
            Assert.Equal(SeededEmailsPerForeignAccount * 2, storedRows.Count);
            Assert.All(storedRows, ownerId => Assert.Equal(foreignOwnerId, ownerId));
            Assert.Equal(2, accountRows.Count);
            Assert.All(accountRows, ownerId => Assert.Equal(foreignOwnerId, ownerId));
            Assert.Equal(2, folderRows.Count);
            Assert.All(folderRows, ownerId => Assert.Equal(foreignOwnerId, ownerId));

            // The same identifier under two owners, counted twice. The account term is identical in both reads, so the
            // zero is the owner column and nothing else.
            Assert.Equal(SeededEmailsPerForeignAccount, underTheirOwner);
            Assert.Equal(0, underOurs);

            // The control says the read reaches this folder at all, which is what makes the absence beside it an
            // absence rather than an empty folder.
            Assert.Contains(ourPage, summary => summary.Subject == ServedOwnerControlSubject);
            Assert.DoesNotContain(
                ourPage,
                summary => summary.Subject?.StartsWith(ForeignSubjectPrefix, StringComparison.Ordinal) == true);

            Assert.Contains(
                PersistenceConstraintNames.StoredEmailAccountTimelineIndexName,
                timelinePlan,
                StringComparison.Ordinal);
        }
        finally
        {
            await OrchestratedForeignOwner.EraseAsync(services, foreignOwnerId);
        }
    }

    private static MailAccountId[] ForeignAccountIds() =>
        [MailAccountId.Create(FirstForeignAccount), MailAccountId.Create(SecondForeignAccount)];

    /// <summary>Stores one message of the served owner's own in this class's folder, as the control the absence needs.</summary>
    private static async Task SeedControlMessageAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrence = SyntheticEmail.OccurrenceIn(binding, ServedOwnerControlUid);

        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                session,
                SyntheticMailAccount.Owner,
                SyntheticEmail.RemoteMetadataOf(occurrence, ServedOwnerControlSubject),
                SyntheticEmail.ExtractionOf(
                    occurrence,
                    ServedOwnerControlSubject,
                    SyntheticEmail.BodyTextContaining("control", wordCount: 20),
                    "recipient@mailfathom.test"),
                StoredEmailContentAvailability.Available,
                token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    /// <summary>Binds one foreign account's folder and stores its share of the corpus, through the production paths.</summary>
    private static async Task SeedForeignMailAsync(
        OrchestratedMailFathomServices services,
        MailOwnerId foreignOwner,
        MailAccountId accountId,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(
            services,
            MailAccountIdentity.Create(foreignOwner, accountId),
            FolderAlias,
            FolderAlias,
            cancellationToken);

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var repository = scope.GetRequiredService<IEmailMetadataRepository>();

                foreach (var position in Enumerable.Range(0, SeededEmailsPerForeignAccount))
                {
                    var occurrence = SyntheticEmail.OccurrenceIn(
                        accountId,
                        binding,
                        FirstForeignUid + (uint)position);
                    var subject = $"{ForeignSubjectPrefix}{accountId.Value}-{position:D4}";

                    await repository.UpsertMetadataAsync(
                        session,
                        foreignOwner,
                        SyntheticEmail.RemoteMetadataOf(occurrence, subject),
                        SyntheticEmail.ExtractionOf(
                            occurrence,
                            subject,
                            SyntheticEmail.BodyTextContaining($"foreign{position}", wordCount: 40),
                            "recipient@mailfathom.test") with
                        {
                            ReceivedAt = SyntheticEmail.ReceivedAt.AddMinutes(position),
                        },
                        StoredEmailContentAvailability.Available,
                        token);
                }
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    /// <summary>
    /// Updates the statistics the plan assertion depends on, because a planner with none for a freshly filled table
    /// chooses from defaults and the assertion would describe that rather than the index.
    /// </summary>
    private static Task<int> AnalyzeAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database
                .ExecuteSqlRawAsync("ANALYZE stored_emails, mail_folders, mailbox_accounts", token),
            cancellationToken);

    private static Task<List<Guid>> ReadForeignRowOwnersAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                .AsNoTracking()
                .Where(email => email.MailboxAccountId == FirstForeignAccount
                    || email.MailboxAccountId == SecondForeignAccount)
                .Select(email => email.OwnerId)
                .ToListAsync(token),
            cancellationToken);

    private static Task<List<Guid>> ReadForeignAccountOwnersAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().MailboxAccounts
                .AsNoTracking()
                .Where(account => account.Id == FirstForeignAccount || account.Id == SecondForeignAccount)
                .Select(account => account.OwnerId)
                .ToListAsync(token),
            cancellationToken);

    private static Task<List<Guid>> ReadForeignFolderOwnersAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().MailFolders
                .AsNoTracking()
                .Where(folder => folder.MailboxAccountId == FirstForeignAccount
                    || folder.MailboxAccountId == SecondForeignAccount)
                .Select(folder => folder.OwnerId)
                .ToListAsync(token),
            cancellationToken);

    /// <summary>Counts one account's stored mail under one owner, through the connection the scoped context owns.</summary>
    private static Task<long> CountAsync(
        OrchestratedMailFathomServices services,
        Guid ownerId,
        string accountId,
        CancellationToken cancellationToken) => OrchestratedQueryPlans.WithConnectionAsync(
            services,
            async (connection, token) =>
            {
                await using var command = OrchestratedQueryPlans.CreateCommand(
                    connection,
                    OwnedMailCountSql,
                    [OwnerParameter(ownerId), AccountParameter(accountId)]);

                return (long)(await command.ExecuteScalarAsync(token))!;
            },
            cancellationToken);

    /// <summary>Reads the served owner's own timeline over this class's folder, the way a deployment resolves it.</summary>
    private static Task<IReadOnlyList<EmailSummary>> ReadServedOwnerPageAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailTimelineReader>().ReadPageAsync(
                EmailTimelineFilter.Create(
                    OrchestratedMailboxScope.Readable(scope, [FolderAlias]),
                    senderAddress: null,
                    recipientAddress: null,
                    subjectFragment: null,
                    receivedOnOrAfter: null,
                    receivedBefore: null,
                    isRemotelySeen: null,
                    isRemotelyFlagged: null,
                    keyword: null,
                    hasAttachments: null,
                    EmailTimelineDirection.NewestFirst),
                continueAfter: null,
                PageSize,
                token),
            cancellationToken);

    private static NpgsqlParameter OwnerParameter(Guid ownerId) =>
        new("ownerId", NpgsqlDbType.Uuid) { Value = ownerId };

    private static NpgsqlParameter AccountParameter(string accountId) =>
        new("accountId", NpgsqlDbType.Text) { Value = accountId };

    private static NpgsqlParameter PageSizeParameter() =>
        new("pageSize", NpgsqlDbType.Integer) { Value = PageSize };
}
