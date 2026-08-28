// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend.Threads;

namespace MailFathom.Client.Presentation.Threads;

/// <summary>What of one conversation has been read, and where reading it continues from.</summary>
/// <param name="ThreadId">The conversation, or <see langword="null" /> where none is open.</param>
/// <param name="Messages">The messages read so far, in the conversation's own order.</param>
/// <param name="Participants">Everybody the deployment named as having written in the conversation.</param>
/// <param name="MessageCount">How many messages the whole conversation holds of those this caller may see.</param>
/// <param name="MoreMessagesNotAssembled">Whether the conversation runs past what one read assembles at all.</param>
/// <param name="MoreParticipantsNotNamed">Whether the conversation has authors the participants do not name.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> where the conversation ends here.</param>
/// <param name="OpenedAt">The message somebody arrived at, or <see langword="null" /> where the newest is the one opened.</param>
/// <remarks>
/// <para>
/// A conversation grows forwards and never backwards, which is what makes this an accumulation rather than the bounded
/// window the message list holds: a thread is read from its beginning and the bound on it is the deployment's own, so
/// there is no far end to drop and nothing to ask for a second time.
/// </para>
/// <para>
/// Everything outside the messages is answered about the whole conversation rather than about the page, so a header
/// drawn from the first page stays true as later pages arrive — and a later page that answered nothing about them
/// leaves what the first one said standing rather than emptying it.
/// </para>
/// <para>
/// The messages are this owner's own correspondence and carry the classification of everything else about mail: they
/// are held for as long as the conversation is open and are written nowhere.
/// </para>
/// </remarks>
internal sealed record ThreadWindow(
    Guid? ThreadId,
    IImmutableList<DeploymentThreadMessage> Messages,
    IImmutableList<DeploymentThreadParticipant> Participants,
    int MessageCount,
    bool MoreMessagesNotAssembled,
    bool MoreParticipantsNotNamed,
    string? NextCursor,
    Guid? OpenedAt)
{
    /// <summary>No conversation open, which is what the screen holds before one is.</summary>
    internal static ThreadWindow Nothing { get; } = new(
        ThreadId: null,
        [],
        [],
        MessageCount: 0,
        MoreMessagesNotAssembled: false,
        MoreParticipantsNotNamed: false,
        NextCursor: null,
        OpenedAt: null);

    /// <summary>Gets whether a conversation is open at all, as against a screen nothing has been opened in.</summary>
    internal bool IsOpen => this.ThreadId is not null;

    /// <summary>Gets whether there is more of the conversation to read.</summary>
    internal bool HasMore => this.NextCursor is not null;

    /// <summary>Reads the first page of a conversation somebody has just opened.</summary>
    /// <param name="opening">Which conversation was opened, and at which message.</param>
    /// <param name="page">What the deployment answered.</param>
    /// <returns>The window that first page establishes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static ThreadWindow Opening(ThreadOpening opening, DeploymentMailThreadPage page)
    {
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(page);

        return new ThreadWindow(
            opening.ThreadId,
            [.. Readable(page)],
            [.. page.Authors],
            page.MessageCount,
            page.MoreMessagesNotAssembled,
            page.MoreParticipantsNotNamed,
            page.NextCursor,
            opening.AtMessage);
    }

    /// <summary>Takes the following page onto the end of what has been read.</summary>
    /// <param name="page">What the deployment answered.</param>
    /// <returns>The window the page extends.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The counts and the participants are taken from the newer answer, because each of them describes the whole
    /// conversation as the deployment reads it now rather than as it read it when the first page was asked for.
    /// </remarks>
    internal ThreadWindow Extended(DeploymentMailThreadPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return this with
        {
            Messages = [.. this.Messages, .. Readable(page)],
            Participants = [.. page.Authors],
            MessageCount = page.MessageCount,
            MoreMessagesNotAssembled = page.MoreMessagesNotAssembled,
            MoreParticipantsNotNamed = page.MoreParticipantsNotNamed,
            NextCursor = page.NextCursor,
        };
    }

    /// <summary>Gets whether this window is of the same reading of the same conversation as another.</summary>
    /// <param name="other">The window a read was started from.</param>
    /// <returns><see langword="true" /> when both hold the same conversation opened at the same message.</returns>
    /// <remarks>
    /// What a page arriving late is held against. A read started while one conversation was open and answered after
    /// another has been is a page of neither, and splicing it on would put one exchange's messages under another's
    /// header.
    /// </remarks>
    internal bool IsOf(ThreadWindow other) =>
        other is not null && this.ThreadId == other.ThreadId && this.OpenedAt == other.OpenedAt;

    /// <summary>Keeps the messages a screen can draw, which is those the deployment described at all.</summary>
    /// <remarks>
    /// A message the answer named without a body of its own is a document this build cannot draw a line from, so it is
    /// left out rather than drawn as a row with nothing in it. It stays counted, because the count is the deployment's
    /// statement about the conversation rather than about what arrived here.
    /// </remarks>
    private static IEnumerable<DeploymentThreadMessage> Readable(DeploymentMailThreadPage page) =>
        page.Written.Where(static message => message.Email is not null);
}
