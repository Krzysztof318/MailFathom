// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Threads;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseThread;

/// <summary>One page of one conversation, with what the whole of that conversation is.</summary>
/// <param name="ThreadId">The conversation this is a page of, named by the identifier the request used.</param>
/// <param name="Messages">The page's messages, in the conversation's own order.</param>
/// <param name="Participants">Everybody who wrote in the assembled conversation, in the order they first wrote in it.</param>
/// <param name="MessageCount">How many messages the conversation holds of those this caller may see and this read assembled.</param>
/// <param name="MoreMessagesNotAssembled">Whether the conversation holds more messages than one read assembles at all.</param>
/// <param name="MoreParticipantsNotNamed">Whether the conversation holds authors this list does not name.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the conversation.</param>
/// <param name="PageSize">How many messages the read ran under, which is what the request asked for or the default it took.</param>
/// <remarks>
/// <para>
/// Everything outside <see cref="Messages" /> describes the conversation rather than the page, which is why a client
/// draws a thread header from the first page and never has to hold the rest to keep it accurate. The two counts are of
/// what this caller may see: a message in a folder an operator withheld is in no conversation this surface publishes and
/// in no count of one, because a count including it would report that folder's contents one integer at a time.
/// </para>
/// <para>
/// Both bounds say when they cut rather than leaving a caller to compare lengths. A conversation longer than
/// <see cref="IEmailThreadReader.MaximumAssembledEmails" /> is assembled that far and says so; a conversation with more
/// authors than <see cref="MaximumNamedParticipants" /> names that many and says so. Neither is a page boundary —
/// <see cref="NextCursor" /> is — so a client that walked every page still learns from these that something was cut.
/// </para>
/// </remarks>
public sealed record BrowsedThread(
    EmailThreadId ThreadId,
    IReadOnlyList<BrowsedThreadMessage> Messages,
    IReadOnlyList<ThreadParticipant> Participants,
    int MessageCount,
    bool MoreMessagesNotAssembled,
    bool MoreParticipantsNotNamed,
    string? NextCursor,
    int PageSize)
{
    /// <summary>The greatest number of authors one conversation's participant list names.</summary>
    /// <remarks>
    /// Set where naming who is in an exchange stops and reproducing a mailing list's membership begins. A conversation
    /// people follow has a handful of authors; a list expansion can have one per message, and a header drawn from
    /// hundreds of them is a header nobody reads. The count of what was assembled stays exact either way, so a cut list
    /// never makes the conversation look smaller than it is.
    /// </remarks>
    public const int MaximumNamedParticipants = 50;
}
