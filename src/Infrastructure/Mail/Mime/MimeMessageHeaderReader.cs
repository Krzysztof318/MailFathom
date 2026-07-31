// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using MailMcp.Application.EmailContent;
using MailMcp.Domain.Emails;
using MimeKit;
using MimeKit.Utils;

namespace MailMcp.Infrastructure.Mail.Mime;

/// <summary>Reads the normalized headers of one parsed message.</summary>
/// <remarks>
/// One reader serves both paths that parse a message — the extraction that fills the index and the read that shows mail
/// to a person — so a message is indexed under exactly the headers it is displayed under. Two readers agreeing today
/// would be two readers that could disagree tomorrow, and the disagreement would surface as mail that cannot be found
/// by what it says.
/// </remarks>
internal static class MimeMessageHeaderReader
{
    /// <summary>Reads one message's headers.</summary>
    /// <param name="message">The parsed message.</param>
    /// <returns>The normalized subject, dates, participants, and thread identifiers.</returns>
    public static EmailContentHeaders Read(MimeMessage message) => new(
        NormalizeSubject(message.Subject),
        ReadHeaderDate(message, HeaderId.Date),
        ReadHeaderDate(message, HeaderId.Received),
        ReadParticipants(message),
        EmailThreadReferences.Create(message.MessageId, message.InReplyTo, message.References));

    private static string? NormalizeSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        // A subject reaches logs, MCP responses, and future indexes as one value, so the line breaks a folded or
        // deliberately crafted header can carry are removed rather than passed on.
        var singleLine = new string([.. subject.Where(character => !char.IsControl(character))]).Trim();

        return singleLine.Length == 0 ? null : singleLine;
    }

    /// <summary>Reads every address the message wrote, each under the header role it appeared in.</summary>
    /// <remarks>
    /// The result is wrapped rather than handed over as the array a collection expression builds, so a consumer of the
    /// record cannot cast the list back to an array and write through it.
    /// </remarks>
    private static ReadOnlyCollection<EmailParticipant> ReadParticipants(MimeMessage message) =>
        new List<EmailParticipant>(
        [
            .. CreateParticipants(EmailAddressRole.Sender, message.Sender is null ? [] : [message.Sender]),
            .. CreateParticipants(EmailAddressRole.From, message.From.Mailboxes),
            .. CreateParticipants(EmailAddressRole.ReplyTo, message.ReplyTo.Mailboxes),
            .. CreateParticipants(EmailAddressRole.To, message.To.Mailboxes),
            .. CreateParticipants(EmailAddressRole.Cc, message.Cc.Mailboxes),
            .. CreateParticipants(EmailAddressRole.Bcc, message.Bcc.Mailboxes),
        ]).AsReadOnly();

    /// <summary>Turns one header's mailboxes into participants, dropping the ones that do not parse as addresses.</summary>
    /// <remarks>
    /// <para>
    /// Group syntax is flattened to its members, because a group name is a label the sender chose rather than a
    /// recipient anything can be filtered by.
    /// </para>
    /// <para>
    /// The count is bounded by <see cref="EmailParticipant.MaximumPerRole" />, and the bound is applied to the usable
    /// addresses rather than to the mailboxes the header declared, so a message padded with unparseable entries cannot
    /// spend the allowance on addresses that would have been dropped anyway. Without it a message could devote most of
    /// its raw MIME allowance to one address header and decide how large every result derived from it becomes.
    /// </para>
    /// </remarks>
    private static IEnumerable<EmailParticipant> CreateParticipants(
        EmailAddressRole role,
        IEnumerable<MailboxAddress> mailboxes) =>
        mailboxes
            .Select(mailbox => EmailAddress.TryCreate(mailbox.Name, mailbox.Address, out var address)
                ? new EmailParticipant(role, address)
                : null)
            .OfType<EmailParticipant>()
            .Take(EmailParticipant.MaximumPerRole);

    /// <summary>Reads one date-bearing header in UTC, or nothing when it is absent or unparseable.</summary>
    /// <remarks>
    /// The <c>Received</c> header is read from the topmost occurrence, which the last receiving hop wrote, and its date
    /// follows the final semicolon of the trace. A header the sender wrote unparseably yields no timestamp rather than
    /// a guessed one.
    /// </remarks>
    private static DateTimeOffset? ReadHeaderDate(MimeMessage message, HeaderId headerId)
    {
        var headerValue = message.Headers[headerId];
        if (headerValue is null)
        {
            return null;
        }

        var dateText = headerId == HeaderId.Received
            ? ReadTraceDate(headerValue)
            : headerValue;

        return dateText is not null && DateUtils.TryParse(dateText, out var date)
            ? date.ToUniversalTime()
            : null;
    }

    private static string? ReadTraceDate(string receivedHeaderValue)
    {
        var separatorIndex = receivedHeaderValue.LastIndexOf(';');

        return separatorIndex < 0 || separatorIndex == receivedHeaderValue.Length - 1
            ? null
            : receivedHeaderValue[(separatorIndex + 1)..];
    }
}
