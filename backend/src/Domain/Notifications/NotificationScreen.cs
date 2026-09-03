// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Notifications;

/// <summary>The place in the client a notification leads to when it names no single record.</summary>
/// <remarks>
/// It names a destination rather than a route, because a route is the client's to spell and the two heads spell it
/// differently. Only the destinations something already leads to are declared: a member nothing produces would be a
/// promise about a screen that need not exist, and the set is appended to as producers arrive.
/// </remarks>
public enum NotificationScreen
{
    /// <summary>The mailbox the person reads their mail in.</summary>
    Mail = 0,

    /// <summary>The settings the person administers their accounts from.</summary>
    Settings = 1,
}
