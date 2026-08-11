// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>One of the two scanners: whether it runs, what it looks for, and what it leaves alone.</summary>
/// <remarks>
/// <para>
/// The switch decides whether the scanner runs at all and never what happens to a finding. With it off nothing is
/// scanned and nothing is redacted; with it on every consumer of that scanner fails closed, because an opt-in that
/// degraded to handing content through under load would be worse than no switch — the operator would believe it was in
/// force.
/// </para>
/// <para>
/// Both scanners take the same shape while being deployed differently, which is deliberate: which of them runs in this
/// process and which reaches a container beside it is not a distinction an operator configures.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class SensitiveContentScannerOptions
{
    /// <summary>Gets or sets whether this scanner runs. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets the categories to look for, which replace the scanner's defaults rather than adding to them.</summary>
    /// <remarks>
    /// Naming nothing yields the defaults, which are the product's opinion rather than an empty set. Naming anything
    /// yields exactly what is named, so a deployment that wants the defaults plus one category writes out the whole
    /// list — a list that added to a set it could not see would leave an operator unable to say what is being scanned
    /// for by reading their own file.
    /// </remarks>
    public IList<string> Categories { get; } = [];

    /// <summary>Gets the individual rules to silence inside categories that stay switched on.</summary>
    public IList<SensitiveContentRuleSuppressionOptions> Suppressions { get; } = [];
}
