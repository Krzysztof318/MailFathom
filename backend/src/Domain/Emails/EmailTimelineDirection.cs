// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>Selects which end of a mailbox timeline a page is read from.</summary>
/// <remarks>
/// The two directions are exact reverses of one another rather than two independent orderings, which is what keeps a
/// page boundary meaningful in both: a cursor taken while reading one direction names the same row in the other, and
/// the timeline index serves the second direction as a backward scan of itself. Where undated mail lands therefore
/// follows from the direction instead of being a separate decision — last when the newest is read first, first when
/// the oldest is.
/// </remarks>
public enum EmailTimelineDirection
{
    /// <summary>Reads the most recently received message first, placing undated mail after every dated message.</summary>
    NewestFirst = 0,

    /// <summary>Reads the least recently received message first, placing undated mail before every dated message.</summary>
    OldestFirst = 1,
}
