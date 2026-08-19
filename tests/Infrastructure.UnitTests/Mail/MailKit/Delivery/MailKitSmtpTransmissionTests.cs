// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Sockets;
using System.Text;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Transmission;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Mail.MailKit.Delivery;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit.Net.Smtp;
using MimeKit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit.Delivery;

/// <summary>Covers the one act that leaves the deployment: offering the envelope and transmitting the message.</summary>
/// <remarks>
/// The submission is never repeated inside one attempt, so what these assert is that each way a server can answer
/// produces the outcome the caller settles on — and, for the answers that are no answer at all, that the envelope the
/// exchange had already collected is what decides.
/// </remarks>
public sealed class MailKitSmtpTransmissionTests
{
    private static readonly ReadOnlyMemory<byte> RawMime = Encoding.ASCII.GetBytes(
        "Message-ID: <one@example.test>\r\nFrom: me@example.test\r\nTo: anna@example.test\r\n"
        + "Subject: A note\r\n\r\nHello.").AsMemory();

    /// <summary>A server that took the message answers the attempt, and nothing it wrote is kept.</summary>
    [Fact]
    public async Task TransmitAsync_ServerTakesTheMessage_ReportsItAccepted()
    {
        // Arrange
        using var context = new TransmissionContext();
        context.Client
            .SendAsync(
                Arg.Any<FormatOptions>(),
                Arg.Any<MimeMessage>(),
                Arg.Any<MailboxAddress>(),
                Arg.Any<IEnumerable<MailboxAddress>>(),
                Arg.Any<CancellationToken>())
            .Returns("2.0.0 Ok: queued as 4bXvR");

        // Act
        var transmission = await context.TransmitAsync();

        // Assert
        Assert.Equal(MailTransmissionOutcome.Accepted, transmission.Outcome);
        Assert.Null(transmission.ReplyCode);
    }

    /// <summary>A refusal is reported as the three digits it was, without the words the server wrote beside them.</summary>
    [Theory]
    [InlineData(SmtpStatusCode.MailboxUnavailable, MailTransmissionOutcome.RefusedPermanently)]
    [InlineData(SmtpStatusCode.MailboxBusy, MailTransmissionOutcome.RefusedTemporarily)]
    public async Task TransmitAsync_ServerRefusesTheMessage_ReportsTheDispositionAndTheCode(
        SmtpStatusCode statusCode,
        MailTransmissionOutcome expectedOutcome)
    {
        // Arrange
        using var context = new TransmissionContext();
        context.Client
            .SendAsync(
                Arg.Any<FormatOptions>(),
                Arg.Any<MimeMessage>(),
                Arg.Any<MailboxAddress>(),
                Arg.Any<IEnumerable<MailboxAddress>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new SmtpCommandException(
                SmtpErrorCode.MessageNotAccepted,
                statusCode,
                "anna@example.test is over quota"));

        // Act
        var transmission = await context.TransmitAsync();

        // Assert
        Assert.Equal(expectedOutcome, transmission.Outcome);
        Assert.Equal((int)statusCode, transmission.ReplyCode);
    }

    /// <summary>
    /// A server that accepted nobody transmitted nothing, so the outcome is read from what it said about the
    /// addresses: one address that may work later defers the message rather than ending it.
    /// </summary>
    [Theory]
    [InlineData(SmtpStatusCode.MailboxBusy, MailTransmissionOutcome.RefusedTemporarily)]
    [InlineData(SmtpStatusCode.MailboxUnavailable, MailTransmissionOutcome.RefusedPermanently)]
    public async Task TransmitAsync_NoAddressWasAccepted_ReportsWhatTheEnvelopeSettled(
        SmtpStatusCode recipientStatusCode,
        MailTransmissionOutcome expectedOutcome)
    {
        // Arrange
        using var context = new TransmissionContext();
        context.AnswerEveryRecipientWith(recipientStatusCode);
        context.Client
            .SendAsync(
                Arg.Any<FormatOptions>(),
                Arg.Any<MimeMessage>(),
                Arg.Any<MailboxAddress>(),
                Arg.Any<IEnumerable<MailboxAddress>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new SmtpNoRecipientsAcceptedException());

        // Act
        var transmission = await context.TransmitAsync();

        // Assert
        Assert.Equal(expectedOutcome, transmission.Outcome);
        Assert.Equal(
            (int)recipientStatusCode,
            Assert.Single(context.Envelope.Replies).ReplyCode);
    }

    /// <summary>
    /// A server that answered nothing settles nothing, so the attempt raises rather than reporting an outcome — and
    /// what the envelope had already collected is left for the record to read.
    /// </summary>
    [Fact]
    public async Task TransmitAsync_ServerStopsAnswering_RaisesTheAccountsUnavailabilityAndKeepsTheEnvelope()
    {
        // Arrange
        using var context = new TransmissionContext();
        context.AnswerEveryRecipientWith(SmtpStatusCode.Ok);
        context.Client
            .SendAsync(
                Arg.Any<FormatOptions>(),
                Arg.Any<MimeMessage>(),
                Arg.Any<MailboxAddress>(),
                Arg.Any<IEnumerable<MailboxAddress>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new SocketException((int)SocketError.ConnectionReset));

        // Act
        var thrown = await Assert.ThrowsAsync<MailDeliveryUnavailableException>(context.TransmitAsync);

        // Assert
        Assert.Equal(SmtpDeliveryTestContext.Account, thrown.AccountId);
        Assert.True(context.Envelope.MayHaveReachedRecipients);
    }

