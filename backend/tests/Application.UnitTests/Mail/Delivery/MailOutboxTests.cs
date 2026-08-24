// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Scheduling;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery;

public sealed class MailOutboxTests
{
    /// <summary>The literal the screened deployment's detector reports, which stands in for a credential in a message.</summary>
    private const string ScreenedMarker = "AKIAEXAMPLEKEY";

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Authored = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DueAt = new(2026, 8, 19, 18, 0, 0, TimeSpan.Zero);

    private static readonly ReadOnlyMemory<byte> RawMime =
        Encoding.ASCII.GetBytes("Message-ID: <one@example.test>\r\n\r\nHello.").AsMemory();

    /// <summary>The record and the message it points at are one write, so a caller cannot leave half a send behind.</summary>
    [Fact]
    public async Task EnqueueAsync_NewRequest_RecordsTheSendAndItsMessageInOneSession()
    {
        // Arrange
        var contentStore = ContentStores.Substituted();
        var stagedSessions = new List<IPersistenceSession>();
        var outbox = CreateOutbox(new InMemoryOutgoingEmailStore(), contentStore, stagedSessions);
        var request = CreateRequest("mfctl-4f2a");

        // Act
        var record = (await outbox.EnqueueAsync(request, RawMime, CancellationToken.None)).Record;

        // Assert
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
        Assert.Equal(RawMime.Length, record.MimeByteLength);
        Assert.Equal(request.Recipients, record.OutstandingRecipients);
        await contentStore.Received(1).SaveOutgoingContentAsync(
            stagedSessions.Single(),
            record.Id,
            Arg.Is<PlacedEmailContent>(placed => placed!.RawMime.ToArray().SequenceEqual(RawMime.ToArray())),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The same authored request arriving twice is one record, which is what makes it one delivery.</summary>
    [Fact]
    public async Task EnqueueAsync_SameIdentityTwice_AnswersWithOneRecord()
    {
        // Arrange
        var contentStore = ContentStores.Substituted();
        var store = new InMemoryOutgoingEmailStore();
        var outbox = CreateOutbox(store, contentStore);
        var first = await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None);

        // Act
        var retried = await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None);

        // Assert
        Assert.Equal(first.Record.Id, retried.Record.Id);
        Assert.True(first.WasRecordedNow);
        Assert.False(retried.WasRecordedNow);
        Assert.Equal(2, store.OpenRequests.Count);
        var outstanding = await store.ReadOutstandingAsync(Account, limit: 10, CancellationToken.None);
        Assert.Single(outstanding);
    }

