// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Transmission;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Outbox;

public sealed class MailOutboxPassTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));
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

    /// <summary>
    /// What the pass leaves behind is measured after it has settled its batch, which is what makes the level an
    /// operator alerts on the backlog rather than a restatement of what the pass had just claimed.
    /// </summary>
    [Fact]
    public async Task RunAsync_MoreIsQueuedThanTheBatchTakes_ReportsWhatStandsAtEachUnfinishedStage()
    {
        // Arrange
        var context = new PassContext(maxDeliveriesPerPass: 1);
        context.Enqueue();
        context.Enqueue();

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.Equal(
            [OutgoingEmailStage.Recorded, OutgoingEmailStage.TransmissionBegun],
            report.OutstandingByStage.Select(stage => stage.Stage));

        // One is left waiting and the one the batch took has finished, so a level published from this says a message is
        // outstanding rather than that two are.
        Assert.Equal(1, report.OutstandingByStage.Single(stage => stage.Stage == OutgoingEmailStage.Recorded).Count);
        Assert.Equal(
            0,
            report.OutstandingByStage.Single(stage => stage.Stage == OutgoingEmailStage.TransmissionBegun).Count);
    }

    /// <summary>
    /// A drained account is measured as zero rather than as nothing, because the level it publishes has to bring a
    /// dashboard's backlog back down instead of leaving the last non-empty measurement standing.
    /// </summary>
    [Fact]
    public async Task RunAsync_TheAccountHasNothingLeft_ReportsZeroAtEveryUnfinishedStage()
    {
        // Arrange
        var context = new PassContext();
        context.Enqueue();

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.NotEmpty(report.OutstandingByStage);
        Assert.All(report.OutstandingByStage, stage => Assert.Equal(0, stage.Count));
    }

    /// <summary>
    /// The measurement is the last step and the only one nothing acts on, so a database that refused it costs the
    /// level rather than the outcomes this pass had already settled and durably written down.
    /// </summary>
    [Fact]
    public async Task RunAsync_TheStoreWillNotCountWhatIsOutstanding_StillReportsWhatThePassSettled()
    {
        // Arrange
        var context = new PassContext();
        var queued = context.Enqueue();
        context.Store.RefusesOutstandingCount = true;

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.Equal([queued], report.Results.Select(result => result.OutgoingEmailId));
        Assert.Equal(1, report.SentCount);
        Assert.Empty(report.OutstandingByStage);
        Assert.Equal(OutgoingEmailStage.Sent, context.Store.Read(queued).Stage);
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

    /// <summary>
    /// A host that begins stopping while a delivered send's copies are being settled records nothing against the send.
    /// Filing never reached a mail server, so a failure written here would leave a delivered message saying its copy
    /// could not be filed — a statement about somebody's mailbox that nothing observed.
    /// </summary>
    [Fact]
    public async Task RunAsync_TheHostStopsWhileASettledSendsCopiesAreFiled_RecordsNoFilingFailure()
    {
        // Arrange
        var context = new PassContext();
        var queued = context.Enqueue();
        using var stopping = new CancellationTokenSource();
        context.Transmit = (request, envelope, _) =>
        {
            foreach (var recipient in request.Recipients)
            {
                envelope.Record(new MailRecipientReply(recipient.Address, 250, MailRecipientAcceptance.Accepted));
            }

            stopping.Cancel();

            return Task.FromResult(new MailTransmission(MailTransmissionOutcome.Accepted, 250));
        };

        // Act
        var report = await context.RunAsync(stopping.Token);

        // Assert
        Assert.Equal(OutgoingEmailStage.Sent, context.Store.Read(queued).Stage);
        Assert.Empty(report.FilingResults);
        Assert.Null(context.Store.Read(queued).LastFilingFailure);
    }

    /// <summary>
    /// A filing that failed before any place was chosen names none. The read filing begins with is what fails here, and
    /// it happens before the withdrawal and the append are even decided between — so reporting the failure against the
    /// sent copy would put a word into a log line and a metric dimension that nothing about this failure established.
    /// </summary>
    [Fact]
    public async Task RunAsync_FilingFailsBeforeAPlaceIsChosen_ReportsTheFailureAgainstNoPlace()
    {
        // Arrange
        var context = new PassContext();
        var queued = context.Enqueue();
        context.Transmit = (request, envelope, _) =>
        {
            foreach (var recipient in request.Recipients)
            {
                envelope.Record(new MailRecipientReply(recipient.Address, 250, MailRecipientAcceptance.Accepted));
            }

            context.Store.ReadFailure = () => new InvalidOperationException("The record could not be read.");

            return Task.FromResult(new MailTransmission(MailTransmissionOutcome.Accepted, 250));
        };

        // Act
        var report = await context.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        context.Store.ReadFailure = null;
        Assert.Equal(OutgoingEmailStage.Sent, context.Store.Read(queued).Stage);

        var filing = Assert.Single(report.FilingResults);
        Assert.Equal(OutgoingMailFilingOutcome.Failed, filing.Outcome);
        Assert.False(filing.Filing.IsSpecified);
        Assert.Equal(OutgoingMailFilingResult.UndeterminedFilingName, filing.FilingName);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailFilingFailedUnexpectedly, filing.Failure);
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

    /// <summary>
    /// The drafts are reached before anything is claimed, so a draft whose folder was unreachable when it was written
    /// is in front of its owner by the time this pass has delivered anything.
    /// </summary>
    [Fact]
    public async Task RunAsync_DraftNothingCouldAppendYet_AppendsItBeforeTheBatchIsClaimed()
    {
        // Arrange
        var context = new PassContext();
        var draft = await context.SaveDraftAsync();
        Assert.Equal(0, context.DraftSide.AppendCount);
        context.DraftSide.MapDraftsFolder(Account.Id);
        context.Enqueue();

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Filed, Assert.Single(report.DraftResults).Outcome);
        Assert.Equal(1, context.DraftSide.AppendCount);
        Assert.Equal(MailDraftStage.Filed, context.DraftSide.Drafts.Peek(draft.Id)!.Stage);
    }

    /// <summary>
    /// A draft is written over IMAP and owes nothing to SMTP, so an account that reads mail without sending it keeps
    /// drafts like any other — and the sweep that resumes what a crash left is the only thing that ever reaches them.
    /// </summary>
    [Fact]
    public async Task RunAsync_AccountHasNoSubmissionEndpoint_StillResumesItsOutstandingDrafts()
    {
        // Arrange
        var context = new PassContext(submits: false);
        var draft = await context.SaveDraftAsync();
        Assert.Equal(0, context.DraftSide.AppendCount);
        context.DraftSide.MapDraftsFolder(Account.Id);

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Filed, Assert.Single(report.DraftResults).Outcome);
        Assert.Equal(MailDraftStage.Filed, context.DraftSide.Drafts.Peek(draft.Id)!.Stage);
        Assert.Empty(report.Results);
        Assert.Empty(report.OutstandingByStage);
    }

    /// <summary>
    /// A delivered send takes the draft it was promoted from out of the drafts folder, in the pass that filed the sent
    /// copy — which is what leaves the message in one of the owner's folders rather than in two.
    /// </summary>
    [Fact]
    public async Task RunAsync_PromotedDraftWhoseSendIsDelivered_WithdrawsTheDraftCopy()
    {
        // Arrange
        var context = new PassContext();
        context.DraftSide.MapDraftsFolder(Account.Id);
        var draft = await context.SaveDraftAsync();
        var appended = Assert.Single(context.DraftSide.Drafts.Peek(draft.Id)!.Copies);
        await context.PromoteDraftAsync(draft.Id, context.Enqueue());

        // Act
        var report = await context.RunAsync();

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Discarded, Assert.Single(report.DraftResults).Outcome);
        Assert.Equal(
            [(appended.Placement.UidValidity!.Value, appended.Placement.Uid!.Value)],
            context.DraftSide.Withdrawn);
        Assert.Null(context.DraftSide.Drafts.Peek(draft.Id));
    }

    /// <summary>Assembles one pass over an in-memory outbox, with the exchange the test writes.</summary>
    private sealed class PassContext
    {
        private readonly FakeTimeProvider clock = new(RanAt);
        private readonly MailOutboxDelivery delivery;
        private readonly IMailTransportSecurityPolicyReader policyReader;
        private readonly MailOutboxSettings settings;

        internal PassContext(bool submits = true, int maxDeliveriesPerPass = 10)
        {
            this.Store = new InMemoryOutgoingEmailStore(timeProvider: this.clock);
            this.Session = new ScriptedMailDeliverySession(
                (request, envelope, token) => this.Transmit(request, envelope, token));

            var contentStore = ContentStores.Substituted();
            contentStore
                .FindOutgoingContentAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>())
                .Returns(new StoredEmailContent(RawMime, RawMime.Length, SHA256.HashData(RawMime.Span)));

            var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
            Assert.True(EmailAddress.TryCreate(displayName: null, "me@example.test", out var sender));
            senderIdentities.FindSenderIdentity(Account.Id).Returns(OutgoingSenderIdentity.Create(Account.Id, sender));

            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                var persistenceSession = Substitute.For<IPersistenceSession>();
                persistenceSession.CommitAsync(Arg.Any<CancellationToken>())
                    .Returns(PersistenceCommitResult.Committed);

                return persistenceSession;
            });

            this.settings = MailOutboxSettings.Create(
                maxDeliveriesPerPass,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(7),
                maxAttempts: 5,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(8));

            this.policyReader = Substitute.For<IMailTransportSecurityPolicyReader>();
            this.policyReader.GetDeliveryPolicy(Account.Id).Returns(submits ? TransportSecurityPolicy() : null);

            this.Filing = new OutgoingMailFilingHarness(this.Store, contentStore, this.settings, this.clock);
            this.DraftSide = new MailDraftHarness(this.clock, this.Store, this.settings);

            this.delivery = new MailOutboxDelivery(
                this.Session,
                this.Store,
                contentStore,
                senderIdentities,
                new OptimisticConcurrencyRetryPolicy(
                    sessionFactory,
                    new PersistenceConcurrencyOptions(),
                    this.clock),
                this.settings,
                this.clock);
        }

        internal InMemoryOutgoingEmailStore Store { get; }

        /// <summary>Gets the filing side of the pass, which files nothing until a test asks it to.</summary>
        internal OutgoingMailFilingHarness Filing { get; }

        /// <summary>Gets the draft side of the pass, which holds nothing until a test writes a draft.</summary>
        internal MailDraftHarness DraftSide { get; }

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

        /// <summary>Runs one pass, assembled where the worker assembles one: from the work units this run will use.</summary>
        /// <remarks>
        /// Built per run rather than once, because the draft side rebuilds its own work unit whenever a test states a
        /// folder mapping, and a pass holding the unit from before that would settle drafts against a mailbox the test
        /// no longer describes.
        /// </remarks>
        internal Task<MailOutboxPassReport> RunAsync(CancellationToken? stoppingToken = null) =>
            new MailOutboxPass(
                    this.Store,
                    this.delivery,
                    this.Filing.Pass,
                    this.DraftSide.Pass,
                    this.policyReader,
                    this.settings)
                .RunAsync(Account, stoppingToken ?? TestContext.Current.CancellationToken);

        /// <summary>Writes one draft of this account, which the pass is then expected to reach on its own.</summary>
        internal Task<MailDraftRecord> SaveDraftAsync()
        {
            Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var recipient));

            return this.DraftSide.Book.SaveAsync(
                Account,
                OutgoingEmailRequester.Command($"mfctl-{Guid.CreateVersion7()}"),
                new ComposedMailDraft(
                    [
                        new MailDraftRecipient(
                            OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To),
                            AuthoredRecipientProvenance.NamedByCaller),
                    ],
                    InternetMessageId.Mint("example.test"),
                    RawMime),
                revises: null,
                TestContext.Current.CancellationToken);
        }

        /// <summary>Records one draft as promoted to one queued send, which is what a promotion leaves behind it.</summary>
        internal Task PromoteDraftAsync(MailDraftId draftId, OutgoingEmailId promotedTo) =>
            this.DraftSide.Drafts.RecordPromotedAsync(
                Substitute.For<IPersistenceSession>(),
                draftId,
                promotedTo,
                TestContext.Current.CancellationToken);

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
