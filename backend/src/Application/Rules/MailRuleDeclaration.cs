// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules.Actions;

namespace MailFathom.Application.Rules;

/// <summary>One rule exactly as it was declared, which is what a rule set's revision identity is derived from.</summary>
/// <param name="Name">The name the rule is declared and reported under.</param>
/// <param name="ConditionText">The condition as the operator wrote it.</param>
/// <param name="Actions">What a match does to the matching email, in the order the changes are applied.</param>
/// <param name="StopWhenMatched">Whether a match ends the pass rather than continuing to the rules below.</param>
/// <param name="Accounts">The accounts the rule was scoped to, in declared order, empty for a rule that applies to every account.</param>
/// <param name="Triggers">The automatic triggers the rule takes part in, in declared order, empty for a rule only a requested walk runs.</param>
/// <param name="Schedule">The occasions a scheduled walk happens on, and <see langword="null" /> for a rule declaring no schedule.</param>
/// <remarks>
/// <para>
/// Deliberately separate from <see cref="MailRule" />, which carries a compiled condition and no authored text. A
/// declaration exists for the moment between binding and compiling: long enough to be hashed into a revision, and never
/// held afterwards. A condition can legitimately contain an address its author typed, so keeping the text out of the
/// rule that is passed around is what stops a run record that names a rule from carrying somebody's address with it.
/// </para>
/// <para>
/// The actions are part of the declaration for the same reason the condition is: what a rule does to the mail it selects
/// is part of what the rule set means, so editing an action moves the revision and the edited rule asks the mailbox
/// afresh instead of being read as the request it already performed.
/// </para>
/// <para>
/// The triggers are the resolved set rather than the text the file named them with, so a rule that leaves the key out
/// and a rule that writes an empty list are one rule set rather than two, and so are two spellings of one trigger's
/// name: they mean the same thing, and a revision that told them apart would supersede a run over an edit that changed
/// nothing.
/// </para>
/// <para>
/// The schedule is the parsed recurrence rather than the text as well, and for the same reason: two spellings of one
/// occasion are one rule set, so a revision that told them apart would supersede a run over an edit that changed nothing
/// about when anything happens.
/// </para>
/// </remarks>
public sealed record MailRuleDeclaration(
    string Name,
    string ConditionText,
    IReadOnlyList<MailRuleAction> Actions,
    bool StopWhenMatched,
    IReadOnlyList<string> Accounts,
    IReadOnlyList<MailRuleTrigger> Triggers,
    JobRecurrence? Schedule = null);
