// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;

namespace MailFathom.Client.Presentation.Messages;

/// <summary>The one message list a run has, drawn from wherever the mailbox tree says somebody is.</summary>
/// <remarks>
/// <para>
/// One for the run rather than one per screen, for the reason the tree and the workspace are: a model is built and
/// discarded as its view is navigated to and away from, so a list each screen kept would forget where it had scrolled
/// the moment somebody moved between spaces — and would ask the deployment for the same page again while doing it.
/// </para>
/// <para>
/// What is selected here is the workspace's scope rather than a state of this list's own. The product's <em>select and
/// ask</em> gesture is the whole reason: a question asked about four messages is asked about whatever the scope says is
/// selected, so a selection only the list knew about would be a selection nothing else could act on.
/// </para>
/// </remarks>
public interface IMessageList
{
    /// <summary>The loaded lines of the list, in the order it is read in.</summary>
    /// <remarks>
    /// A list feed rather than a feed of a list, so the three states this genuinely has are the framework's rather than
    /// each one remembered by a view: the page under way, the read that failed, and the place holding no mail to draw.
    /// </remarks>
    IListFeed<MessageRow> Rows { get; }

    /// <summary>What is selected in the list, which the workspace scope is written from.</summary>
    /// <remarks>
    /// Held as a state the list control writes rather than as something composed from clicks, because a multi-selection
    /// is the platform's own gesture — the modifiers, the range, the drag, and the touch selection mode all belong to
    /// the control, and a list that reimplemented them would feel like neither platform. The write is the view's, not
    /// MVUX's <c>Selection</c> operator: that operator keeps a list feed transient until a selector attaches, and the
    /// selector lives inside the <c>FeedView</c> that is waiting for the feed to leave progress.
    /// </remarks>
    IState<IImmutableList<MessageRow>> Chosen { get; }

    /// <summary>The order the list is read in and what it keeps, as the controls offering both are drawn from.</summary>
    IFeed<MessageListArrangement> Arrangement { get; }

    /// <summary>Whether there is more mail after what is loaded.</summary>
    IFeed<bool> HasMoreAfter { get; }

    /// <summary>Whether there is more mail before what is loaded, which there is once the window has moved on.</summary>
    IFeed<bool> HasMoreBefore { get; }

    /// <summary>Whether the last attempt to take another page did not arrive.</summary>
    /// <remarks>Its own answer rather than the list's error state, because a page that failed leaves what is already drawn on the screen — putting the whole list into an error would take a folder's worth of mail away over one request.</remarks>
    IFeed<bool> PagingFailed { get; }

    /// <summary>Takes the next page onto the end of what is loaded.</summary>
    /// <param name="cancellationToken">Abandons the page.</param>
    /// <returns>A task completing once the page has arrived or the attempt has been reported.</returns>
    ValueTask ShowMoreAsync(CancellationToken cancellationToken);

    /// <summary>Takes the previous page back onto the start of what is loaded.</summary>
    /// <param name="cancellationToken">Abandons the page.</param>
    /// <returns>A task completing once the page has arrived or the attempt has been reported.</returns>
    /// <remarks>What the window being bounded costs and what makes it affordable: the pages scrolled past are asked for again rather than kept.</remarks>
    ValueTask ShowEarlierAsync(CancellationToken cancellationToken);

    /// <summary>Reads the list again under a different order or different filters.</summary>
    /// <param name="arrangement">How the list is to be arranged.</param>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the list is being read again.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="arrangement" /> is <see langword="null" />.</exception>
    /// <remarks>Every cursor held under the old arrangement names a list that no longer exists, so the list is read from its leading end rather than continued.</remarks>
    ValueTask ArrangeAsync(MessageListArrangement arrangement, CancellationToken cancellationToken);

    /// <summary>Asks the deployment again, which is what a person presses when the list did not arrive.</summary>
    /// <param name="cancellationToken">Abandons the ask.</param>
    /// <returns>A task completing once the ask has been made.</returns>
    ValueTask AskAgainAsync(CancellationToken cancellationToken);
}
