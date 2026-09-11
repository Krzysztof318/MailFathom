// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Hosting.Workers;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that two replicas sharing one database walk one re-derivation scope once, and that it moves when the walk ends.</summary>
/// <remarks>
/// Each replica is a service graph of its own over the orchestrated database, and each asks for the scope through the
/// runner a segment walks under, by the name the segment gives it. No segment is run here, because what decides whether
/// a second walk starts is that lease being granted, and a walk would also need stored mail this suite shares with
/// every other test.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedRederivationHoldTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>A lease long enough that nothing in the test expires underneath it, so only the walk ending can move the scope.</summary>
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task TryRunUnderLeaseAsync_TwoReplicasAskingForOneScope_WalkItOnceAndMoveItWhenTheWalkEnds()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var firstReplica = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var secondReplica = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scope = StoredMailRederivationHandler.LeaseScopeOf(new StoredMailScope(
            MailAccountIdentity.Create(firstReplica.ServedUser, MailAccountId.Create("rederivation-handover")),
            Folder: null));
        var secondWalkedAlongside = true;

        // Act
        var firstWalked = await RunUnderLeaseAsync(
            firstReplica,
            scope,
            async token => secondWalkedAlongside = await RunUnderLeaseAsync(secondReplica, scope, _ => Task.CompletedTask, token),
            cancellationToken);
        var secondWalkedAfter = await RunUnderLeaseAsync(secondReplica, scope, _ => Task.CompletedTask, cancellationToken);

        // Assert
        Assert.Equal((true, false, true), (firstWalked, secondWalkedAlongside, secondWalkedAfter));
    }

    private static Task<bool> RunUnderLeaseAsync(
        OrchestratedMailFathomServices replica,
        WorkScope scope,
        Func<CancellationToken, Task> walk,
        CancellationToken cancellationToken) =>
        replica.InScopeAsync(
            (serviceScope, token) => new WorkLeaseRunner(
                serviceScope.GetRequiredService<IServiceScopeFactory>(),
                NullLoggerFactory.Instance,
                TimeProvider.System,
                LeaseDuration,
                LeaseRenewalInterval).TryRunUnderLeaseAsync(scope, walk, token),
            cancellationToken);
}
