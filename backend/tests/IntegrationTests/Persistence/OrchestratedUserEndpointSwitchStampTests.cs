// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.EndpointSwitches;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the stamp on a user's endpoint switches: which writes keep it matching, which rows trail it, and what deriving one again does.</summary>
/// <remarks>
/// <para>
/// A row written past a settings policy is told apart by two columns and nothing else, so every statement that
/// writes or steps them, the predicate that finds a trailing row, and the lock a derivation takes are claims about
/// PostgreSQL that no substitute stands in for. A build that does not know policies is played by the statements such
/// a build issues: a write of the document and the switches that raises the version, and a write of the organization
/// alone.
/// </para>
/// <para>
/// The shared database holds trailing rows other classes left — a user this suite inserts directly names no stamp —
/// so a claim here is about the rows the test wrote and never about how many rows trail.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedUserEndpointSwitchStampTests(MailFathomOrchestrationFixture orchestration)
{
    private const string StatingEnglish = """{"Language":"English"}""";

    private const string ForcingOffMcp = """{"Users":{"Forced":{"EndpointAccess":{"McpEndpoint":false}}}}""";

    /// <summary>Past every row the shared database holds, so a reading here is never cut short of the rows the test wrote.</summary>
    private const int EveryRow = 100_000;

    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    private static readonly UserEndpointAccess McpOff = new(McpEndpoint: false, ClientEndpoint: true);

    /// <summary>
    /// Every write of this build leaves the stamp matching — the record, the move, the policy, and an assignment
    /// that only steps the version — and the record an older build then writes past the force is found, derived
    /// under the policy, and stamped without its version moving. A second derivation finds nothing to do.
    /// </summary>
    [Fact]
    public async Task DeriveAgainAsync_ARecordAnOlderBuildWrotePastAForce_DerivesItUnderThePolicyAndStampsTheRow()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        var member = Guid.CreateVersion7();
        var account = Guid.CreateVersion7();

        try
        {
            await CreateOrganizationAsync(services, organization, cancellationToken);
            await ProvisionStatingEnglishAsync(services, member, cancellationToken);
            await MoveAsync(services, member, organization, cancellationToken);
            await CommitPolicyAsync(services, organization, ForcingOffMcp, cancellationToken);
            await AssignAnAccountAsync(services, account, organization, member, expectedUserVersion: 3, cancellationToken);
            var trailedAfterThisBuildsWrites = await TrailsAsync(services, member, cancellationToken);

            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database.ExecuteSqlAsync(
                    $"""
                     UPDATE settings_accounts
                     SET "Document" = CAST({StatingEnglish} AS jsonb), "Version" = "Version" + 1,
                         "McpEndpointEnabled" = TRUE, "ClientEndpointEnabled" = TRUE
                     WHERE "Id" = {member}
                     """,
                    token),
                cancellationToken);

            // Act
            var trailedAfterTheOlderBuildsWrite = await TrailsAsync(services, member, cancellationToken);
            var derived = await DeriveAgainAsync(services, member, cancellationToken);
            var derivedTwice = await DeriveAgainAsync(services, member, cancellationToken);

            // Assert
            Assert.False(trailedAfterThisBuildsWrites);
            Assert.True(trailedAfterTheOlderBuildsWrite);
            Assert.True(derived);
            Assert.False(derivedTwice);
            Assert.Equal(McpOff, await ReadSwitchesAsync(services, member, cancellationToken));
            Assert.False(await TrailsAsync(services, member, cancellationToken));
            Assert.Equal(5, await ReadVersionAsync(services, member, cancellationToken));
        }
        finally
        {
            await RemoveAsync(services, organization, member);
        }
    }

    /// <summary>
    /// An older build's move writes the organization and nothing else, so neither the version nor the switches say
    /// the row changed hands: the organization the stamp names is what finds it, and the policy of the organization
    /// the user now stands in is what its switches are derived under.
    /// </summary>
    [Fact]
    public async Task DeriveAgainAsync_AUserAnOlderBuildMoved_DerivesTheirSwitchesUnderTheOrganizationTheyStandIn()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        var moved = Guid.CreateVersion7();

        try
        {
            await CreateOrganizationAsync(services, organization, cancellationToken);
            await CommitPolicyAsync(services, organization, ForcingOffMcp, cancellationToken);
            await ProvisionStatingEnglishAsync(services, moved, cancellationToken);

            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database.ExecuteSqlAsync(
                    $"""UPDATE settings_accounts SET "OrganizationId" = {organization} WHERE "Id" = {moved}""",
                    token),
                cancellationToken);

            // Act
            var trailed = await TrailsAsync(services, moved, cancellationToken);
            var derived = await DeriveAgainAsync(services, moved, cancellationToken);

            // Assert
            Assert.True(trailed);
            Assert.True(derived);
            Assert.Equal(McpOff, await ReadSwitchesAsync(services, moved, cancellationToken));
            Assert.False(await TrailsAsync(services, moved, cancellationToken));
        }
        finally
        {
            await RemoveAsync(services, organization, moved);
        }
    }

    /// <summary>
    /// A reading carries on past the user the one before it ended on, and a derivation that answers nothing leaves
    /// the row exactly as it stood — switches and stamp — for a later reading to find.
    /// </summary>
    [Fact]
    public async Task ReadTrailingAsync_AReadingPastAUserThatCouldNotBeStamped_LeavesThatUserTrailingAndReadsTheNext()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        Guid[] inserted = [Guid.CreateVersion7(), Guid.CreateVersion7()];

        try
        {
            foreach (var user in inserted)
            {
                await OrchestratedForeignUser.ProvisionAsync(services, user, cancellationToken);
            }

            var trailing = await ReadTrailingAsync(services, after: null, cancellationToken);
            Guid[] inReadingOrder = [.. trailing.Where(inserted.Contains)];

            // Act
            var leftAsItStood = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IUserEndpointSwitchStore>()
                    .DeriveAgainAsync(UserId.Create(inReadingOrder[0]), _ => null, token),
                cancellationToken);
            var pastTheFirst = await ReadTrailingAsync(services, after: inReadingOrder[0], cancellationToken);

            // Assert
            Assert.Equal(2, inReadingOrder.Length);
            Assert.False(leftAsItStood);
            Assert.True(await TrailsAsync(services, inReadingOrder[0], cancellationToken));
            Assert.DoesNotContain(inReadingOrder[0], pastTheFirst);
            Assert.Contains(inReadingOrder[1], pastTheFirst);
        }
        finally
        {
            foreach (var user in inserted)
            {
                await OrchestratedForeignUser.EraseAsync(services, user);
            }
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

    /// <summary>Provisions a user and commits a record stating the language a record must name, which is the write that first stamps the row.</summary>
    private static async Task ProvisionStatingEnglishAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        CancellationToken cancellationToken)
    {
        await OrchestratedForeignUser.ProvisionAsync(services, user, cancellationToken);

        Assert.Equal(
            2,
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IUserSettingsDocumentWriter>().CommitAsync(
                    UserId.Create(user),
                    expectedVersion: 1,
                    _ => new UserRecordCandidate(StatingEnglish, UserEndpointAccess.Everywhere),
                    token),
                cancellationToken));
    }

    private static async Task MoveAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        Guid organization,
        CancellationToken cancellationToken) =>
        Assert.Equal(
            OrganizationWriteOutcome.Written,
            (await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>()
                    .SetUserOrganizationAsync(UserId.Create(user), organization, token),
                cancellationToken)).Outcome);

    private static async Task CommitPolicyAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        string json,
        CancellationToken cancellationToken) =>
        Assert.Equal(
            SettingsPolicyCommitResult.Committed,
            (await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<ISettingsPolicyStore>().CommitAsync(
                    organization,
                    json,
                    expectedVersion: 0,
                    new GovernedRecordsToJudge(Users: true, MailAccounts: false, EveryRow),
                    token),
                cancellationToken)).Result);

    /// <summary>Records an account in the organization and assigns it to the user, which steps the user's version and leaves their switches alone.</summary>
    private static async Task AssignAnAccountAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        Guid organization,
        Guid user,
        long expectedUserVersion,
        CancellationToken cancellationToken)
    {
        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (_, session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                    context.MailAccountRecords.Add(new MailAccountRecordEntity
                    {
                        Id = account,
                        DisplayName = $"account-{account:N}",
                        Document = "{}",
                        OrganizationId = organization,
                        Version = 1,
                        CreatedAt = RecordedAt,
                        UpdatedAt = RecordedAt,
                    });

                    await context.SaveChangesAsync(token);
                },
                cancellationToken));

        Assert.Equal(
            MailAccountWriteResult.Committed,
            (await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IMailAccountRecordStore>()
                    .AssignAsync(account, UserId.Create(user), expectedUserVersion, token),
                cancellationToken)).Result);
    }

    /// <summary>Derives one user's switches again the way the reconciliation does: through the deployment's own judge, under the policies the store reads.</summary>
    private static Task<bool> DeriveAgainAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IUserEndpointSwitchStore>().DeriveAgainAsync(
                UserId.Create(user),
                record => scope.GetRequiredService<IUserRecordPolicyJudge>().Judge([record])[0].EndpointAccess,
                token),
            cancellationToken);

    private static async Task<bool> TrailsAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        CancellationToken cancellationToken) =>
        (await ReadTrailingAsync(services, after: null, cancellationToken)).Contains(user);

    private static async Task<IReadOnlyList<Guid>> ReadTrailingAsync(
        OrchestratedMailFathomServices services,
        Guid? after,
        CancellationToken cancellationToken) =>
        [
            .. (await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IUserEndpointSwitchStore>().ReadTrailingAsync(after, EveryRow, token),
                cancellationToken)).Select(user => user.Value),
        ];

    private static Task<UserEndpointAccess> ReadSwitchesAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .UserAccounts
                .AsNoTracking()
                .Where(record => record.Id == user)
                .Select(record => new UserEndpointAccess(record.McpEndpointEnabled, record.ClientEndpointEnabled))
                .SingleAsync(token),
            cancellationToken);

    private static Task<long> ReadVersionAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .UserAccounts
                .AsNoTracking()
                .Where(record => record.Id == user)
                .Select(record => record.Version)
                .SingleAsync(token),
            cancellationToken);

    /// <summary>Erases the users a test provisioned and then the organization, which an organization with members refuses.</summary>
    /// <remarks>Uncancellable because it runs in a <c>finally</c>, where the test's own token may already be cancelled.</remarks>
    private static async Task RemoveAsync(OrchestratedMailFathomServices services, Guid organization, params Guid[] users)
    {
        foreach (var user in users)
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
        }

        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
            CancellationToken.None);
    }
}
