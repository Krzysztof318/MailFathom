// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend.Timeline;

namespace MailFathom.Client.Presentation.Messages;

/// <summary>One page a deployment answered with, together with the request that produced it.</summary>
/// <param name="Messages">The rows, in the order the list is sorted in.</param>
/// <param name="NextCursor">The cursor reading the page after this one, or <see langword="null" /> at the end of the list.</param>
/// <param name="PreviousCursor">The cursor reading the page before this one, or <see langword="null" /> at the beginning of the list.</param>
/// <param name="ReadCursor">The cursor this page was asked with, or <see langword="null" /> where it was read from the leading end.</param>
/// <param name="ReadDirection">Which way this page was asked for from <paramref name="ReadCursor" />.</param>
/// <remarks>
/// <para>
/// A page keeps the request that produced it because that pair is the only thing that reproduces it exactly. The two
/// cursors a page answers with name its own first and last row, and neither of them, asked in either direction, reads
/// the page they came from — so a list that remembered only those could reopen next to where somebody was rather than
/// at it. The pair here reopens the page itself.
/// </para>
/// <para>
/// The rows are this owner's own correspondence and carry the classification of everything else about mail: they are
/// held for as long as the window holds this page and are written nowhere.
/// </para>
/// </remarks>
internal sealed record MessagePage(
    IImmutableList<DeploymentMailMessage> Messages,
    string? NextCursor,
    string? PreviousCursor,
    string? ReadCursor,
    MailTimelinePageDirection ReadDirection)
{
    /// <summary>Reads what a deployment answered as a page of the window.</summary>
    /// <param name="answered">What the deployment served.</param>
    /// <param name="readCursor">The cursor the page was asked with.</param>
    /// <param name="readDirection">The direction the page was asked in.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="answered" /> is <see langword="null" />.</exception>
    internal static MessagePage Of(
        DeploymentMailTimelinePage answered,
        string? readCursor,
        MailTimelinePageDirection readDirection)
    {
        ArgumentNullException.ThrowIfNull(answered);

        return new MessagePage(
            [.. answered.Rows],
            answered.NextCursor,
            answered.PreviousCursor,
            readCursor,
            readDirection);
    }

    /// <summary>Gets whether the deployment answered this page with no mail at all.</summary>
    /// <remarks>
    /// Its own question because an empty page is not a page to keep: it carries no cursor at either end, so adding one
    /// to a window would take the window's own cursors away with it. What it does establish is that the list ends
    /// there, which is what the window does with it instead.
    /// </remarks>
    internal bool IsEmpty => this.Messages.Count is 0;
}
