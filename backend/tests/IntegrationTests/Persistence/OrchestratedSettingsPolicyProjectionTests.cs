// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves what a settings policy does to the rows of the users it governs, in the commit of each write that can move them.</summary>
/// <remarks>
/// <para>
/// What a request authenticates against is two columns on a user's row, and three writes can move what those columns
/// should say: a write of a policy governing the user, a write of the user's record, and a move of the user into or
/// out of an organization. Each derives the columns from the policies it reads inside its own transaction and judges
/// the record under them there, which is a claim about statements and locks that no substitute stands in for.
/// </para>
/// <para>
/// Every policy here is an organization's, written for an organization the test created and removes, so nothing in
/// this class touches the deployment's own policy row and no other class in the collection reads what it writes.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedSettingsPolicyProjectionTests(MailFathomOrchestrationFixture orchestration)
{
    private const string StatingEnglish = """{"Language":"English"}""";

    private const string DefaultingPolish = """{"Users":{"Defaults":{"Language":"Polish"}}}""";

    private const string ForcingOffMcp = """{"Users":{"Forced":{"EndpointAccess":{"McpEndpoint":false}}}}""";

    /// <summary>How many user records one policy write here may judge, which is past everything the shared database holds.</summary>
    private const int MaximumJudged = 100_000;

    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A policy forcing a switch rewrites the row of the member it governs in its own commit and leaves a user of
    /// no organization alone, and the policy lifting the force puts the member's own answer back.
    /// </summary>
    [Fact]
    public async Task CommitAsync_AnOrganizationsPolicyForcingASwitch_RewritesItsMembersRowsAndNoOthers()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        var member = Guid.CreateVersion7();
        var stranger = Guid.CreateVersion7();

        try
        {
            await CreateOrganizationAsync(services, organization, cancellationToken);
            await ProvisionStatingEnglishAsync(services, member, cancellationToken);
            await ProvisionStatingEnglishAsync(services, stranger, cancellationToken);
            await MoveAsync(services, member, organization, cancellationToken);

            // Act
            var forced = await CommitPolicyAsync(services, organization, ForcingOffMcp, expectedVersion: 0, cancellationToken);
            var whileForced = await ReadSwitchesAsync(services, member, cancellationToken);
            var strangerWhileForced = await ReadSwitchesAsync(services, stranger, cancellationToken);
            var lifted = await CommitPolicyAsync(services, organization, "{}", expectedVersion: 1, cancellationToken);

            // Assert
            Assert.Equal(SettingsPolicyCommitResult.Committed, forced.Result);
            Assert.Equal(new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true), whileForced);
            Assert.Equal(UserEndpointAccess.Everywhere, strangerWhileForced);
            Assert.Equal(SettingsPolicyCommitResult.Committed, lifted.Result);
            Assert.Equal(UserEndpointAccess.Everywhere, await ReadSwitchesAsync(services, member, cancellationToken));
            Assert.Equal(UserEndpointAccess.Everywhere, await ReadSwitchesAsync(services, stranger, cancellationToken));
        }
        finally
        {
            await RemoveAsync(services, organization, member, stranger);
        }
    }

    /// <summary>
    /// A policy that would take away the language a member's record relies on is refused naming the member, and the
    /// policy in force stays where it was.
    /// </summary>
    [Fact]
    public async Task CommitAsync_APolicyThatWouldLeaveAMembersRecordUnservable_IsRefusedNamingTheRecordAndStoresNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        var member = Guid.CreateVersion7();

        try
        {
            await CreateOrganizationAsync(services, organization, cancellationToken);
            await OrchestratedForeignUser.ProvisionAsync(services, member, cancellationToken);
            await MoveAsync(services, member, organization, cancellationToken);
            await CommitPolicyAsync(services, organization, DefaultingPolish, expectedVersion: 0, cancellationToken);

            // Act
            var refused = await CommitPolicyAsync(services, organization, "{}", expectedVersion: 1, cancellationToken);

            // Assert
            Assert.Equal(SettingsPolicyCommitResult.RecordsLeftInvalid, refused.Result);
            Assert.Equal(member, Assert.Single(refused.RecordsInTheWay).Identity);
            var standing = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<ISettingsPolicyStore>().ReadAsync(organization, token),
                cancellationToken);
            Assert.Equal(1, standing?.Version);
            Assert.Contains("\"Polish\"", standing?.Json, StringComparison.Ordinal);
        }
        finally
        {
            await RemoveAsync(services, organization, member);
        }
    }

    /// <summary>
    /// A record's commit is composed under the policies read inside its transaction, so the writer is handed the
    /// organization's policy as stored and the row carries what the candidate resolved to.
    /// </summary>
    [Fact]
    public async Task CommitAsync_ARecordWrittenUnderAStoredPolicy_IsComposedUnderThatPolicy()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        var member = Guid.CreateVersion7();
        GoverningSettingsPolicies? composedUnder = null;

        try
        {
            await CreateOrganizationAsync(services, organization, cancellationToken);
            await ProvisionStatingEnglishAsync(services, member, cancellationToken);
            await MoveAsync(services, member, organization, cancellationToken);
            await CommitPolicyAsync(services, organization, ForcingOffMcp, expectedVersion: 0, cancellationToken);

            // Act
            var committed = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IUserSettingsDocumentWriter>().CommitAsync(
                    UserId.Create(member),
                    expectedVersion: 3,
                    policies =>
                    {
                        composedUnder = policies;

                        return new UserRecordCandidate(
                            """{"Language":"Polish"}""",
                            new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true));
                    },
                    token),
                cancellationToken);

            // Assert
            Assert.Equal(4, committed);
            Assert.Contains("\"McpEndpoint\"", composedUnder?.Organization?.Json, StringComparison.Ordinal);
            Assert.Equal(1, composedUnder?.Organization?.Version);
            Assert.Equal(
                new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true),
                await ReadSwitchesAsync(services, member, cancellationToken));
        }
        finally
        {
            await RemoveAsync(services, organization, member);
        }
    }

    /// <summary>
    /// A candidate the next read would refuse is refused inside the commit that composed it and stores nothing: a
    /// document whose root is no object, and one past what a record binds from. Either left on the row would lock the
    /// user out, and the writer is the one place every composition passes through.
    /// </summary>
    [Theory]
    [InlineData("[]")]
    [InlineData(null)]
    public async Task CommitAsync_ACandidateTheNextReadWouldRefuse_IsRefusedAndLeavesTheRowAsItStood(string? composed)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var candidate = composed
            ?? $$"""{"Language":"English","Filler":"{{new string('f', UserSettingsDocument.MaximumOctets + 1024)}}"}""";

        try
        {
            await ProvisionStatingEnglishAsync(services, user, cancellationToken);

            // Act
            var refusal = await Record.ExceptionAsync(() => services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IUserSettingsDocumentWriter>().CommitAsync(
                    UserId.Create(user),
                    expectedVersion: 2,
                    _ => new UserRecordCandidate(candidate, new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: false)),
                    token),
                cancellationToken));

            // Assert
            Assert.IsType<ArgumentException>(refusal);
            Assert.Equal(2, await ReadVersionAsync(services, user, cancellationToken));
            Assert.Equal(UserEndpointAccess.Everywhere, await ReadSwitchesAsync(services, user, cancellationToken));
            var standing = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IUserSettingsDocumentReader>().ReadAsync(UserId.Create(user), token),
                cancellationToken);
            Assert.Contains("\"English\"", standing?.Json, StringComparison.Ordinal);
            Assert.DoesNotContain("Filler", standing?.Json, StringComparison.Ordinal);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
        }
    }

    /// <summary>
    /// A move changes which policy governs the user: into an organization whose policy forces a switch it rewrites
    /// the row and raises the record's version, and out of one whose default the record relied on it is refused.
    /// </summary>
    [Fact]
    public async Task SetUserOrganizationAsync_AMoveBetweenPolicies_ProjectsTheSwitchesAndRefusesARecordItWouldLeaveUnservable()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var forcing = Guid.CreateVersion7();
        var defaulting = Guid.CreateVersion7();
        var stating = Guid.CreateVersion7();
        var relying = Guid.CreateVersion7();

        try
        {
            await CreateOrganizationAsync(services, forcing, cancellationToken);
            await CreateOrganizationAsync(services, defaulting, cancellationToken);
            await CommitPolicyAsync(services, forcing, ForcingOffMcp, expectedVersion: 0, cancellationToken);
            await CommitPolicyAsync(services, defaulting, DefaultingPolish, expectedVersion: 0, cancellationToken);
            await ProvisionStatingEnglishAsync(services, stating, cancellationToken);
            await OrchestratedForeignUser.ProvisionAsync(services, relying, cancellationToken);
            await MoveAsync(services, relying, defaulting, cancellationToken);

            // Act
            var joined = await MoveAsync(services, stating, forcing, cancellationToken);
            var left = await MoveAsync(services, relying, organization: null, cancellationToken);

            // Assert
            Assert.Equal(OrganizationWriteOutcome.Written, joined.Outcome);
            Assert.Equal(
                new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true),
                await ReadSwitchesAsync(services, stating, cancellationToken));
            Assert.Equal(3, await ReadVersionAsync(services, stating, cancellationToken));
            Assert.Equal(OrganizationWriteOutcome.RecordLeftInvalid, left.Outcome);
            Assert.NotEmpty(left.Corrections);
            Assert.Equal(defaulting, await ReadOrganizationAsync(services, relying, cancellationToken));
        }
        finally
        {
            await RemoveAsync(services, forcing, stating);
            await RemoveAsync(services, defaulting, relying);
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

    /// <summary>Provisions a user whose record states the language a record must name, so the record is servable under no policy at all.</summary>
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

    private static Task<OrganizationWriteResult> MoveAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        Guid? organization,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>()
                .SetUserOrganizationAsync(UserId.Create(user), organization, token),
            cancellationToken);

    private static Task<SettingsPolicyCommit> CommitPolicyAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        string json,
        long expectedVersion,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<ISettingsPolicyStore>()
                .CommitAsync(organization, json, expectedVersion, new GovernedRecordsToJudge(Users: true, MailAccounts: true, MaximumJudged), token),
            cancellationToken);

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

    private static Task<Guid?> ReadOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .UserAccounts
                .AsNoTracking()
                .Where(record => record.Id == user)
                .Select(record => record.OrganizationId)
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
