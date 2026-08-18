// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Transmission;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Transport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Outbox;

public sealed class MailOutboxPassTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");
    private static readonly DateTimeOffset RanAt = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

    private static readonly ReadOnlyMemory<byte> RawMime =
        Encoding.ASCII.GetBytes("Message-ID: <one@example.test>\r\n\r\nHello.").AsMemory();

    /// <summary>An account that submits nowhere claims nothing, so a read-only account never takes work it cannot attempt.</summary>
    [Fact]
    public async Task RunAsync_AccountHasNoSubmissionEndpoint_ClaimsNothing()
    {
        // Arrange
        var context = new PassContext(submits: false);
        var queued = context.Enqueue();

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.Empty(report.Results);
        Assert.False(report.BatchFilled);
        Assert.Equal(0, context.Store.Read(queued).AttemptCount);
    }

    /// <summary>Every send the batch claims is attempted, and the report names each of them rather than a total.</summary>
    [Fact]
    public async Task RunAsync_SeveralSendsAreDue_SettlesEachOfThem()
    {
        // Arrange
        var context = new PassContext();
        var first = context.Enqueue();
        var second = context.Enqueue();

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.Equal([first, second], report.Results.Select(result => result.OutgoingEmailId));
        Assert.Equal(2, report.SentCount);
        Assert.False(report.AccountDeferred);
        Assert.Equal(OutgoingEmailStage.Sent, context.Store.Read(first).Stage);
        Assert.Equal(OutgoingEmailStage.Sent, context.Store.Read(second).Stage);
    }

    /// <summary>What the batch leaves is said to be waiting, so the loop comes back for it instead of sleeping on it.</summary>
    [Fact]
    public async Task RunAsync_MoreIsQueuedThanTheBatchTakes_ReportsTheBatchAsFilled()
    {
        // Arrange
        var context = new PassContext(maxDeliveriesPerPass: 1);
        context.Enqueue();
        var behind = context.Enqueue();

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.Single(report.Results);
        Assert.True(report.BatchFilled);
        Assert.Equal(OutgoingEmailStage.Recorded, context.Store.Read(behind).Stage);
    }

    /// <summary>A send whose outcome the store would not take ends alone, and the send behind it in the batch is still reached.</summary>
    [Fact]
    public async Task RunAsync_TheStoreWillNotRecordOneSendsOutcome_StillReachesTheSendBehindIt()
    {
        // Arrange
        var context = new PassContext();
        var unrecordable = context.Enqueue();
        var behind = context.Enqueue();
        context.Store.RefusesWrites = outgoingEmailId => outgoingEmailId == unrecordable;

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.Equal([unrecordable, behind], report.Results.Select(result => result.OutgoingEmailId));
        Assert.Equal(MailOutboxDeliveryOutcome.NotRecorded, report.Results[0].Outcome);
        Assert.Equal(1, report.NotRecordedCount);
        Assert.Equal(1, report.SentCount);
        Assert.Equal(OutgoingEmailStage.Sent, context.Store.Read(behind).Stage);
    }

    /// <summary>A send a stopped process left mid-transmission is stamped with the reason before anything is claimed.</summary>
    [Fact]
    public async Task RunAsync_ARecordWasLeftMidTransmission_MarksItWithTheUnknownOutcome()
    {
        // Arrange
        var context = new PassContext();
        var stranded = await context.StrandMidTransmissionAsync();

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.Equal(1, report.MarkedUnknownCount);
        var record = context.Store.Read(stranded);
        Assert.Equal(OutgoingEmailStage.TransmissionBegun, record.Stage);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailOutcomeUnknown, record.LastFailure);
        Assert.Empty(report.Results);
    }

    /// <summary>
    /// The reason a stranded record carries is replaced rather than left. A failure an earlier attempt recorded
    /// describes a send that came back and was attempted again, so reading it on a record that may have reached
    /// somebody would say "deferred" about the one case nobody can decide.
    /// </summary>
    [Fact]
    public async Task RunAsync_AStrandedRecordCarriesAnEarlierFailure_ReplacesItWithTheUnknownOutcome()
    {
        // Arrange
        var context = new PassContext();
        var stranded = await context.StrandMidTransmissionAsync(MailFathomErrorCode.MailDeliveryUnavailable);

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.Equal(1, report.MarkedUnknownCount);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailOutcomeUnknown, context.Store.Read(stranded).LastFailure);
    }

    /// <summary>Marking is idempotent, so a record an operator leaves standing is not recounted by every later pass.</summary>
    [Fact]
    public async Task RunAsync_AStrandedRecordAlreadyMarked_IsNotMarkedAgain()
    {
        // Arrange
        var context = new PassContext();
        await context.StrandMidTransmissionAsync();
        await context.RunAsync();

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.Equal(0, report.MarkedUnknownCount);
    }

    /// <summary>A provider that will not serve the account defers its sends, which is what the report says about the account.</summary>
    [Fact]
    public async Task RunAsync_ServerRefusesForNow_ReportsTheAccountAsDeferred()
    {
        // Arrange
        var context = new PassContext();
        context.Enqueue();
        context.Transmit = (_, _, _) =>
            Task.FromResult(new MailTransmission(MailTransmissionOutcome.RefusedTemporarily, 451));

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.Equal(1, report.DeferredCount);
        Assert.True(report.AccountDeferred);
    }

    /// <summary>Assembles one pass over an in-memory outbox, with the exchange the test writes.</summary>
    private sealed class PassContext
    {
        private readonly FakeTimeProvider clock = new(RanAt);
        private readonly MailOutboxPass pass;

        internal PassContext(bool submits = true, int maxDeliveriesPerPass = 10)
        {
            this.Store = new InMemoryOutgoingEmailStore(timeProvider: this.clock);
            this.Session = new ScriptedMailDeliverySession(
                (request, envelope, token) => this.Transmit(request, envelope, token));

            var contentStore = Substitute.For<IEmailContentStore>();
            contentStore
                .FindOutgoingContentAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>())
                .Returns(new StoredEmailContent(RawMime, RawMime.Length, SHA256.HashData(RawMime.Span)));

            var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
            Assert.True(EmailAddress.TryCreate(displayName: null, "me@example.test", out var sender));
            senderIdentities.FindSenderIdentity(Account).Returns(OutgoingSenderIdentity.Create(Account, sender));

            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                var persistenceSession = Substitute.For<IPersistenceSession>();
                persistenceSession.CommitAsync(Arg.Any<CancellationToken>())
                    .Returns(PersistenceCommitResult.Committed);

                return persistenceSession;
            });

            var settings = MailOutboxSettings.Create(
                maxDeliveriesPerPass,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(7),
                maxAttempts: 5,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromHours(1));

            var policyReader = Substitute.For<IMailTransportSecurityPolicyReader>();
            policyReader.GetDeliveryPolicy(Account).Returns(submits ? TransportSecurityPolicy() : null);

            this.pass = new MailOutboxPass(
                this.Store,
                new MailOutboxDelivery(
                    this.Session,
                    this.Store,
                    contentStore,
                    senderIdentities,
                    new OptimisticConcurrencyRetryPolicy(
                        sessionFactory,
                        new PersistenceConcurrencyOptions(),
                        this.clock),
                    settings,
                    this.clock),
                policyReader,
                settings);
        }

        internal InMemoryOutgoingEmailStore Store { get; }

        internal ScriptedMailDeliverySession Session { get; }

        /// <summary>Gets or sets the exchange with the submission server, which defaults to a server that takes everything.</summary>
        internal Func<MailTransmissionRequest, MailEnvelopeLedger, CancellationToken, Task<MailTransmission>> Transmit
        {
            get;
            set;
        } = (request, envelope, _) =>
        {
            foreach (var recipient in request.Recipients)
            {
                envelope.Record(new MailRecipientReply(recipient.Address, 250, MailRecipientAcceptance.Accepted));
            }

            return Task.FromResult(new MailTransmission(MailTransmissionOutcome.Accepted, 250));
        };

        internal OutgoingEmailId Enqueue()
        {
            Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var recipient));

            var request = OutgoingEmailRequest.Create(
                Account,
                OutgoingEmailRequester.Command($"mfctl-{Guid.CreateVersion7()}"),
                [OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To)]);

            var queued = this.Store.Publish(request, RawMime.Length).Id;

            // One second between two sends, so "oldest first" is decided by the instant each was recorded rather than
            // by how two identifiers minted in the same tick happen to compare.
            this.clock.Advance(TimeSpan.FromSeconds(1));

            return queued;
        }

        /// <summary>Leaves a record exactly where a process that stopped mid-transmission leaves one: begun, unanswered, and unheld.</summary>
        /// <param name="earlierFailure">What an attempt before this one had already recorded against the record, if any.</param>
        internal async Task<OutgoingEmailId> StrandMidTransmissionAsync(MailFathomErrorCode? earlierFailure = null)
        {
            var stranded = this.Enqueue();

            var claimed = await this.Store.ClaimAsync(
                OutgoingEmailClaimRequest.Create(Account, batchSize: 10, TimeSpan.FromMinutes(10)),
                TestContext.Current.CancellationToken);

            var lease = claimed.Single().Lease;
            var session = Substitute.For<IPersistenceSession>();

            if (earlierFailure is { } failure)
            {
                await this.Store.RecordFailureAsync(
                    session,
                    lease,
                    stranded,
                    failure,
                    TestContext.Current.CancellationToken);
            }

            await this.Store.RecordTransmissionBegunAsync(
                session,
                lease,
                stranded,
                TestContext.Current.CancellationToken);

            this.clock.Advance(TimeSpan.FromMinutes(30));

            return stranded;
        }

        internal Task<MailOutboxPassReport> RunAsync() =>
            this.pass.RunAsync(Account, TestContext.Current.CancellationToken);

        private static MailTransportSecurityPolicy TransportSecurityPolicy() => MailTransportSecurityPolicy.Create(
            MailConnectionSecurity.StartTlsRequired,
            MailAuthenticationPolicy.Create(
                [MailAuthenticationMechanism.ScramSha256],
                allowInsecureConnection: false,
                allowClearTextAuthenticationOverUnencryptedConnection: false),
            MailServerCertificateTrust.SystemTrustStore,
            trustedCertificateAuthorityReference: null);
    }
}
