// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Domain.Delivery;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes what became of one message the caller queued.</summary>
/// <remarks>
/// <para>
/// It is what makes <c>queued</c> an acceptable answer from a sending tool. A caller holding an identifier and no way
/// to learn the outcome does the worst thing available to it and sends again, so this is the other half of the contract
/// rather than an addition to it. Both the read and the withdrawal answer with it, because both answer the same
/// question — where this send stands now.
/// </para>
/// <para>
/// What it carries is bounded by what the caller supplied. The recipients are the addresses that caller wrote down and
/// the account is the one it named; there is no subject, no body, no raw MIME, no <c>Message-ID</c>, no folder the copy
/// was filed into, and no address the caller did not already know. A read of a send is not a read of the message.
/// </para>
/// </remarks>
[Description("Where one queued message stands: how far it has got, what has been said about each person it is addressed to, and why it stopped if it did.")]
internal sealed record OutgoingEmailToolResult
{
    /// <summary>Gets the stable identity of the record.</summary>
    [Description("The stable identifier of the message, the same value the sending tool answered with.")]
    public required string OutgoingEmailId { get; init; }

    /// <summary>Gets the account the message is sent as.</summary>
    [Description("The configured MailFathom account identifier the message is sent as.")]
    public required string AccountId { get; init; }

    /// <summary>Gets how far the record has durably got.</summary>
    [Description("How far this message has got.")]
    public required SendEmailState State { get; init; }

    /// <summary>Gets how many delivery attempts have been counted against the record.</summary>
    [Description("How many delivery attempts have been made for this message. It is counted before each attempt rather than after it, so a message being attempted right now already shows that attempt.")]
    public required int AttemptCount { get; init; }

    /// <summary>Gets when the send was first written down.</summary>
    [Description("When the send was first written down, as an ISO 8601 timestamp.")]
    public required DateTimeOffset QueuedAt { get; init; }

    /// <summary>Gets what has been said about each person the message is addressed to.</summary>
    [Description("One entry per person the message is addressed to, in the order the headers name them. These are the addresses the send named and no others.")]
    public required IReadOnlyList<OutgoingEmailRecipientResult> Recipients { get; init; }

    /// <summary>Gets the code of the failure the last attempt ended in.</summary>
    /// <remarks>
    /// The code is published and the message is not, which is the record's own rule rather than this boundary's: a code
    /// is a stable identity an operator can look up, and a failure message is text assembled at the failure site that
    /// may repeat what a remote server wrote.
    /// </remarks>
    [Description("The five-digit MailFathom error code of the failure the last delivery attempt ended in, or absent while no attempt has failed. It is the same code a failed call reports, and it can be present on a message that later succeeds.")]
    public string? FailureCode { get; init; }

    /// <summary>Publishes the record as it stands.</summary>
    /// <param name="record">The durable record.</param>
    /// <returns>The wire representation of <paramref name="record" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the record carries a stage, a role, or a recipient status this surface does not publish.</exception>
    public static OutgoingEmailToolResult From(OutgoingEmailRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new OutgoingEmailToolResult
        {
            OutgoingEmailId = record.Id.ToString(),
            AccountId = record.AccountId.Value,
            State = SendEmailStateMapping.Published(record.Stage),
            AttemptCount = record.AttemptCount,
            QueuedAt = record.RecordedAt,
            Recipients = [.. record.Recipients.Select(OutgoingEmailRecipientResult.From)],
            FailureCode = record.LastFailure?.ToString(),
        };
    }
}
