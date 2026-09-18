// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Contacts.Correspondence;

/// <summary>Bounds the correlation a contact is read with, so opening one person never costs a walk over a mailbox.</summary>
/// <remarks>
/// <para>
/// Three bounds rather than one, because they stop three different things. The window keeps the query off years of
/// mail somebody is not looking at; the scan keeps the rows the database returns proportional to a card rather than to
/// a correspondence; and the two list bounds keep what leaves this deployment proportional to what a screen draws.
/// </para>
/// <para>
/// The scan is what makes the conversations a whole answer rather than a sample of one. A message names a
/// conversation, so the distinct conversations among the most recent <see cref="ScannedMessages" /> messages naming
/// this person are what the list is cut from — which is the most recent <see cref="Threads" /> of them unless this
/// person wrote more than that many messages inside one exchange, and a person who did is somebody whose recent
/// correspondence really is that exchange.
/// </para>
/// </remarks>
public static class ContactCorrespondenceBounds
{
    /// <summary>How far back the correlation reaches, in days.</summary>
    /// <remarks>
    /// A year rather than everything, because what an opened contact answers is <em>what have we been doing</em> rather
    /// than <em>what have we ever done</em>. Mail older than this is still reachable through search, which is the
    /// surface written for a question about the whole mailbox.
    /// </remarks>
    public const int WindowDays = 365;

    /// <summary>The greatest number of messages one correlation reads before it cuts its lists.</summary>
    /// <remarks>
    /// The bound the database is actually held to, and the reason neither list needs an aggregate over the window: the
    /// rows come back in the order the lists are cut in, so the work is a bounded keyset walk of an indexed column
    /// rather than a grouping over everything the window admits.
    /// </remarks>
    public const int ScannedMessages = 200;

    /// <summary>The greatest number of conversations one correlation publishes.</summary>
    public const int Threads = 10;

    /// <summary>The greatest number of documents one correlation publishes.</summary>
    public const int Documents = 10;
}