    /// <summary>The blind copies of a message are written on the envelope and never onto the wire in a header.</summary>
    [Fact]
    public async Task TransmitAsync_Always_WritesTheMessageWithBlindCopiesHidden()
    {
        // Arrange
        using var context = new TransmissionContext();
        FormatOptions? written = null;
        context.Client
            .SendAsync(
                Arg.Do<FormatOptions>(options => written = options),
                Arg.Any<MimeMessage>(),
                Arg.Any<MailboxAddress>(),
                Arg.Any<IEnumerable<MailboxAddress>>(),
                Arg.Any<CancellationToken>())
            .Returns("2.0.0 Ok");

        // Act
        await context.TransmitAsync();

        // Assert
        Assert.NotNull(written);
        Assert.Contains(HeaderId.Bcc, written.HiddenHeaders);
        Assert.Contains(HeaderId.ResentBcc, written.HiddenHeaders);
        Assert.Equal(NewLineFormat.Dos, written.NewLineFormat);
    }

    /// <summary>A client between submissions reports to nobody, whatever the submission before it ended in.</summary>
    [Fact]
    public async Task TransmitAsync_SubmissionEnded_LeavesTheClientReportingToNobody()
    {
        // Arrange
        using var context = new TransmissionContext();
        context.Client
            .SendAsync(
                Arg.Any<FormatOptions>(),
                Arg.Any<MimeMessage>(),
                Arg.Any<MailboxAddress>(),
                Arg.Any<IEnumerable<MailboxAddress>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new SmtpCommandException(
                SmtpErrorCode.MessageNotAccepted,
                SmtpStatusCode.MailboxUnavailable,
                "no"));

        // Act
        await context.TransmitAsync();

        // Assert
        Assert.Null(context.Client.Envelope);
    }

    /// <summary>The envelope records the address the attempt offered, so a server's spelling of it changes nothing.</summary>
    [Fact]
    public async Task TransmitAsync_ServerEchoesTheAddressDifferently_RecordsTheOfferedOne()
    {
        // Arrange
        using var context = new TransmissionContext();
        context.Client
            .SendAsync(
                Arg.Any<FormatOptions>(),
                Arg.Any<MimeMessage>(),
                Arg.Any<MailboxAddress>(),
                Arg.Any<IEnumerable<MailboxAddress>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                context.Client.Envelope!.RecipientAnswered(
                    new MailboxAddress(name: null, "ANNA@Example.Test"),
                    new SmtpResponse(SmtpStatusCode.Ok, "accepted"));

                return "2.0.0 Ok";
            });

        // Act
        await context.TransmitAsync();

        // Assert
        Assert.Equal("anna@example.test", Assert.Single(context.Envelope.Replies).Address.Address);
    }

    /// <summary>An answer about somebody the attempt never offered is dropped rather than put on the record.</summary>
    [Fact]
    public async Task TransmitAsync_ServerAnswersAboutAnAddressNobodyOffered_DropsIt()
    {
        // Arrange
        using var context = new TransmissionContext();
        context.Client
            .SendAsync(
                Arg.Any<FormatOptions>(),
                Arg.Any<MimeMessage>(),
                Arg.Any<MailboxAddress>(),
                Arg.Any<IEnumerable<MailboxAddress>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                context.Client.Envelope!.RecipientAnswered(
                    new MailboxAddress(name: null, "somebody-else@example.test"),
                    new SmtpResponse(SmtpStatusCode.Ok, "accepted"));

                return "2.0.0 Ok";
            });

        // Act
        await context.TransmitAsync();

        // Assert
        Assert.Empty(context.Envelope.Replies);
    }

    /// <summary>Assembles one established session over a scripted submission server.</summary>
    private sealed class TransmissionContext : IDisposable
    {
        private readonly OutboundResilienceTestHost resilience =
            SmtpDeliveryTestContext.CreateSingleAttemptResilience();

        private readonly ScriptedSubmissionTransport transport = new();

        internal TransmissionContext() => this.Client = SmtpDeliveryTestContext.CreateClient("PLAIN");

        internal ISubmissionClient Client { get; }

        internal MailEnvelopeLedger Envelope { get; } = new();

        /// <summary>Answers every offered address the way a server does before it decides about the body.</summary>
        internal void AnswerEveryRecipientWith(SmtpStatusCode statusCode) =>
            this.Client
                .When(client => client.SendAsync(
                    Arg.Any<FormatOptions>(),
                    Arg.Any<MimeMessage>(),
                    Arg.Any<MailboxAddress>(),
                    Arg.Any<IEnumerable<MailboxAddress>>(),
                    Arg.Any<CancellationToken>()))
                .Do(_ => this.Client.Envelope!.RecipientAnswered(
                    new MailboxAddress(name: null, "anna@example.test"),
                    new SmtpResponse(statusCode, "answered")));

        internal async Task<MailTransmission> TransmitAsync()
        {
            await using var session = await SmtpDeliveryTestContext
                .CreateFactory(this.resilience, this.Client, this.transport)
                .OpenForDeliveryAsync(
                    SmtpDeliveryTestContext.Account,
                    SmtpDeliveryTestContext.TlsOnConnectWithPlainPolicy,
                    TestContext.Current.CancellationToken);

            return await session.TransmitAsync(
                Request(),
                this.Envelope,
                TestContext.Current.CancellationToken);
        }

        public void Dispose()
        {
            this.transport.Dispose();
            this.resilience.Dispose();
            this.Client.Dispose();
        }

        private static MailTransmissionRequest Request()
        {
            Assert.True(EmailAddress.TryCreate(displayName: null, "me@example.test", out var sender));
            Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var recipient));

            return MailTransmissionRequest.Create(
                OutgoingEmailId.Create(Guid.CreateVersion7()),
                sender,
                [OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To)],
                RawMime);
        }
    }
}
