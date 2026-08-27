// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Mailboxes;

/// <summary>How long ago a mailbox or a folder last took mail in, in the bands somebody decides on rather than as a duration.</summary>
/// <remarks>
/// <para>
/// Bands rather than a count, and that is a decision about language rather than about precision. "Updated 3 minutes
/// ago" has to be written twice in Polish and four times in a language with more plural forms, and the client would
/// carry plural rules for a sentence nobody reads twice. A band is one string per language and says the thing that is
/// actually being asked — whether what is on the screen is current enough to act on.
/// </para>
/// <para>
/// The gap is stated rather than left to be worked out from a timestamp, which is what the architecture asks of every
/// screen that shows a copy of a mailbox. It is deliberately not a judgement: nothing here calls a copy stale,
/// because how old is too old is the reader's to decide and not this client's.
/// </para>
/// </remarks>
public enum FreshnessGap
{
    /// <summary>Nothing has ever been taken in, so there is no gap to state and no copy to trust.</summary>
    Never = 0,

    /// <summary>Mail was taken in within the last hour.</summary>
    WithinTheHour = 1,

    /// <summary>Mail was taken in within the last day.</summary>
    Today = 2,

    /// <summary>Mail was taken in within the last week.</summary>
    WithinTheWeek = 3,

    /// <summary>Nothing has been taken in for more than a week.</summary>
    LongerAgo = 4,
}
