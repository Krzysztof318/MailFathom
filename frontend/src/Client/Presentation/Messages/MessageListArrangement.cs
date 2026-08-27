// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Timeline;

namespace MailFathom.Client.Presentation.Messages;

/// <summary>How a message list is arranged: the order it is read in, and what it keeps.</summary>
/// <remarks>
/// <para>
/// One record rather than five properties spread over a model, because it is one thing on screen and one thing in a
/// request: a cursor names the list it was taken from, so changing any part of this invalidates every cursor held
/// under it and the list is read again from its leading end. Keeping the parts together is what makes that one
/// decision rather than five places to remember it.
/// </para>
/// <para>
/// It is remembered with the place rather than for the application, because how somebody reads their inbox and how
/// they read an archive are different questions. A place nobody has arranged reads newest first and keeps everything,
/// which is what a mail client does before it is told otherwise.
/// </para>
/// </remarks>
public sealed record MessageListArrangement
{
    /// <summary>How a list nobody has arranged is read.</summary>
    public static MessageListArrangement Default { get; } = new();

    /// <summary>Which end of the list leads.</summary>
    public MailTimelineOrder Order { get; init; } = MailTimelineOrder.NewestFirst;

    /// <summary>Whether only unread mail is kept.</summary>
    public bool UnreadOnly { get; init; }

    /// <summary>Whether only flagged mail is kept.</summary>
    public bool FlaggedOnly { get; init; }

    /// <summary>Whether only mail carrying an attachment is kept.</summary>
    public bool WithAttachmentsOnly { get; init; }

    /// <summary>Whether junk mail takes part where the place would otherwise leave it out.</summary>
    public bool IncludeJunk { get; init; }

    /// <summary>Gets whether the list is read oldest first, which is what the control offering the order is bound to.</summary>
    /// <remarks>Stated as its own affirmative rather than read backwards from the order, so a two-way binding has a boolean to write and nothing has to invert one.</remarks>
    public bool OldestFirst => this.Order is MailTimelineOrder.OldestFirst;

    /// <summary>Gets whether anything here narrows the list, which is what the indicator on the filter control is shown on.</summary>
    public bool KeepsLessThanEverything =>
        this.UnreadOnly || this.FlaggedOnly || this.WithAttachmentsOnly || this.IncludeJunk;

    /// <summary>Composes the request one page of this list is asked for with.</summary>
    /// <param name="place">Where the list is drawn from.</param>
    /// <param name="cursor">The cursor the page continues from, or <see langword="null" /> for the leading end.</param>
    /// <param name="direction">Which way the page continues from that cursor.</param>
    /// <param name="pageSize">How many rows the page may hold.</param>
    /// <returns>The query.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="place" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A filter that keeps everything is left unstated rather than sent as "both", so a request says what somebody
    /// narrowed and nothing else. The role a place names is written as the folder, because a special-use role taken
    /// across mailboxes is a folder reference on this surface rather than a parameter of its own.
    /// </remarks>
    public MailTimelineQuery QueryFor(
        MessagePlace place,
        string? cursor,
        MailTimelinePageDirection direction,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(place);

        return new MailTimelineQuery
        {
            Account = place.Account,
            Folder = place.Role is { } role ? $"role:{role}" : place.Folder,
            IncludeJunk = this.IncludeJunk || place.IsChosenFolder,
            Unread = this.UnreadOnly ? true : null,
            Flagged = this.FlaggedOnly ? true : null,
            HasAttachments = this.WithAttachmentsOnly ? true : null,
            Order = this.Order,
            Direction = direction,
            PageSize = pageSize,
            Cursor = cursor,
        };
    }
}
