// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using System.Runtime.ExceptionServices;
using MailFathom.Application.Mail.Delivery.Transmission;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Mail.MailKit.Delivery;
using MailKit.Net.Smtp;
using MimeKit;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit.Delivery;

/// <summary>Covers the three hooks this client overrides, which are the whole of what it changes about the library's.</summary>
/// <remarks>
/// <para>
/// They are what makes a mistyped address among five leave the other four delivered, so a change that let one of them
/// raise again would undo the guarantee without failing anything else. The library calls them from inside a live
/// protocol exchange and publishes no other way in, so they are invoked directly on a real client that never opens a
/// connection; reaching them through a server would need one that can be scripted to refuse a recipient, which the
/// integration suite's accepts everything it is offered instead.
/// </para>
/// <para>
/// The addresses are synthetic, what is written down is the three digits a server states, and nothing here opens a
/// socket.
/// </para>
/// </remarks>
public sealed class SubmissionSmtpClientTests
{
    private const string Accepted = "anna@example.test";
    private const string Refused = "nobody@example.test";

    /// <summary>An accepted address is written onto the ledger the attempt reads its envelope from.</summary>
    [Fact]
    public void OnRecipientAccepted_ServerTakesTheAddress_RecordsItAsAccepted()
    {
        // Arrange
        using var client = new SubmissionSmtpClient();
        var envelope = new MailEnvelopeLedger();
        client.Envelope = ObserverFor(envelope);

        // Act
        Invoke(
            client,
            "OnRecipientAccepted",
            new MimeMessage(),
            new MailboxAddress(name: null, Accepted),
            new SmtpResponse(SmtpStatusCode.Ok, "accepted"));

        // Assert
        var reply = Assert.Single(envelope.Replies);
        Assert.Equal(Accepted, reply.Address.Address);
        Assert.True(reply.IsAccepted);
    }

    /// <summary>A refused address is settled on the ledger and does not stop the submission, which is the guarantee.</summary>
    [Fact]
    public void OnRecipientNotAccepted_ServerRefusesOneAddress_RecordsItAndDoesNotRaise()
    {
        // Arrange
        using var client = new SubmissionSmtpClient();
        var envelope = new MailEnvelopeLedger();
        client.Envelope = ObserverFor(envelope);

        // Act
        Invoke(
            client,
            "OnRecipientNotAccepted",
            new MimeMessage(),
            new MailboxAddress(name: null, Refused),
            new SmtpResponse(SmtpStatusCode.MailboxUnavailable, "refused"));

        // Assert
        var reply = Assert.Single(envelope.Replies);
        Assert.Equal(Refused, reply.Address.Address);
        Assert.False(reply.IsAccepted);
    }

    /// <summary>An envelope nobody was accepted for has nothing to transmit into, and is the one case that raises.</summary>
    [Fact]
    public void OnNoRecipientsAccepted_ServerRefusedEveryAddress_StopsTheSubmission()
    {
        // Arrange
        using var client = new SubmissionSmtpClient();
        var envelope = new MailEnvelopeLedger();
        client.Envelope = ObserverFor(envelope);

        // Act
        var stopping = () => Invoke(client, "OnNoRecipientsAccepted", new MimeMessage());

        // Assert
        Assert.Throws<SmtpNoRecipientsAcceptedException>(stopping);
    }

    private static SmtpEnvelopeObserver ObserverFor(MailEnvelopeLedger envelope)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, Accepted, out var accepted));
        Assert.True(EmailAddress.TryCreate(displayName: null, Refused, out var refused));

        return new SmtpEnvelopeObserver(
            [
                OutgoingRecipient.Create(accepted, OutgoingRecipientRole.To),
                OutgoingRecipient.Create(refused, OutgoingRecipientRole.To),
            ],
            envelope);
    }

    /// <summary>Calls one of the client's own hooks, reporting what it raised rather than the reflection wrapper.</summary>
    private static void Invoke(SubmissionSmtpClient client, string hook, params object?[] arguments)
    {
        var method = typeof(SubmissionSmtpClient).GetMethod(
            hook,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        Assert.NotNull(method);

        try
        {
            method.Invoke(client, arguments);
        }
        catch (TargetInvocationException wrapper) when (wrapper.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(wrapper.InnerException).Throw();
        }
    }
}
