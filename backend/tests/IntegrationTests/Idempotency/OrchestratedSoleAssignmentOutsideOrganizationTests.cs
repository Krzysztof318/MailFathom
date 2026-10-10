// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Idempotency;

/// <summary>Proves a mail account in no organization ends up assigned to one user, whoever else asks at the same moment.</summary>
/// <remarks>
/// <para>
/// The rule is a count read under a row lock, so it holds or fails in PostgreSQL and nowhere else: two assignments
/// that each read the account as unassigned and then both insert is the defect, and neither a substitute nor a
/// sequential repeat can produce it. The move out of every organization is the other write that could leave such an
/// account shared, and it is here for the same reason — its refusal is a count taken under the lock an assignment
/// waits on.
/// </para>
/// <para>
/// This class shares a database with every other in the collection, so every row it writes names a user, an account,
/// or an organization it created for the test alone, and all of them are removed in a <c>finally</c>.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedSoleAssignmentOutsideOrganizationTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How many users are assigned one account at once: enough that some reliably lose, and few enough to cost seconds.</summary>
    private const int ConcurrentAssignments = 6;

    /// <summary>The version <see cref="OrchestratedForeignUser.ProvisionAsync" /> writes a user record at.</summary>
    private const long ProvisionedUserVersion = 1;

    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// One account in no organization assigned to several users at once is assigned to one of them, and every other
    /// attempt is answered that a mailbox is shared only inside an organization.
    /// </summary>
    [Fact]
    public async Task AssignAsync_ManyUsersAssignedOneAccountInNoOrganizationAtOnce_AssignsItToOneOfThem()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = Guid.CreateVersion7();
        Guid[] users = [.. Enumerable.Range(0, ConcurrentAssignments).Select(_ => Guid.CreateVersion7())];

        try
        {
            foreach (var user in users)
            {
                Assert.Equal(
                    PersistenceCommitResult.Committed,
                    await OrchestratedForeignUser.ProvisionAsync(services, user, cancellationToken));
            }

            await SeedAsync(services, [Record(account, organizationId: null)], [], cancellationToken);

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                $"{nameof(IMailAccountRecordStore)}.{nameof(IMailAccountRecordStore.AssignAsync)}",
                ConcurrentAssignments,
                (ordinal, attemptToken) => services.InScopeAsync(
                    (scope, token) => scope.GetRequiredService<IMailAccountRecordStore>()
                        .AssignAsync(account, UserId.Create(users[ordinal]), ProvisionedUserVersion, token),
                    attemptToken),
                cancellationToken);

            // Assert
            attempts.AssertSingleEffect((await ReadAssignedUsersAsync(services, account, cancellationToken)).Length);
            Assert.Empty(attempts.Failures);
            Assert.Equal(
                [
                    MailAccountWriteResult.Committed,
                    .. Enumerable.Repeat(MailAccountWriteResult.SharedOnlyInOrganization, ConcurrentAssignments - 1),
                ],
                attempts.Results.Select(write => write.Result).Order());
        }
        finally
        {
            await RemoveAccountAsync(services, account);

            foreach (var user in users)
            {
                await OrchestratedForeignUser.EraseAsync(services, user);
            }
        }
    }

    /// <summary>
    /// A shared account asked to leave every organization stays where it is with both of its users, and leaves once
    /// it is one person's — which is the control showing the refusal was the rule's and not the move's.
    /// </summary>
    [Fact]
    public async Task SetMailAccountOrganizationAsync_ASharedAccountLeavingEveryOrganization_IsRefusedUntilItIsOnePersons()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var holder = Guid.CreateVersion7();
        var colleague = Guid.CreateVersion7();
        var organization = Guid.CreateVersion7();
        var account = Guid.CreateVersion7();

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, holder, cancellationToken));
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, colleague, cancellationToken));
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

            // Both users are in no organization, so nothing but the account being shared stands in the move's way.
            await SeedAsync(
                services,
                [Record(account, organization)],
                [Assignment(account, holder), Assignment(account, colleague)],
                cancellationToken);

            // Act
            var refused = await MoveOutOfEveryOrganizationAsync(services, account, cancellationToken);
            var stillShared = await ReadAssignedUsersAsync(services, account, cancellationToken);
            var stillIn = await ReadOrganizationAsync(services, account, cancellationToken);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                    .MailAccountAssignments
                    .Where(assignment => assignment.MailAccountId == account && assignment.UserId == colleague)
                    .ExecuteDeleteAsync(token),
                cancellationToken);
            var moved = await MoveOutOfEveryOrganizationAsync(services, account, cancellationToken);

            // Assert
            Assert.Equal(OrganizationWriteOutcome.SharedOnlyInOrganization, refused.Outcome);
            Assert.Equal(2, refused.StandingAssignments);
            Assert.Equal(new[] { holder, colleague }.Order(), stillShared.Order());
            Assert.Equal(organization, stillIn);
            Assert.Equal(OrganizationWriteOutcome.Written, moved.Outcome);
            Assert.Null(await ReadOrganizationAsync(services, account, cancellationToken));
        }
        finally
        {
            await RemoveAccountAsync(services, account);
            await OrchestratedForeignUser.EraseAsync(services, holder);
            await OrchestratedForeignUser.EraseAsync(services, colleague);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                CancellationToken.None);
        }
    }

    private static Task<OrganizationWriteResult> MoveOutOfEveryOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>()
                .SetMailAccountOrganizationAsync(account, organizationId: null, token),
            cancellationToken);

    private static Task<Guid[]> ReadAssignedUsersAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .MailAccountAssignments
                .AsNoTracking()
                .Where(assignment => assignment.MailAccountId == account)
                .Select(assignment => assignment.UserId)
                .ToArrayAsync(token),
            cancellationToken);

    private static Task<Guid?> ReadOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .MailAccountRecords
                .AsNoTracking()
                .Where(record => record.Id == account)
                .Select(record => record.OrganizationId)
                .SingleAsync(token),
            cancellationToken);

    private static async Task SeedAsync(
        OrchestratedMailFathomServices services,
        MailAccountRecordEntity[] accounts,
        MailAccountAssignmentEntity[] assignments,
        CancellationToken cancellationToken) => Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (_, session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                    context.MailAccountRecords.AddRange(accounts);
                    context.MailAccountAssignments.AddRange(assignments);

                    await context.SaveChangesAsync(token);
                },
                cancellationToken));

    /// <summary>Removes an account this class wrote, and every assignment to it with it.</summary>
    /// <remarks>Uncancellable for the reason <see cref="OrchestratedForeignUser.EraseAsync" /> is: it runs in a <c>finally</c>.</remarks>
    private static Task<int> RemoveAccountAsync(OrchestratedMailFathomServices services, Guid account) =>
        services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .MailAccountRecords
                .Where(record => record.Id == account)
                .ExecuteDeleteAsync(token),
            CancellationToken.None);

    private static MailAccountRecordEntity Record(Guid id, Guid? organizationId) => new()
    {
        Id = id,
        DisplayName = $"account-{id:N}",
        Document = "{}",
        OrganizationId = organizationId,
        Version = 1,
        CreatedAt = RecordedAt,
        UpdatedAt = RecordedAt,
    };

    private static MailAccountAssignmentEntity Assignment(Guid account, Guid user) => new()
    {
        UserId = user,
        MailAccountId = account,
        AssignedAt = RecordedAt,
    };
}
