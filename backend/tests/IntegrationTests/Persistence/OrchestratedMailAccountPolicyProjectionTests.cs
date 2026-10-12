// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Synchronization;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves what a settings policy does to the rows of the mail accounts it governs, in the commit of each write that can move them.</summary>
/// <remarks>
/// <para>
/// What the deployment reads across accounts is the columns beside each declaration, and they hold what the account
/// is served rather than what its document states. A write of a policy governing the account and a write of the
/// account each derive them from the policies read inside their own transaction, and judge the account under those
/// there — a claim about statements and locks that no substitute stands in for.
/// </para>
/// <para>
/// Every policy here is an organization's, written for an organization the test created and removes, and every
/// account is one the test seeded and deletes, so nothing in this class touches the deployment's own policy row and
/// no other class in the collection reads what it writes.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailAccountPolicyProjectionTests(MailFathomOrchestrationFixture orchestration)
{
    private const string ForcingPush = """{"MailAccounts":{"Forced":{"Mode":"Push"}}}""";

    private const string DefaultingPolish = """{"MailAccounts":{"Defaults":{"Language":"Polish"}}}""";

    private const string StatingEnglish =
        """
        {
          "Language": "English",
          "Host": "imap.example.test",
          "UserName": "synthetic@example.test",
          "Secrets": { "Password": { "Name": "imap-password", "SecretReference": "systemd-credential:imap-password" } }
        }
        """;

    private const string StatingNoLanguage =
        """
        {
          "Host": "imap.example.test",
          "UserName": "synthetic@example.test",
          "Secrets": { "Password": { "Name": "imap-password", "SecretReference": "systemd-credential:imap-password" } }
        }
        """;

    /// <summary>How many records one policy write here may judge, which is past everything the shared database holds.</summary>
    private const int MaximumJudged = 100_000;

    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A policy forcing the mode rewrites the column of the account it governs in its own commit and leaves an
    /// account of no organization alone, and the policy lifting the force puts the account's own answer back — while
    /// the account's document is never touched.
    /// </summary>
    [Fact]
    public async Task CommitAsync_AnOrganizationsPolicyForcingTheMode_RewritesTheColumnsOfItsAccountsAndNoOthers()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        var governed = Guid.CreateVersion7();
        var elsewhere = Guid.CreateVersion7();

        try
        {
            await CreateOrganizationAsync(services, organization, cancellationToken);
            await SeedAsync(
                services,
                [Row(governed, organization, StatingEnglish), Row(elsewhere, organizationId: null, StatingEnglish)],
                cancellationToken);

            // Act
            var forced = await CommitPolicyAsync(services, organization, ForcingPush, expectedVersion: 0, cancellationToken);
            var whileForced = await ReadAsync(services, governed, cancellationToken);
            var elsewhereWhileForced = await ReadAsync(services, elsewhere, cancellationToken);
            var lifted = await CommitPolicyAsync(services, organization, "{}", expectedVersion: 1, cancellationToken);
            var afterwards = await ReadAsync(services, governed, cancellationToken);

            // Assert
            Assert.Equal(SettingsPolicyCommitResult.Committed, forced.Result);
            Assert.Equal(MailSynchronizationMode.Push, whileForced.SynchronizationMode);
            Assert.True(whileForced.HasReadableSettings);
            Assert.DoesNotContain("Push", whileForced.Document, StringComparison.Ordinal);
            Assert.Equal(MailSynchronizationMode.Polling, elsewhereWhileForced.SynchronizationMode);
            Assert.Equal(SettingsPolicyCommitResult.Committed, lifted.Result);
            Assert.Equal(MailSynchronizationMode.Polling, afterwards.SynchronizationMode);
        }
        finally
        {
            await RemoveAsync(services, organization, governed, elsewhere);
        }
    }

    /// <summary>
    /// A policy that would take away the language an account relies on is refused naming the account as a mail
    /// account, and the policy in force stays where it was.
    /// </summary>
    [Fact]
    public async Task CommitAsync_APolicyThatWouldLeaveAnAccountUnservable_IsRefusedNamingTheAccountAndStoresNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        var relying = Guid.CreateVersion7();

        try
        {
            await CreateOrganizationAsync(services, organization, cancellationToken);
            await SeedAsync(services, [Row(relying, organization, StatingNoLanguage)], cancellationToken);
            await CommitPolicyAsync(services, organization, DefaultingPolish, expectedVersion: 0, cancellationToken);

            // Act
            var refused = await CommitPolicyAsync(services, organization, "{}", expectedVersion: 1, cancellationToken);

            // Assert
            Assert.Equal(SettingsPolicyCommitResult.RecordsLeftInvalid, refused.Result);
            var named = Assert.Single(refused.RecordsInTheWay);
            Assert.Equal(relying, named.Identity);
            Assert.Equal(SettingsPolicyRecordKind.MailAccount, named.Kind);
            var standing = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<ISettingsPolicyStore>().ReadAsync(organization, token),
                cancellationToken);
            Assert.Equal(1, standing?.Version);
        }
        finally
        {
            await RemoveAsync(services, organization, relying);
        }
    }

    /// <summary>
    /// An account's write is composed under the policies read inside its transaction, so the composer is handed the
    /// organization's policy as stored, and what it answers nothing for is not written.
    /// </summary>
    [Fact]
    public async Task SaveAsync_AnAccountWrittenUnderAStoredPolicy_IsComposedUnderThatPolicyAndWritesNothingItRefuses()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        var account = Guid.CreateVersion7();
        GoverningSettingsPolicies? composedUnder = null;

        try
        {
            await CreateOrganizationAsync(services, organization, cancellationToken);
            await SeedAsync(services, [Row(account, organization, StatingEnglish)], cancellationToken);
            await CommitPolicyAsync(services, organization, ForcingPush, expectedVersion: 0, cancellationToken);

            // Act
            var refused = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IMailAccountRecordStore>().SaveAsync(
                    account,
                    expectedVersion: 1,
                    policies =>
                    {
                        composedUnder = policies;

                        return null;
                    },
                    token),
                cancellationToken);

            // Assert
            Assert.Equal(MailAccountWriteResult.NothingComposed, refused.Result);
            Assert.Contains("\"Push\"", composedUnder?.Organization?.Json, StringComparison.Ordinal);
            Assert.Equal(1, composedUnder?.Organization?.Version);
            Assert.Contains("English", (await ReadAsync(services, account, cancellationToken)).Document, StringComparison.Ordinal);
        }
        finally
        {
            await RemoveAsync(services, organization, account);
        }
    }

    private static async Task CreateOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        CancellationToken cancellationToken) =>
        Assert.Equal(
            OrganizationWriteOutcome.Written,
            (await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                    organization,
                    $"Organization {organization:N}",
                    OrganizationShortName.Create($"{organization:N}"),
                    RecordedAt,
                    token),
                cancellationToken)).Outcome);

    private static async Task SeedAsync(
        OrchestratedMailFathomServices services,
        MailAccountRecordEntity[] rows,
        CancellationToken cancellationToken) =>
        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (_, session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                    context.MailAccountRecords.AddRange(rows);

                    await context.SaveChangesAsync(token);
                },
                cancellationToken));

    /// <summary>An account as a build older than the columns left it: its document, and columns nothing has read out of it yet.</summary>
    private static MailAccountRecordEntity Row(Guid id, Guid? organizationId, string document) => new()
    {
        Id = id,
        DisplayName = $"account-{id:N}",
        Document = document,
        OrganizationId = organizationId,
        Version = 1,
        CreatedAt = RecordedAt,
        UpdatedAt = RecordedAt,
    };

    private static Task<SettingsPolicyCommit> CommitPolicyAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        string json,
        long expectedVersion,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<ISettingsPolicyStore>().CommitAsync(
                organization,
                json,
                expectedVersion,
                new GovernedRecordsToJudge(Users: false, MailAccounts: true, MaximumJudged),
                token),
            cancellationToken);

    private static Task<MailAccountRecordEntity> ReadAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .MailAccountRecords
                .AsNoTracking()
                .SingleAsync(record => record.Id == account, token),
            cancellationToken);

    /// <summary>Deletes the accounts a test seeded and then the organization, which an organization holding accounts refuses.</summary>
    /// <remarks>Uncancellable because it runs in a <c>finally</c>, where the test's own token may already be cancelled.</remarks>
    private static async Task RemoveAsync(OrchestratedMailFathomServices services, Guid organization, params Guid[] accounts)
    {
        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .MailAccountRecords
                .Where(account => accounts.Contains(account.Id))
                .ExecuteDeleteAsync(token),
            CancellationToken.None);

        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
            CancellationToken.None);
    }
}
