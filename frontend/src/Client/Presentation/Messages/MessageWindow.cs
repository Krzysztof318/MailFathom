// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend.Timeline;

namespace MailFathom.Client.Presentation.Messages;

/// <summary>The part of a message list that is loaded: a bounded run of pages, and the cursors continuing it.</summary>
/// <remarks>
/// <para>
/// A window rather than everything that has been paged, and that is the whole design. A folder holds as much mail as it
/// holds, and a list that kept every page somebody scrolled past would hold a mailbox's worth of objects on a phone —
/// so the window keeps <see cref="MaximumPages" /> of them and drops the far one as it takes a near one. The count of
/// loaded rows is therefore a property of this type rather than of how long somebody scrolled.
/// </para>
/// <para>
/// Dropping a page is only safe because a page keeps the request that produced it: the page that becomes the far end
/// carries the cursor that reads back towards what was dropped, so scrolling back is asking for it again rather than
/// having kept it. That is why paging runs in both directions here where a list that only ever grew would need one.
/// </para>
/// <para>
/// The place and the arrangement travel on the window because a page arriving for a list somebody has already left is
/// an ordinary race rather than an error: a request in flight when the folder changed is answered by a window that
/// declines to take it, without the caller having to hold a token to compare.
/// </para>
/// </remarks>
internal sealed record MessageWindow
{
    /// <summary>How many pages the window holds before it drops one from the far end.</summary>
    /// <remarks>
    /// Four rather than a number of rows, because a page is the unit a cursor names and a window cut mid-page could not
    /// be scrolled back into. At the page size the list asks for it is a few hundred rows: enough that a reader
    /// scrolling steadily never reaches the seam, and bounded whatever the folder holds.
    /// </remarks>
    internal const int MaximumPages = 4;

    private MessageWindow(
        MessagePlace place,
        MessageListArrangement arrangement,
        IImmutableList<MessagePage> pages)
    {
        this.Place = place;
        this.Arrangement = arrangement;
        this.Pages = pages;

        // Materialized once here rather than projected on each read, because the view binds it, the shape reduces it,
        // and a deferred query would walk every page again for each of them.
        this.Messages = [.. pages.SelectMany(page => page.Messages)];
    }

    /// <summary>Gets where the loaded pages were drawn from.</summary>
    internal MessagePlace Place { get; }

    /// <summary>Gets the order and the filters the loaded pages were read under.</summary>
    internal MessageListArrangement Arrangement { get; }

    /// <summary>Gets the loaded pages, in the order the list is read in.</summary>
    internal IImmutableList<MessagePage> Pages { get; }

    /// <summary>Gets every loaded row, in the order the list is read in.</summary>
    internal IImmutableList<DeploymentMailMessage> Messages { get; }

