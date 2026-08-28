// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Threads;

/// <summary>One conversation as a deployment serves it, and one page of the messages in it.</summary>
/// <param name="ThreadId">The conversation, as the request named it.</param>
/// <param name="Messages">The page's messages, in the conversation's own order.</param>
/// <param name="Participants">Everybody who wrote in the conversation, in the order they first wrote in it.</param>
/// <param name="MessageCount">How many messages the conversation holds of those this caller may see.</param>
/// <param name="MoreMessagesNotAssembled">Whether the conversation runs past what one read assembles at all.</param>
/// <param name="MoreParticipantsNotNamed">Whether the conversation has authors <paramref name="Participants" /> does not name.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the conversation.</param>
/// <param name="PageSize">How many messages the read ran under, which is what the request asked for or the default it took.</param>
/// <remarks>
/// <para>
/// Everything outside the messages describes the whole conversation rather than the page, which is what lets a header be
/// drawn from the first page and stay accurate without the rest being held: the participants, the count, and both bounds
/// are answers about the conversation.
/// </para>
/// <para>
/// A conversation is not scoped to a folder and this record carries none. The question is in the inbox, the answer is in
/// the sent folder, and a forwarded copy is somewhere else again, so what is served spans every folder of every account
/// the signed-in owner owns.
/// </para>
/// <para>
/// All of it is that owner's own correspondence and carries the classification the root instructions put on mail: it is
/// put in front of that owner alone and reaches no log, no telemetry, and no local store.
/// </para>
/// </remarks>
public sealed record DeploymentMailThreadPage(
    Guid ThreadId,
    IReadOnlyList<DeploymentThreadMessage> Messages,
    IReadOnlyList<DeploymentThreadParticipant> Participants,
    int MessageCount,
    bool MoreMessagesNotAssembled,
    bool MoreParticipantsNotNamed,
    string? NextCursor,
    int PageSize)
{
    /// <summary>Gets the messages, reading a document that named none as a page that holds nothing.</summary>
    /// <remarks>
    /// A missing member deserializes to <see langword="null" /> rather than to an empty list, and every reader wants the
    /// same answer for the two. Said once here rather than at each reader, as the list and folders documents already
    /// say it.
    /// </remarks>
    public IReadOnlyList<DeploymentThreadMessage> Written => this.Messages ?? [];

    /// <summary>Gets the authors, reading a document that named none as a conversation nobody is named in.</summary>
    public IReadOnlyList<DeploymentThreadParticipant> Authors => this.Participants ?? [];
}
