// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseTimeline;

/// <summary>Which way a page continues from the cursor it was asked with.</summary>
/// <remarks>
/// <para>
/// It is a different question from <see cref="EmailTimelineDirection" /> and the two are needed together. That one says
/// which end of the timeline the list is sorted from and is part of what a cursor was issued for; this one says whether
/// the page being asked for lies after that cursor or before it. A person scrolling down a newest-first list and a
/// person scrolling back up it are reading the same order in both directions, so folding the two into one value would
/// make scrolling back a re-sort — a list that turned upside down under somebody's finger.
/// </para>
/// <para>
/// The order a page is returned in never changes with it. A backward page is read away from the cursor and handed back
/// in the sorted order, so a client appends or prepends what it received without reversing anything.
/// </para>
/// </remarks>
public enum TimelinePageDirection
{
    /// <summary>The page after the cursor in the sorted order, which is what continued scrolling asks for.</summary>
    Forward = 0,

    /// <summary>The page before the cursor in the sorted order, which is what scrolling back asks for.</summary>
    Backward = 1,
}
