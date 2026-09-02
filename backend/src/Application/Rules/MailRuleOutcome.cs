// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules;

/// <summary>What one rule concluded about one email.</summary>
/// <remarks>
/// Three values rather than a boolean, because a rule that could not be evaluated is neither of the other two. Folding
/// it into either would be the silent failure this whole contract exists to prevent: into a match, and a rule acts on
/// mail it never actually matched; into a non-match, and a rule that has stopped working looks exactly like a rule
/// nothing matches.
/// </remarks>
public enum MailRuleOutcome
{
    /// <summary>The condition answered that the email matches.</summary>
    Matched = 0,

    /// <summary>The condition answered that the email does not match.</summary>
    NotMatched = 1,

    /// <summary>The condition produced no answer, and the reason is recorded beside this.</summary>
    Failed = 2,
}
