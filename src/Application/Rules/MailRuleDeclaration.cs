// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules;

/// <summary>One rule exactly as it was declared, which is what a rule set's revision identity is derived from.</summary>
/// <param name="Name">The name the rule is declared and reported under.</param>
/// <param name="ConditionText">The condition as the operator wrote it.</param>
/// <param name="StopWhenMatched">Whether a match ends the pass rather than continuing to the rules below.</param>
/// <param name="Accounts">The accounts the rule was scoped to, in declared order, empty for a rule that applies to every account.</param>
/// <remarks>
/// Deliberately separate from <see cref="MailRule" />, which carries a compiled condition and no authored text. A
/// declaration exists for the moment between binding and compiling: long enough to be hashed into a revision, and never
/// held afterwards. A condition can legitimately contain an address its author typed, so keeping the text out of the
/// rule that is passed around is what stops a run record that names a rule from carrying somebody's address with it.
/// </remarks>
public sealed record MailRuleDeclaration(
    string Name,
    string ConditionText,
    bool StopWhenMatched,
    IReadOnlyList<string> Accounts);
