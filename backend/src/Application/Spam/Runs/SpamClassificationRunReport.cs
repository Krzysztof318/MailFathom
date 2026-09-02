// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Runs;

/// <summary>What one account run's share of a whole-mailbox classification run produced.</summary>
/// <param name="Profile">The settings the run is bound to, unspecified when no run was carried.</param>
/// <param name="Walk">What the pass did, or <see langword="null" /> when the account had no run outstanding.</param>
/// <param name="Ending">How the run ended during this pass, or <see langword="null" /> when it is still outstanding.</param>
/// <remarks>
/// An absent walk and an empty one are held apart deliberately: no run outstanding is silence, while a run that reached
/// no mail this time is something an operator watching a mailbox being walked has to be able to see.
/// </remarks>
public sealed record SpamClassificationRunReport(
    SpamClassificationProfile Profile,
    SpamClassificationWalk? Walk,
    SpamClassificationRunEnding? Ending)
{
    /// <summary>Gets the report of a pass that found no run to carry.</summary>
    public static SpamClassificationRunReport NoRun { get; } = new(default, Walk: null, Ending: null);
}
