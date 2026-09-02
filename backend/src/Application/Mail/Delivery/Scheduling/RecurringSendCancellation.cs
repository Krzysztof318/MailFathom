// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Scheduling;

/// <summary>States what became of a request to stop a message repeating.</summary>
/// <remarks>
/// There is no answer here for a request that came too late, and that is the difference between stopping a declaration
/// and stopping one message. A declaration has no moment past which it cannot be stopped: what it produces is future
/// occasions, and there are always none of those left once it is stopped. A message already written down for an
/// occasion that has passed is a message, and stopping it is the other act.
/// </remarks>
public enum RecurringSendCancellation
{
    /// <summary>The declaration was active, and it produces no further occurrence.</summary>
    Cancelled = 0,

    /// <summary>The declaration had already been stopped, so this request changed nothing.</summary>
    AlreadyCancelled = 1,

    /// <summary>No declaration carries that identifier, as far as this deployment holds one.</summary>
    NotFound = 2,
}
