// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Idempotency;

/// <summary>Proves the last room under the stored-content ceiling is handed to one caller however many ask at once.</summary>
/// <remarks>
/// <para>
/// The invariant rather than the boundary, because what is at stake is not that a claim can be written but that the
/// room a ceiling admits cannot be handed out twice. The ceiling used to be a figure each process measured and a
/// reservation each process remembered, so concurrent runs — and, above one replica, concurrent processes — each
/// concluded they had the room the others were taking and the operator's disk filled past what was configured.
/// </para>
/// <para>
/// Nothing here can be established by calling the claim twice in sequence: the second caller would find what the first
/// committed, which was never in doubt. What decides it is a transaction-level advisory lock around one statement that
/// sweeps, measures, decides and inserts, and the only way to observe that lock working is to have several callers
/// reach it at the same moment and count what survived.
/// </para>
/// <para>
/// The attempts are split across two independently started service graphs, because two scopes of one process would
/// still prove nothing about a second process: the claim under test is that the reservation lives in the database and
/// not in whichever graph took it.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedStoredContentRoomIdempotencyTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>What one payload of this class claims, so the room below is stated in whole payloads.</summary>
    private const long PayloadByteCount = 4_096;

    /// <summary>Enough callers that one reliably loses the lock, and few enough that the claim costs seconds.</summary>
    private const int CompetingClaimants = 8;

    /// <summary>How long a claim of this class binds, generously enough that nothing here expires mid-test.</summary>
    private static readonly TimeSpan ClaimLifetime = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ClaimAsync_EveryCallerReachingTheLastRoomAtOnce_HandsItToExactlyOneOfThem()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var onOneHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var onAnotherHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = OrchestratedDeploymentUser.Shared.User;
        var roomForOne = await CeilingsWithRoomForOnePayloadAsync(onOneHost, cancellationToken);

        // Act
        var attempts = await ConcurrentIdempotency.RunAsync(
            "Claiming the last room under the deployment's stored-content ceiling",
            CompetingClaimants,
            (claimant, token) => ClaimAsync(
                claimant % 2 == 0 ? onOneHost : onAnotherHost,
                user,
                roomForOne,
                token),
            cancellationToken);

        // Assert
        var granted = attempts.Results.Where(record => record.IsGranted).ToArray();

        attempts.AssertSingleEffect(granted.Length);
        Assert.All(
            attempts.Results.Where(record => !record.IsGranted),
            record => Assert.Equal(StoredContentBound.Deployment, record.ReachedBound));

        foreach (var record in granted)
        {
            await ReleaseAsync(onOneHost, record.ClaimId!.Value, cancellationToken);
        }
    }

    /// <summary>States a ceiling admitting exactly one more payload over whatever storage already occupies.</summary>
    /// <remarks>
    /// The deployment figure is read through <see cref="IStoredEmailContentInventory" />, which answers the same
    /// catalogue quantity the claim statement measures — so the room stated here is the room the statement will find,
    /// whatever mail the rest of the suite left in the table. A literal would be a claim about what another class
    /// happened to store. The user's ceiling is left wide, because what these claimants contend for is the
    /// deployment's.
    /// </remarks>
    private static async Task<StoredContentCeilings> CeilingsWithRoomForOnePayloadAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var occupied = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailContentInventory>()
                .GetStoredContentBytesAsync(token),
            cancellationToken);

        return new StoredContentCeilings(DeploymentBytes: occupied + PayloadByteCount, UserBytes: long.MaxValue);
    }

    private static Task<StoredContentClaimRecord> ClaimAsync(
        OrchestratedMailFathomServices services,
        MailUserId user,
        StoredContentCeilings ceilings,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredContentClaimStore>()
                .ClaimAsync(user, PayloadByteCount, ceilings, ClaimLifetime, token),
            cancellationToken);

    private static Task<Guid> ReleaseAsync(
        OrchestratedMailFathomServices services,
        Guid claimId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IStoredContentClaimStore>().ReleaseAsync(claimId, token);

                return claimId;
            },
            cancellationToken);
}
