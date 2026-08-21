// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules;

/// <summary>Hands out the rule set new passes run against.</summary>
/// <remarks>
/// <para>
/// A pass reads this once when it starts and holds what it was given for its duration. That is the whole of the reload
/// contract for rules: an edit reaches the next pass rather than one already running, so a pass finishes against the
/// revision it began with and nothing changes what a rule means halfway through an email.
/// </para>
/// <para>
/// Reading is deliberately not asynchronous and does not fail. The rule set was proven usable before it was published,
/// so a reader has nothing to handle and no reason to wait; a candidate that could not be proven never becomes the
/// current one.
/// </para>
/// </remarks>
public interface IMailRuleSetSource
{
    /// <summary>Gets the rule set a pass starting now runs against.</summary>
    MailRuleSet Current { get; }
}
