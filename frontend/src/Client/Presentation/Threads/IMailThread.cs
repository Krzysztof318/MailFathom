// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Threads;

/// <summary>The one conversation a run has open, wherever it was opened from.</summary>
/// <remarks>
/// <para>
/// One for the run rather than one per screen, for the reason the mailbox tree and the message list are: a model is
/// built and discarded as its view is navigated to and away from, so a conversation each screen kept would be read
/// again every time somebody moved between spaces — and would lose which of its messages they had opened while doing
/// it.
/// </para>
/// <para>
/// It is opened at a message as readily as at a conversation, and that is not a convenience: a search result and a
/// citation both reach mail by naming one message inside an exchange, so a screen that could only open a conversation
/// at its start would lose the context somebody arrived with. The message named is scrolled to and shown, and the rest
/// of the conversation collapses to a line each.
/// </para>
/// </remarks>
public interface IMailThread
{
    /// <summary>The conversation's header, answered about the whole of it rather than about what has been read.</summary>
    IFeed<ThreadReading> Reading { get; }

    /// <summary>The messages read so far, in the conversation's own order.</summary>
    /// <remarks>
    /// A list feed rather than a feed of a list, so the three states this genuinely has are the framework's rather than
    /// each one remembered by a view: the read under way, the read that failed, and no conversation open to draw.
    /// </remarks>
    IListFeed<ThreadMessageRow> Messages { get; }

    /// <summary>Whether there is more of the conversation after what has been read.</summary>
    IFeed<bool> HasMoreMessages { get; }

    /// <summary>Whether the last attempt to read more of the conversation did not arrive.</summary>
    /// <remarks>
    /// Its own answer rather than the conversation's error state, because a page that failed leaves what is already
    /// drawn on the screen: putting the whole conversation into an error would take an exchange somebody is reading
    /// away over one request.
    /// </remarks>
    IFeed<bool> PagingFailed { get; }

    /// <summary>Opens a conversation, at the message somebody arrived at where they arrived at one.</summary>
    /// <param name="threadId">The conversation to open, or <see langword="null" /> to leave the screen with nothing in it.</param>
    /// <param name="atMessage">The message to show and scroll to, or <see langword="null" /> to open at the newest.</param>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the conversation is being read.</returns>
    ValueTask OpenAsync(Guid? threadId, Guid? atMessage, CancellationToken cancellationToken);

    /// <summary>Shows what one message added, or collapses it back to the line it stands for.</summary>
    /// <param name="key">The message, as a row names itself.</param>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the message has been opened or closed.</returns>
    /// <remarks>Collapsing a message drops the whole of it with the answer about its remote pictures, so nothing an expansion asked for outlives it.</remarks>
    ValueTask ToggleAsync(string key, CancellationToken cancellationToken);

    /// <summary>Reads the whole of one message, which is what the quoted history under it is reached by.</summary>
    /// <param name="key">The message, as a row names itself.</param>
    /// <param name="cancellationToken">Abandons the read.</param>
    /// <returns>A task completing once the whole message has arrived or the attempt has been reported.</returns>
    /// <remarks>
    /// The read every other message of the conversation does not make. What each message added arrived with the
    /// conversation, so this is the one gesture that costs a request — which is why it is a gesture rather than
    /// something the screen does on everybody's behalf.
    /// </remarks>
    ValueTask ShowWholeMessageAsync(string key, CancellationToken cancellationToken);

    /// <summary>Reads one whole message again, this time fetching what it asks for from somebody else's server.</summary>
    /// <param name="key">The message, as a row names itself.</param>
    /// <param name="cancellationToken">Abandons the read.</param>
    /// <returns>A task completing once the second read has been asked for.</returns>
    /// <remarks>
    /// The reader's act taken for the message in front of them, on the terms the reading pane already takes it: it is
    /// not written down, not carried to the next message, and not carried to this one the next time it is opened.
    /// </remarks>
    ValueTask ShowRemoteContentAsync(string key, CancellationToken cancellationToken);

    /// <summary>Takes the next page of the conversation onto the end of what has been read.</summary>
    /// <param name="cancellationToken">Abandons the page.</param>
    /// <returns>A task completing once the page has arrived or the attempt has been reported.</returns>
    ValueTask ShowMoreAsync(CancellationToken cancellationToken);

    /// <summary>Asks the deployment again, which is what a person presses when the conversation did not arrive.</summary>
    /// <param name="cancellationToken">Abandons the ask.</param>
    /// <returns>A task completing once the ask has been made.</returns>
    ValueTask AskAgainAsync(CancellationToken cancellationToken);
}
