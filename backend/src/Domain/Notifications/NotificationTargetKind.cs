// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Notifications;

/// <summary>What kind of thing a notification leads to when somebody opens it.</summary>
public enum NotificationTargetKind
{
    /// <summary>The notification leads nowhere, which is what a statement with nothing to open says.</summary>
    Nothing = 0,

    /// <summary>The notification leads to one stored message.</summary>
    Message = 1,

    /// <summary>The notification leads to a screen rather than to a record.</summary>
    Screen = 2,
}
