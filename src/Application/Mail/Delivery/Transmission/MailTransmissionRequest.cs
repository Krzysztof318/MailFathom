// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Transmission;

/// <summary>States one submission: who it is from, who is still owed it, and the bytes to transmit.</summary>
/// <remarks>
/// <para>
/// The recipients are the ones a later attempt still offers rather than everybody the message names, which is what
/// makes a partial acceptance recoverable: an address the server already took is absent here, so the person behind it
/// does not receive the message twice.
/// </para>
/// <para>
/// The MIME is the stored bytes and is never recomposed for an attempt. A <c>Message-ID</c> that changed between two
/// attempts would thread as a second message in every recipient's client, so the message a retry transmits is the one
/// the first attempt may already have begun transmitting.
/// </para>
/// </remarks>
public sealed record MailTransmissionRequest
{
    private MailTransmissionRequest(
        EmailAddress sender,
        IReadOnlyList<OutgoingRecipient> recipients,
        ReadOnlyMemory<byte> rawMime)
    {
        this.Sender = sender;
        this.Recipients = recipients;
        this.RawMime = rawMime;
    }

    /// <summary>Gets the address the envelope names as the reverse path.</summary>
    public EmailAddress Sender { get; }

    /// <summary>Gets the people this attempt offers the message to.</summary>
    public IReadOnlyList<OutgoingRecipient> Recipients { get; }

    /// <summary>Gets the composed RFC 822 bytes to transmit.</summary>
    public ReadOnlyMemory<byte> RawMime { get; }

    /// <summary>States a submission to make.</summary>
    /// <param name="sender">The address the envelope names as the reverse path.</param>
    /// <param name="recipients">The people this attempt offers the message to.</param>
    /// <param name="rawMime">The composed RFC 822 bytes to transmit.</param>
    /// <returns>The submission to issue.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recipients" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sender" /> names no mailbox, when <paramref name="recipients" /> is empty, or when <paramref name="rawMime" /> is empty.</exception>
    public static MailTransmissionRequest Create(
        EmailAddress sender,
        IReadOnlyList<OutgoingRecipient> recipients,
        ReadOnlyMemory<byte> rawMime)
    {
        ArgumentNullException.ThrowIfNull(recipients);

        if (string.IsNullOrEmpty(sender.Address))
        {
            throw new ArgumentException("A submission names the mailbox it is sent from.", nameof(sender));
        }

        if (recipients.Count == 0)
        {
            // A message nobody is still owed is a record that should have reached a terminal stage instead of an
            // envelope with no RCPT TO in it, so it is refused here rather than offered to a server.
            throw new ArgumentException("A submission offers the message to at least one recipient.", nameof(recipients));
        }

        if (rawMime.IsEmpty)
        {
            throw new ArgumentException("A submission transmits the stored MIME of the message.", nameof(rawMime));
        }

        return new MailTransmissionRequest(sender, [.. recipients], rawMime);
    }
}
