// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Timeline;

namespace MailFathom.Client.Presentation.Messages;

/// <summary>Where a message list was left, so that coming back to a folder is coming back rather than starting over.</summary>
/// <param name="PlaceKey">The place this was written for, as <see cref="MessagePlace.RememberedAs" /> composes it.</param>
/// <param name="Cursor">The cursor the leading loaded page was asked with, or <see langword="null" /> where it was read from the leading end of the list.</param>
/// <param name="Direction">Which way that page was asked for from <paramref name="Cursor" />.</param>
/// <param name="Arrangement">The order and the filters the list was read under, which the cursor was issued against.</param>
/// <remarks>
/// <para>
/// The cursor and the arrangement travel together because neither is usable without the other: a cursor names the list
/// it was taken from, so a deployment refuses one presented against different filters. Remembering the pair is what
/// makes returning a continuation instead of a refusal.
/// </para>
/// <para>
/// The position is remembered to the page rather than to the pixel. A page is the unit a cursor names, so reopening
/// puts somebody back among the mail they were reading; where inside that page the list had been scrolled is not
/// written down, and the list opens at its top.
/// </para>
/// </remarks>
public sealed record RememberedMessageList(
    string PlaceKey,
    string? Cursor,
    MailTimelinePageDirection Direction,
    MessageListArrangement Arrangement)
{
    /// <summary>A place nobody has read yet, which opens at the leading end of the list arranged as a list arrives.</summary>
    /// <param name="placeKey">The place being opened.</param>
    /// <returns>What a first visit remembers, which is nothing beyond where it is.</returns>
    public static RememberedMessageList Nothing(string placeKey) =>
        new(placeKey, Cursor: null, MailTimelinePageDirection.Forward, MessageListArrangement.Default);
}
