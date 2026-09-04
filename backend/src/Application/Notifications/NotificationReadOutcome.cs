// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Notifications;

/// <summary>What became of a request to change one notification's read state.</summary>
/// <remarks>
/// Three answers rather than two, because marking a notification unread is the one direction that can be refused by
/// something other than the notification's absence: the deduplication rule holds one unread notification per condition,
/// so a condition said again after this one was read already stands unread in its place.
/// </remarks>
public enum NotificationReadOutcome
{
    /// <summary>The notification now stands in the read state the request asked for, whether or not it had to move to get there.</summary>
    Applied = 0,

    /// <summary>The caller's own notifications hold none under that identifier, which is also the answer for one somebody else holds.</summary>
    NotFound = 1,

    /// <summary>Marking it unread would have put a second unread notification on a condition that already stands unread.</summary>
    ConditionAlreadyStanding = 2,
}
