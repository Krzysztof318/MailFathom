// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery.Governance;

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>What this deployment has decided about a caller asking it to send something.</summary>
/// <param name="UnvouchedRecipients">What to do about a recipient the caller named that nothing this deployment holds vouches for.</param>
/// <remarks>
/// One value today, and a record rather than that value passed on its own, because what a caller may be talked into is
/// a group of decisions an operator makes together: the confirmation
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0013-what-a-caller-must-do-before-mail-leaves.md">ADR 0013</see>
/// leaves to its own contract belongs beside this one rather than in a second registration nothing relates to it.
/// </remarks>
public sealed record AuthoredSendSettings(UnvouchedRecipientPosture UnvouchedRecipients)
{
    /// <summary>Gets the settings of a deployment that decided nothing, which refuse nothing.</summary>
    public static AuthoredSendSettings Permissive { get; } = new(UnvouchedRecipientPosture.Admit);
}
