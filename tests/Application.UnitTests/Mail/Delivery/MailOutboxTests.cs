// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery;

public sealed class MailOutboxTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly ReadOnlyMemory<byte> RawMime =
        Encoding.ASCII.GetBytes("Message-ID: <one@example.test>\r\n\r\nHello.").AsMemory();

    /// <summary>The record and the message it points at are one write, so a caller cannot leave half a send behind.</summary>
    [Fact]
    public async Task EnqueueAsync_NewRequest_RecordsTheSendAndItsMessageInOneSession()
    {
        // Arrange
        var contentStore = Substitute.For<IEmailContentStore>();
        var stagedSessions = new List<IPersistenceSession>();
        var outbox = CreateOutbox(new InMemoryOutgoingEmailStore(), contentStore, stagedSessions);
        var request = CreateRequest("mfctl-4f2a");

        // Act
        var record = await outbox.EnqueueAsync(request, RawMime, CancellationToken.None);

        // Assert
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
        Assert.Equal(RawMime.Length, record.MimeByteLength);
        Assert.Equal(request.Recipients, record.OutstandingRecipients);
        await contentStore.Received(1).SaveOutgoingContentAsync(
            stagedSessions.Single(),
            record.Id,
            RawMime,
            Arg.Any<CancellationToken>());
    }

    /// <summary>The same authored request arriving twice is one record, which is what makes it one delivery.</summary>
    [Fact]
    public async Task EnqueueAsync_SameIdentityTwice_AnswersWithOneRecord()
    {
        // Arrange
        var contentStore = Substitute.For<IEmailContentStore>();
        var store = new InMemoryOutgoingEmailStore();
        var outbox = CreateOutbox(store, contentStore);
        var first = await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None);

        // Act
        var retried = await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None);

        // Assert
        Assert.Equal(first.Id, retried.Id);
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
        var outbox = CreateOutbox(store, Substitute.For<IEmailContentStore>());
        var first = await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None);

        // Act
        var second = await outbox.EnqueueAsync(CreateRequest("mfctl-91bd"), RawMime, CancellationToken.None);

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
        var outbox = CreateOutbox(store, Substitute.For<IEmailContentStore>());

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
        var contentStore = Substitute.For<IEmailContentStore>();
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
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend),
            OutgoingMailGovernors.Permitting());

        // Act
        var record = await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None);

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
        var outbox = CreateOutbox(new InMemoryOutgoingEmailStore(), Substitute.For<IEmailContentStore>(), signal: signal);

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
        var outbox = CreateOutbox(new InMemoryOutgoingEmailStore(), Substitute.For<IEmailContentStore>(), signal: signal);

        // Act
        await Assert.ThrowsAsync<ArgumentException>(
            () => outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), ReadOnlyMemory<byte>.Empty, CancellationToken.None));

        // Assert
        Assert.Equal(0, signal.Depth);
    }

    /// <summary>
    /// The transport is not the authority: a caller whose grant omits sending is refused by the outbox itself, before a
    /// record or a message is written, so an entrypoint that never passed a filter reaches the same answer.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_CallerWithoutTheSendGrant_RefusesAndRecordsNothing()
    {
        // Arrange
        var contentStore = Substitute.For<IEmailContentStore>();
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
            Arg.Any<ReadOnlyMemory<byte>>(),
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
            Substitute.For<IEmailContentStore>(),
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
            Substitute.For<IEmailContentStore>(),
            authorization: AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));

        // Act
        var record = await outbox.EnqueueAsync(CreateRuleRequest(), RawMime, CancellationToken.None);

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
            Substitute.For<IEmailContentStore>(),
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
        var contentStore = Substitute.For<IEmailContentStore>();
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
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
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
            Substitute.For<IEmailContentStore>(),
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
            Substitute.For<IEmailContentStore>(),
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
            Substitute.For<IEmailContentStore>(),
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

    private static MailOutbox CreateOutbox(
        IOutgoingEmailStore store,
        IEmailContentStore contentStore,
        List<IPersistenceSession>? stagedSessions = null,
        MailOutboxSignal? signal = null,
        AccessAuthorization? authorization = null,
        OutgoingMailGovernor? governor = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
            stagedSessions?.Add(session);

            return session;
        });

        return new MailOutbox(
            store,
            contentStore,
            CreateRetryPolicy(sessionFactory),
            signal ?? new MailOutboxSignal(capacity: 8),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend),
            governor ?? OutgoingMailGovernors.Permitting());
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
