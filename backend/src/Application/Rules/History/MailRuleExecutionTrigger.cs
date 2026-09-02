// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.History;

/// <summary>Which walk reached the email, which is the whole of an execution's identity beyond its rule.</summary>
/// <remarks>
/// The three are worth telling apart because they answer different questions. Mail is evaluated on arrival exactly once,
/// so an execution recorded there is what a rule did to a message as it came in; an execution recorded for a requested
/// run is what the rules concluded when somebody asked for the mailbox to be walked again, and the run it belongs to is
/// named beside it. A scheduled walk is neither: nobody asked for it and no mail arrived, so recording one as either of
/// the others would make the history answer a question the operator did not put.
/// </remarks>
public enum MailRuleExecutionTrigger
{
    /// <summary>The email was reached by the arrival walk, which evaluates mail no pass has evaluated before.</summary>
    Arrival = 0,

    /// <summary>The email was reached by a whole-mailbox run somebody asked for.</summary>
    RequestedRun = 1,

    /// <summary>The email was reached by a whole-mailbox run a rule's own declared schedule started.</summary>
    /// <remarks>
    /// Such a run walks the same mail a requested one does and reaches only the rules that declared the schedule
    /// trigger, which is why it is a walk of its own rather than a requested run with a different origin.
    /// </remarks>
    ScheduledRun = 2,
}
