// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Scheduling;

/// <summary>States how much repeating this deployment carries, as the one number every reader of the declarations shares.</summary>
/// <remarks>
/// It is a constant rather than a setting, because it is not a policy anybody tunes: it is the point past which a
/// mailbox is not doing correspondence any more. A person has a handful of messages that repeat — a weekly report, a
/// monthly reminder — and a deployment approaching this number has something wrong with it rather than a bound to
/// raise.
/// </remarks>
public static class RecurringSendBounds
{
    /// <summary>The greatest number of declarations that dispatch at all.</summary>
    /// <remarks>
    /// It bounds the query a dispatch pass makes, which is what keeps that pass from growing with a table. It is a
    /// ceiling rather than a page, deliberately and visibly: the declarations are read oldest first, so a deployment
    /// that somehow passed it would have its newest declarations dispatch nothing rather than have every declaration
    /// dispatch late — and that is worth being a fault somebody notices instead of a delay nobody can account for.
    /// </remarks>
    public const int MaximumActiveDeclarations = 500;
}
