// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Notifications;

/// <summary>What part of MailFathom a notification is about.</summary>
/// <remarks>
/// The kind says how a row is drawn and grouped rather than where it leads, which is why it is separate from
/// <see cref="NotificationTarget" />: a mail notification may lead to a message, to a screen, or nowhere, and so may a
/// calendar one. The four kinds nothing produces yet are declared because the stages that build calendars, tasks, and
/// cases write into this same record rather than into stores of their own, so the set is the record's rather than one
/// producer's.
/// </remarks>
public enum NotificationKind
{
    /// <summary>Something happened to the person's mail.</summary>
    Mail = 0,

    /// <summary>Something happened in the person's calendar.</summary>
    Calendar = 1,

    /// <summary>Something happened to a case the person is following.</summary>
    Case = 2,

    /// <summary>Something happened to one of the person's tasks.</summary>
    Task = 3,

    /// <summary>MailFathom itself has something to say, which is usually something that needs a person.</summary>
    System = 4,
}
