// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that a user and a mail account are placed for an administrative scope from the rows a deployment holds.</summary>
/// <remarks>
/// The placement is two statements a substitute cannot settle: an outer join that has to answer an account assigned to
/// nobody with its organization rather than with nothing, and a bound of two rows that has to tell one assignee from
/// several. Every row it writes carries identifiers of its own and is removed in a <c>finally</c>.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedAdministrativeTargetsTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PlaceMailAccountAsync_AnAccountInAnOrganization_IsCoveredByItsOrganizationAndByItsOneAssigneeUntilASecondIsAssigned()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var holder = Guid.CreateVersion7();
        var colleague = Guid.CreateVersion7();
        var organization = Guid.CreateVersion7();
        var account = Guid.CreateVersion7();
        await OrchestratedForeignUser.ProvisionAsync(services, holder, cancellationToken);
        await OrchestratedForeignUser.ProvisionAsync(services, colleague, cancellationToken);

        try
        {
            await CreateOrganizationAsync(services, organization, cancellationToken);
            await RecordAccountAsync(services, account, organization, cancellationToken);
            var unassigned = await PlaceAccountAsync(services, account, cancellationToken);
            await AssignAsync(services, account, holder, cancellationToken);
            var heldAlone = await PlaceAccountAsync(services, account, cancellationToken);
            await AssignAsync(services, account, colleague, cancellationToken);

            // Act
            var shared = await PlaceAccountAsync(services, account, cancellationToken);

            // Assert
            Assert.Equal(organization, unassigned.Organization);
            Assert.Null(unassigned.SoleUser);
            Assert.Equal(UserId.Create(holder), heldAlone.SoleUser);
            Assert.True(heldAlone.IsCoveredBy(AssignmentScope.Organization(organization)));
            Assert.Equal(organization, shared.Organization);
            Assert.Null(shared.SoleUser);
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

    [Fact]
    public async Task PlaceUserAsync_AUserMovedIntoAnOrganization_IsPlacedInItAndAnUnknownUserIsUnplaced()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var organization = Guid.CreateVersion7();
        await OrchestratedForeignUser.ProvisionAsync(services, user, cancellationToken);

        try
        {
            await CreateOrganizationAsync(services, organization, cancellationToken);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().SetUserOrganizationAsync(UserId.Create(user), organization, token),
                cancellationToken);

            // Act
            var placed = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IAdministrativeTargets>().PlaceUserAsync(UserId.Create(user), token),
                cancellationToken);
            var unknown = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IAdministrativeTargets>().PlaceUserAsync(UserId.Create(Guid.CreateVersion7()), token),
                cancellationToken);

            // Assert
            Assert.Equal(organization, placed.Organization);
            Assert.Equal(UserId.Create(user), placed.SoleUser);
            Assert.Same(AdministrativeTarget.Unplaced, unknown);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                CancellationToken.None);
        }
    }

    private static Task<OrganizationWriteResult> CreateOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                organization,
                $"Organization {organization:N}",
                OrganizationShortName.Create($"{organization:N}"),
                RecordedAt,
                token),
            cancellationToken);

    private static Task<PersistenceCommitResult> RecordAccountAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        Guid organization,
        CancellationToken cancellationToken) => services.CommitAsync(
            async (_, session, token) =>
            {
                var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                context.MailAccountRecords.Add(new MailAccountRecordEntity
                {
                    Id = account,
                    DisplayName = $"account-{account:N}",
                    OrganizationId = organization,
                    Document = "{}",
                    Version = 1,
                    CreatedAt = RecordedAt,
                    UpdatedAt = RecordedAt,
                });

                await context.SaveChangesAsync(token);
            },
            cancellationToken);

    private static Task<PersistenceCommitResult> AssignAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        Guid user,
        CancellationToken cancellationToken) => services.CommitAsync(
            async (_, session, token) =>
            {
                var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                context.MailAccountAssignments.Add(new MailAccountAssignmentEntity
                {
                    MailAccountId = account,
                    UserId = user,
                    AssignedAt = RecordedAt,
                });

                await context.SaveChangesAsync(token);
            },
            cancellationToken);

    private static Task<AdministrativeTarget> PlaceAccountAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IAdministrativeTargets>().PlaceMailAccountAsync(
                MailAccountId.Create(account.ToString("D")),
                token),
            cancellationToken);

    private static Task<int> RemoveAccountAsync(OrchestratedMailFathomServices services, Guid account) =>
        services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();

                await context.MailAccountAssignments.Where(assignment => assignment.MailAccountId == account).ExecuteDeleteAsync(token);

                return await context.MailAccountRecords.Where(record => record.Id == account).ExecuteDeleteAsync(token);
            },
            CancellationToken.None);
}
