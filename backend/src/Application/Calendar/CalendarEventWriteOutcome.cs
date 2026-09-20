// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Calendar;

/// <summary>How a write to somebody's calendar ended.</summary>
/// <remarks>
/// Each value but the first is a refusal a person acts on and continues from — a title they can correct, a span they
/// can widen, an event somebody else already dealt with — which is why a write answers with a result rather than
/// raising. None of them is a failure to unwind through, and none of them names the event's own text.
/// </remarks>
public enum CalendarEventWriteOutcome
{
    /// <summary>The calendar holds what the caller asked for.</summary>
    Written = 0,

    /// <summary>That calendar holds no such event, which is equally what a caller naming somebody else's is told.</summary>
    NotFound = 1,

    /// <summary>The title is blank, too long, or carries a character that renders as nothing.</summary>
    TitleRefused = 2,

    /// <summary>The event states an end that is not after its start.</summary>
    EndNotAfterStart = 3,

    /// <summary>The event is already on the calendar, so there is no proposal left to accept.</summary>
    AlreadyOnTheCalendar = 4,
}
