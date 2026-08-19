// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Domain.Delivery;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes what became of one person the caller addressed its message to.</summary>
/// <remarks>
/// The address is published because the caller supplied it. That is the whole rule this result follows: a caller
/// reading back its own send learns what happened to the people it named and nothing it did not already know, so an
/// address a contact was resolved to is here where the caller wrote it and the contact behind one is not.
/// </remarks>
internal sealed record OutgoingEmailRecipientResult
{
    /// <summary>Gets the address the message is offered to.</summary>
    [Description("The mail address, exactly as this message is addressed to it.")]
    public required string Address { get; init; }

    /// <summary>Gets the header the message names this recipient in.</summary>
    [Description("Which header of the message names this person.")]
    public required OutgoingEmailRecipientHeader Header { get; init; }

    /// <summary>Gets what a mail server has said about this recipient.</summary>
    [Description("What a mail server has said about this address so far.")]
    public required OutgoingEmailRecipientState State { get; init; }

    /// <summary>Gets the reply code a server last answered about this recipient with.</summary>
    [Description("The three-digit SMTP reply code a mail server last answered about this address with, or absent while none has answered about it.")]
    public int? LastReplyCode { get; init; }

    /// <summary>Publishes what one stored outcome says about one recipient.</summary>
    /// <param name="outcome">The stored outcome.</param>
    /// <returns>The wire representation of <paramref name="outcome" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="outcome" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the outcome carries a role or a status this surface does not publish.</exception>
    public static OutgoingEmailRecipientResult From(OutgoingRecipientOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new OutgoingEmailRecipientResult
        {
            Address = outcome.Recipient.Address.Address,
            Header = Published(outcome.Recipient.Role),
            State = Published(outcome.Status),
            LastReplyCode = outcome.LastReplyCode,
        };
    }

    /// <summary>Reads the published header one stored role is reported under.</summary>
    /// <remarks>Written out rather than cast, so a role added to the domain has to be given a published spelling before it can reach a client.</remarks>
    private static OutgoingEmailRecipientHeader Published(OutgoingRecipientRole role) => role switch
    {
        OutgoingRecipientRole.To => OutgoingEmailRecipientHeader.To,
        OutgoingRecipientRole.Cc => OutgoingEmailRecipientHeader.Cc,
        OutgoingRecipientRole.Bcc => OutgoingEmailRecipientHeader.Bcc,
        _ => throw new ArgumentOutOfRangeException(
            nameof(role),
            role,
            "The outgoing recipient role is not one this surface publishes."),
    };

    /// <summary>Reads the published state one stored recipient status is reported under.</summary>
    /// <remarks>Written out for the reason the header is, and because a status added without a spelling here would otherwise publish whichever name sat at the same ordinal.</remarks>
    private static OutgoingEmailRecipientState Published(OutgoingRecipientStatus status) => status switch
    {
        OutgoingRecipientStatus.Pending => OutgoingEmailRecipientState.Pending,
        OutgoingRecipientStatus.Accepted => OutgoingEmailRecipientState.Accepted,
        OutgoingRecipientStatus.Refused => OutgoingEmailRecipientState.Refused,
        _ => throw new ArgumentOutOfRangeException(
            nameof(status),
            status,
            "The outgoing recipient status is not one this surface publishes."),
    };
}
