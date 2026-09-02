// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails.Threads;

namespace MailFathom.Mcp.Tools.Content;

/// <summary>Publishes one other message of the conversation a read email belongs to.</summary>
/// <remarks>
/// The message is named rather than reproduced: enough to recognize it and to ask for it, and no body text at all. That
/// is the data-minimization control on this part of the read — a caller that wants what a message says asks for that
/// message, rather than receiving a conversation inside every call that touches one of it.
/// </remarks>
[Description("One other message of the same conversation, named rather than reproduced: no body text, no attachments, and no raw MIME.")]
internal sealed record NamedThreadedEmail
{
    /// <summary>Gets the stable local identity a caller reads this message's content by.</summary>
    [Description("The stable local identifier of the message. Pass it as a storedEmailId to read this message's content.")]
    public required string StoredEmailId { get; init; }

    /// <summary>Gets the zero-based place the message holds in the conversation's order.</summary>
    [Description("The zero-based place this message holds in the conversation's order. The order is the reply relation first, the sent timestamp between messages answering the same parent, and the local identifier where both are equal — so it is stable across reads and is not the order the messages were received in.")]
    public required int Position { get; init; }

    /// <summary>Gets the message this one answers, or <see langword="null" /> when it opens what is shown.</summary>
    [Description("The storedEmailId of the message this one answers, or null when it is a root of what you are shown. Null does not mean the message opened the conversation: a message whose parent is not held here, or sits in a folder withheld from tools, is published as a root.")]
    public string? InReplyToStoredEmailId { get; init; }

    /// <summary>Gets the decoded subject, or <see langword="null" /> when the message carried none.</summary>
    [Description("The decoded subject, or null when the message carried no subject header.")]
    public string? Subject { get; init; }

    /// <summary>Gets when the message says it was sent, or <see langword="null" /> when no header carried a usable date.</summary>
    [Description("When the sender claims the message was sent, as an ISO 8601 timestamp, or null when the Date header was missing or unparseable. It is what a sender's own clock asserted, so it can contradict the conversation's order rather than produce it.")]
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>Gets the sender address as the message wrote it, or <see langword="null" /> when it carried none usable.</summary>
    [Description("The sender address as written by the message, or null when it carried no usable sender address. Display names are not published here; they belong to reading the message.")]
    public string? SenderAddress { get; init; }

    /// <summary>Publishes one placed message of a conversation.</summary>
    /// <param name="placed">The message and where it sits in the conversation's order.</param>
    /// <returns>The wire representation of <paramref name="placed" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="placed" /> is <see langword="null" />.</exception>
    public static NamedThreadedEmail From(PlacedThreadedEmail placed)
    {
        ArgumentNullException.ThrowIfNull(placed);

        return new NamedThreadedEmail
        {
            StoredEmailId = placed.Email.StoredEmailId.ToString(),
            Position = placed.Position,
            InReplyToStoredEmailId = placed.AnsweredStoredEmailId?.ToString(),
            Subject = placed.Email.Subject,
            SentAt = placed.Email.SentAt,
            SenderAddress = placed.Email.SenderAddress,
        };
    }
}
