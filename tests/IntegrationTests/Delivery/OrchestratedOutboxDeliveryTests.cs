// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.AppHost;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Delivery;

/// <summary>Proves a queued send reaches a real mailbox through a real SMTP server, exactly once.</summary>
/// <remarks>
/// <para>
/// This is the one claim no substitute settles. A scripted client reports the outcome the test told it to report, so
/// what a message written into the outbox actually does — whether it leaves, whether it arrives, and whether asking
/// twice produces one copy or two — is established here, against the server the suite runs and the database the record
/// lives in. The mailbox is read back over a connection the code under test knows nothing about, which is what makes
/// the arrival an observation rather than the outbox agreeing with itself.
/// </para>
/// <para>
/// What the orchestrated server cannot settle is a refusal. GreenMail accepts every recipient it is offered and creates
/// the mailbox behind it, so neither a message-level permanent rejection nor a per-recipient one can be provoked from
/// it; both are proven in the unit suite against scripted replies, where every reply class can be stated. The same
/// division is already why the size and eight-bit capabilities are asserted there rather than here.
/// </para>
/// <para>
/// Every claim in this class is made about the record the test itself queued. The collection shares one account, so a
/// pass legitimately claims and delivers whatever another class left outstanding, and an assertion over a total would
/// be an assertion about the run's ordering.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOutboxDeliveryTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The one mailbox the orchestrated server has, which is both the sender and the addressee here.</summary>
    private const string Mailbox = OrchestrationContract.MailServerAccountEmailAddress;

    private static readonly MailAccountId Account = SyntheticMailAccount.AccountId;

    /// <summary>A queued send leaves, arrives, and leaves the record saying so with the reply the server gave.</summary>
    [Fact]
    public async Task RunAsync_AQueuedSend_DeliversItToTheServerAndRecordsItSent()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var subject = "outbox-delivery-arrives";
        var queued = await EnqueueAsync(services, subject, subject, cancellationToken);

        // Act
        var report = await RunPassAsync(services, cancellationToken);

        // Assert
        var result = Assert.Single(report.Results, entry => entry.OutgoingEmailId == queued.Id);
        Assert.Equal(MailOutboxDeliveryOutcome.Sent, result.Outcome);
        Assert.Null(result.Failure);

        var record = await FindAsync(services, queued.Id, cancellationToken);
        Assert.Equal(OutgoingEmailStage.Sent, record.Stage);
        Assert.Empty(record.OutstandingRecipients);
        Assert.Equal(OutgoingRecipientStatus.Accepted, Assert.Single(record.Recipients).Status);
        Assert.Equal(1, record.AttemptCount);

        // The independent witness: the message is in the mailbox, once, read over a connection nothing under test owns.
        Assert.Single(await this.ReadDeliveredAsync(subject, cancellationToken));
    }

    /// <summary>The same authored send asked for twice is one record and therefore one message in the mailbox.</summary>
    [Fact]
    public async Task RunAsync_TheSameAuthoredSendQueuedTwice_DeliversOneMessage()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var subject = "outbox-delivery-delivers-once";
        var first = await EnqueueAsync(services, subject, subject, cancellationToken);
        var retried = await EnqueueAsync(services, subject, $"{subject}-recomposed", cancellationToken);

        // Act
        var report = await RunPassAsync(services, cancellationToken);

        // Assert
        Assert.Equal(first.Id, retried.Id);
        Assert.Equal(
            MailOutboxDeliveryOutcome.Sent,
            Assert.Single(report.Results, entry => entry.OutgoingEmailId == first.Id).Outcome);

        // One copy, and it is the message the first request stored: a recomposed body would arrive as a second message
        // in every recipient's client rather than as the same one.
        var delivered = Assert.Single(await this.ReadDeliveredAsync(subject, cancellationToken));
        Assert.Equal(subject, delivered.Subject);
    }

    /// <summary>
    /// A send a stopped process left mid-transmission is never handed out again. The pass stamps it with the reason
    /// instead, so an operator reads why it is stuck rather than finding a second copy in somebody's mailbox.
    /// </summary>
    [Fact]
    public async Task RunAsync_ASendLeftMidTransmission_MarksItAndTransmitsNothingFurther()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var subject = "outbox-delivery-stranded";
        var queued = await EnqueueAsync(services, subject, subject, cancellationToken);

        // The lease is stated as already spent, which is how a process that died mid-transmission leaves the row: the
        // holder is gone, and nothing but the elapsed lease says so.
        var claimed = await services.InScopeAsync(
            async (scope, token) =>
            {
                var claim = await scope.GetRequiredService<IOutgoingEmailStore>().ClaimAsync(
                    OutgoingEmailClaimRequest.Create(Account, batchSize: 100, TimeSpan.FromTicks(1)),
                    token);

                return claim.Single(entry => entry.Record.Id == queued.Id);
            },
            cancellationToken);

        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>()
                .RecordTransmissionBegunAsync(session, claimed.Lease, queued.Id, token),
            cancellationToken);

        // Act
        var report = await RunPassAsync(services, cancellationToken);

        // Assert
        Assert.True(report.MarkedUnknownCount >= 1);
        Assert.DoesNotContain(report.Results, entry => entry.OutgoingEmailId == queued.Id);

        var record = await FindAsync(services, queued.Id, cancellationToken);
        Assert.Equal(OutgoingEmailStage.TransmissionBegun, record.Stage);
        Assert.True(record.HasUnknownOutcome);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailOutcomeUnknown, record.LastFailure);

        // Nothing was transmitted for it, so the mailbox holds no copy of a message whose outcome nobody can establish.
        Assert.Empty(await this.ReadDeliveredAsync(subject, cancellationToken));
    }

    /// <summary>
    /// A record a claim is holding is invisible to the next claim, which is what stops two passes attempting one send.
    /// Only PostgreSQL settles this: the exclusion is a lease predicate evaluated inside <c>FOR UPDATE SKIP LOCKED</c>.
    /// </summary>
    [Fact]
    public async Task ClaimAsync_ARecordAlreadyHeld_IsNotHandedToTheNextClaim()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var queued = await EnqueueAsync(
            services,
            "outbox-delivery-one-holder",
            "outbox-delivery-one-holder",
            cancellationToken);

        // Act
        var claims = await services.InTwoScopesAsync(
            async (firstScope, secondScope, token) =>
            {
                var first = await firstScope.GetRequiredService<IOutgoingEmailStore>().ClaimAsync(
                    OutgoingEmailClaimRequest.Create(Account, batchSize: 100, TimeSpan.FromMinutes(10)),
                    token);
                var second = await secondScope.GetRequiredService<IOutgoingEmailStore>().ClaimAsync(
                    OutgoingEmailClaimRequest.Create(Account, batchSize: 100, TimeSpan.FromMinutes(10)),
                    token);

                return (First: first, Second: second);
            },
            cancellationToken);

        // Assert
        var held = Assert.Single(claims.First, entry => entry.Record.Id == queued.Id);
        Assert.Equal(1, held.Record.AttemptCount);
        Assert.DoesNotContain(claims.Second, entry => entry.Record.Id == queued.Id);
    }

    private static async Task<OutgoingEmailRecord> EnqueueAsync(
        OrchestratedMailFathomServices services,
        string invocationIdentity,
        string subject,
        CancellationToken cancellationToken)
    {
        var opened = await services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutbox>().EnqueueAsync(
                RequestFor(invocationIdentity),
                MimeOf(subject),
                token),
            [MailFathomPermission.MailSend],
            cancellationToken);

        return opened.Record;
    }

    private static Task<MailOutboxPassReport> RunPassAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutboxPass>().RunAsync(Account, token),
            cancellationToken);

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

    /// <summary>Reads the mailbox the message was addressed to and narrows it to the subject the test composed.</summary>
    private async Task<IReadOnlyList<ObservedEmail>> ReadDeliveredAsync(
        string subject,
        CancellationToken cancellationToken)
    {
        var delivered = await new OrchestratedMailbox(orchestration.MailServer)
            .ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken);

        return [.. delivered.Where(message => message.Subject == subject)];
    }

    /// <summary>Addresses the send to the one mailbox the orchestrated server has, so a delivery is observable.</summary>
    private static OutgoingEmailRequest RequestFor(string invocationIdentity)
    {
        Assert.True(
            EmailAddress.TryCreate(displayName: null, Mailbox, out var recipient));

        return OutgoingEmailRequest.Create(
            Account,
            OutgoingEmailRequester.Command(invocationIdentity),
            [OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To)]);
    }

    /// <summary>Builds a synthetic outgoing message whose subject is how a test recognizes its own delivery.</summary>
    private static ReadOnlyMemory<byte> MimeOf(string subject) => Encoding.ASCII.GetBytes(
        $"Message-ID: <{subject}@mailfathom.test>\r\n"
        + $"From: {Mailbox}\r\n"
        + $"To: {Mailbox}\r\n"
        + $"Subject: {subject}\r\n"
        + "MIME-Version: 1.0\r\n"
        + "Content-Type: text/plain; charset=us-ascii\r\n\r\n"
        + "Synthetic body.\r\n")
        .AsMemory();
}
