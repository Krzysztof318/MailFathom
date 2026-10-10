// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Paging;
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

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the mail-account listing returns exactly the accounts a narrower administrator's scopes cover.</summary>
/// <remarks>
/// <para>
/// The filter restates <c>AdministrativeTarget.MailAccount</c> as a query PostgreSQL runs — an account in an
/// organization, matched by that organization or by exactly one assignment naming a user in reach — so a translation
/// that matched a shared mailbox under one user's scope, or an account in no organization under any scope, would hand
/// a listing rows its reader may not see. A substitute translates nothing, which is why the claim is made here.
/// </para>
/// <para>
/// This class shares a database with every other in the collection, so each scope it reads names an organization or a
/// user it created for the test alone, and everything it wrote is removed in a <c>finally</c>.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailAccountListingScopeTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    private static readonly AdministrativeListingQuery FirstPage =
        AdministrativeListingQuery.Create(AdministrativeListingQuery.MaximumPageSize, after: null)!;

    [Fact]
    public async Task ReadPageAsync_AnOrganizationScopeAndAUserScope_ListExactlyTheAccountsEachCovers()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var owner = Guid.CreateVersion7();
        var colleague = Guid.CreateVersion7();
        var organization = Guid.CreateVersion7();
        var otherOrganization = Guid.CreateVersion7();
        var inOrganization = Guid.CreateVersion7();
        var inOtherOrganization = Guid.CreateVersion7();
        var heldAlone = Guid.CreateVersion7();
        var shared = Guid.CreateVersion7();
        var inNoOrganization = Guid.CreateVersion7();
        Guid[] accounts = [inOrganization, inOtherOrganization, heldAlone, shared, inNoOrganization];

        Assert.Equal(
            PersistenceCommitResult.Committed,
            await OrchestratedForeignUser.ProvisionAsync(services, owner, cancellationToken));
        Assert.Equal(
            PersistenceCommitResult.Committed,
            await OrchestratedForeignUser.ProvisionAsync(services, colleague, cancellationToken));

        try
        {
            await CreateOrganizationAsync(services, organization, cancellationToken);
            await CreateOrganizationAsync(services, otherOrganization, cancellationToken);
            var seeded = await services.CommitAsync(
                async (_, session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                    context.MailAccountRecords.AddRange(
                        Record(inOrganization, organization),
                        Record(inOtherOrganization, otherOrganization),
                        Record(heldAlone, organization),
                        Record(shared, organization),
                        Record(inNoOrganization, organizationId: null));
                    context.MailAccountAssignments.AddRange(
                        Assignment(heldAlone, owner),
                        Assignment(shared, owner),
                        Assignment(shared, colleague),
                        Assignment(inNoOrganization, owner));

                    await context.SaveChangesAsync(token);
                },
                cancellationToken);
            Assert.Equal(PersistenceCommitResult.Committed, seeded);

            // Act
            var byOrganization = await ListAsync(services, AssignmentScope.Organization(organization), cancellationToken);
            var byOwner = await ListAsync(services, AssignmentScope.User(UserId.Create(owner)), cancellationToken);

            // Assert
            Assert.Equal(new[] { inOrganization, heldAlone, shared }.Order(), byOrganization.Order());
            Assert.Equal([heldAlone], byOwner);
        }
        finally
        {
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                    .MailAccountRecords
                    .Where(account => accounts.Contains(account.Id))
                    .ExecuteDeleteAsync(token),
                CancellationToken.None);
            await OrchestratedForeignUser.EraseAsync(services, owner);
            await OrchestratedForeignUser.EraseAsync(services, colleague);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                CancellationToken.None);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(otherOrganization, token),
                CancellationToken.None);
        }
    }

    private static async Task<Guid[]> ListAsync(
        OrchestratedMailFathomServices services,
        AssignmentScope reach,
        CancellationToken cancellationToken)
    {
        var page = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailAccountRecordStore>()
                .ReadPageAsync(FirstPage, new HashSet<AssignmentScope> { reach }, token),
            cancellationToken);

        return [.. page.Entries.Select(account => account.Id)];
    }

    private static async Task CreateOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        CancellationToken cancellationToken) => Assert.Equal(
            OrganizationWriteOutcome.Written,
            (await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                    organization,
                    $"Organization {organization:N}",
                    OrganizationShortName.Create($"{organization:N}"),
                    RecordedAt,
                    token),
                cancellationToken)).Outcome);

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
