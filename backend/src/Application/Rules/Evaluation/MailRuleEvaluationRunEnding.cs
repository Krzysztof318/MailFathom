// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>How a requested whole-mailbox run stopped being outstanding.</summary>
/// <remarks>
/// A run that is still outstanding has no ending, which is what the absence of a value means rather than an unremarkable
/// one. Both members are terminal: nothing resumes a run that has an ending, and asking for another one is a new request.
/// </remarks>
public enum MailRuleEvaluationRunEnding
{
    /// <summary>The run reached the end of the account's mail under the rule set it started with.</summary>
    Completed = 0,

    /// <summary>The rule set changed while the run was outstanding, so it stopped rather than finishing under rules it did not start with.</summary>
    /// <remarks>
    /// A run is bound to one revision, and MailFathom keeps only the rule set its configuration currently declares — so
    /// a reload leaves no way to finish the run as itself. Ending it here says that plainly instead of quietly applying
    /// two rule sets to one mailbox, and the remedy is to ask for the run again under the rules now in force.
    /// </remarks>
    Superseded = 1,
}
