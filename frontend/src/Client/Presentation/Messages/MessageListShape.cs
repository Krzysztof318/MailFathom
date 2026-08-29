// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using System.Globalization;
using MailFathom.Client.Backend.Timeline;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Messages;

/// <summary>Reduces a loaded window of mail to the lines the list draws.</summary>
/// <remarks>
/// <para>
/// A pure reduction of what the deployment answered, where the place puts a correspondent, when the reading is
/// happening, and the words the application is read in — so the whole of what a row says is reachable by an ordinary
/// unit test rather than only by looking at a running head.
/// </para>
/// <para>
/// It composes rather than formats in the view for the reason the mailbox tree does: a binding cannot choose between a
/// time and a date, cannot stand a sentence in for a subject nobody wrote, and cannot compose the one line a screen
/// reader is given instead of the six controls a sighted reader sees.
/// </para>
/// </remarks>
internal static class MessageListShape
{
    /// <summary>Draws the loaded window as the list's rows.</summary>
    /// <param name="window">What has been loaded, in the order the list is read in.</param>
    /// <param name="now">When the reading is happening, which is what decides whether a message is dated by its time or by its day.</param>
    /// <param name="words">Where the sentences a row is composed from come from.</param>
    /// <returns>The rows, in the order the list is read in.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    internal static IImmutableList<MessageRow> Of(
        MessageWindow window,
        DateTimeOffset now,
        IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(words);

        return [.. window.Messages.Select(message => Of(message, window.Place, now, words))];
    }

    /// <summary>Draws one message as the line the list shows for it.</summary>
    internal static MessageRow Of(
        DeploymentMailMessage message,
        MessagePlace place,
        DateTimeOffset now,
        IStringLocalizer words)
    {
        var correspondent = Correspondent(message, place, words);
        var subject = string.IsNullOrWhiteSpace(message.Subject) ? words[MessageWords.NoSubjectKey].Value : message.Subject;
        var received = Received(message.ReceivedAt, now, words);

        return new MessageRow(
            message.Id.ToString("D", CultureInfo.InvariantCulture),
            message.ThreadId,
            correspondent,
            subject,
            message.Preview ?? string.Empty,
            received,
            Announced(correspondent, subject, received, message, words),
            message.Unread,
            message.Flagged,
            message.Answered,
            message.HasAttachments,
            message.AttachmentCount);
    }

    /// <summary>Names who the message is with, which is not the same question in a sent folder as in an inbox.</summary>
    /// <remarks>
    /// The display name the sender wrote is preferred over their address, because it is what a person recognizes and
    /// what every mail client shows. A message with neither is drawn under a sentence rather than under a blank, since
    /// a row with nothing in that column reads as a row that failed to load.
    /// </remarks>
    private static string Correspondent(
        DeploymentMailMessage message,
        MessagePlace place,
        IStringLocalizer words) =>
        place.ShowsRecipients
            ? Recipients(message.Recipients, words)
            : Named(message.SenderDisplayName, message.SenderAddress) ?? Recipients(message.Recipients, words);

    /// <summary>Names the recipients as the first of them beside how many others there are.</summary>
    private static string Recipients(IReadOnlyList<string> recipients, IStringLocalizer words) => recipients switch
    {
        [] => words[MessageWords.NoSenderKey].Value,
        [var only] => only,
        [var first, ..] => words[MessageWords.MoreRecipientsKey, first, recipients.Count - 1].Value,
    };

    /// <summary>Takes the display name where the header carried one and the address otherwise.</summary>
    private static string? Named(string? displayName, string? address) =>
        string.IsNullOrWhiteSpace(displayName)
            ? (string.IsNullOrWhiteSpace(address) ? null : address)
            : displayName;

    /// <summary>
    /// Writes when the message arrived: its time on the day it arrived, its day and month within the year, and its
    /// whole date before that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three bands are what a mail list has always shown, and each is written with the reader's own culture rather
    /// than with a pattern this application invented — a date is one of the few things every language writes
    /// differently and nobody has to be taught.
    /// </para>
    /// <para>
    /// Both instants are read in the reader's own time zone before their days are compared, because "today" is a
    /// question about where the reader is rather than about the offset a mail server wrote into a header.
    /// </para>
    /// </remarks>
    private static string Received(DateTimeOffset? receivedAt, DateTimeOffset now, IStringLocalizer words)
    {
        if (receivedAt is not { } arrived)
        {
            return words[MessageWords.NoDateKey].Value;
        }

        var local = arrived.ToLocalTime();
        var today = now.ToLocalTime();

        if (local.Date == today.Date)
        {
            return local.ToString("t", CultureInfo.CurrentCulture);
        }

        return local.Year == today.Year
            ? local.ToString("m", CultureInfo.CurrentCulture)
            : local.ToString("d", CultureInfo.CurrentCulture);
    }

    /// <summary>Composes the one sentence a screen reader is given for the row.</summary>
    /// <remarks>
    /// A list of six unlabelled controls per row is what a screen reader would otherwise read out fifty times over, so
    /// the row states itself once and the controls inside it are drawn rather than announced. The three flags and the
    /// attachment are appended rather than folded into the sentence, because each is absent from most rows.
    /// </remarks>
    private static string Announced(
        string correspondent,
        string subject,
        string received,
        DeploymentMailMessage message,
        IStringLocalizer words)
    {
        var announced = words[MessageWords.AnnouncementKey, correspondent, subject, received].Value;

        var marks = new List<string>(4);

        if (message.Unread)
        {
            marks.Add(words[MessageWords.UnreadKey].Value);
        }

        if (message.Flagged)
        {
            marks.Add(words[MessageWords.FlaggedKey].Value);
        }

        if (message.Answered)
        {
            marks.Add(words[MessageWords.AnsweredKey].Value);
        }

        if (message.HasAttachments)
        {
            marks.Add(words[MessageWords.AttachmentsKey].Value);
        }

        return marks.Count is 0 ? announced : $"{announced} {string.Join(' ', marks)}";
    }
}
