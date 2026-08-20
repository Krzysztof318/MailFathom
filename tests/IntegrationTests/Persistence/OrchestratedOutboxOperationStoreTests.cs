// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves what an operator's two decisions about one send actually do to the row, and what refuses them.</summary>
/// <remarks>
/// <para>
/// Both decisions are a single conditional <c>UPDATE</c> whose condition is the whole of the exclusion, so what they
/// refuse is decided by PostgreSQL evaluating a stage and a lease rather than by anything a substitute could stand in
/// for. That is why every claim here is against the real database: a statement that matched no row and a statement that
/// matched the wrong one are indistinguishable from a fake, and the difference between them is a message withdrawn and
/// a message sent twice.
/// </para>
/// <para>
/// The reading side is here for a second reason. A cursor walk is a keyset comparison over two columns of which one is
/// a <c>uuid</c>, and how PostgreSQL orders those is exactly what a unit test cannot ask.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOutboxOperationStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly MailAccountId Account = SyntheticMailAccount.AccountId;

    /// <summary>
    /// The ordinary withdrawal, and the two refusals that follow it. A send already withdrawn cannot be withdrawn
    /// again, and a decision naming a record this deployment never held says so rather than reporting a stage.
    /// </summary>
    [Fact]
    public async Task CancelAsync_ASendNothingHasClaimed_WithdrawsItAndRefusesEveryDecisionAfterwards()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var enqueued = await EnqueueAsync(services, "outbox-operations-cancel", cancellationToken);

        // Act
        var withdrawn = await CancelAsync(services, enqueued.Id, cancellationToken);
        var again = await CancelAsync(services, enqueued.Id, cancellationToken);
        var unheld = await CancelAsync(services, OutgoingEmailId.Create(Guid.CreateVersion7()), cancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.Accepted, withdrawn);
        Assert.Equal(OutboxDecisionOutcome.StageDoesNotAllowIt, again);
        Assert.Equal(OutboxDecisionOutcome.RecordUnknown, unheld);

        var record = await FindAsync(services, enqueued.Id, cancellationToken);
        Assert.Equal(OutgoingEmailStage.Cancelled, record.Stage);
    }

    /// <summary>
    /// The race the condition exists for. A delivery pass holds the record, the operator's withdrawal reaches the same
    /// row, and the statement matches nothing — so the message keeps going rather than being withdrawn underneath the
    /// attempt that is transmitting it.
    /// </summary>
    [Fact]
    public async Task CancelAsync_ASendADeliveryAttemptHolds_LeavesItAloneAndReportsTheAttempt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var enqueued = await EnqueueAsync(services, "outbox-operations-claimed", cancellationToken);

        var claimed = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().ClaimAsync(
                OutgoingEmailClaimRequest.Create(Account, batchSize: 100, TimeSpan.FromMinutes(10)),
                token),
            cancellationToken);
        Assert.Contains(claimed, entry => entry.Record.Id == enqueued.Id);

        // Act
        var refused = await CancelAsync(services, enqueued.Id, cancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.AttemptUnderWay, refused);

        var record = await FindAsync(services, enqueued.Id, cancellationToken);
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
    }

    /// <summary>
    /// A send nothing will attempt again, offered another chance. The refusal has to be restated before the statement
    /// matches it, and once it does the record is back where a pass finds it, with the attempts it already spent
    /// cleared rather than counted against the chance it was just given.
    /// </summary>
    [Fact]
    public async Task RequeueAsync_APermanentlyRefusedSend_IsOfferedAgainOnlyOnceTheRefusalIsRestated()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var enqueued = await EnqueueAsync(services, "outbox-operations-requeue", cancellationToken);

        var claimed = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().ClaimAsync(
                OutgoingEmailClaimRequest.Create(Account, batchSize: 100, TimeSpan.FromMinutes(10)),
                token),
            cancellationToken);
        var lease = Assert.Single(claimed, entry => entry.Record.Id == enqueued.Id).Lease;

        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>().AdvanceAsync(
                session,
                lease,
                enqueued.Id,
                OutgoingEmailStage.Refused,
                replyCode: 550,
                token),
            cancellationToken);

        // Act
        var withoutRestatement = await RequeueAsync(
            services,
            enqueued.Id,
            refusalRestated: false,
            cancellationToken);
        var restated = await RequeueAsync(services, enqueued.Id, refusalRestated: true, cancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.RefusalNotRestated, withoutRestatement);
        Assert.Equal(OutboxDecisionOutcome.Accepted, restated);

        var record = await FindAsync(services, enqueued.Id, cancellationToken);
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
        Assert.Equal(0, record.AttemptCount);

        // Left where the rest of the collection expects an account's outbox to be, rather than queued for a delivery
        // pass another class in this collection would then have to account for.
        Assert.Equal(OutboxDecisionOutcome.Accepted, await CancelAsync(services, enqueued.Id, cancellationToken));
    }

    /// <summary>
    /// A walk of the whole account, one short page at a time. What it establishes is that the keyset boundary neither
    /// repeats a send nor steps over one — including where two were written down in the same instant, which is decided
    /// by how PostgreSQL compares two <c>uuid</c> values rather than by how the CLR would.
    /// </summary>
    [Fact]
    public async Task ReadPageAsync_WalkedWithTheCursorItIssues_ServesEverySendOfTheAccountExactlyOnce()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var first = await EnqueueAsync(services, "outbox-operations-page-first", cancellationToken);
        var second = await EnqueueAsync(services, "outbox-operations-page-second", cancellationToken);

        // Act
        var walked = new List<OutgoingEmailId>();
        OutboxCursor? cursor = null;

        do
        {
            var page = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOutboxOperationStore>().ReadPageAsync(
                    OutboxQuery.Create(Account, stage: null, pageSize: 2, cursor).Query!,
                    token),
                cancellationToken);

            walked.AddRange(page.Sends.Select(send => send.OutgoingEmailId));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        // Assert
        Assert.Equal(walked.Count, walked.Distinct().Count());
        Assert.Contains(first.Id, walked);
        Assert.Contains(second.Id, walked);

        // Every account in this collection is one account, so the counts are asserted as covering the walk rather than
        // as a total of their own: another class's queued send is a row this reading legitimately reports.
        var counted = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutboxOperationStore>()
                .CountByStageAsync(Account, token),
            cancellationToken);

        Assert.Equal(walked.Count, counted.Sum(stage => stage.Count));
    }

    /// <summary>Takes one decision against the outbox, in a scope of its own, as an administrative request does.</summary>
    private static Task<OutboxDecisionOutcome> CancelAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutboxOperationStore>()
                .CancelAsync(outgoingEmailId, token),
            cancellationToken);

    /// <summary>Offers one send again, in a scope of its own, as an administrative request does.</summary>
    private static Task<OutboxDecisionOutcome> RequeueAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailId outgoingEmailId,
        bool refusalRestated,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutboxOperationStore>()
                .RequeueAsync(outgoingEmailId, refusalRestated, token),
            cancellationToken);

    /// <summary>Reads one record back through a scope of its own, failing the test where nothing holds it.</summary>
    private static async Task<OutgoingEmailRecord> FindAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var record = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().FindAsync(outgoingEmailId, token),
            cancellationToken);

        Assert.NotNull(record);

        return record;
    }

    /// <summary>Writes one send down as the agent that asked for it, which is the principal the outbox admits a command from.</summary>
    private static async Task<OutgoingEmailRecord> EnqueueAsync(
        OrchestratedMailFathomServices services,
        string invocationIdentity,
        CancellationToken cancellationToken)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var recipient));

        var request = OutgoingEmailRequest.Create(
            Account,
            OutgoingEmailRequester.Command(invocationIdentity),
            [OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To, contact: null)]);

        var opened = await services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutbox>()
                .EnqueueAsync(request, MimeOf(invocationIdentity), token),
            [MailFathomPermission.MailSend],
            cancellationToken);

        return opened.Record;
    }

    /// <summary>Builds a synthetic outgoing message whose bytes differ per scenario.</summary>
    private static ReadOnlyMemory<byte> MimeOf(string discriminator) => Encoding.ASCII.GetBytes(
        $"Message-ID: <{discriminator}@example.test>\r\nSubject: {discriminator}\r\n\r\nSynthetic body.\r\n")
        .AsMemory();
}
