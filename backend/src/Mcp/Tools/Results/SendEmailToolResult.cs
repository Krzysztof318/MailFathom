// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Domain.Delivery;
using MailFathom.Mcp.Tools.Outgoing;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes what a request to send a message was written down as, which is never a delivery.</summary>
/// <remarks>
/// <para>
/// It answers with a record rather than with an outcome, because at the moment it is produced no SMTP command has gone
/// out. Saying so in the state's own spelling is what stops a model from reporting that the mail arrived: a caller told
/// <c>queued</c> looks for the record again, and a caller told <c>recorded</c> tells somebody their message was sent.
/// </para>
/// <para>
/// Nothing about the message appears. No address, no subject, no body, no <c>Message-ID</c>, and no MIME — the identity
/// and the account are MailFathom's own names for things, and the recipient count says how many people the message was
/// addressed to without saying who any of them are. A caller that wants any of the rest already holds what it sent.
/// </para>
/// </remarks>
[Description("What the send was written down as: one durable record, queued for the delivery pass that will offer it to a mail server. Nothing has been transmitted at the moment this is returned.")]
internal sealed record SendEmailToolResult
{
    /// <summary>Gets the stable identity of the record the message was written down as.</summary>
    [Description("The stable identifier of the queued message. It is what this send is known by afterwards, and an identical call carrying the same idempotencyKey answers with this same identifier rather than queueing a second message.")]
    public required string OutgoingEmailId { get; init; }

    /// <summary>Gets the account the message is sent as.</summary>
    [Description("The configured MailFathom account identifier the message is sent as. Its Delivery configuration decides the From address, which a caller never supplies.")]
    public required string AccountId { get; init; }

    /// <summary>Gets how far the record has durably got.</summary>
    [Description("How far this message has got. A fresh send is queued, meaning it is written down and has not been transmitted. A repeated call carrying an idempotencyKey already used answers with whatever the first message has reached since.")]
    public required SendEmailState State { get; init; }

    /// <summary>Gets how many people the message is addressed to.</summary>
    [Description("How many people the message will be offered to across its to, cc, and bcc headers, after addresses named twice were reduced to one. Nobody is named.")]
    public required int RecipientCount { get; init; }

    /// <summary>Gets when the send was first written down.</summary>
    [Description("When the send was first written down, as an ISO 8601 timestamp. For a repeated call it is when the first identical call wrote the record, not when this one was made.")]
    public required DateTimeOffset QueuedAt { get; init; }

    /// <summary>Publishes the record the submission wrote down.</summary>
    /// <param name="record">The durable record.</param>
    /// <returns>The wire representation of <paramref name="record" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the record carries a stage this surface does not publish, which is a stage added without deciding what a caller should be told about it.</exception>
    public static SendEmailToolResult From(OutgoingEmailRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new SendEmailToolResult
        {
            OutgoingEmailId = record.Id.ToString(),
            AccountId = record.AccountId.Value,
            State = SendEmailStateMapping.Published(record.Stage),
            RecipientCount = record.Recipients.Count,
            QueuedAt = record.RecordedAt,
        };
    }
}
