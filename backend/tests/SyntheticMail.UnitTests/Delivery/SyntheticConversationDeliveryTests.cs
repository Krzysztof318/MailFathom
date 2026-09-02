// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Delivery;
using MailFathom.SyntheticMail.Generation;
using MailFathom.SyntheticMail.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using MimeKit;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Delivery;

/// <summary>How an exchange reaches a mailbox: which half is submitted, which is filed, and where each reply's ancestry comes from.</summary>
/// <remarks>
/// The mailbox double rewrites every identifier it is handed, which is the behaviour the whole mode exists for. A
/// server that left <c>Message-Id</c> alone would make a broken implementation and a working one indistinguishable.
/// </remarks>
public sealed class SyntheticConversationDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 11, 30, 0, TimeSpan.Zero);

    private static readonly MailboxAddress WatchedMailbox = new("Developer", "developer@example.com");

    [Fact]
    public async Task DeliverAsync_AnExchange_SubmitsTheCorrespondentsTurnsAndFilesTheMailboxesOwn()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();
        var conversation = Conversation(turns: 4);

        // Act
        await Deliver(transport, mailbox, [conversation]);

        // Assert
        // Both halves end up in the watched mailbox and neither takes the other's route: an appended copy is what a
        // mail client leaves in Sent, and a submitted one is what arrives.
        Assert.Equal(
            [conversation.Messages[0].MessageId, conversation.Messages[2].MessageId],
            transport.Submissions.Select(submission => submission.MessageId));

        Assert.Equal(
            [conversation.Messages[1].MessageId, conversation.Messages[3].MessageId],
            mailbox.Appended.Select(appended => appended.MessageId));
    }

    [Fact]
    public async Task DeliverAsync_AnExchange_BuildsEveryReplyFromTheIdentifierTheServerAssigned()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();
        var conversation = Conversation(turns: 4);

        // Act
        await Deliver(transport, mailbox, [conversation]);

        // Assert
        // The reply answers what the mailbox actually holds. Threading on the proposed identifier is exactly the
        // failure this mode replaces, and it is invisible until the server rewrites one.
        var assignedFirst = RecordingWatchedMailbox.AssignedPrefix + conversation.Messages[0].MessageId;
        var appendedReply = mailbox.Appended[0];

        Assert.Equal(assignedFirst, appendedReply.InReplyTo);
        Assert.Equal([assignedFirst], appendedReply.References);
    }

    [Fact]
    public async Task DeliverAsync_AnExchange_CarriesTheWholeAncestryForwardInOrder()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();
        var conversation = Conversation(turns: 4);

        // Act
        await Deliver(transport, mailbox, [conversation]);

        // Assert
        var first = RecordingWatchedMailbox.AssignedPrefix + conversation.Messages[0].MessageId;
        var second = conversation.Messages[1].MessageId;
        var thirdTurn = transport.Submissions[1];

        // The mailbox's own turn keeps the identifier it was composed with, because nothing rewrote the copy this run
        // appended, so the ancestry alternates between assigned and composed values rather than being one or the other.
        Assert.Equal(second, thirdTurn.InReplyTo);
        Assert.Equal([first, second], thirdTurn.References);
    }

    [Fact]
    public async Task DeliverAsync_AnExchange_OpensEveryThreadWithoutAnAncestry()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();

        // Act
        await Deliver(transport, mailbox, [Conversation(turns: 2), Conversation(turns: 2, thread: 1)]);

        // Assert
        Assert.All(transport.Submissions, submission =>
        {
            Assert.Null(submission.InReplyTo);
            Assert.Empty(submission.References);
        });
    }

    [Fact]
    public async Task DeliverAsync_AnExchange_StampsEverySubmissionSoItsDeliveredCopyCanBeFound()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();
        var conversation = Conversation(turns: 3);

        // Act
        await Deliver(transport, mailbox, [conversation]);

        // Assert
        // The proposed identifier is the marker, and it is what the mailbox is searched for, because the identifier
        // itself is the value a submission server is free to replace.
        Assert.Equal(
            transport.Submissions.Select(submission => submission.MessageId),
            transport.Submissions.Select(submission => submission.Marker));

        Assert.Equal(transport.Submissions.Select(submission => submission.MessageId), mailbox.Searches);
    }

    [Fact]
    public async Task DeliverAsync_AnExchange_AddressesTheMailboxesReplyToWhoeverWroteToIt()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();
        var conversation = Conversation(turns: 2);

        // Act
        await Deliver(transport, mailbox, [conversation]);

        // Assert
        var reply = mailbox.Appended[0];

        Assert.Equal([WatchedMailbox.Address], reply.From);
        Assert.Equal([conversation.Correspondent.Address], reply.To);
        Assert.Null(reply.Sender);
        Assert.Empty(reply.ReplyTo);
    }

    [Fact]
    public async Task DeliverAsync_AnAccountThatSubmitsUnderItsOwnIdentity_AddressesTheReplyToThatAccount()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();
        var conversation = Conversation(turns: 2);

        // Act
        await Deliver(transport, mailbox, [conversation], account: Account(SyntheticAuthorIdentity.SendingAccount));

        // Assert
        // Under that identity the inbound half is `From` the sending account, so replying to the invented participant
        // would be replying to somebody the message never appeared to come from.
        Assert.Equal(["throwaway@example.test"], mailbox.Appended[0].To);
    }

    [Fact]
    public async Task DeliverAsync_AMessageThatNeverArrives_EndsItsThreadAndReportsEveryTurnItGaveUpOn()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox(_ => null);
        var conversation = Conversation(turns: 4);

        // Act
        var report = await Deliver(transport, mailbox, [conversation]);

        // Assert
        // Every turn after the failure answers the one that failed, so continuing would build replies on an identifier
        // no mailbox holds — which is the broken threading this mode exists to stop producing.
        Assert.Equal(4, report.Attempted);
        Assert.Equal(0, report.Delivered);
        Assert.Equal(4, report.Failures.Count);
        Assert.Contains("no copy of it reached the mailbox", report.Failures[0].Reason, StringComparison.Ordinal);
        Assert.All(report.Failures.Skip(1), failure =>
            Assert.Contains("not attempted", failure.Reason, StringComparison.Ordinal));

        Assert.Empty(mailbox.Appended);
    }

    [Fact]
    public async Task DeliverAsync_AMessageTheServerRefuses_EndsItsThreadAndCarriesOnWithTheNext()
    {
        // Arrange
        var refused = Conversation(turns: 2);
        var accepted = Conversation(turns: 2, thread: 1);

        await using var transport = new RecordingSyntheticMailTransport(
            message => message.MessageId == refused.Messages[0].MessageId ? "mailbox full" : null);

        await using var mailbox = new RecordingWatchedMailbox();

        // Act
        var report = await Deliver(transport, mailbox, [refused, accepted]);

        // Assert
        Assert.Equal(4, report.Attempted);
        Assert.Equal(2, report.Delivered);
        Assert.Contains("mailbox full", report.Failures[0].Reason, StringComparison.Ordinal);
        Assert.Single(mailbox.Appended);
    }

    [Fact]
    public async Task DeliverAsync_ANullArgument_IsRefused()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();
        var delivery = new SyntheticConversationDelivery(transport, mailbox, new FakeTimeProvider(Now));

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => delivery.DeliverAsync(
            null!,
            Account(),
            WatchedMailbox,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentNullException>(() => delivery.DeliverAsync(
            [],
            null!,
            WatchedMailbox,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentNullException>(() => delivery.DeliverAsync(
            [],
            Account(),
            null!,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TestContext.Current.CancellationToken));
    }

    private static Task<DeliveryReport> Deliver(
        ISyntheticMailTransport transport,
        IWatchedMailbox mailbox,
        IReadOnlyList<SyntheticConversation> conversations,
        SendingAccount? account = null) =>
        new SyntheticConversationDelivery(transport, mailbox, new FakeTimeProvider(Now)).DeliverAsync(
            conversations,
            account ?? Account(),
            WatchedMailbox,
            TimeSpan.Zero,
            // No wait at all, so the loop looks once and answers. Waiting is what a fake clock cannot be advanced
            // through from the same thread the loop is running on.
            TimeSpan.Zero,
            TestContext.Current.CancellationToken);

    /// <summary>Builds one exchange by hand, so a test states the shape it is asserting rather than drawing it.</summary>
    private static SyntheticConversation Conversation(int turns, int thread = 0)
    {
        var correspondent = new SyntheticParticipant($"Correspondent {thread}", $"correspondent{thread}@example.test");
        var mailbox = new SyntheticParticipant("Developer", WatchedMailbox.Address);

        var messages = Enumerable
            .Range(0, turns)
            .Select(turn => new SyntheticEmail(
                $"thread{thread}.turn{turn}@example.test",
                // The ancestry the generator produced is provisional and delivery replaces it, so it is deliberately
                // wrong here: a test asserting the delivered ancestry must not be able to read it back unchanged.
                turn == 0 ? null : "proposed@example.test",
                turn == 0 ? [] : ["proposed@example.test"],
                SyntheticConversation.SideOf(turn) == SyntheticThreadSide.Correspondent ? correspondent : mailbox,
                [],
                turn == 0 ? "Quarterly figures" : "Re: Quarterly figures",
                Now.AddHours(turn),
                new SyntheticEmailBody(
                    SyntheticBodyShape.PlainTextOnly,
                    "Hello.",
                    "<html><body><p>Hello.</p></body></html>",
                    SyntheticCharacterSet.Utf8,
                    null),
                null,
                null))
            .ToArray();

        return new SyntheticConversation(correspondent, messages);
    }

    private static SendingAccount Account(
        SyntheticAuthorIdentity identity = SyntheticAuthorIdentity.Fabricated) => new(
        "smtp.example.test",
        587,
        MailTransportSecurity.StartTls,
        new MailboxAddress("Throwaway", "throwaway@example.test"),
        "throwaway@example.test",
        "not-a-real-password",
        identity);
}
