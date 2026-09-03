// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>Proves what withdrawing a batch of recorded changes does to the rows, against real PostgreSQL.</summary>
/// <remarks>
/// <para>
/// One test, because there is one claim no substitute can establish: the withdrawal is a single query over a set of
/// identities — <c>identifiers.Contains(...)</c> joined to the folder, tracked, with the stage guard applied in memory
/// to the rows it loaded — and what a substitute proves about it is that the fake was written to agree with the
/// production code. Whether that expression translates at all, whether the join carries the binding a record is rebuilt
/// from, and whether the guard leaves a record already past <em>recorded</em> alone are three things only the real
/// provider and the real schema answer.
/// </para>
/// <para>
/// The batched read is asserted in the same arrangement rather than in a test of its own, because it is the other half
/// of one round trip a caller makes: a client withdraws what it named and then reads back where those records stand,
/// and both queries are new here. What the unit suite already covers, and what buys nothing against a database, is
/// which records a caller may reach and which grant each route is published under.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailboxMutationWithdrawalTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly MailboxMutationRequester Requester =
        MailboxMutationRequester.Command("withdraw-batch");

    /// <summary>
    /// The whole point of the stage guard, stated as the two records a batch mixes. One has issued nothing and is the
    /// caller's to take back; the other has already reached the server and must survive being named in the same call,
    /// because withdrawing it would leave MailFathom believing it never asked for something the mailbox has done.
    /// </summary>
    [Fact]
    public async Task WithdrawAsync_ABatchMixingARecordedChangeWithOneAlreadyIssued_CancelsOnlyTheRecordedOne()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "mutation-withdrawal", cancellationToken);

        var pending = await OpenAsync(services, binding, uid: 7101U, isSeen: true, cancellationToken);
        var alsoPending = await OpenAsync(services, binding, uid: 7102U, isSeen: false, cancellationToken);
        var issued = await OpenAsync(services, binding, uid: 7103U, isSeen: true, cancellationToken);

        await services.CommitProducingAsync(
            async (scope, session, token) =>
            {
                await scope.GetRequiredService<IMailboxMutationRecordStore>()
                    .RecordPlacementIssuedAsync(session, issued, requiresSourceRemoval: false, token);

                return issued;
            },
            cancellationToken);

        // A record nobody opened, named in the same call, so the query's answer is proved to be the rows it found
        // rather than one entry per identity the caller supplied.
        var unknown = MailboxMutationRecordId.Create(Guid.CreateVersion7());

        // Act
        var withdrawn = await services.CommitProducingAsync(
            (scope, session, token) => scope.GetRequiredService<IMailboxMutationRecordStore>()
                .WithdrawAsync(
                    session,
                    SyntheticMailAccount.Owner,
                    [pending, alsoPending, issued, unknown],
                    token),
            cancellationToken);

        // Assert
        Assert.Equal(3, withdrawn.Count);
        Assert.Equal(
            [MailboxMutationStage.Cancelled, MailboxMutationStage.Cancelled, MailboxMutationStage.PlacementIssued],
            [.. new[] { pending, alsoPending, issued }.Select(StageOf)]);

        // Read back through the batched reader, because a stage written inside a commit and a stage a later reader
        // answers with are two different claims, and it is the second one a client acts on.
        var readBack = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxMutationRecordStore>()
                .ReadAsync(SyntheticMailAccount.Owner, [pending, alsoPending, issued, unknown], token),
            cancellationToken);

        Assert.Equal(3, readBack.Count);
        Assert.All(readBack, record => Assert.Equal(binding.Alias, record.Request.Occurrence.FolderResolutionId.Alias));
        Assert.Equal(2, readBack.Count(record => record.Stage is MailboxMutationStage.Cancelled));
        Assert.Single(readBack, record => record.Stage is MailboxMutationStage.PlacementIssued);

        MailboxMutationStage StageOf(MailboxMutationRecordId recordId) =>
            withdrawn.Single(record => record.Id == recordId).Stage;
    }

    /// <summary>Stores one message and opens a flag change against it, which is the state a withdrawal acts on.</summary>
    private static async Task<MailboxMutationRecordId> OpenAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        uint uid,
        bool isSeen,
        CancellationToken cancellationToken)
    {
        var occurrence = SyntheticEmail.OccurrenceIn(binding, uid);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            occurrence,
            $"withdraw-{uid}",
            cancellationToken);

        // The requester carries the UID, so each record is its own idempotency identity rather than a repeat of the
        // one before it — which the database would answer by handing back the first record instead of opening a second.
        var request = MailboxMutationRequest.SetSeen(
            storedEmailId,
            SyntheticMailAccount.Owner,
            occurrence,
            MailboxMutationRequester.Command($"{Requester.Identity}-{uid}"),
            isSeen);

        return await services.CommitProducingAsync(
            async (scope, session, token) => (await scope.GetRequiredService<IMailboxMutationRecordStore>()
                .OpenAsync(session, request, token)).Id,
            cancellationToken);
    }
}
