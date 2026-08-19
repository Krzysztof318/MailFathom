// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Filing;

/// <summary>Covers what a pass puts into the mailbox's own folders, and what it deliberately does not.</summary>
public sealed class OutgoingMailFilingPassTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset RanAt = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] StoredMime = Encoding.ASCII.GetBytes(
        "Message-ID: <mint-1@mailfathom.invalid>\r\nSubject: synthetic\r\n\r\nbody\r\n");

    private static readonly ReadOnlyMemory<byte> RawMime = StoredMime;

    /// <summary>An account that asked for no sent copy gets none, and nothing reaches its mail server over it.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AnAccountThatFilesNoSentCopy_AppendsNothing()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        var delivered = await context.DeliverAsync();

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(OutgoingMailFilingOutcome.NotRequested, result.Outcome);
        Assert.Empty(context.Filing.Filings.Read(delivered));
        await context.Filing.WriteSession.DidNotReceive().AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<AppendedMailFlags>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A delivered message the account files reaches the sent folder as read, from the stored bytes.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_ADeliveredMessage_AppendsTheStoredMimeToTheSentFolderAsSeen()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        context.Filing.AppendAnswer = new AppendedMailCopy(
            RemoteEmailPlacement.Reported(ImapUidValidity.Create(42), ImapUid.Create(7)),
            "mint-1@mailfathom.invalid");
        var delivered = await context.DeliverAsync();

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(OutgoingMailFilingOutcome.Filed, result.Outcome);

        await context.Filing.WriteSession.Received(1).AppendAsync(
            Arg.Is<ReadOnlyMemory<byte>>(mime => mime.ToArray().SequenceEqual(StoredMime)),
            AppendedMailFlags.Seen,
            RanAt,
            Arg.Any<CancellationToken>());

        var filed = Assert.Single(context.Filing.Filings.Read(delivered));
        Assert.Equal(OutgoingMailFiling.Sent, filed.Filing);
        Assert.Equal(OutgoingMailFilingStage.Confirmed, filed.Stage);
        Assert.Equal(ImapUid.Create(7), filed.Placement.Uid);
        Assert.Equal("mint-1@mailfathom.invalid", filed.InternetMessageId);
    }

    /// <summary>
    /// The one ending nothing repeats. The server took the append and never said so, and a second one would put a
    /// second copy of the owner's message in their sent folder with nothing afterwards telling the two apart.
    /// </summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AnAppendTheServerNeverAnswered_IsNeverAppendedAgain()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        context.Filing.WriteSession
            .AppendAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<AppendedMailFlags>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new MailboxUnavailableException(
                Account,
                MailFolderAlias.Create("sent"),
                new TimeoutException("The append was issued and the server never answered.")));
        var delivered = await context.DeliverAsync();

        // Act
        var first = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);
        var second = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingMailFilingOutcome.OutcomeUnknown, Assert.Single(first).Outcome);
        Assert.Equal(OutgoingMailFilingOutcome.OutcomeUnknown, Assert.Single(second).Outcome);

        await context.Filing.WriteSession.Received(1).AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<AppendedMailFlags>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A copy that could not be filed leaves the delivery exactly as delivered. The record says the message was sent,
    /// says why the copy is not in the folder, and nothing offers the message to anybody again.
    /// </summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AFailedAppendAfterADelivery_LeavesTheSendDeliveredAndNotFiled()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.FileSentCopies(Account);
        var delivered = await context.DeliverAsync();

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(OutgoingMailFilingOutcome.DestinationUnavailable, result.Outcome);

        var record = context.Store.Read(delivered);
        Assert.Equal(OutgoingEmailStage.Sent, record.Stage);
        Assert.Null(record.LastFailure);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailFilingDestinationUnavailable, record.LastFilingFailure);
        Assert.Empty(record.Filings);
        Assert.Equal(1, record.AttemptCount);
    }

    /// <summary>
    /// A host that stopped says nothing about the copy. The filing never reached the mail server, so the shutdown is
    /// raised for the caller that already tells one from a failure rather than written onto the record as one — which
    /// would leave a delivered send permanently recorded as one whose copy could not be filed.
    /// </summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_TheHostStopsBeforeTheAppendIsIssued_RecordsNoFilingFailure()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        var delivered = await context.DeliverAsync();

        using var shutdown = new CancellationTokenSource();
        context.Content
            .FindOutgoingContentAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>())
            .Returns<StoredEmailContent?>(_ =>
            {
                shutdown.Cancel();

                throw new OperationCanceledException(shutdown.Token);
            });

        // Act
        var stopped = await Assert.ThrowsAsync<OperationCanceledException>(
            () => context.Filing.Pass.SettleFiledCopiesAsync(delivered, shutdown.Token));

        // Assert
        Assert.Equal(shutdown.Token, stopped.CancellationToken);

        var record = context.Store.Read(delivered);
        Assert.Equal(OutgoingEmailStage.Sent, record.Stage);
        Assert.Null(record.LastFilingFailure);
        Assert.Empty(record.Filings);
        await context.Filing.WriteSession.DidNotReceiveWithAnyArgs().AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<AppendedMailFlags>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A deployment that mapped no outbox folder mirrors nothing, which is every deployment that says nothing.</summary>
    [Fact]
    public async Task MirrorWaitingSendsAsync_AnAccountThatMapsNoOutboxFolder_MirrorsNothing()
    {
        // Arrange
        var context = new FilingContext();
        await context.EnqueueAsync(availableIn: TimeSpan.FromHours(4));

        // Act
        var results = await context.Filing.Pass.MirrorWaitingSendsAsync(
            Account,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
        await context.Filing.WriteSession.DidNotReceive().AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<AppendedMailFlags>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A message waiting for an instant still ahead is what an owner would look for in their own client.</summary>
    [Fact]
    public async Task MirrorWaitingSendsAsync_ASendWaitingForALaterInstant_IsMirroredAsADraft()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Outbox, "outbox", "INBOX.Outbox");
        var waiting = await context.EnqueueAsync(availableIn: TimeSpan.FromHours(4));

        // Act
        var results = await context.Filing.Pass.MirrorWaitingSendsAsync(
            Account,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingMailFilingOutcome.Filed, Assert.Single(results).Outcome);
        await context.Filing.WriteSession.Received(1).AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            AppendedMailFlags.Draft,
            RanAt,
            Arg.Any<CancellationToken>());

        var mirrored = Assert.Single(context.Filing.Filings.Read(waiting));
        Assert.Equal(OutgoingMailFiling.Held, mirrored.Filing);
    }

    /// <summary>A send the very next claim takes is gone in seconds, and appending a copy of it would append one per send.</summary>
    [Fact]
    public async Task MirrorWaitingSendsAsync_ASendThatIsMerelyQueued_IsMirroredNowhere()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Outbox, "outbox", "INBOX.Outbox");
        await context.EnqueueAsync();

        // Act
        var results = await context.Filing.Pass.MirrorWaitingSendsAsync(
            Account,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
        await context.Filing.WriteSession.DidNotReceive().AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<AppendedMailFlags>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A mirrored message that has left is taken out of the folder, so an outbox an owner reads drains.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AMirroredSendThatHasLeft_WithdrawsTheMirror()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Outbox, "outbox", "INBOX.Outbox");
        context.Filing.AppendAnswer = new AppendedMailCopy(
            RemoteEmailPlacement.Reported(ImapUidValidity.Create(11), ImapUid.Create(3)),
            "mint-1@mailfathom.invalid");
        var waiting = await context.EnqueueAsync(availableIn: TimeSpan.FromHours(4));
        await context.Filing.Pass.MirrorWaitingSendsAsync(Account, TestContext.Current.CancellationToken);
        context.Advance(TimeSpan.FromHours(5));
        await context.MarkDeliveredAsync(waiting);

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            waiting,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [OutgoingMailFilingOutcome.Withdrawn, OutgoingMailFilingOutcome.NotRequested],
            results.Select(result => result.Outcome));
        await context.Filing.WriteSession.Received(1).WithdrawAppendedAsync(
            ImapUidValidity.Create(11),
            ImapUid.Create(3),
            Arg.Any<CancellationToken>());

        var mirrored = Assert.Single(context.Filing.Filings.Read(waiting));
        Assert.Equal(OutgoingMailFilingStage.Withdrawn, mirrored.Stage);
    }

    /// <summary>
    /// A mirror whose append was never answered is left exactly where it is. Nobody knows whether the copy reached the
    /// folder, so recording it withdrawn would be MailFathom stating that it did not — and it would take the one row
    /// that still reports the ambiguity out of every reading of the record.
    /// </summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AMirrorWhoseAppendWasNeverAnswered_IsNeitherWithdrawnNorResolved()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Outbox, "outbox", "INBOX.Outbox");
        context.Filing.WriteSession
            .AppendAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<AppendedMailFlags>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new MailboxUnavailableException(
                Account,
                MailFolderAlias.Create("outbox"),
                new TimeoutException("The append was issued and the server never answered.")));
        var waiting = await context.EnqueueAsync(availableIn: TimeSpan.FromHours(4));
        await context.Filing.Pass.MirrorWaitingSendsAsync(Account, TestContext.Current.CancellationToken);
        context.Advance(TimeSpan.FromHours(5));
        await context.MarkDeliveredAsync(waiting);

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            waiting,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [OutgoingMailFilingOutcome.OutcomeUnknown, OutgoingMailFilingOutcome.NotRequested],
            results.Select(result => result.Outcome));

        // Nothing is asked of the server, because the row names no placement to ask about: a copy the server never
        // reported cannot be reached, and searching the folder for something that looks like the message is a guess.
        await context.Filing.WriteSession.DidNotReceiveWithAnyArgs().WithdrawAppendedAsync(
            Arg.Any<ImapUidValidity>(),
            Arg.Any<ImapUid>(),
            Arg.Any<CancellationToken>());

        var mirrored = Assert.Single(context.Filing.Filings.Read(waiting));
        Assert.Equal(OutgoingMailFilingStage.Issued, mirrored.Stage);
        Assert.True(mirrored.HasUnknownOutcome);
    }

    /// <summary>A send nothing has settled yet has nothing to say about its copies.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_ASendThatIsStillQueued_DoesNothing()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        var queued = await context.EnqueueAsync();

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            queued,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
        await context.Filing.WriteSession.DidNotReceive().AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<AppendedMailFlags>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A message that reached nobody is not a message to say <em>sent</em> about in the owner's own folder.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_ARefusedSend_FilesNoSentCopy()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        var refused = await context.EnqueueAsync();
        await context.SettleAsync(refused, OutgoingEmailStage.Refused);

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            refused,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
        await context.Filing.WriteSession.DidNotReceive().AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<AppendedMailFlags>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Arranges an outbox whose sends a test settles by hand, beside the filing the pass performs.</summary>
    private sealed class FilingContext
    {
        private readonly FakeTimeProvider clock = new(RanAt);

        internal FilingContext()
        {
            this.Store = new InMemoryOutgoingEmailStore(timeProvider: this.clock);

            this.Content = Substitute.For<IEmailContentStore>();
            this.Content
                .FindOutgoingContentAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>())
                .Returns(new StoredEmailContent(RawMime, RawMime.Length, SHA256.HashData(RawMime.Span)));

            var settings = MailOutboxSettings.Create(
                maxDeliveriesPerPass: 10,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(7),
                maxAttempts: 5,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromHours(1));

            this.Filing = new OutgoingMailFilingHarness(this.Store, this.Content, settings, this.clock);
        }

        internal InMemoryOutgoingEmailStore Store { get; }

        /// <summary>Gets the store the appended bytes come from, which is the last step before anything is written down.</summary>
        internal IEmailContentStore Content { get; }

        internal OutgoingMailFilingHarness Filing { get; }

        /// <summary>Moves the clock on, which is how a send that was waiting becomes one a claim may take.</summary>
        internal void Advance(TimeSpan delay) => this.clock.Advance(delay);

        /// <summary>Writes down one send, optionally one that is waiting for an instant still ahead.</summary>
        internal async Task<OutgoingEmailId> EnqueueAsync(TimeSpan? availableIn = null)
        {
            Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var recipient));

            var request = OutgoingEmailRequest.Create(
                Account,
                OutgoingEmailRequester.Command($"mfctl-{Guid.CreateVersion7()}"),
                [OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To)]);

            var queued = this.Store.Publish(request, RawMime.Length).Id;

            if (availableIn is { } delay)
            {
                await this.DeferAsync(queued, this.clock.GetUtcNow() + delay);
            }

            return queued;
        }

        /// <summary>Writes down one send that a submission server has already accepted.</summary>
        internal async Task<OutgoingEmailId> DeliverAsync()
        {
            var queued = await this.EnqueueAsync();
            await this.MarkDeliveredAsync(queued);

            return queued;
        }

        /// <summary>Carries one send through a claim and a transmission the server accepted.</summary>
        internal Task MarkDeliveredAsync(OutgoingEmailId outgoingEmailId) =>
            this.SettleAsync(outgoingEmailId, OutgoingEmailStage.Sent);

        /// <summary>Carries one send to a terminal stage, through the transitions the store permits.</summary>
        internal async Task SettleAsync(OutgoingEmailId outgoingEmailId, OutgoingEmailStage stage)
        {
            var lease = await this.ClaimAsync(outgoingEmailId);
            var session = Substitute.For<IPersistenceSession>();

            if (stage == OutgoingEmailStage.Sent)
            {
                await this.Store.RecordTransmissionBegunAsync(
                    session,
                    lease,
                    outgoingEmailId,
                    TestContext.Current.CancellationToken);
            }

            await this.Store.AdvanceAsync(
                session,
                lease,
                outgoingEmailId,
                stage,
                replyCode: 250,
                TestContext.Current.CancellationToken);
        }

        private async Task DeferAsync(OutgoingEmailId outgoingEmailId, DateTimeOffset availableAt)
        {
            var lease = await this.ClaimAsync(outgoingEmailId);

            await this.Store.DeferAsync(
                Substitute.For<IPersistenceSession>(),
                lease,
                outgoingEmailId,
                availableAt,
                failure: null,
                TestContext.Current.CancellationToken);
        }

        private async Task<OutgoingEmailLease> ClaimAsync(OutgoingEmailId outgoingEmailId)
        {
            var claimed = await this.Store.ClaimAsync(
                OutgoingEmailClaimRequest.Create(Account, batchSize: 10, TimeSpan.FromMinutes(10)),
                TestContext.Current.CancellationToken);

            return claimed.Single(send => send.Record.Id == outgoingEmailId).Lease;
        }
    }
}
