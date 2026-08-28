// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using System.Globalization;
using MailFathom.Client.Backend.Threads;
using MailFathom.Client.Backend.Timeline;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Spaces.Mail.Reading;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Threads;

/// <summary>Reduces a read conversation to the header and the messages the screen draws.</summary>
/// <remarks>
/// <para>
/// A pure reduction of what the deployment answered and what the reader has opened, so the whole of what a conversation
/// says is reachable by an ordinary unit test rather than only by looking at a running head — which is the same reason
/// the message list is shaped outside its view.
/// </para>
/// <para>
/// It composes rather than formats in the view because a binding cannot stand a sentence in for a subject nobody wrote,
/// cannot write a date in the reader's own culture, and cannot compose the one line a screen reader is given instead of
/// the several controls a sighted reader sees.
/// </para>
/// </remarks>
internal static class ThreadShape
{
    /// <summary>Draws the conversation's header, which is answered about the whole of it rather than about the page.</summary>
    /// <param name="window">What has been read of the conversation.</param>
    /// <param name="words">Where the sentences the header is composed from come from.</param>
    /// <returns>The header, or the empty one where no conversation is open.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    internal static ThreadReading Header(ThreadWindow window, IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(words);

        if (!window.IsOpen)
        {
            return ThreadReading.Nothing;
        }

        var subject = Subject(window, words);
        var count = words[ThreadWords.MessageCountKey, window.MessageCount].Value;