    /// <summary>Opens a window on the first page of a list.</summary>
    /// <param name="place">Where the list is drawn from.</param>
    /// <param name="arrangement">The order and the filters it is read under.</param>
    /// <param name="page">The page that was read.</param>
    /// <returns>The window, holding no page where the deployment answered with nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    internal static MessageWindow Opening(
        MessagePlace place,
        MessageListArrangement arrangement,
        MessagePage page)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(page);

        return new MessageWindow(
            place,
            arrangement,
            page.IsEmpty ? [] : [page]);
    }

    /// <summary>Opens a window that has read nothing, which is what a list holds before a place has been asked for.</summary>
    /// <param name="place">Where the list would be drawn from.</param>
    /// <param name="arrangement">The order and the filters it would be read under.</param>
    /// <returns>The empty window.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    internal static MessageWindow Nothing(MessagePlace place, MessageListArrangement arrangement)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(arrangement);

        return new MessageWindow(place, arrangement, []);
    }

    /// <summary>Gets the cursor the page after this window is asked with, or <see langword="null" /> at the end of the list.</summary>
    internal string? ForwardCursor => this.Pages.Count is 0 ? null : this.Pages[^1].NextCursor;

    /// <summary>Gets the cursor the page before this window is asked with, or <see langword="null" /> at its beginning.</summary>
    internal string? BackwardCursor => this.Pages.Count is 0 ? null : this.Pages[0].PreviousCursor;

    /// <summary>Gets whether there is more mail after what is loaded.</summary>
    internal bool HasMoreAfter => this.ForwardCursor is not null;

    /// <summary>Gets whether there is more mail before what is loaded.</summary>
    internal bool HasMoreBefore => this.BackwardCursor is not null;

    /// <summary>Gets whether this window has nothing to draw.</summary>
    internal bool IsEmpty => this.Messages.Count is 0;

    /// <summary>Gets the request that reopens the leading page of this window, or <see langword="null" /> where none is loaded.</summary>
    /// <remarks>What is written down when somebody leaves the folder, and what puts them back where they were when they return.</remarks>
    internal MessagePage? LeadingPage => this.Pages.Count is 0 ? null : this.Pages[0];

    /// <summary>Answers whether this window is of a given list.</summary>
    /// <param name="place">Where a read was asked for.</param>
    /// <param name="arrangement">What that read ran under.</param>
    /// <returns><see langword="true" /> where this window is of that same list.</returns>
    /// <remarks>
    /// What a caller asks before acting on a read it started: a window loaded for a different place or under a
    /// different arrangement is a list somebody has already left, and everything the abandoned read would have said
    /// about it is stale.
    /// </remarks>
    internal bool IsOf(MessagePlace place, MessageListArrangement arrangement) =>
        this.Place == place && this.Arrangement == arrangement;

    /// <summary>Takes one more page onto the window, dropping the far one where the bound is reached.</summary>
    /// <param name="page">The page that was read.</param>
    /// <param name="direction">Which end of the window it was read from.</param>
    /// <param name="place">Where the read was asked for, which has to be this window's own.</param>
    /// <param name="arrangement">What the read ran under, which has to be this window's own.</param>
    /// <returns>The extended window, or this one unchanged where the page belongs to a list this window is no longer of.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A page that came back empty is the list having ended between two requests rather than a page to keep: mail can be
    /// expunged, so a cursor that named a row can outlive it. The cursor at that end is dropped instead, which stops the
    /// list asking for the same nothing again.
    /// </remarks>
    internal MessageWindow Extended(
        MessagePage page,
        MailTimelinePageDirection direction,
        MessagePlace place,
        MessageListArrangement arrangement)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(arrangement);

        if (!this.IsOf(place, arrangement))
        {
            return this;
        }

        if (page.IsEmpty)
        {
            return this.WithoutTheCursorAt(direction);
        }

        return direction is MailTimelinePageDirection.Forward
            ? this.With(Bounded(this.Pages.Add(page), droppedFromTheStart: true))
            : this.With(Bounded(this.Pages.Insert(0, page), droppedFromTheStart: false));
    }

    /// <summary>Keeps the window within its bound by dropping the page furthest from the one just taken.</summary>
    private static IImmutableList<MessagePage> Bounded(
        IImmutableList<MessagePage> pages,
        bool droppedFromTheStart) =>
        pages.Count <= MaximumPages
            ? pages
            : pages.RemoveAt(droppedFromTheStart ? 0 : pages.Count - 1);

    /// <summary>Marks the end the empty page was read from as the end of the list.</summary>
    private MessageWindow WithoutTheCursorAt(MailTimelinePageDirection direction)
    {
        if (this.Pages.Count is 0)
        {
            return this;
        }

        return direction is MailTimelinePageDirection.Forward
            ? this.With(this.Pages.SetItem(this.Pages.Count - 1, this.Pages[^1] with { NextCursor = null }))
            : this.With(this.Pages.SetItem(0, this.Pages[0] with { PreviousCursor = null }));
    }

    private MessageWindow With(IImmutableList<MessagePage> pages) =>
        new(this.Place, this.Arrangement, pages);
}
