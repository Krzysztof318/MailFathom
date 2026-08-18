// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery;
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

public sealed class MailOutboxDeliveryTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");
    private static readonly DateTimeOffset ClaimedAt = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

    private static readonly ReadOnlyMemory<byte> RawMime =
        Encoding.ASCII.GetBytes("Message-ID: <one@example.test>\r\n\r\nHello.").AsMemory();

    /// <summary>A server that took the message for everybody it was offered to finishes the send and gives the record back.</summary>
    [Fact]
    public async Task DeliverAsync_ServerAcceptsEveryRecipient_MarksTheSendAsSentAndReleasesTheLease()
    {
        // Arrange
        var context = new DeliveryContext();
        var claimed = await context.ClaimAsync("anna@example.test");
        context.Transmit = (request, envelope, _) =>
        {
            AcceptEveryRecipient(request, envelope);

            return Task.FromResult(new MailTransmission(MailTransmissionOutcome.Accepted, 250));
        };

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.Sent, result.Outcome);
        Assert.Equal(250, result.ReplyCode);
        var record = context.Store.Read(claimed.Record.Id);
        Assert.Equal(OutgoingEmailStage.Sent, record.Stage);
        Assert.Empty(record.OutstandingRecipients);
        Assert.All(record.Recipients, outcome => Assert.Equal(OutgoingRecipientStatus.Accepted, outcome.Status));
        Assert.False(context.Store.IsLeased(claimed.Record.Id));
    }

    /// <summary>A permanent refusal ends the send at the server's first answer, with nothing left to attempt.</summary>
    [Fact]
    public async Task DeliverAsync_ServerRefusesTheMessagePermanently_EndsTheSendWithoutAnotherAttempt()
    {
        // Arrange
        var context = new DeliveryContext();
        var claimed = await context.ClaimAsync("anna@example.test");
        context.Transmit = (_, _, _) =>
            Task.FromResult(new MailTransmission(MailTransmissionOutcome.RefusedPermanently, 550));

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.Refused, result.Outcome);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailRefused, result.Failure);
        var record = context.Store.Read(claimed.Record.Id);
        Assert.Equal(OutgoingEmailStage.Refused, record.Stage);
        Assert.Equal(550, record.LastReplyCode);
        Assert.True(record.IsTerminal);
    }

    /// <summary>A temporary refusal leaves the send claimable again, and the delay it waits is the backoff rather than nothing.</summary>
    [Fact]
    public async Task DeliverAsync_ServerRefusesTheMessageForNow_GivesTheRecordBackForALaterAttempt()
    {
        // Arrange
        var context = new DeliveryContext();
        var claimed = await context.ClaimAsync("anna@example.test");
        context.Transmit = (_, _, _) =>
            Task.FromResult(new MailTransmission(MailTransmissionOutcome.RefusedTemporarily, 451));

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.Deferred, result.Outcome);
        Assert.Equal(MailFathomErrorCode.MailDeliveryUnavailable, result.Failure);
        var record = context.Store.Read(claimed.Record.Id);
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
        Assert.Equal(MailFathomErrorCode.MailDeliveryUnavailable, record.LastFailure);
        Assert.True(context.Store.ReadAvailableAt(claimed.Record.Id) > ClaimedAt);
        Assert.False(context.Store.IsLeased(claimed.Record.Id));
    }

    /// <summary>The retry bound is read from the attempt the claim counted, so a send that always fails ends visibly.</summary>
    [Fact]
    public async Task DeliverAsync_LastAllowedAttemptFailsTransiently_EndsTheSendAsExhausted()
    {
        // Arrange
        var context = new DeliveryContext(maxAttempts: 1);
        var claimed = await context.ClaimAsync("anna@example.test");
        context.Transmit = (_, _, _) =>
            Task.FromResult(new MailTransmission(MailTransmissionOutcome.RefusedTemporarily, 451));

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.Refused, result.Outcome);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailAttemptsExhausted, result.Failure);
        var record = context.Store.Read(claimed.Record.Id);
        Assert.Equal(OutgoingEmailStage.Refused, record.Stage);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailAttemptsExhausted, record.LastFailure);
    }

    /// <summary>
    /// One address refused for now does not fail the message: the addresses the server took are settled, and the next
    /// attempt is offered only the one that is still owed it.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_OneRecipientRefusedForNow_SettlesTheOthersAndRetriesOnlyThatAddress()
    {
        // Arrange
        var context = new DeliveryContext();
        var claimed = await context.ClaimAsync("anna@example.test", "over-quota@example.test");
        context.Transmit = (request, envelope, _) =>
        {
            foreach (var recipient in request.Recipients)
            {
                var deferred = recipient.Address.Address.StartsWith("over-quota", StringComparison.Ordinal);
                envelope.Record(new MailRecipientReply(
                    recipient.Address,
                    deferred ? 452 : 250,
                    deferred ? MailRecipientAcceptance.RefusedTemporarily : MailRecipientAcceptance.Accepted));
            }

            return Task.FromResult(new MailTransmission(MailTransmissionOutcome.Accepted, 250));
        };

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.Deferred, result.Outcome);
        Assert.Null(result.Failure);
        var record = context.Store.Read(claimed.Record.Id);
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
        Assert.Equal(
            "over-quota@example.test",
            Assert.Single(record.OutstandingRecipients).Address.Address);

        // Act: the attempt that follows offers the message only to the address that is still owed it.
        var retried = await context.ClaimAsync(claimed.Record.Id);
        await context.DeliverAsync(retried, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["anna@example.test", "over-quota@example.test"],
            context.Session.Transmitted[0].Recipients.Select(recipient => recipient.Address.Address));
        Assert.Equal(
            "over-quota@example.test",
            Assert.Single(context.Session.Transmitted[1].Recipients).Address.Address);
    }

    /// <summary>An address the server refused outright is settled as refused, and the message still reaches the others.</summary>
    [Fact]
    public async Task DeliverAsync_OneRecipientRefusedOutright_RecordsThatAddressAndSendsToTheRest()
    {
        // Arrange
        var context = new DeliveryContext();
        var claimed = await context.ClaimAsync("anna@example.test", "nobody@example.test");
        context.Transmit = (request, envelope, _) =>
        {
            foreach (var recipient in request.Recipients)
            {
                var unknown = recipient.Address.Address.StartsWith("nobody", StringComparison.Ordinal);
                envelope.Record(new MailRecipientReply(
                    recipient.Address,
                    unknown ? 550 : 250,
                    unknown ? MailRecipientAcceptance.RefusedPermanently : MailRecipientAcceptance.Accepted));
            }

            return Task.FromResult(new MailTransmission(MailTransmissionOutcome.Accepted, 250));
        };

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.Sent, result.Outcome);
        var record = context.Store.Read(claimed.Record.Id);
        Assert.Equal(OutgoingEmailStage.Sent, record.Stage);
        Assert.Empty(record.OutstandingRecipients);
        var refused = record.Recipients.Single(outcome =>
            outcome.Recipient.Address.Address == "nobody@example.test");
        Assert.Equal(OutgoingRecipientStatus.Refused, refused.Status);
        Assert.Equal(550, refused.LastReplyCode);
    }

    /// <summary>A message every one of whose addresses was refused is terminal rather than pending forever.</summary>
    [Fact]
    public async Task DeliverAsync_EveryRecipientRefused_EndsTheSend()
    {
        // Arrange
        var context = new DeliveryContext();
        var claimed = await context.ClaimAsync("nobody@example.test");
        context.Transmit = (request, envelope, _) =>
        {
            foreach (var recipient in request.Recipients)
            {
                envelope.Record(new MailRecipientReply(
                    recipient.Address,
                    550,
                    MailRecipientAcceptance.RefusedPermanently));
            }

            return Task.FromResult(new MailTransmission(MailTransmissionOutcome.RefusedPermanently, 554));
        };

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.Refused, result.Outcome);
        var record = context.Store.Read(claimed.Record.Id);
        Assert.Equal(OutgoingEmailStage.Refused, record.Stage);
        Assert.Empty(record.OutstandingRecipients);
        Assert.Equal(OutgoingRecipientStatus.Refused, Assert.Single(record.Recipients).Status);
    }

    /// <summary>
    /// A submission that failed before any address was accepted transmitted nothing, so the record is taken back to
    /// where it was and offered again.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_FailsBeforeAnyRecipientWasAccepted_RewindsTheRecordForAnotherAttempt()
    {
        // Arrange
        var context = new DeliveryContext();
        var claimed = await context.ClaimAsync("anna@example.test");
        context.Transmit = (_, _, _) => throw new SocketException((int)SocketError.ConnectionReset);

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.Deferred, result.Outcome);
        var record = context.Store.Read(claimed.Record.Id);
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailDeliveryFailedUnexpectedly, record.LastFailure);
        Assert.All(record.Recipients, outcome => Assert.True(outcome.IsOutstanding));
    }

    /// <summary>
    /// A submission that failed after an address was accepted may already have put the message in a mailbox, so the
    /// record stays where it is and says the outcome is unknown.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_FailsAfterAnAddressWasAccepted_LeavesTheOutcomeUnknownAndOffersNothingAgain()
    {
        // Arrange
        var context = new DeliveryContext();
        var claimed = await context.ClaimAsync("anna@example.test");
        context.Transmit = (request, envelope, _) =>
        {
            AcceptEveryRecipient(request, envelope);

            throw new IOException("The connection ended while the body was going out.");
        };

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.OutcomeUnknown, result.Outcome);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailOutcomeUnknown, result.Failure);
        var record = context.Store.Read(claimed.Record.Id);
        Assert.Equal(OutgoingEmailStage.TransmissionBegun, record.Stage);
        Assert.True(record.HasUnknownOutcome);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailOutcomeUnknown, record.LastFailure);

        // Assert: an accepted address whose transmission was never acknowledged is not recorded as delivered.
        Assert.All(record.Recipients, outcome => Assert.True(outcome.IsOutstanding));

        // Assert: and the record is not handed out again, whatever its lease says.
        var reclaimed = await context.Store.ClaimAsync(
            OutgoingEmailClaimRequest.Create(Account, batchSize: 10, TimeSpan.FromMinutes(10)),
            CancellationToken.None);
        Assert.Empty(reclaimed);
    }

    /// <summary>A host that stopped before anything was transmitted costs the send nothing, not even its attempt.</summary>
    [Fact]
    public async Task DeliverAsync_HostStopsBeforeAnythingWasTransmitted_GivesTheAttemptBack()
    {
        // Arrange
        var context = new DeliveryContext();
        var claimed = await context.ClaimAsync("anna@example.test");
        using var stopping = new CancellationTokenSource();
        context.Transmit = async (_, _, token) =>
        {
            await stopping.CancelAsync();
            token.ThrowIfCancellationRequested();

            return new MailTransmission(MailTransmissionOutcome.Accepted, 250);
        };

        // Act
        var result = await context.DeliverAsync(claimed, stopping.Token);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.ReleasedForShutdown, result.Outcome);
        var record = context.Store.Read(claimed.Record.Id);
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
        Assert.Equal(0, record.AttemptCount);
        Assert.False(context.Store.IsLeased(claimed.Record.Id));
    }

    /// <summary>A host that stopped after an address was accepted is the unknown window, not a free release.</summary>
    [Fact]
    public async Task DeliverAsync_HostStopsAfterAnAddressWasAccepted_LeavesTheOutcomeUnknown()
    {
        // Arrange
        var context = new DeliveryContext();
        var claimed = await context.ClaimAsync("anna@example.test");
        using var stopping = new CancellationTokenSource();
        context.Transmit = async (request, envelope, token) =>
        {
            AcceptEveryRecipient(request, envelope);
            await stopping.CancelAsync();
            token.ThrowIfCancellationRequested();

            return new MailTransmission(MailTransmissionOutcome.Accepted, 250);
        };

        // Act
        var result = await context.DeliverAsync(claimed, stopping.Token);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.OutcomeUnknown, result.Outcome);
        Assert.Equal(
            OutgoingEmailStage.TransmissionBegun,
            context.Store.Read(claimed.Record.Id).Stage);
    }

    /// <summary>
    /// A lease that moved on while the attempt was transmitting means somebody else's answer counts, so this attempt
    /// reports it and writes nothing.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_LeaseMovesOnDuringTheTransmission_WritesNothingAndReportsIt()
    {
        // Arrange
        var context = new DeliveryContext();
        var claimed = await context.ClaimAsync("anna@example.test");
        context.Transmit = (request, envelope, _) =>
        {
            AcceptEveryRecipient(request, envelope);
            context.Store.Reassign(claimed.Record.Id);

            return Task.FromResult(new MailTransmission(MailTransmissionOutcome.Accepted, 250));
        };

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.LeaseLost, result.Outcome);
        var record = context.Store.Read(claimed.Record.Id);
        Assert.Equal(OutgoingEmailStage.TransmissionBegun, record.Stage);
        Assert.Null(record.LastFailure);
    }

    /// <summary>
    /// One claim stamps a whole batch with one expiry, so a send far enough down a slow batch is reached after its own
    /// lease ran out. It is reported without a connection being opened for it.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_LeaseHadAlreadyRunOut_ReportsItWithoutOfferingAnything()
    {
        // Arrange
        var context = new DeliveryContext();
        var claimed = await context.ClaimAsync("anna@example.test");
        context.Advance(claimed.Lease.ExpiresAt - ClaimedAt);

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.LeaseLost, result.Outcome);
        Assert.Empty(context.Session.Transmitted);
        Assert.Equal(OutgoingEmailStage.Recorded, context.Store.Read(claimed.Record.Id).Stage);
    }

    /// <summary>An account with no address to send from has nothing a later attempt could repair.</summary>
    [Fact]
    public async Task DeliverAsync_AccountConfiguresNoSendingAddress_EndsTheSend()
    {
        // Arrange
        var context = new DeliveryContext(sender: null);
        var claimed = await context.ClaimAsync("anna@example.test");

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.Refused, result.Outcome);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailSenderUnconfigured, result.Failure);
        Assert.Equal(OutgoingEmailStage.Refused, context.Store.Read(claimed.Record.Id).Stage);
        Assert.Empty(context.Session.Transmitted);
    }

    /// <summary>
    /// A message beyond what the server declared it will accept is refused before the body crosses the network, and
    /// the answer will not change while the server advertises what it advertises.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_MessageExceedsTheServersDeclaredSize_EndsTheSendBeforeTransmitting()
    {
        // Arrange
        var context = new DeliveryContext(
            capabilities: new MailDeliveryCapabilities(MaxMessageBytes: 1, true, true));

        var claimed = await context.ClaimAsync("anna@example.test");

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.Refused, result.Outcome);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailBoundExceeded, result.Failure);
        Assert.Empty(context.Session.Transmitted);
        Assert.Equal(OutgoingEmailStage.Refused, context.Store.Read(claimed.Record.Id).Stage);
    }

    /// <summary>A record whose message is not there describes a send that can never happen rather than one still on its way.</summary>
    [Fact]
    public async Task DeliverAsync_StoredMessageIsMissing_EndsTheSend()
    {
        // Arrange
        var context = new DeliveryContext(storeContent: false);
        var claimed = await context.ClaimAsync("anna@example.test");

        // Act
        var result = await context.DeliverAsync(claimed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailOutboxDeliveryOutcome.Refused, result.Outcome);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailDeliveryFailedUnexpectedly, result.Failure);
        Assert.Empty(context.Session.Transmitted);
    }

    private static void AcceptEveryRecipient(MailTransmissionRequest request, MailEnvelopeLedger envelope)
    {
        foreach (var recipient in request.Recipients)
        {
            envelope.Record(new MailRecipientReply(recipient.Address, 250, MailRecipientAcceptance.Accepted));
        }
    }

    /// <summary>Assembles one attempt over an in-memory outbox, with the exchange the test writes.</summary>
    private sealed class DeliveryContext
    {
        private readonly FakeTimeProvider clock = new(ClaimedAt);
        private readonly MailOutboxDelivery delivery;

        internal DeliveryContext(
            int maxAttempts = 5,
            MailDeliveryCapabilities? capabilities = null,
            bool storeContent = true,
            string? sender = "me@example.test")
        {
            this.Store = new InMemoryOutgoingEmailStore(timeProvider: this.clock);
            this.Session = new ScriptedMailDeliverySession(
                (request, envelope, token) => this.Transmit(request, envelope, token),
                capabilities);

            var contentStore = Substitute.For<IEmailContentStore>();
            contentStore
                .FindOutgoingContentAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>())
                .Returns(storeContent
                    ? new StoredEmailContent(RawMime, RawMime.Length, SHA256.HashData(RawMime.Span))
                    : null);

            var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
            senderIdentities.FindSenderIdentity(Account).Returns(sender is null ? null : SenderIdentity(sender));

            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                var session = Substitute.For<IPersistenceSession>();
                session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

                return session;
            });

            this.delivery = new MailOutboxDelivery(
                this.Session,
                this.Store,
                contentStore,
                senderIdentities,
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), this.clock),
                MailOutboxSettings.Create(
                    maxDeliveriesPerPass: 10,
                    TimeSpan.FromMinutes(10),
                    TimeSpan.FromMinutes(7),
                    maxAttempts,
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromHours(1)),
                this.clock);
        }

        internal InMemoryOutgoingEmailStore Store { get; }

        internal ScriptedMailDeliverySession Session { get; }

        /// <summary>Gets or sets the exchange with the submission server, which every test writes for itself.</summary>
        internal Func<MailTransmissionRequest, MailEnvelopeLedger, CancellationToken, Task<MailTransmission>> Transmit
        {
            get;
            set;
        } = (_, _, _) => Task.FromResult(new MailTransmission(MailTransmissionOutcome.Accepted, 250));

        internal async Task<ClaimedOutgoingEmail> ClaimAsync(params string[] recipientAddresses)
        {
            this.Store.Publish(RequestFor(recipientAddresses), RawMime.Length);

            var claimed = await this.Store.ClaimAsync(
                OutgoingEmailClaimRequest.Create(Account, batchSize: 10, TimeSpan.FromMinutes(10)),
                CancellationToken.None);

            return claimed.Single();
        }

        internal async Task<ClaimedOutgoingEmail> ClaimAsync(OutgoingEmailId outgoingEmailId)
        {
            // The backoff the previous attempt wrote is in the future, so the clock is moved past it rather than the
            // record being made claimable some other way.
            this.clock.SetUtcNow(this.Store.ReadAvailableAt(outgoingEmailId).AddSeconds(1));

            var claimed = await this.Store.ClaimAsync(
                OutgoingEmailClaimRequest.Create(Account, batchSize: 10, TimeSpan.FromMinutes(10)),
                CancellationToken.None);

            return claimed.Single(entry => entry.Record.Id == outgoingEmailId);
        }

        /// <summary>Moves the clock on, which is how a lease is made to run out before an attempt reaches its send.</summary>
        internal void Advance(TimeSpan elapsed) => this.clock.Advance(elapsed);

        internal Task<MailOutboxDeliveryResult> DeliverAsync(
            ClaimedOutgoingEmail claimed,
            CancellationToken stoppingToken) =>
            this.delivery.DeliverAsync(claimed, TransportSecurityPolicy(), stoppingToken);

        private static OutgoingSenderIdentity SenderIdentity(string address)
        {
            Assert.True(EmailAddress.TryCreate(displayName: null, address, out var sender));

            return OutgoingSenderIdentity.Create(Account, sender);
        }

        private static OutgoingEmailRequest RequestFor(IReadOnlyList<string> recipientAddresses) =>
            OutgoingEmailRequest.Create(
                Account,
                OutgoingEmailRequester.Command($"mfctl-{Guid.CreateVersion7()}"),
                [.. recipientAddresses.Select(address =>
                {
                    Assert.True(EmailAddress.TryCreate(displayName: null, address, out var recipient));

                    return OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To);
                })]);

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
