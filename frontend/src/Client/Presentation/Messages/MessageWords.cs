// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Messages;

/// <summary>The words a message row is written in, and the one place their entries are named.</summary>
/// <remarks>
/// A row is composed from what a deployment answered rather than fixed per control, so its sentences are asked for from
/// code instead of through a <c>x:Uid</c> — which makes a name written here the single way a reader would meet a
/// resource key on the screen instead of a sentence. The unit suite holds every authored table to answering all of them.
/// </remarks>
internal static class MessageWords
{
    /// <summary>The entry standing in for a message whose sender no header carried.</summary>
    internal const string NoSenderKey = "Messages.NoSender";

    /// <summary>The entry standing in for a message carrying no subject.</summary>
    internal const string NoSubjectKey = "Messages.NoSubject";

    /// <summary>The entry standing in for a message no header dated.</summary>
    internal const string NoDateKey = "Messages.NoDate";

    /// <summary>The entry naming the first recipient beside how many others there are, which takes both.</summary>
    internal const string MoreRecipientsKey = "Messages.MoreRecipients";

    /// <summary>The entry a row's whole announcement is composed through, which takes the correspondent, the subject, and the date.</summary>
    internal const string AnnouncementKey = "Messages.Announcement";

    /// <summary>The entry announcing that a row has not been read.</summary>
    internal const string UnreadKey = "Messages.Announcement.Unread";

    /// <summary>The entry announcing that a row is flagged.</summary>
    internal const string FlaggedKey = "Messages.Announcement.Flagged";

    /// <summary>The entry announcing that a row has been answered.</summary>
    internal const string AnsweredKey = "Messages.Announcement.Answered";

    /// <summary>The entry announcing that a row carries an attachment.</summary>
    internal const string AttachmentsKey = "Messages.Announcement.Attachments";

    /// <summary>
    /// Every entry a row asks the tables for, which is what the suite holds each authored table to answering.
    /// </summary>
    /// <remarks>
    /// Named as one list rather than asserted key by key, so an entry added to a row and nowhere else is reported by the
    /// suite rather than met by a reader as the key itself.
    /// </remarks>
    internal static IReadOnlyList<string> ResourceKeys { get; } =
    [
        NoSenderKey,
        NoSubjectKey,
        NoDateKey,
        MoreRecipientsKey,
        AnnouncementKey,
        UnreadKey,
        FlaggedKey,
        AnsweredKey,
        AttachmentsKey,
    ];
}
