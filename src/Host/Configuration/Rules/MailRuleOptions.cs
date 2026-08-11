// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>One rule as an operator declares it: what it is called, what it matches, and what a match does to the pass.</summary>
/// <remarks>
/// The order rules appear in the configuration is the order they are evaluated in, so nothing here declares a position.
/// A rule that should run before another is moved above it in the file, which is the one place that ordering can be
/// read without cross-referencing anything.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailRuleOptions
{
    /// <summary>Gets or sets the name the rule is reported under.</summary>
    /// <remarks>
    /// Restricted to letters, digits, and the three separators, because the name is what a log line and a run record
    /// name a rule by. Everything else about a rule may carry an address the operator typed; the name is the one part
    /// this section can promise carries no such thing, and that promise is only worth having if it is enforced.
    /// </remarks>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    [RegularExpression("^[A-Za-z0-9][A-Za-z0-9 ._-]*$")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression deciding whether an email matches this rule.</summary>
    [Required]
    public string Condition { get; set; } = string.Empty;

    /// <summary>Gets or sets whether a match ends the pass rather than continuing to the rules below this one.</summary>
    public bool StopWhenMatched { get; set; }

    /// <summary>Gets or sets whether the rule takes part in a pass at all.</summary>
    /// <remarks>
    /// A rule switched off is left out of the bound set entirely, so it costs nothing and changes the set's revision
    /// exactly as removing it would. It exists so that a rule can be taken out of service without the condition being
    /// deleted and rewritten from memory later.
    /// </remarks>
    public bool Enabled { get; set; } = true;
}