    /// <summary>A second send that was genuinely authored carries a key of its own and is a second record.</summary>
    [Fact]
    public async Task EnqueueAsync_SecondAuthoredRequest_IsASecondRecord()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var outbox = CreateOutbox(store, ContentStores.Substituted());
        var first = (await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None)).Record;

        // Act
        var second = (await outbox.EnqueueAsync(CreateRequest("mfctl-91bd"), RawMime, CancellationToken.None)).Record;

        // Assert
        Assert.NotEqual(first.Id, second.Id);
        var outstanding = await store.ReadOutstandingAsync(Account, limit: 10, CancellationToken.None);
        Assert.Equal(2, outstanding.Count);
    }

    /// <summary>A send with nothing to transmit is refused before anything is durable.</summary>
    [Fact]
    public async Task EnqueueAsync_NoMime_IsRefusedBeforeAnythingIsWritten()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var outbox = CreateOutbox(store, ContentStores.Substituted());

        // Act
        var thrown = await Assert.ThrowsAsync<ArgumentException>(
            () => outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), ReadOnlyMemory<byte>.Empty, CancellationToken.None));

        // Assert
        Assert.Equal("rawMime", thrown.ParamName);
        Assert.Empty(store.OpenRequests);
    }

    /// <summary>
    /// The losing side of a race for one identity retries in a fresh session and finds the winner's record, which is
    /// how two callers asking together deliver once.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_LosesTheRaceForOneIdentity_RetriesAndFindsTheWinnersRecord()
    {
        // Arrange
        var losing = Substitute.For<IPersistenceSession>();
        var retrying = Substitute.For<IPersistenceSession>();
        var store = new InMemoryOutgoingEmailStore(session => session != losing);
        var contentStore = ContentStores.Substituted();
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(losing, retrying);
        OutgoingEmailRecord? winnersRecord = null;

        // The other caller's row appears while this one holds an open session, which is the moment the unique index
        // refuses this insert. Its own staged record is discarded with the session, so only the winner's survives.
        losing.CommitAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            winnersRecord ??= store.Publish(CreateRequest("mfctl-4f2a"), RawMime.Length);

            return PersistenceCommitResult.ConcurrencyConflict;
        });
        retrying.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        var outbox = new MailOutbox(
            store,
            contentStore,
            CreateRetryPolicy(sessionFactory),
            new MailOutboxSignal(capacity: 8),
            Substitute.For<IJobStore>(),
            Substitute.For<IOutboxOperationStore>(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend),
            OutgoingMailGovernors.Permitting(),
            OutgoingMailScreenings.Inactive(),
            TimeProvider.System);

        // Act
        var record = (await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None)).Record;

        // Assert
        Assert.Equal(winnersRecord?.Id, record.Id);
        Assert.Equal(2, store.OpenRequests.Count);
        var outstanding = await store.ReadOutstandingAsync(Account, limit: 10, CancellationToken.None);
        Assert.Equal(record.Id, Assert.Single(outstanding).Id);
    }

    /// <summary>A durable record says so, which is what makes an authored send leave in seconds rather than at the next run.</summary>
    [Fact]
    public async Task EnqueueAsync_NewRequest_SignalsTheAccountAfterTheRecordIsDurable()
    {
        // Arrange
        var signal = new MailOutboxSignal(capacity: 4);
        var outbox = CreateOutbox(new InMemoryOutgoingEmailStore(), ContentStores.Substituted(), signal: signal);

        // Act
        await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None);

        // Assert
        Assert.Equal(1, signal.Depth);
    }

    /// <summary>A send that was never written down signals nothing, so no pass is woken for work that does not exist.</summary>
    [Fact]
    public async Task EnqueueAsync_NoMime_SignalsNothing()
    {
        // Arrange
        var signal = new MailOutboxSignal(capacity: 4);
        var outbox = CreateOutbox(new InMemoryOutgoingEmailStore(), ContentStores.Substituted(), signal: signal);

        // Act
        await Assert.ThrowsAsync<ArgumentException>(
            () => outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), ReadOnlyMemory<byte>.Empty, CancellationToken.None));

        // Assert
        Assert.Equal(0, signal.Depth);
    }

    /// <summary>Nobody states who asked for a send: the record remembers what this deployment admitted.</summary>
    /// <remarks>
    /// It is what confines reading a send back and withdrawing one to the caller that queued it, so a request able to
    /// state it would be a request able to claim somebody else's sends.
    /// </remarks>
    [Fact]
    public async Task EnqueueAsync_ACallerGrantedSending_RecordsThePrincipalItWasAdmittedUnder()
    {
        // Arrange
        var outbox = CreateOutbox(
            new InMemoryOutgoingEmailStore(),
            ContentStores.Substituted(),
            authorization: AccessAuthorizations.ForPrincipal(
                AuthorizedPrincipal.Caller("agent-key", [MailFathomPermission.MailSend])));

        // Act
        var record = (await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None)).Record;

        // Assert
        Assert.Equal(OutgoingEmailPrincipal.Of("agent-key"), record.Principal);
    }

    /// <summary>A rule's send is recorded under this process rather than under any caller, which is what keeps it out of every caller's reach.</summary>
    [Fact]
    public async Task EnqueueAsync_ARuleSend_RecordsThePrincipalOfThisProcess()
    {
        // Arrange
        var outbox = CreateOutbox(
            new InMemoryOutgoingEmailStore(),
            ContentStores.Substituted(),
            authorization: AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));

        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address));

        var request = OutgoingEmailRequest.Create(
            Account,
            OutgoingEmailRequester.Rule("archive", "r1", StoredEmailId.Create(Guid.CreateVersion7())),
            [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)]);

        // Act
        var record = (await outbox.EnqueueAsync(request, RawMime, CancellationToken.None)).Record;

        // Assert
        Assert.Equal(
            OutgoingEmailPrincipal.Of(AuthorizedPrincipal.ProcessIdentityName),
            record.Principal);
    }

    /// <summary>
    /// The transport is not the authority: a caller whose grant omits sending is refused by the outbox itself, before a
    /// record or a message is written, so an entrypoint that never passed a filter reaches the same answer.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_CallerWithoutTheSendGrant_RefusesAndRecordsNothing()
    {
        // Arrange
        var contentStore = ContentStores.Substituted();
        var signal = new MailOutboxSignal(capacity: 4);
        var outbox = CreateOutbox(
            new InMemoryOutgoingEmailStore(),
            contentStore,
            signal: signal,
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomPermission.MailSend, refusal.RequiredPermission);
        Assert.Equal(0, signal.Depth);
        await contentStore.DidNotReceive().SaveOutgoingContentAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<OutgoingEmailId>(),
            Arg.Any<PlacedEmailContent>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Reading a mailbox is not writing from it, so the grant that reaches every other mail tool reaches no send.</summary>
    [Theory]
    [InlineData("mailfathom.mail.read")]
    [InlineData("mailfathom.mail.ask")]
    [InlineData("mailfathom.mail.flags.write")]
    [InlineData("mailfathom.mail.contacts.read")]
    [InlineData("mailfathom.mail.contacts.write")]
    public async Task EnqueueAsync_CallerGrantedAnotherMailPermission_Refuses(string grantedPermissionName)
    {
        // Arrange
        Assert.True(MailFathomPermission.TryParse(grantedPermissionName, out var granted));
        var outbox = CreateOutbox(
            new InMemoryOutgoingEmailStore(),
            ContentStores.Substituted(),
            authorization: AccessAuthorizations.ForCallerGranted(granted));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomPermission.MailSend, refusal.RequiredPermission);
    }

    /// <summary>A rule sends without anybody present, so the origin it states is admitted under this process's own identity and under nothing a grant can carry.</summary>
    [Fact]
    public async Task EnqueueAsync_RuleAskingUnderTheProcessIdentity_RecordsTheSend()
    {
        // Arrange
        var outbox = CreateOutbox(
            new InMemoryOutgoingEmailStore(),
            ContentStores.Substituted(),
            authorization: AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));

        // Act
        var record = (await outbox.EnqueueAsync(CreateRuleRequest(), RawMime, CancellationToken.None)).Record;

        // Assert
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
    }

    /// <summary>
    /// The origin is checked rather than believed, in both directions: a caller cannot enqueue as a rule and so cannot
    /// borrow a rule's idempotency identity, and work no caller requested cannot enqueue as a command, because the
    /// process identity holds no permission whatever an operator granted.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnqueueAsync_OriginTheReachingPrincipalCannotProduce_Refuses(bool askedAsARule)
    {
        // Arrange
        var outbox = CreateOutbox(
            new InMemoryOutgoingEmailStore(),
            ContentStores.Substituted(),
            authorization: askedAsARule
                ? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend)
                : AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => outbox.EnqueueAsync(
                askedAsARule ? CreateRuleRequest() : CreateRequest("mfctl-4f2a"),
                RawMime,
                CancellationToken.None));

        // Assert
        Assert.False(refusal.RequiredPermission.IsSpecified);
    }

    /// <summary>
    /// The bounds are asked at the one way into the outbox, so nothing is written down for a deployment that may not
    /// send — whether because nobody turned the account on, which is every account's default, or because the whole
    /// installation is running read-only.
    /// </summary>
    [Theory]
    [InlineData(OutgoingSendRefusalReason.AccountNotEnabled)]
    [InlineData(OutgoingSendRefusalReason.DeploymentIsReadOnly)]
    public async Task EnqueueAsync_DeploymentThatMayNotSend_RefusesAndWritesNothing(OutgoingSendRefusalReason reason)
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var contentStore = ContentStores.Substituted();
        var signal = new MailOutboxSignal(capacity: 4);
        var outbox = CreateOutbox(
            store,
            contentStore,
            signal: signal,
            governor: OutgoingMailGovernors.Governing(refusal: reason));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailSendingNotEnabled, refusal.ErrorCode);
        Assert.Empty(store.OpenRequests);
        Assert.Equal(0, signal.Depth);
        await contentStore.DidNotReceive().SaveOutgoingContentAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<OutgoingEmailId>(),
            Arg.Any<PlacedEmailContent>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A message carrying what this deployment screens outgoing mail for is refused at the one way in, so no record,
    /// no stored message, and no signal survives it — and the refusal names the category rather than the message.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_MessageCarryingScreenedMaterial_RefusesAndWritesNothing()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var contentStore = ContentStores.Substituted();
        var signal = new MailOutboxSignal(capacity: 4);

        using var egress = ScanningSensitiveContentEgress.Finding(ScreenedMarker, new FakeTimeProvider(Authored));

        var outbox = CreateOutbox(
            store,
            contentStore,
            signal: signal,
            screening: OutgoingMailScreenings.Through(egress.Screen));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => outbox.EnqueueAsync(
                CreateRequest("mfctl-4f2a"),
                Encoding.UTF8.GetBytes($"the deployment key is {ScreenedMarker}"),
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingMailContentRefused, refusal.ErrorCode);
        Assert.Contains(MarkerSensitiveContentScanner.Category.Name, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ScreenedMarker, refusal.Message, StringComparison.Ordinal);
        Assert.Empty(store.OpenRequests);
        Assert.Equal(0, signal.Depth);
        await contentStore.DidNotReceive().SaveOutgoingContentAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<OutgoingEmailId>(),
            Arg.Any<PlacedEmailContent>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A message the ceiling cut is refused for the length rather than for a category, because nothing established what
    /// its remainder carried. The code is what a caller acts on and it is a different remedy from the one above — the
    /// author shortens the message, or the operator raises the ceiling — so it is asserted through the outbox rather
    /// than only where the screen produces it.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_MessageLongerThanOneScanAnalyzes_RefusesForTheLengthAndWritesNothing()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var signal = new MailOutboxSignal(capacity: 4);

        using var egress = ScanningSensitiveContentEgress.Finding(
            ScreenedMarker,
            new FakeTimeProvider(Authored),
            bounds: SensitiveContentScanBounds.Create(
                maximumAnalyzedCharacters: 16,
                TimeSpan.FromSeconds(15),
                maximumConcurrentScans: 4));

        var outbox = CreateOutbox(
            store,
            ContentStores.Substituted(),
            signal: signal,
            screening: OutgoingMailScreenings.Through(egress.Screen));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => outbox.EnqueueAsync(
                CreateRequest("mfctl-4f2a"),
                Encoding.UTF8.GetBytes("a message far longer than this deployment analyzes in one scan"),
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingMailNotFullyScanned, refusal.ErrorCode);
        Assert.Empty(store.OpenRequests);
        Assert.Equal(0, signal.Depth);
    }

    /// <summary>A message carrying nothing the deployment screens for is queued exactly as an unscreened one is.</summary>
    [Fact]
    public async Task EnqueueAsync_ScreenedDeploymentAndAnOrdinaryMessage_QueuesIt()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();

        using var egress = ScanningSensitiveContentEgress.Finding(ScreenedMarker, new FakeTimeProvider(Authored));

        var outbox = CreateOutbox(
            store,
            ContentStores.Substituted(),
            screening: OutgoingMailScreenings.Through(egress.Screen));

        // Act
        var opened = await outbox.EnqueueAsync(
            CreateRequest("mfctl-4f2a"),
            Encoding.UTF8.GetBytes("an ordinary message"),
            CancellationToken.None);

        // Assert
        Assert.True(opened.WasRecordedNow);
        Assert.Single(store.OpenRequests);
    }

    /// <summary>
    /// A scanner that cannot say what the message carries refuses the send rather than queueing it unscreened, which is
    /// the fail-closed answer every other consumer of the port gives.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_ScannerThatCannotAnswer_RefusesRatherThanQueueingUnscreened()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();

        using var egress = ScanningSensitiveContentEgress.Unavailable(new FakeTimeProvider(Authored));

        var outbox = CreateOutbox(
            store,
            ContentStores.Substituted(),
            screening: OutgoingMailScreenings.Through(egress.Screen));

        // Act
        await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(
            () => outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None));

        // Assert
        Assert.Empty(store.OpenRequests);
    }

    /// <summary>A message naming a recipient the policy refuses is refused whole, and no part of it becomes a record.</summary>
    [Fact]
    public async Task EnqueueAsync_RecipientTheDeploymentMayNotWriteTo_RefusesTheWholeMessage()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        Assert.True(OutgoingRecipientRule.TryCreateForDomain("rival.test", out var denied));
        var outbox = CreateOutbox(
            store,
            ContentStores.Substituted(),
            governor: OutgoingMailGovernors.Governing(
                recipientPolicy: OutgoingRecipientPolicy.Create([], [denied])));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => outbox.EnqueueAsync(
                CreateRequest("mfctl-4f2a", "anna@example.test", "bruno@rival.test"),
                RawMime,
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingRecipientRefusedByPolicy, refusal.ErrorCode);
        Assert.Empty(store.OpenRequests);
    }

    /// <summary>A send that would carry the period past a ceiling is refused before anything is written down.</summary>
    [Fact]
    public async Task EnqueueAsync_SendBeyondACeiling_RefusesAndWritesNothing()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var outbox = CreateOutbox(
            store,
            ContentStores.Substituted(),
            governor: OutgoingMailGovernors.Governing(
                ceilings: OutgoingMailCeilings.Create(
                    TimeSpan.FromDays(1),
                    maxMessagesPerAccount: 2,
                    maxRecipientsPerAccount: 0,
                    maxMessagesPerDeployment: 0,
                    maxRecipientsPerDeployment: 0),
                usage: new OutgoingMailUsage(
                    AccountMessageCount: 2,
                    AccountRecipientCount: 2,
                    DeploymentMessageCount: 2,
                    DeploymentRecipientCount: 2)));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingMailCeilingReached, refusal.ErrorCode);
        Assert.Empty(store.OpenRequests);
    }

    /// <summary>Work no caller requested meets the same bounds, which is what makes them the deployment's rather than a caller's.</summary>
    [Fact]
    public async Task EnqueueAsync_RuleAskingUnderADeploymentThatMayNotSend_IsRefusedToo()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var outbox = CreateOutbox(
            store,
            ContentStores.Substituted(),
            authorization: AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process),
            governor: OutgoingMailGovernors.Governing(
                refusal: OutgoingSendRefusalReason.DeploymentIsReadOnly));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => outbox.EnqueueAsync(CreateRuleRequest(), RawMime, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailSendingNotEnabled, refusal.ErrorCode);
        Assert.Empty(store.OpenRequests);
    }

    /// <summary>
    /// A send written for a later time is not announced but queued for the moment it names, which is the whole of what
    /// holding one costs: no timer, no scheduler, and no queue of this feature's own.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_ASendWrittenForALaterTime_QueuesTheMomentItIsDueInsteadOfSignalling()
    {
        // Arrange
        var jobs = Substitute.For<IJobStore>();
        var signal = new MailOutboxSignal(capacity: 4);
        var outbox = CreateOutbox(
            new InMemoryOutgoingEmailStore(),
            ContentStores.Substituted(),
            signal: signal,
            jobs: jobs,
            timeProvider: new FakeTimeProvider(Authored));

        // Act
        var record = (await outbox.EnqueueAsync(HeldRequest("mfctl-4f2a", DueAt), RawMime, CancellationToken.None)).Record;

        // Assert
        Assert.Equal(0, signal.Depth);
        Assert.Equal(DueAt, record.AvailableAt);
        await jobs.Received(1).EnqueueAsync(
            Arg.Is<JobEnqueueRequest>(request =>
                request != null
                && request.Key.Value == $"held-send:{record.Id}"
                && request.JobType == JobType.DispatchHeldSend
                && request.AvailableAt == DueAt
                && request.AccountId == Account),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A send whose named time has already come is an ordinary send, so it is announced rather than queued for later.</summary>
    [Fact]
    public async Task EnqueueAsync_ASendWhoseNamedTimeHasCome_SignalsRatherThanQueuingAMoment()
    {
        // Arrange
        var jobs = Substitute.For<IJobStore>();
        var signal = new MailOutboxSignal(capacity: 4);
        var outbox = CreateOutbox(
            new InMemoryOutgoingEmailStore(),
            ContentStores.Substituted(),
            signal: signal,
            jobs: jobs,
            timeProvider: new FakeTimeProvider(DueAt));

        // Act
        await outbox.EnqueueAsync(HeldRequest("mfctl-4f2a", DueAt), RawMime, CancellationToken.None);

        // Assert
        Assert.Equal(1, signal.Depth);
        await jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A message is withdrawable for the whole of the hold, through the one statement that writes the withdrawal.</summary>
    /// <remarks>
    /// What the outcome means is the operation store's own suite and the integration suite's, because the stage rule
    /// and the live lease are conditions of a single statement rather than decisions this use case takes. What belongs
    /// here is that the use case reaches that statement for the record the caller named, and answers with what it said.
    /// </remarks>
    [Fact]
    public async Task CancelAsync_ASendStillBeingHeld_AsksTheOneWithdrawalForThatRecord()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();
        var store = new InMemoryOutgoingEmailStore();
        var outbox = CreateOutbox(
            store,
            ContentStores.Substituted(),
            outboxOperations: operations,
            timeProvider: new FakeTimeProvider(Authored));
        var record = (await outbox.EnqueueAsync(HeldRequest("mfctl-4f2a", DueAt), RawMime, CancellationToken.None)).Record;
        operations.CancelAsync(record.Id, Arg.Any<CancellationToken>()).Returns(OutboxDecisionOutcome.Accepted);

        // Act
        var outcome = await outbox.CancelAsync(record.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.Accepted, outcome);
        await operations.Received(1).CancelAsync(record.Id, Arg.Any<CancellationToken>());
    }

    /// <summary>A refusal is answered rather than raised over, because a caller acting on a stale reading cannot tell the cases apart.</summary>
    [Theory]
    [InlineData(OutboxDecisionOutcome.RecordUnknown)]
    [InlineData(OutboxDecisionOutcome.StageDoesNotAllowIt)]
    [InlineData(OutboxDecisionOutcome.AttemptUnderWay)]
    public async Task CancelAsync_AWithdrawalTheRecordDoesNotAllow_AnswersWithWhatTheStoreSaid(
        OutboxDecisionOutcome refusal)
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();
        operations.CancelAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>()).Returns(refusal);
        var outbox = CreateOutbox(
            new InMemoryOutgoingEmailStore(),
            ContentStores.Substituted(),
            outboxOperations: operations);

        // Act
        var outcome = await outbox.CancelAsync(
            OutgoingEmailId.Create(Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(refusal, outcome);
    }

    /// <summary>Stopping somebody's mail is a decision about their correspondents exactly as sending it is, so it asks for the same grant.</summary>
    [Fact]
    public async Task CancelAsync_CallerWithoutTheSendGrant_RefusesAndWithdrawsNothing()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();
        var outbox = CreateOutbox(
            new InMemoryOutgoingEmailStore(),
            ContentStores.Substituted(),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
            outboxOperations: operations);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => outbox.CancelAsync(OutgoingEmailId.Create(Guid.CreateVersion7()), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailSend, refusal.RequiredPermission);
        await operations.DidNotReceiveWithAnyArgs().CancelAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>An occurrence of a recurring send is composed with nobody present, so it is admitted exactly as a rule's message is.</summary>
    [Fact]
    public async Task EnqueueAsync_AnOccurrenceOfARecurringSendUnderTheProcessIdentity_RecordsTheSend()
    {
        // Arrange
        var outbox = CreateOutbox(
            new InMemoryOutgoingEmailStore(),
            ContentStores.Substituted(),
            authorization: AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process),
            timeProvider: new FakeTimeProvider(Authored));

        // Act
        var record = (await outbox.EnqueueAsync(CreateOccurrenceRequest(), RawMime, CancellationToken.None)).Record;

        // Assert
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
        Assert.Equal(OutgoingEmailOrigin.Schedule, record.Requester.Origin);
    }

    private static OutgoingEmailRequest HeldRequest(string invocationIdentity, DateTimeOffset dueAt) =>
        CreateRequest(invocationIdentity).HeldUntil(ZonedInstant.At(dueAt));

    private static OutgoingEmailRequest CreateOccurrenceRequest()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address));

        return OutgoingEmailRequest.Create(
            Account,
            OutgoingEmailRequester.Schedule(RecurringSendId.Create(Guid.CreateVersion7()), DueAt),
            [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)]);
    }

    private static OutgoingEmailRequest CreateRuleRequest()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address));

        return OutgoingEmailRequest.Create(
            Account,
            OutgoingEmailRequester.Rule("auto-reply", "revision-1", StoredEmailId.Create(Guid.CreateVersion7())),
            [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)]);
    }

    private static OutgoingEmailRequest CreateRequest(
        string invocationIdentity,
        params string[] recipientAddresses)
    {
        var recipients = (recipientAddresses.Length == 0 ? ["anna@example.test"] : recipientAddresses)
            .Select(candidate =>
            {
                Assert.True(EmailAddress.TryCreate(displayName: null, candidate, out var address));

                return OutgoingRecipient.Create(address, OutgoingRecipientRole.To);
            })
            .ToArray();

        return OutgoingEmailRequest.Create(
            Account,
            OutgoingEmailRequester.Command(invocationIdentity),
            recipients);
    }

    /// <summary>
    /// The message is handed over before anything opens a unit of work, which is the whole of what makes the object
    /// backend legal here: joining a session is what opens its transaction, so a placement made while no session exists
    /// is a placement made with no transaction open across it.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_NewRequest_PlacesTheMessageBeforeAnyPersistenceSessionExists()
    {
        // Arrange
        var stagedSessions = new List<IPersistenceSession>();
        var sessionsOpenWhenPlaced = -1;
        var contentStore = ContentStores.Substituted();
        contentStore
            .PlaceContentAsync(
                Arg.Any<EmailContentKind>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sessionsOpenWhenPlaced = stagedSessions.Count;

                return Task.FromResult(PlacedEmailContent.InDatabase(call.ArgAt<ReadOnlyMemory<byte>>(1)));
            });

        var outbox = CreateOutbox(new InMemoryOutgoingEmailStore(), contentStore, stagedSessions);

        // Act
        await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None);

        // Assert
        Assert.Equal(0, sessionsOpenWhenPlaced);
        Assert.Single(stagedSessions);
    }

    /// <summary>
    /// A conflicted attempt replays the whole unit of work, and the placement is not part of it. Every attempt stages
    /// the same locator over the same object, so the endpoint sees one write however many times the commit is repeated.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_APersistenceConflictThenACommit_PlacesTheMessageOnceAcrossBothAttempts()
    {
        // Arrange
        var contentStore = ContentStores.Substituted();
        var stagedSessions = new List<IPersistenceSession>();
        var outbox = CreateOutbox(
            new InMemoryOutgoingEmailStore(),
            contentStore,
            stagedSessions,
            conflictingAttempts: 1);

        // Act
        var record = (await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None)).Record;

        // Assert
        Assert.Equal(2, stagedSessions.Count);
        await contentStore.Received(1).PlaceContentAsync(
            EmailContentKind.OutgoingMessage,
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
        await contentStore.Received(2).SaveOutgoingContentAsync(
            Arg.Any<IPersistenceSession>(),
            record.Id,
            Arg.Any<PlacedEmailContent>(),
            Arg.Any<CancellationToken>());
    }

    private static MailOutbox CreateOutbox(
        IOutgoingEmailStore store,
        IEmailContentStore contentStore,
        List<IPersistenceSession>? stagedSessions = null,
        MailOutboxSignal? signal = null,
        AccessAuthorization? authorization = null,
        OutgoingMailGovernor? governor = null,
        IJobStore? jobs = null,
        IOutboxOperationStore? outboxOperations = null,
        OutgoingMailScreening? screening = null,
        TimeProvider? timeProvider = null,
        int conflictingAttempts = 0)
    {
        var conflictsLeft = conflictingAttempts;
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(
                conflictsLeft-- > 0 ? PersistenceCommitResult.ConcurrencyConflict : PersistenceCommitResult.Committed);
            stagedSessions?.Add(session);

            return session;
        });

        return new MailOutbox(
            store,
            contentStore,
            CreateRetryPolicy(sessionFactory),
            signal ?? new MailOutboxSignal(capacity: 8),
            jobs ?? Substitute.For<IJobStore>(),
            outboxOperations ?? Substitute.For<IOutboxOperationStore>(),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend),
            governor ?? OutgoingMailGovernors.Permitting(),
            screening ?? OutgoingMailScreenings.Inactive(),
            timeProvider ?? TimeProvider.System);
    }

    /// <summary>Builds the policy the outbox commits through, over the real clock the policy's own tests use.</summary>
    /// <remarks>
    /// A controlled clock would deadlock rather than help: the backoff between attempts is a
    /// <c>Task.Delay</c> against this provider, and one that never advances never completes it. The delay the one
    /// conflicting test pays is a few tens of milliseconds.
    /// </remarks>
    private static OptimisticConcurrencyRetryPolicy CreateRetryPolicy(IPersistenceSessionFactory sessionFactory) =>
        new(sessionFactory, new PersistenceConcurrencyOptions(), TimeProvider.System);
}
