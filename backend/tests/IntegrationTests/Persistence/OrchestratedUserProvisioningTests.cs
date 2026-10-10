// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Organizations;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that a user recorded into an organization enters it in the statement that records them.</summary>
/// <remarks>
/// The organization is selected inside the insert rather than written as a value, so an organization the deployment
/// does not hold leaves no row rather than raising its foreign key's violation, and the reads afterwards tell that
/// apart from a label somebody else carries. Both halves are a raw statement only PostgreSQL can answer. Every user and
/// organization arranged here is removed in a <c>finally</c>, because a second user row left behind breaks every later
/// start in this collection rather than the test that wrote it.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedUserProvisioningTests(MailFathomOrchestrationFixture orchestration)
{
    [Fact]
    public async Task ProvisionAsync_AnOrganizationTheDeploymentHolds_RecordsTheUserInsideIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var organization = Guid.CreateVersion7();

        try
        {
            var created = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                    organization,
                    $"Organization {organization:N}",
                    OrganizationShortName.Create($"{organization:N}"),
                    DateTimeOffset.UnixEpoch,
                    token),
                cancellationToken);

            Assert.Equal(OrganizationWriteOutcome.Written, created.Outcome);

            // Act
            var outcome = await ProvisionAsync(services, user, organization, cancellationToken);

            // Assert
            Assert.Equal(UserProvisioningResult.Provisioned, outcome);
            Assert.Equal(organization, await ReadOrganizationOfAsync(services, user, cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProvisionAsync_AnOrganizationTheDeploymentDoesNotHold_RecordsNobody()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();

        try
        {
            // Act
            var outcome = await ProvisionAsync(services, user, Guid.CreateVersion7(), cancellationToken);

            // Assert
            Assert.Equal(UserProvisioningResult.UnknownOrganization, outcome);
            Assert.False(await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                    .UserAccounts
                    .AsNoTracking()
                    .AnyAsync(record => record.Id == user, token),
                cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
        }
    }

    private static Task<UserProvisioningResult> ProvisionAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        Guid organization,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IUserProvisioning>().ProvisionAsync(
                UserId.Create(user),
                $"provisioned-{user:N}",
                organization,
                token),
            cancellationToken);

    private static Task<Guid?> ReadOrganizationOfAsync(
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
}
