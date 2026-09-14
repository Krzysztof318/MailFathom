// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
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

/// <summary>Proves a stored message belongs to the account it names, and reaches only a caller assigned that account.</summary>
/// <remarks>
/// <para>
/// Three claims live here that no substitute can make. That the row names the account at all is what a real
/// <c>INSERT</c> leaves behind, and a column left at its default would be reported by nothing else. That a read
/// narrowed by the caller's assignments returns none of an unassigned account's mail is a predicate PostgreSQL
/// evaluates, stated here as two counts under two account identifiers so the account column is the only thing
/// separating them. And that a timeline naming one account is served from the index built for it is a plan, which
/// needs enough rows for the account term to stop being the whole of the selectivity.
/// </para>
/// <para>
/// The mail of the two unassigned accounts is stored through the production paths and then left unassigned, which is
/// the state this claim turns on: an account is reached by the users an administrator assigned it to, so an account
/// nobody is assigned is read by nobody. Two accounts rather than one, because a corpus in which the alias and the
/// account narrow to the same rows would let a plan chosen for either look like a plan chosen for the account.
/// </para>
/// <para>
/// No user is provisioned beside the suite's own, because a stored message no longer carries one: what separates the
/// served caller's mail from the rest is which accounts that caller is assigned, and the suite's assignment relation
/// already names exactly one.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailOwnershipTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The alias every account in this class binds, so what separates their mail is whose mailbox it is.</summary>
    private const string FolderAlias = "mail-ownership";

    private const string FirstUnassignedAccount = "mail-ownership-first";

    private const string SecondUnassignedAccount = "mail-ownership-second";

    /// <summary>
    /// Enough mail for a sequential scan to be the more expensive plan, split over two accounts so the account
    /// predicate carries half the selectivity rather than all of it.
    /// </summary>
    private const int SeededEmailsPerUnassignedAccount = 300;

    /// <summary>The first UID of the block this class writes, which is what tells its rows from another class's.</summary>
    private const uint FirstUnassignedUid = 60_000;

    /// <summary>The UID of the one message the assigned account holds here, which is this class's control.</summary>
    /// <remarks>
    /// An absence proves nothing unless the same observation reports a presence, and the observation below is the
    /// caller's own timeline read over this alias. Without a message of their own account in it, a read that returned
    /// nothing because the folder was empty would look exactly like a read that returned nothing because the mail
    /// belongs to a mailbox they are not assigned.
    /// </remarks>
    private const uint AssignedAccountControlUid = 60_900;

    private const int PageSize = 50;

    /// <summary>The subject prefix every seeded unassigned message carries, which is how one is recognized in a page.</summary>
    private const string UnassignedSubjectPrefix = "mail-ownership-unassigned-";

    private const string AssignedAccountControlSubject = "mail-ownership-assigned-control";

    /// <summary>Counts one account's stored mail, which is the whole of the narrowing a stored message now carries.</summary>
    private const string AccountMailCountSql =
        """
        SELECT count(*)
        FROM stored_emails
        WHERE "MailboxAccountId" = @accountId
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
        WHERE "MailboxAccountId" = @accountId
        ORDER BY "ReceivedAt" DESC NULLS LAST, "Id" DESC
        LIMIT @pageSize
        """;

    [Fact]
    public async Task StoredEmails_MailOfAnAccountTheCallerIsNotAssigned_HangsOnThatAccountAndReachesNoReadOfTheirs()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var controlStoredEmailId = await SeedControlMessageAsync(services, cancellationToken);

        foreach (var accountId in UnassignedAccountIds())
        {
            await SeedUnassignedAccountMailAsync(services, accountId, cancellationToken);
        }

        await AnalyzeAsync(services, cancellationToken);

        // Act
        var storedRowAccounts = await ReadUnassignedRowAccountsAsync(services, cancellationToken);
        var accountRows = await ReadUnassignedAccountRowsAsync(services, cancellationToken);
        var folderRows = await ReadUnassignedFolderRowsAsync(services, cancellationToken);

        var underTheirs = await CountAsync(services, FirstUnassignedAccount, cancellationToken);
        var controlAccount = await ReadOwningAccountAsync(services, controlStoredEmailId, cancellationToken);

        var ourPage = await ReadAssignedCallerPageAsync(services, cancellationToken);

        var timelinePlan = await OrchestratedQueryPlans.ReadAsync(
            services,
            AccountTimelinePageSql,
            [AccountParameter(FirstUnassignedAccount), PageSizeParameter()],
            cancellationToken);

        // Assert
        // Every row the write path left behind names the account it was stored under, on the three tables one binding
        // and one upsert touch.
        Assert.Equal(SeededEmailsPerUnassignedAccount * 2, storedRowAccounts.Count);
        Assert.All(
            storedRowAccounts,
            accountId => Assert.Contains(accountId, (string[])[FirstUnassignedAccount, SecondUnassignedAccount]));
        Assert.Equal([FirstUnassignedAccount, SecondUnassignedAccount], [.. accountRows.Order(StringComparer.Ordinal)]);
        Assert.Equal([FirstUnassignedAccount, SecondUnassignedAccount], [.. folderRows.Order(StringComparer.Ordinal)]);

        // The mail is counted by its account and by nothing beside it, which is what the rekeying left.
        Assert.Equal(SeededEmailsPerUnassignedAccount, underTheirs);

        // The one port a worker reaches an account through answers the account the control message was stored under,
        // rather than a user nothing keys onto any more.
        Assert.Equal(SyntheticMailAccount.Account, controlAccount);

        // The control says the read reaches this folder at all, which is what makes the absence beside it an absence
        // rather than an empty folder.
        Assert.Contains(ourPage, summary => summary.Subject == AssignedAccountControlSubject);
        Assert.DoesNotContain(
            ourPage,
            summary => summary.Subject?.StartsWith(UnassignedSubjectPrefix, StringComparison.Ordinal) == true);

        Assert.Contains(
            PersistenceConstraintNames.StoredEmailAccountTimelineIndexName,
            timelinePlan,
            StringComparison.Ordinal);
    }

    private static MailAccountId[] UnassignedAccountIds() =>
        [MailAccountId.Create(FirstUnassignedAccount), MailAccountId.Create(SecondUnassignedAccount)];

    /// <summary>Stores one message of the assigned account in this class's folder, as the control the absence needs.</summary>
    private static async Task<StoredEmailId> SeedControlMessageAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrence = SyntheticEmail.OccurrenceIn(binding, AssignedAccountControlUid);
        StoredEmailId storedEmailId = default;

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                storedEmailId = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrence, AssignedAccountControlSubject),
                    SyntheticEmail.ExtractionOf(
                        occurrence,
                        AssignedAccountControlSubject,
                        SyntheticEmail.BodyTextContaining("control", wordCount: 20),
                        "recipient@mailfathom.test"),
                    StoredEmailContentAvailability.Available,
                    token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailId;
    }

    /// <summary>Binds one unassigned account's folder and stores its share of the corpus, through the production paths.</summary>
    private static async Task SeedUnassignedAccountMailAsync(
        OrchestratedMailFathomServices services,
        MailAccountId accountId,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(
            services,
            accountId,
            FolderAlias,
            FolderAlias,
            cancellationToken);

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var repository = scope.GetRequiredService<IEmailMetadataRepository>();

                foreach (var position in Enumerable.Range(0, SeededEmailsPerUnassignedAccount))
                {
                    var occurrence = SyntheticEmail.OccurrenceIn(
                        accountId,
                        binding,
                        FirstUnassignedUid + (uint)position);
                    var subject = $"{UnassignedSubjectPrefix}{accountId.Value}-{position:D4}";

                    await repository.UpsertMetadataAsync(
                        session,
                        SyntheticEmail.RemoteMetadataOf(occurrence, subject),
                        SyntheticEmail.ExtractionOf(
                            occurrence,
                            subject,
                            SyntheticEmail.BodyTextContaining($"unassigned{position}", wordCount: 40),
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

    private static Task<List<string>> ReadUnassignedRowAccountsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                .AsNoTracking()
                .Where(email => email.MailboxAccountId == FirstUnassignedAccount
                    || email.MailboxAccountId == SecondUnassignedAccount)
                .Select(email => email.MailboxAccountId)
                .ToListAsync(token),
            cancellationToken);

    private static Task<List<string>> ReadUnassignedAccountRowsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().MailboxAccounts
                .AsNoTracking()
                .Where(account => account.Id == FirstUnassignedAccount || account.Id == SecondUnassignedAccount)
                .Select(account => account.Id)
                .ToListAsync(token),
            cancellationToken);

    private static Task<List<string>> ReadUnassignedFolderRowsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().MailFolders
                .AsNoTracking()
                .Where(folder => folder.MailboxAccountId == FirstUnassignedAccount
                    || folder.MailboxAccountId == SecondUnassignedAccount)
                .Select(folder => folder.MailboxAccountId)
                .ToListAsync(token),
            cancellationToken);

    /// <summary>Reads the account one stored message belongs to, through the port a worker resolves it by.</summary>
    private static Task<MailAccountId> ReadOwningAccountAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailOwnership>()
                .ReadStoredEmailAccountAsync(storedEmailId, token),
            cancellationToken);

    /// <summary>Counts one account's stored mail, through the connection the scoped context owns.</summary>
    private static Task<long> CountAsync(
        OrchestratedMailFathomServices services,
        string accountId,
        CancellationToken cancellationToken) => OrchestratedQueryPlans.WithConnectionAsync(
            services,
            async (connection, token) =>
            {
                await using var command = OrchestratedQueryPlans.CreateCommand(
                    connection,
                    AccountMailCountSql,
                    [AccountParameter(accountId)]);

                return (long)(await command.ExecuteScalarAsync(token))!;
            },
            cancellationToken);

    /// <summary>Reads this class's folder as an admitted caller, which is the narrowing the absence is claimed under.</summary>
    /// <remarks>
    /// Read as a caller rather than as the process, because the scope a read model resolves is the accounts the caller
    /// is assigned and work no caller requested is assigned nothing at all. The grant is empty because the timeline
    /// reader is a store rather than a published use case: what admits the read is being a caller, and what bounds it
    /// is the assignment relation.
    /// </remarks>
    private static Task<IReadOnlyList<EmailSummary>> ReadAssignedCallerPageAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.AsCallerInScopeAsync(
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
            [],
            cancellationToken);

    private static NpgsqlParameter AccountParameter(string accountId) =>
        new("accountId", NpgsqlDbType.Text) { Value = accountId };

    private static NpgsqlParameter PageSizeParameter() =>
        new("pageSize", NpgsqlDbType.Integer) { Value = PageSize };
}
