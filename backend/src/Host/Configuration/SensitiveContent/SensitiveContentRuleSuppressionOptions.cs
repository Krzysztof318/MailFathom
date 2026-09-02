// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>Silences one rule of a rule corpus without giving up the category around it.</summary>
/// <remarks>
/// The pair is written out rather than collapsed into one dotted string because the two halves are validated against
/// different things — the category against what the scanner declares, the rule against what that category holds — and a
/// single string would report a typo in either half as one unrecognized value.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class SensitiveContentRuleSuppressionOptions
{
    /// <summary>Gets or sets the category the rule belongs to, which must be one the scanner declares.</summary>
    /// <remarks>
    /// Naming a category here never switches it on. A suppression describes something inside a category that is already
    /// being looked for, so one naming a category this deployment does not look for changes nothing.
    /// </remarks>
    public string Category { get; set; } = string.Empty;

    /// <summary>Gets or sets the rule to silence, which must be one that category holds.</summary>
    public string Rule { get; set; } = string.Empty;

    /// <inheritdoc />
    public override string ToString() => $"{this.Category}:{this.Rule}";
}
