// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the stored-content ceiling is the deployment's: a claim one host holds is what another is refused against.</summary>
/// <remarks>
/// <para>
/// This is the claim the defect was about and the one nothing below a real database can settle. The ceiling used to be
/// a figure each process measured and a reservation each process remembered, so two replicas each concluded they had
/// the whole of it and the operator's disk filled to the ceiling multiplied by the replica count. What replaces it is
/// one statement per claim — a sweep, a measurement of what storage occupies plus what every unexpired claim reserves,
/// a decision against both ceilings, and an insert — serialized on a transaction-level advisory lock. Every part of
/// that is PostgreSQL's: the catalogue's own accounting of the content table, <c>now()</c> as the one clock the
/// expiries are read against, and the lock itself.
/// </para>
/// <para>
/// Two hosts rather than two scopes, because two scopes of one process would still prove nothing about a second
/// process: what is asserted here is that the reservation lives in the database and not in whichever graph took it.
/// </para>
/// <para>
/// What happens when several callers reach the last room at the same moment is a claim of its own and is stated where
/// this suite states those — <c>OrchestratedStoredContentRoomIdempotencyTests</c>, under
/// <c>backend/tests/IntegrationTests/Idempotency/</c>, raced through <c>ConcurrentIdempotency</c> rather than through
/// anything that could stagger the attempts.
/// </para>
/// <para>
/// The deployment ceiling is stated relative to what the content table already occupies, read through the same
/// catalogue figure the claim statement reads. A literal would be a claim about how much mail the rest of the suite
/// happened to leave behind, which is not this class's subject and changes whenever another class stores a message.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedStoredContentClaimTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>What one payload of this class claims, small enough that the room below is stated in whole payloads.</summary>
    private const long PayloadByteCount = 4_096;

    /// <summary>How long a claim of this class binds, generously enough that nothing here expires mid-test.</summary>
    private static readonly TimeSpan ClaimLifetime = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ClaimAsync_AClaimHeldByAnotherHost_RefusesThePayloadHereAndAdmitsItOnceItIsReleased()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var onOneHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var onAnotherHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = OrchestratedDeploymentUser.Shared.User;
        var ceilings = await CeilingsWithRoomForAsync(onOneHost, payloadCount: 1, cancellationToken);

        // Act
        var taken = await ClaimAsync(onOneHost, user, ceilings, cancellationToken);
        var refusedElsewhere = await ClaimAsync(onAnotherHost, user, ceilings, cancellationToken);

        await ReleaseAsync(onOneHost, taken.ClaimId!.Value, cancellationToken);

        var admittedElsewhere = await ClaimAsync(onAnotherHost, user, ceilings, cancellationToken);

        // Assert
        Assert.True(taken.IsGranted);
        Assert.Equal(StoredContentBound.Deployment, refusedElsewhere.ReachedBound);
        Assert.True(admittedElsewhere.IsGranted);

        await ReleaseAsync(onAnotherHost, admittedElsewhere.ClaimId!.Value, cancellationToken);
    }

    /// <summary>
    /// The two ceilings are counted in two quantities, so a claim under the deployment's is still refused by the
    /// user's — and the deployment's refusal is what a caller is told when both are reached.
    /// </summary>
    [Fact]
    public async Task ClaimAsync_TheUsersShareIsReachedWhileTheDeploymentHasRoom_RefusesAndNamesTheUser()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = OrchestratedDeploymentUser.Shared.User;
        var withRoom = await CeilingsWithRoomForAsync(services, payloadCount: 4, cancellationToken);
        var userIsFull = withRoom with { UserBytes = PayloadByteCount - 1 };

        // Act
        var record = await ClaimAsync(services, user, userIsFull, cancellationToken);

        // Assert
        Assert.False(record.IsGranted);
        Assert.Equal(StoredContentBound.User, record.ReachedBound);
    }

    /// <summary>A deployment that bounds neither population reaches no table at all, so nothing is reserved.</summary>
    [Fact]
    public async Task ClaimAsync_NeitherPopulationIsBounded_AnswersUnboundedWithoutWritingAClaim()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = OrchestratedDeploymentUser.Shared.User;

        // Act
        var record = await ClaimAsync(
            services,
            user,
            new StoredContentCeilings(DeploymentBytes: null, UserBytes: null),
            cancellationToken);

        // Assert
        Assert.Equal(StoredContentClaimRecord.Unbounded, record);
    }

    /// <summary>States ceilings that admit exactly the payloads asked for, over whatever storage already occupies.</summary>
    /// <remarks>
    /// The deployment figure is read through <see cref="IStoredEmailContentInventory" />, which answers the same
    /// catalogue quantity the claim statement measures — so the room stated here is the room the statement will find,
    /// whatever mail the rest of the suite left in the table. The user's ceiling is left wide, because the claims this
    /// class holds are what the deployment's is measured against.
    /// </remarks>
    private static async Task<StoredContentCeilings> CeilingsWithRoomForAsync(
        OrchestratedMailFathomServices services,
        int payloadCount,
        CancellationToken cancellationToken)
    {
        var occupied = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailContentInventory>()
                .GetStoredContentBytesAsync(token),
            cancellationToken);

        return new StoredContentCeilings(
            DeploymentBytes: occupied + (PayloadByteCount * payloadCount),
            UserBytes: long.MaxValue);
    }

    private static Task<StoredContentClaimRecord> ClaimAsync(
        OrchestratedMailFathomServices services,
        MailUserId user,
        StoredContentCeilings ceilings,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredContentClaimStore>()
                .ClaimAsync(user, PayloadByteCount, ceilings, ClaimLifetime, token),
            cancellationToken);

    /// <summary>Gives one claim's room back, through a scope of its own the way the ceiling's own disposal does.</summary>
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
