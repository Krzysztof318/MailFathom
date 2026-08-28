// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Threads;

/// <summary>The words a conversation is written in that no control authors, and the one place their entries are named.</summary>
/// <remarks>
/// <para>
/// Only what is composed from what a deployment answered is here. Everything a control states about itself — the button
/// that asks for the whole message, the notice that a conversation runs past what is assembled — is named by a
/// <c>x:Uid</c> on that control instead, which is where a reader of the page looks for it.
/// </para>
/// <para>
/// What a message row shares with the message list is asked for through <see cref="Messages.MessageWords" /> rather
/// than named again here. A conversation's rows and a folder's rows stand in for the same absences — a message nobody
/// dated, a sender no header carried, a subject nobody wrote — and two tables saying so would be one sentence
/// translated twice.
/// </para>
/// </remarks>
internal static class ThreadWords
{
    /// <summary>The entry saying how many messages the conversation holds, which takes the count.</summary>
    internal const string MessageCountKey = "Thread.MessageCount";

    /// <summary>The entry naming who a message went to, which takes the recipients already written as a line.</summary>
    internal const string RecipientsKey = "Thread.Recipients";

    /// <summary>The entry saying how many of the conversation's messages one author wrote, which takes the count.</summary>
    internal const string ParticipantMessagesKey = "Thread.Participant.Messages";

    /// <summary>The entry a participant's whole announcement is composed through, which takes the author and the count.</summary>
    internal const string ParticipantAnnouncementKey = "Thread.Participant.Announcement";

    /// <summary>The entry the header's whole announcement is composed through, which takes the subject and the count.</summary>
    internal const string AnnouncementKey = "Thread.Announcement";

    /// <summary>
    /// Every entry a conversation asks the tables for, which is what the suite holds each authored table to answering.
    /// </summary>
    /// <remarks>
    /// Named as one list rather than asserted key by key, so an entry added to the header or to a message and nowhere
    /// else is reported by the suite rather than met by a reader as the key itself.
    /// </remarks>
    internal static IReadOnlyList<string> ResourceKeys { get; } =
    [
        MessageCountKey,
        RecipientsKey,
        ParticipantMessagesKey,
        ParticipantAnnouncementKey,
        AnnouncementKey,
    ];
}
