// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Hosting.Workers;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that two replicas sharing one database supervise one account once, and that it moves when the holder stops.</summary>
/// <remarks>
/// Each replica is a service graph of its own over the orchestrated database, and each asks for the account through
/// the hold the synchronization coordinator takes before it starts a supervisor, under the scope the coordinator names
/// it by. A running coordinator is not composed here, because what decides whether a second supervisor starts is that
/// hold being granted, and a supervisor would also need a mailbox this suite shares with every other test.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedAccountSupervisionHoldTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>A lease long enough that nothing in the test expires underneath it, so only a release can move the account.</summary>
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task TryTakeAsync_TwoReplicasConfiguredWithOneAccount_SuperviseItOnceAndMoveItWhenTheHolderStops()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var firstReplica = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var secondReplica = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scope = MailAccountSupervisionScope.For(
            MailAccountIdentity.Create(firstReplica.ServedUser, MailAccountId.Create("replica-handover")));

        // Act
        using var firstHold = await TakeAsync(firstReplica, scope, cancellationToken);
        using var refusedHold = await TakeAsync(secondReplica, scope, cancellationToken);
        await firstHold!.ReleaseAsync();
        using var movedHold = await TakeAsync(secondReplica, scope, cancellationToken);

        // Assert
        Assert.Null(refusedHold);
        Assert.NotNull(movedHold);
        await movedHold.ReleaseAsync();
    }

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
