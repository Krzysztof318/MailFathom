// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Idempotency;

/// <summary>Proves one scope holds one settings policy, and that of several writers composing over one version one commits.</summary>
/// <remarks>
/// <para>
/// Both halves are decided by PostgreSQL and by nothing a substitute could stand in for. A first policy is an insert
/// that does nothing on a conflict, so what makes it one row is the unique index — and for the deployment's row, which
/// names no organization, that the index treats nulls as not distinct. Every later policy is an update matching the
/// version it was composed over, so what refuses the second writer is the row lock the first one holds while it
/// commits.
/// </para>
/// <para>
/// This class shares a database with every other in the collection. An organization's policy is written for an
/// organization the test created and removes, which takes the policy with it; the deployment's row is the one row
/// here that is nobody's in particular, so the test that writes it removes it before and after, and no other class
/// reads it.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedSettingsPolicyWriteTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How many writers save one policy at once: enough that some reliably lose, and few enough to cost seconds.</summary>
    private const int ConcurrentWriters = 6;

    private const string ForcingPolling = """{"MailAccounts":{"Forced":{"Mode":"Polling"}}}""";

    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The deployment's first policy saved by several writers at once is one row at version one, which is the claim
    /// the index's treatment of nulls exists for: an index holding each null distinct would admit every one of them.
    /// </summary>
    [Fact]
    public async Task CommitAsync_ManyWritersSavingTheDeploymentsFirstPolicyAtOnce_StoresOnePolicy()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await RemoveDeploymentPolicyAsync(services);

        try
        {
            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                $"{nameof(ISettingsPolicyStore)}.{nameof(ISettingsPolicyStore.CommitAsync)}",
                ConcurrentWriters,
                (_, attemptToken) => CommitAsync(services, organizationId: null, ForcingPolling, expectedVersion: 0, attemptToken),
                cancellationToken);

            // Assert
            attempts.AssertSingleEffect(await CountPoliciesAsync(services, organizationId: null, cancellationToken));
            Assert.Empty(attempts.Failures);
            Assert.Equal(
                [.. Enumerable.Repeat<long?>(null, ConcurrentWriters - 1), 1L],
                attempts.Results.Order());
            Assert.Equal(1, (await ReadAsync(services, organizationId: null, cancellationToken))?.Version);
        }
        finally
        {
            await RemoveDeploymentPolicyAsync(services);
        }
    }

    /// <summary>
    /// An organization's policy is read as stating nothing until one is saved, is moved by one of several writers who
    /// all read the same version, and goes with the organization when it is removed — after which the organization
    /// is one this deployment does not hold, and its policy is not a row left stating defaults for nobody.
    /// </summary>
    [Fact]
    public async Task CommitAsync_ManyWritersComposingOverOneVersionOfAnOrganizationsPolicy_CommitsOneAndThePolicyLeavesWithTheOrganization()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();

        try
        {
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

            var unwritten = await ReadAsync(services, organization, cancellationToken);
            var first = await CommitAsync(services, organization, ForcingPolling, expectedVersion: 0, cancellationToken);

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                $"{nameof(ISettingsPolicyStore)}.{nameof(ISettingsPolicyStore.CommitAsync)}",
                ConcurrentWriters,
                (ordinal, attemptToken) => CommitAsync(
                    services,
                    organization,
                    $$$$"""{"MailAccounts":{"Defaults":{"Port":{{{{993 + ordinal}}}}}}}""",
                    expectedVersion: 1,
                    attemptToken),
                cancellationToken);
            var moved = await ReadAsync(services, organization, cancellationToken);
            var held = await CountPoliciesAsync(services, organization, cancellationToken);

            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                cancellationToken);

            // Assert
            Assert.Equal(SettingsPolicyDocument.Unwritten(organization), unwritten);
            Assert.Equal(1, first);
            Assert.Empty(attempts.Failures);
            Assert.Equal(
                [.. Enumerable.Repeat<long?>(null, ConcurrentWriters - 1), 2L],
                attempts.Results.Order());
            Assert.Equal(2, moved?.Version);
            Assert.Equal(1, held);
            Assert.Null(await ReadAsync(services, organization, cancellationToken));
            Assert.Equal(0, await CountPoliciesAsync(services, organization, cancellationToken));
            Assert.Null(await CommitAsync(services, organization, ForcingPolling, expectedVersion: 0, cancellationToken));
        }
        finally
        {
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                CancellationToken.None);
        }
    }

    private static Task<long?> CommitAsync(
        OrchestratedMailFathomServices services,
        Guid? organizationId,
        string json,
        long expectedVersion,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<ISettingsPolicyStore>()
                .CommitAsync(organizationId, json, expectedVersion, token),
            cancellationToken);

    private static Task<SettingsPolicyDocument?> ReadAsync(
        OrchestratedMailFathomServices services,
        Guid? organizationId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<ISettingsPolicyStore>().ReadAsync(organizationId, token),
            cancellationToken);

    private static Task<int> CountPoliciesAsync(
        OrchestratedMailFathomServices services,
        Guid? organizationId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .SettingsPolicies
                .AsNoTracking()
                .CountAsync(policy => policy.OrganizationId == organizationId, token),
            cancellationToken);

    /// <summary>Removes the deployment's own policy, which is the one row this class writes that no organization's removal takes.</summary>
    /// <remarks>Uncancellable because it also runs in a <c>finally</c>, where the test's own token may already be cancelled.</remarks>
    private static Task<int> RemoveDeploymentPolicyAsync(OrchestratedMailFathomServices services) =>
        services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .SettingsPolicies
                .Where(policy => policy.OrganizationId == null)
                .ExecuteDeleteAsync(token),
            CancellationToken.None);
}
