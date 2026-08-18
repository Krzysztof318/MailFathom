// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Reads what a message said about having been sent by a machine rather than written to one person.</summary>
/// <remarks>
/// <para>
/// Every claim here is one the sender made in a header defined for exactly this purpose, so nothing is inferred from
/// how an address is spelled or what a body looks like. That matters most for a mailing list, whose postings carry the
/// real address of the person who wrote them: no rule about mailbox names could tell one from ordinary correspondence,
/// and <c>List-Id</c> can.
/// </para>
/// <para>
/// The three are asked in the order of how much they establish. A list header (RFC 2919 and RFC 2369) says a
/// distributor handled the message; <c>Auto-Submitted</c> (RFC 3834) says a program composed it, and its own defined
/// value <c>no</c> is the one that says the opposite; <c>Precedence</c> is the oldest and loosest of the three, so it is
/// read last and only for the three values that have ever meant bulk distribution.
/// </para>
/// <para>
/// A header the sender wrote unusably — present but blank — establishes nothing, which is the safe answer in the
/// direction that matters: a message read as ordinary correspondence is still held against every other bound the
/// reader of this value applies.
/// </para>
/// </remarks>
internal static class MailAutomationReading
{
    /// <summary>The values <c>Precedence</c> has carried to mean a message went to many rather than to one.</summary>
    private static readonly string[] BulkPrecedences = ["BULK", "LIST", "JUNK"];

    /// <summary>The headers a mailing list stamps onto what it distributes.</summary>
    private static readonly HeaderId[] MailingListHeaders = [HeaderId.ListId, HeaderId.ListPost, HeaderId.ListUnsubscribe];

    /// <summary>Reads what one message claimed about itself.</summary>
    /// <param name="message">The parsed message.</param>
    /// <returns>The claim it carried, or <see cref="EmailAutomation.None" /> when it carried none.</returns>
    public static EmailAutomation Read(MimeMessage message)
    {
        if (MailingListHeaders.Any(header => HasValue(message, header)))
        {
            return EmailAutomation.MailingList;
        }

        if (ReadValue(message, HeaderId.AutoSubmitted) is { } autoSubmitted && IsAutomaticallySubmitted(autoSubmitted))
        {
            return EmailAutomation.AutomaticallySubmitted;
        }

        return ReadValue(message, HeaderId.Precedence) is { } precedence
            && BulkPrecedences.Contains(precedence.ToUpperInvariant(), StringComparer.Ordinal)
                ? EmailAutomation.BulkPrecedence
                : EmailAutomation.None;
    }

    /// <summary>Answers whether an <c>Auto-Submitted</c> value states that a program composed the message.</summary>
    /// <remarks>
    /// RFC 3834 defines <c>no</c> as the value a message carries when a person wrote it, and every other keyword —
    /// <c>auto-generated</c>, <c>auto-replied</c>, and whatever a later registration adds — as a statement that one did
    /// not. Reading it that way round is what keeps the rule working for a keyword this system has never heard of. The
    /// keyword ends at the semicolon its optional parameters follow.
    /// </remarks>
    private static bool IsAutomaticallySubmitted(string value)
    {
        var keyword = value.Split(';', 2)[0].Trim();

        return keyword.Length > 0 && !string.Equals(keyword, "no", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasValue(MimeMessage message, HeaderId headerId) => ReadValue(message, headerId) is not null;

    /// <summary>Reads one header's value, treating a blank one as absent.</summary>
    private static string? ReadValue(MimeMessage message, HeaderId headerId)
    {
        var value = message.Headers[headerId];

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
