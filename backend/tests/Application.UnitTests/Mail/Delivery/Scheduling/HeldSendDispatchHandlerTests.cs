// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Scheduling;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Scheduling;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Scheduling;

/// <summary>Covers the short job that says a held message is now due, and what it deliberately does not say.</summary>
public sealed class HeldSendDispatchHandlerTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private static readonly DateTimeOffset Authored = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The job this handler answers is the one the outbox queues for a held send.</summary>
    [Fact]
    public void JobType_IsTheOneTheOutboxQueuesAHeldSendUnder()
    {
        // Arrange
        var handler = new HeldSendDispatchHandler(new InMemoryOutgoingEmailStore(), new MailOutboxSignal(capacity: 4));

        // Act, Assert
        Assert.Equal(JobType.DispatchHeldSend, handler.JobType);
    }

    /// <summary>A message still waiting is announced, which is the whole of what the job does.</summary>
    [Fact]
    public async Task RunAsync_AMessageStillWaitingToBeSent_AnnouncesItsAccount()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore(timeProvider: new FakeTimeProvider(Authored));
        var signal = new MailOutboxSignal(capacity: 4);
        var record = store.Publish(HeldRequest("mfctl-4f2a"), mimeByteLength: 64);
        var handler = new HeldSendDispatchHandler(store, signal);

        // Act
        await handler.RunAsync(
            HeldSendJobPayload.For(Account, record.Id),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, signal.Depth);
    }

    /// <summary>Announcing the same held send twice announces one account once, which is what makes the job repeatable.</summary>
    [Fact]
    public async Task RunAsync_TheSamePayloadTwice_AnnouncesTheAccountOnce()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore(timeProvider: new FakeTimeProvider(Authored));
        var signal = new MailOutboxSignal(capacity: 4);
        var record = store.Publish(HeldRequest("mfctl-4f2a"), mimeByteLength: 64);
        var handler = new HeldSendDispatchHandler(store, signal);
        var payload = HeldSendJobPayload.For(Account, record.Id);

        // Act
        await handler.RunAsync(payload, TestContext.Current.CancellationToken);
        await handler.RunAsync(payload, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, signal.Depth);
    }

    /// <summary>A message withdrawn or already sent during the hold announces nothing, so no pass is woken for work nobody is waiting on.</summary>
    [Theory]
    [InlineData(OutgoingEmailStage.Cancelled)]
    [InlineData(OutgoingEmailStage.Sent)]
    [InlineData(OutgoingEmailStage.Refused)]
    public async Task RunAsync_AMessageThatHasAlreadyEnded_AnnouncesNothing(OutgoingEmailStage stage)
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore(timeProvider: new FakeTimeProvider(Authored));
        var signal = new MailOutboxSignal(capacity: 4);
        var record = store.Publish(HeldRequest("mfctl-4f2a"), mimeByteLength: 64);
        store.Arrange(record.Id, stage);
        var handler = new HeldSendDispatchHandler(store, signal);

        // Act
        await handler.RunAsync(
            HeldSendJobPayload.For(Account, record.Id),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, signal.Depth);
    }

    /// <summary>A record the outbox no longer holds announces nothing rather than raising, because the job cannot repair it.</summary>
    [Fact]
    public async Task RunAsync_ARecordTheOutboxNoLongerHolds_AnnouncesNothing()
    {
        // Arrange
        var signal = new MailOutboxSignal(capacity: 4);
        var handler = new HeldSendDispatchHandler(new InMemoryOutgoingEmailStore(), signal);

        // Act
        await handler.RunAsync(
            HeldSendJobPayload.For(Account, OutgoingEmailId.Create(Guid.CreateVersion7())),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, signal.Depth);
    }

    /// <summary>A payload of another job's contract is a defect in what enqueued it rather than a send to guess at.</summary>
    [Fact]
    public async Task RunAsync_APayloadOfAnotherContract_IsRefused()
    {
        // Arrange
        var handler = new HeldSendDispatchHandler(new InMemoryOutgoingEmailStore(), new MailOutboxSignal(capacity: 4));

        // Act
        var thrown = await Assert.ThrowsAsync<ArgumentException>(
            () => handler.RunAsync(RunScheduledMailRulesJobPayload.For(Account), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("payload", thrown.ParamName);
    }

    private static OutgoingEmailRequest HeldRequest(string invocationIdentity)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address));

        return OutgoingEmailRequest
            .Create(
                Account,
                OutgoingEmailRequester.Command(invocationIdentity),
                [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)])
            .HeldUntil(ZonedInstant.At(Authored.AddHours(9)));
    }
}
