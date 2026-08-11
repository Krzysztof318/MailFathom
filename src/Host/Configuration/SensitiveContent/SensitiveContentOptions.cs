// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.SensitiveContent;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>Configures what this deployment scans mail for before that mail is copied or handed out.</summary>
/// <remarks>
/// <para>
/// A configuration root of its own, because what a deployment scans for is a property of the deployment rather than of
/// its database, its mail accounts, or its AI providers, and because both switches below reach several of those at once.
/// </para>
/// <para>
/// Both scanners are off by default, and an absent section is that default rather than a startup failure. What the
/// section holds beside them is what one scan may spend, which every deployment has whether or not it scans anything.
/// </para>
/// <para>
/// The names configured under each switch are validated against what the registered scanners declare, which is a
/// separate pass rather than an attribute here: the answer depends on which scanners this build carries, so it is
/// reached through <see cref="SensitiveContentDeclarationRules" /> at startup instead of from a rule on a property.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class SensitiveContentOptions : IValidatableObject
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "SensitiveContent";

    /// <summary>Gets the scanner that looks for credentials, tokens, and keys, and runs inside this process.</summary>
    public SensitiveContentScannerOptions Secrets { get; } = new();

    /// <summary>Gets the scanner that looks for personal data, and reaches an analyzer deployed beside this process.</summary>
    public SensitiveContentScannerOptions Pii { get; } = new();

    /// <summary>Gets or sets the greatest number of characters one scan analyzes.</summary>
    /// <remarks>
    /// Text beyond it is dropped from the result rather than handed on unscanned. The default matches what a single
    /// content read may return in total, so an ordinary mail body is analyzed whole.
    /// </remarks>
    [Range(1, 10_000_000)]
    public int MaximumAnalyzedCharacters { get; set; } = SensitiveContentScanBounds.Default.MaximumAnalyzedCharacters;

    /// <summary>Gets or sets how long one call to one scanner may take before the operation it guards is refused.</summary>
    public TimeSpan ScanTimeout { get; set; } = SensitiveContentScanBounds.Default.ScanTimeout;

    /// <summary>Gets or sets how many scans may run at once across the process.</summary>
    [Range(1, 256)]
    public int MaximumConcurrentScans { get; set; } = SensitiveContentScanBounds.Default.MaximumConcurrentScans;

    /// <summary>Gets whether this deployment scans anything at all.</summary>
    /// <remarks>
    /// This is what the composition root registers on. With both switches off no plan is composed, no redactor exists,
    /// and no detector is constructed, so an opt-in nobody took costs nothing on any path.
    /// </remarks>
    public bool IsAnyScannerEnabled => this.Secrets.Enabled || this.Pii.Enabled;

    /// <summary>Finds the scanner options one switch is configured by.</summary>
    /// <param name="scanner">The switch to read.</param>
    /// <returns>The options under that switch.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value names no switch.</exception>
    public SensitiveContentScannerOptions For(SensitiveContentScannerKind scanner) => scanner switch
    {
        SensitiveContentScannerKind.Secrets => this.Secrets,
        SensitiveContentScannerKind.Pii => this.Pii,
        _ => throw new ArgumentOutOfRangeException(nameof(scanner), scanner, "The value names no configured scanner."),
    };

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // A range attribute cannot express a TimeSpan bound, and an unbounded one is worth refusing in both directions:
        // a budget below a second refuses ordinary mail on a busy machine, and one measured in minutes is a stall
        // rather than a timeout.
        if (this.ScanTimeout < TimeSpan.FromSeconds(1) || this.ScanTimeout > TimeSpan.FromMinutes(2))
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:ScanTimeout is {1}, which is outside the range one scan may take: at least one second and at most two minutes.",
                    SectionName,
                    this.ScanTimeout),
                [nameof(this.ScanTimeout)]);
        }
    }
}