        return ThreadReading.Of(
            subject,
            count,
            words[ThreadWords.AnnouncementKey, subject, count].Value,
            [.. window.Participants.Select(participant => Draw(participant, words)).OfType<ThreadParticipantRow>()],
            window.MoreParticipantsNotNamed,
            window.MoreMessagesNotAssembled);
    }

    /// <summary>Draws what has been read of the conversation as the messages the screen shows.</summary>
    /// <param name="window">What has been read, in the conversation's own order.</param>
    /// <param name="opened">How much of each message the reader has asked to see, by the message's own identity, for the messages they have asked anything of.</param>
    /// <param name="words">Where the sentences a message is composed from come from.</param>
    /// <returns>The messages, in the conversation's own order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    internal static IImmutableList<ThreadMessageRow> Messages(
        ThreadWindow window,
        IImmutableDictionary<string, ThreadMessageDetail> opened,
        IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(opened);
        ArgumentNullException.ThrowIfNull(words);

        var openedAt = OpenedKey(window);

        return [.. window.Messages.Select(message => Draw(message, DetailOf(message, opened, openedAt), openedAt, words))];
    }

    /// <summary>Says how much of one message is shown, for a message the reader has said nothing about.</summary>
    /// <remarks>
    /// The message a conversation opened at is expanded without anything having been written down, which is what keeps
    /// opening one from being a write: a conversation opened and left alone has an empty set of disclosures, and the
    /// same conversation reopened at another message opens there rather than where the last one did. Collapsing the
    /// opened message writes an entry, and that entry then decides — so the reader's own act always wins over the
    /// default.
    /// </remarks>
    internal static ThreadMessageDetail DetailOf(
        DeploymentThreadMessage message,
        IImmutableDictionary<string, ThreadMessageDetail> opened,
        string openedAt)
    {
        ArgumentNullException.ThrowIfNull(opened);

        var key = KeyOf(message);

        if (opened.TryGetValue(key, out var detail))
        {
            return detail;
        }

        return string.Equals(key, openedAt, StringComparison.Ordinal)
            ? ThreadMessageDetail.Opened
            : ThreadMessageDetail.Collapsed;
    }

    /// <summary>Names the message a conversation opens showing, which is the one somebody arrived at or the newest.</summary>
    /// <param name="window">What has been read of the conversation.</param>
    /// <returns>The message's identity, or an empty string where the conversation holds none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="window" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Arriving at a particular message is not an edge case — it is how a search result and a citation reach mail at
    /// all — so the message named is the one opened whether or not it is the newest. Where nobody named one, the newest
    /// is what somebody catching up on the conversation came for, and the order is the conversation's own, so the
    /// newest is the last of it that has been read. A message the conversation no longer shows names nothing, which
    /// leaves the newest opened rather than the conversation opened at nothing.
    /// </remarks>
    internal static string OpenedKey(ThreadWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Messages.Count is 0)
        {
            return string.Empty;
        }

        if (window.OpenedAt is { } arrivedAt)
        {
            var named = KeyOf(arrivedAt);

            if (window.Messages.Any(message => string.Equals(KeyOf(message), named, StringComparison.Ordinal)))
            {
                return named;
            }
        }

        return KeyOf(window.Messages[^1]);
    }

    /// <summary>Names one message of the conversation as every other part of the client names it.</summary>
    internal static string KeyOf(DeploymentThreadMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.Email is { } email ? KeyOf(email.Id) : string.Empty;
    }

    /// <summary>Names one message as every other part of the client names it, from the identity alone.</summary>
    private static string KeyOf(Guid identity) => identity.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>Takes what the conversation is about from the message that began it.</summary>
    /// <remarks>
    /// The first message rather than the newest, because a subject rewritten halfway down a thread describes a reply
    /// rather than the exchange, and because paging forward would otherwise rewrite the header somebody is reading
    /// under.
    /// </remarks>
    private static string Subject(ThreadWindow window, IStringLocalizer words)
    {
        var opening = window.Messages.Count is 0 ? null : window.Messages[0].Email?.Subject;

        return string.IsNullOrWhiteSpace(opening) ? words[MessageWords.NoSubjectKey].Value : opening;
    }

    /// <summary>Draws one author of the conversation, or nothing where the answer named neither a name nor an address.</summary>
    private static ThreadParticipantRow? Draw(DeploymentThreadParticipant participant, IStringLocalizer words)
    {
        var author = Named(participant.DisplayName, participant.Address);

        if (author is null)
        {
            return null;
        }

        return new ThreadParticipantRow(
            string.IsNullOrWhiteSpace(participant.Address) ? author : participant.Address,
            author,
            words[ThreadWords.ParticipantMessagesKey, participant.MessageCount].Value,

            // The count rather than what is drawn beside the name: the two entries are written for different readers,
            // and a sentence composed from a bracketed number would be read out as one.
            words[ThreadWords.ParticipantAnnouncementKey, author, participant.MessageCount].Value);
    }

    /// <summary>Draws one message of the conversation as the screen shows it.</summary>
    private static ThreadMessageRow Draw(
        DeploymentThreadMessage message,
        ThreadMessageDetail detail,
        string openedAt,
        IStringLocalizer words)
    {
        var email = message.Email!;
        var key = KeyOf(email.Id);
        var author = Named(email.SenderDisplayName, email.SenderAddress) ?? words[MessageWords.NoSenderKey].Value;
        var subject = string.IsNullOrWhiteSpace(email.Subject)
            ? words[MessageWords.NoSubjectKey].Value
            : email.Subject;
        var written = Written(email, words);

        return new ThreadMessageRow(
            key,
            author,
            subject,
            Recipients(email, words),
            email.Preview ?? string.Empty,
            written,
            Announced(author, subject, written, email, words),
            detail.Expanded,
            string.Equals(key, openedAt, StringComparison.Ordinal),
            email.Unread,
            email.Flagged,
            email.Answered,
            email.HasAttachments,
            email.AttachmentCount,
            detail.Message is { } describedMessage
                ? MailMessageReading.Of(describedMessage, detail.Attachments, words)
                : null,
            detail.WholeMessage,
            detail.IsReadingWholeMessage,
            detail.WholeMessageFailed);
    }

    /// <summary>Writes who a message went to, or nothing where it named nobody this screen can draw.</summary>
    private static string Recipients(DeploymentMailMessage email, IStringLocalizer words) => email.Recipients switch
    {
        [] => string.Empty,
        [var only] => words[ThreadWords.RecipientsKey, only].Value,
        [var first, ..] => words[
            ThreadWords.RecipientsKey,
            words[MessageWords.MoreRecipientsKey, first, email.Recipients.Count - 1].Value].Value,
    };

    /// <summary>Takes the display name where the header carried one and the address otherwise.</summary>
    private static string? Named(string? displayName, string? address) =>
        string.IsNullOrWhiteSpace(displayName)
            ? (string.IsNullOrWhiteSpace(address) ? null : address)
            : displayName;

    /// <summary>
    /// Writes when a message was written, with its date and its time and in the reader's own culture.
    /// </summary>
    /// <remarks>
    /// In full rather than in the three bands the message list writes. A list is read down one folder's recent mail, so
    /// a time is enough for today and a day is enough for this year; a conversation is read across whatever span it
    /// covers, and two replies a year apart written as <c>14:02</c> and <c>3 Aug</c> would place neither.
    /// </remarks>
    private static string Written(DeploymentMailMessage email, IStringLocalizer words)
    {
        var written = email.SentAt ?? email.ReceivedAt;

        return written is { } instant
            ? instant.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : words[MessageWords.NoDateKey].Value;
    }

    /// <summary>Composes the one sentence a screen reader is given for a message.</summary>
    /// <remarks>
    /// The same shape a list row is announced with, and through the same entries: a conversation's messages and a
    /// folder's rows stand for the same thing, and announcing them differently would make one exchange read as two
    /// kinds of mail. The marks are appended rather than folded in, because each is absent from most messages.
    /// </remarks>
    private static string Announced(
        string author,
        string subject,
        string written,
        DeploymentMailMessage email,
        IStringLocalizer words)
    {
        var announced = words[MessageWords.AnnouncementKey, author, subject, written].Value;

        var marks = new List<string>(4);

        if (email.Unread)
        {
            marks.Add(words[MessageWords.UnreadKey].Value);
        }

        if (email.Flagged)
        {
            marks.Add(words[MessageWords.FlaggedKey].Value);
        }

        if (email.Answered)
        {
            marks.Add(words[MessageWords.AnsweredKey].Value);
        }

        if (email.HasAttachments)
        {
            marks.Add(words[MessageWords.AttachmentsKey].Value);
        }

        return marks.Count is 0 ? announced : $"{announced} {string.Join(' ', marks)}";
    }
}
