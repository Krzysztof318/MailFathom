// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Authoring;

/// <summary>Decides who an answer to a stored email is addressed to.</summary>
/// <remarks>
/// <para>
/// This is a decision rather than a copy, and each act makes a different one. A reply goes where the message asked for
/// answers to go: the <c>Reply-To</c> header where the sender wrote one, and the <c>From</c> header otherwise — a
/// mailing list, an automated system, and a person sending from a shared mailbox all rely on that header being read
/// rather than ignored. A reply to all keeps everybody else the exchange was between. A forward addresses nobody at
/// all, because the people it goes to are people the original never named.
/// </para>
/// <para>
/// The mailboxes the sending account owns are removed from what a reply to all <i>adds</i>, and from nothing else. A
/// deployment that answers a message it was copied on and mails itself has written a loop, and one that runs rules over
/// arriving mail will run them over its own answer. The message being answered is not that: whoever asked for answers
/// is who an answer goes to even when that is this account's own address, which is what a message somebody sent
/// themselves and a shared mailbox two colleagues both send as both look like. Filtering there would resolve a plain
/// reply to nobody and refuse it, which is a worse answer than the one a mail client gives.
/// </para>
/// </remarks>
internal static class AnsweredEmailRecipients
{
    /// <summary>Resolves everybody one authored answer is addressed to.</summary>
    /// <param name="act">Which answer is being authored.</param>
    /// <param name="headers">The answered message's own headers.</param>
    /// <param name="ownedByAccount">The mailboxes the sending account owns, which a reply to all does not copy back in.</param>
    /// <param name="namedByAuthor">The people the author named themselves.</param>
    /// <returns>The recipients the answer is composed with, in the order the headers place them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A mailbox named twice is offered once, and in the most visible header it was named in, which is the rule the
    /// composition applies to any authored list. It is applied here as well because the count this produces is what a
    /// caller sees: a reply to all resolving forty people from a message naming twenty twice would read as a message
    /// going somewhere it is not.
    /// </remarks>
    public static IReadOnlyList<AuthoredEmailRecipient> For(
        AuthoredResponseAct act,
        EmailContentHeaders headers,
        IReadOnlySet<EmailAddress> ownedByAccount,
        IReadOnlyList<AuthoredEmailRecipient> namedByAuthor)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(ownedByAccount);
        ArgumentNullException.ThrowIfNull(namedByAuthor);

        var answered = AnsweredParty(headers, act);
        var keptInTheConversation = KeptInTheConversation(headers, act);

        var placed = new List<AuthoredEmailRecipient>(
            answered.Count + keptInTheConversation.Count + namedByAuthor.Count);
        var alreadyPlaced = new HashSet<EmailAddress>();

        foreach (var (role, address) in answered)
        {
            if (alreadyPlaced.Add(address))
            {
                placed.Add(new AuthoredEmailRecipient(role, address.Address, address.DisplayName));
            }
        }

        foreach (var (role, address) in keptInTheConversation)
        {
            if (!ownedByAccount.Contains(address) && alreadyPlaced.Add(address))
            {
                placed.Add(new AuthoredEmailRecipient(role, address.Address, address.DisplayName));
            }
        }

        // What the author named is carried through unparsed, exactly as it would be on a message answering nothing.
        // Resolving it against what was derived would mean parsing an address here, which is the composition's to do
        // and to refuse.
        placed.AddRange(namedByAuthor);

        return placed;
    }

    /// <summary>Reads whoever the answered message asked for answers to, which every reply is addressed to.</summary>
    /// <remarks>
    /// It comes first within <c>To</c>, because they are who the message is to and everybody else is being kept in the
    /// conversation. A forward asks nobody, so it resolves to nothing here.
    /// </remarks>
    private static IReadOnlyList<(OutgoingRecipientRole Role, EmailAddress Address)> AnsweredParty(
        EmailContentHeaders headers,
        AuthoredResponseAct act)
    {
        if (act is AuthoredResponseAct.Forward)
        {
            return [];
        }

        var answered = Role(headers, EmailAddressRole.ReplyTo) is { Count: > 0 } replyTo
            ? replyTo
            : Role(headers, EmailAddressRole.From);

        return [.. answered.Select(address => (OutgoingRecipientRole.To, address))];
    }

    /// <summary>Reads everybody else the exchange was between, which only a reply to all keeps.</summary>
    /// <remarks>
    /// They are read in the order of decreasing visibility, so somebody named in two of them is placed in the more
    /// visible one by the resolution above.
    /// </remarks>
    private static IReadOnlyList<(OutgoingRecipientRole Role, EmailAddress Address)> KeptInTheConversation(
        EmailContentHeaders headers,
        AuthoredResponseAct act) =>
        act is not AuthoredResponseAct.ReplyToAll
            ? []
            :
            [
                .. Role(headers, EmailAddressRole.To).Select(address => (OutgoingRecipientRole.To, address)),
                .. Role(headers, EmailAddressRole.Cc).Select(address => (OutgoingRecipientRole.Cc, address)),
            ];

    private static IReadOnlyList<EmailAddress> Role(EmailContentHeaders headers, EmailAddressRole role) =>
        [.. headers.Participants.Where(participant => participant.Role == role).Select(participant => participant.Address)];
}
