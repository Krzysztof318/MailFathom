// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery;

/// <summary>States what a submission server said it will accept, as facts rather than as advertised extension names.</summary>
/// <param name="MaxMessageBytes">
/// The largest message the server declared it will accept, or <see langword="null" /> when it declared no bound. A
/// server that advertises the size extension without a number is stating that it has no fixed maximum, which is the
/// same absence.
/// </param>
/// <param name="AcceptsEightBitContent">Whether the server accepts a body whose bytes are not restricted to seven bits.</param>
/// <param name="AcceptsInternationalizedAddresses">Whether the server accepts an envelope address outside ASCII.</param>
/// <remarks>
/// <para>
/// These three decide whether a message can be sent at all rather than how it is sent, which is why they are the ones
/// a session reports. A message beyond the declared size is refused after its whole body has crossed the network; an
/// internationalized address the server cannot carry is refused outright; and a body the server cannot take
/// unencoded has to be encoded before it is transmitted. A caller left to guess at any of the three pays for the guess
/// in a transmission that was never going to be accepted.
/// </para>
/// <para>
/// Everything else a server advertises is absent on purpose. Pipelining, chunking, and delivery status notifications
/// change how a submission is carried out and never whether it may happen, so they belong to whatever issues the
/// commands rather than to a contract every caller above reads.
/// </para>
/// </remarks>
public sealed record MailDeliveryCapabilities(
    long? MaxMessageBytes,
    bool AcceptsEightBitContent,
    bool AcceptsInternationalizedAddresses)
{
    /// <summary>What a message is composed against when it is written down long before a server is opened to carry it.</summary>
    /// <remarks>
    /// <para>
    /// A send is recorded by one act and transmitted by another, so the message exists before any submission server has
    /// been asked anything. This is what the composition is held to in that gap, and each of the three values is the
    /// answer that stays correct whatever the server turns out to say.
    /// </para>
    /// <para>
    /// No size is declared, because the deployment's own bound is checked at composition regardless and the server's is
    /// checked against the stored length before the message is offered — so assuming a number here would refuse a
    /// message a server would have taken without protecting one it would not.
    /// </para>
    /// <para>
    /// Eight-bit content is not assumed, so the message is transfer-encoded to seven bits. That is accepted by every
    /// submission server, including the ones that would have taken it unencoded, and costs a fraction of the size;
    /// composing eight-bit and meeting a server that takes none would be a message that can never be delivered.
    /// </para>
    /// <para>
    /// Internationalized addresses are not assumed either, and that one is a refusal rather than a cost: an address
    /// outside ASCII is refused while the caller is still there to be told, instead of being queued and failing against
    /// the server hours later with nobody to answer for it.
    /// </para>
    /// </remarks>
    public static MailDeliveryCapabilities BeforeAnyServerHasSpoken { get; } = new(
        MaxMessageBytes: null,
        AcceptsEightBitContent: false,
        AcceptsInternationalizedAddresses: false);

    /// <summary>Reports whether a message of the supplied size is within what the server declared it will accept.</summary>
    /// <param name="messageBytes">The size of the message as it would be transmitted.</param>
    /// <returns><see langword="true" /> when the server declared no bound or the message is within it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="messageBytes" /> is negative.</exception>
    /// <remarks>
    /// The comparison is inclusive, because the size extension states the largest message that is accepted rather than
    /// the first one that is not. A server declaring no bound permits every size here and may still refuse a message
    /// for a reason of its own; this answers what the server said, not what it will do.
    /// </remarks>
    public bool PermitsMessageOfSize(long messageBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(messageBytes);

        return this.MaxMessageBytes is not { } declaredMaximum || messageBytes <= declaredMaximum;
    }
}
