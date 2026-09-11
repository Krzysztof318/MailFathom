// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Domain.Access;
using MailFathom.Host.Hosting.Workers;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that two replicas sharing one database give the same answer about an account only one of them holds.</summary>
/// <remarks>
/// The lease table is the only thing either replica can read about the other, so this is the claim no unit test settles:
/// a substitute can be made to return whichever lease a test wants, while what a deployment actually depends on is one
/// replica's claim being visible to the other through PostgreSQL. Each replica here is a service graph of its own with
/// an identity of its own, which is what two hosts against one database are.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedSynchronizationStatusTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>A lease long enough that nothing in the test expires underneath it, so only a release can move the account.</summary>
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ReadAsync_OneReplicaHoldingTheAccount_AnswersAlikeFromEitherReplica()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var holdingReplica = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var otherReplica = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scope = MailAccountSupervisionScope.For(SyntheticMailAccount.Account);

        // Act
        using var hold = await TakeAsync(holdingReplica, scope, cancellationToken);
        var fromHolder = await ReadAccountAsync(holdingReplica, cancellationToken);
        var fromOther = await ReadAccountAsync(otherReplica, cancellationToken);
        await hold!.ReleaseAsync();

        // Assert
        Assert.Equal(holdingReplica.Replica, fromHolder.Supervision?.Replica);
        Assert.Equal(holdingReplica.Replica, fromOther.Supervision?.Replica);
        Assert.Equal(MailAccountRunPhase.NotStarted, fromHolder.Run.Phase);
        Assert.Equal(MailAccountRunPhase.SupervisedElsewhere, fromOther.Run.Phase);
    }

    /// <summary>An account no replica holds is reported as held by none, rather than as this replica's business.</summary>
    [Fact]
    public async Task ReadAsync_NoReplicaHoldingTheAccount_ReportsNoSupervisionAndTheReplicaThatAnswered()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var replica = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var status = await replica.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailSynchronizationStatusReader>().ReadAsync(token),
            [MailFathomPermission.AdminRead],
            cancellationToken);

        // Assert
        Assert.Equal(replica.Replica, status.Replica);
        Assert.All(status.Accounts, account => Assert.Null(account.Supervision));
    }

    private static Task<MailAccountSynchronizationStatus> ReadAccountAsync(
        OrchestratedMailFathomServices replica,
        CancellationToken cancellationToken) =>
        replica.AsCallerInScopeAsync(
            async (scope, token) =>
            {
                var status = await scope.GetRequiredService<MailSynchronizationStatusReader>().ReadAsync(token);

                return status.Accounts.Single(account => account.AccountId == SyntheticMailAccount.AccountId);
            },
            [MailFathomPermission.AdminRead],
            cancellationToken);

    private static Task<WorkLeaseHold?> TakeAsync(
        OrchestratedMailFathomServices replica,
        WorkScope scope,
        CancellationToken cancellationToken) =>
        replica.InScopeAsync(
            (serviceScope, token) => WorkLeaseHold.TryTakeAsync(
                scope,
                LeaseDuration,
                LeaseRenewalInterval,
                serviceScope.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<WorkLeaseHold>.Instance,
                TimeProvider.System,
                token),
            cancellationToken);
}
