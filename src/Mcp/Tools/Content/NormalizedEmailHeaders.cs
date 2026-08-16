// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Mcp.Tools.Senders;

namespace MailFathom.Mcp.Tools.Content;

/// <summary>Publishes the normalized headers one email displays above its body.</summary>
/// <remarks>
/// <para>
/// These are read from the stored message rather than from the columns a listing is served out of, so they carry what
/// the listing deliberately narrows away: display names, every participant role, and the threading identifiers. An
/// email whose content the size limit kept out of storage is the one exception, and its body state says so.
/// </para>
/// <para>
/// The three threading identifiers are published beside the rest rather than nested under a heading of their own,
/// because a caller either walks a conversation and needs all three or ignores all three.
/// </para>
/// </remarks>
[Description("The headers of the email, decoded and normalized: what it displays above its body and what places it in a conversation.")]
internal sealed record NormalizedEmailHeaders
{
    /// <summary>Gets the decoded subject, or <see langword="null" /> when the email carried none.</summary>
    [Description("The decoded subject, or null when the email carried no subject header.")]
    public string? Subject { get; init; }

    /// <summary>Gets when the email was sent according to its own header, or <see langword="null" /> when it carried no usable date.</summary>
    [Description("When the sender claims the email was sent, as an ISO 8601 timestamp, or null when the Date header was missing or unparseable. A sender controls this value, so prefer receivedAt when the order of events matters.")]
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>Gets when the last receiving hop recorded the email, or <see langword="null" /> when no header carried a usable date.</summary>
    [Description("When the receiving infrastructure recorded the email, as an ISO 8601 timestamp, or null when no Received header carried a usable date.")]
    public DateTimeOffset? ReceivedAt { get; init; }

    /// <summary>Gets every usable address the email wrote, each paired with the header it appeared in.</summary>
    [Description("Every usable address the email wrote, in header order, each with the header it appeared in. Addresses no mail parser could read are left out rather than repaired, and a header naming more than 256 addresses is published up to that bound.")]
    public required IReadOnlyList<EmailHeaderParticipant> Participants { get; init; }

    /// <summary>Gets the email's own <c>Message-ID</c>, or <see langword="null" /> when it carried none.</summary>
    [Description("The Message-ID the email carried, without its angle brackets, or null when it carried none. Name the email by storedEmailId rather than by this value in a later request.")]
    public string? MessageId { get; init; }

    /// <summary>Gets the identifier of the email this one answers, or <see langword="null" /> when it answers none.</summary>
    [Description("The identifier of the email this one replies to, or null when it replies to none.")]
    public string? InReplyTo { get; init; }

    /// <summary>Gets the referenced ancestors in the order the header listed them.</summary>
    [Description("The identifiers of the earlier emails in the conversation, in the order the References header listed them, which is the path back to its root. Empty when the email carried none. A path longer than 256 identifiers is published as its root followed by its most recent ancestors.")]
    public required IReadOnlyList<string> References { get; init; }

    /// <summary>Gets what the email's author conclusion was reached from.</summary>
    /// <remarks>
    /// It sits with the headers because that is what it describes: the trusted receiving server wrote its findings into
    /// one of them, and the domain the email displayed is the <c>From</c> header's own. The conclusion itself is
    /// published beside the headers rather than inside them, as the result's <c>senderVerification</c>.
    /// </remarks>
    public required ReportedSenderAuthentication SenderAuthentication { get; init; }

    /// <summary>Publishes the headers a read produced.</summary>
    /// <param name="headers">The headers the use case returned.</param>
    /// <param name="evidence">What the email's stored author conclusion was reached from.</param>
    /// <returns>The wire representation of <paramref name="headers" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="headers" /> or <paramref name="evidence" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The evidence arrives beside the headers rather than within them because the two come from different places: the
    /// headers were parsed out of the stored raw MIME by this read, and the evidence was read off the columns
    /// synchronization wrote. An email whose raw MIME was never stored has the narrower headers a row can answer for and
    /// the same stored evidence as any other.
    /// </remarks>
    public static NormalizedEmailHeaders From(EmailContentHeaders headers, SenderAuthenticationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(evidence);

        return new NormalizedEmailHeaders
        {
            SenderAuthentication = ReportedSenderAuthentication.From(evidence),
            Subject = headers.Subject,
            SentAt = headers.SentAt,
            ReceivedAt = headers.ReceivedAt,
            Participants = [.. headers.Participants.Select(EmailHeaderParticipant.From)],
            MessageId = headers.ThreadReferences.MessageId,
            InReplyTo = headers.ThreadReferences.InReplyTo,
            References = headers.ThreadReferences.References,
        };
    }
}
