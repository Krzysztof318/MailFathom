// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Configures whether and where mail is classified as spam.</summary>
/// <remarks>
/// <para>
/// Every switch here is off when the section is absent, so a deployment that configures nothing classifies nothing,
/// consults no scanner, and adds no path. The section binds successfully with none of its keys set, which is what makes
/// the feature genuinely opt-in rather than merely defaulted off.
/// </para>
/// <para>
/// It is bound strictly, so a misspelled key fails startup naming itself instead of being ignored while the operator
/// reads their own file as proof of something that is switched off.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class SpamClassificationOptions : IValidatableObject
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "SpamClassification";

    /// <summary>The smallest scanner threshold an operator may configure.</summary>
    /// <remarks>
    /// Above zero because a threshold at or below it makes every message spam whatever a scanner answered, which is a
    /// configuration nobody means and one whose effect an operator would only discover on their own mail.
    /// </remarks>
    internal const double SmallestThreshold = 0.1;

    /// <summary>The largest scanner threshold an operator may configure.</summary>
    /// <remarks>
    /// Far above any score a rule corpus assigns in practice, and finite, so a value beyond it is a typed digit rather
    /// than an intent — a threshold nothing can reach silently switches the scanner's half of the feature off.
    /// </remarks>
    internal const double LargestThreshold = 1000;

    /// <summary>Gets or sets whether mail is classified at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets whether a configured scanner is consulted after the deterministic stage.</summary>
    /// <remarks>
    /// Independent of <see cref="Enabled" /> in the binding and dependent on it in effect: a scanner is consulted only
    /// where classification runs. Validation refuses the combination rather than normalizing it, so an operator who
    /// switched the scanner on and left classification off is told instead of being given the quiet answer.
    /// </remarks>
    public bool UseScanner { get; set; }

    /// <summary>Gets or sets the folder aliases classification runs over, or <see langword="null" /> to take each account's inbox.</summary>
    /// <remarks>
    /// Nullable so that leaving the key out and writing an empty list stay distinguishable, which an
    /// <c>IList</c>-typed property could not express: the binder builds an empty list for both. Absent is the default
    /// scope — every account's inbox mapping — and an explicitly empty list is an operator saying no folder, which is a
    /// legitimate way to switch the work off without switching the section off.
    /// </remarks>
    public string[]? ScannedFolders { get; set; }

    /// <summary>Gets or sets the score at or above which a scanner's verdict is spam, or <see langword="null" /> to take the scanner's own.</summary>
    /// <remarks>
    /// A scanner answers with the threshold it was configured with, and that is what a classification records unless
    /// this names another. Setting it is how an operator tightens or loosens a scanner they do not administer; it never
    /// reaches the deterministic stage, whose provider headers carry a threshold in a scale of their own.
    /// </remarks>
    public double? ScannerThreshold { get; set; }

    /// <summary>Gets or sets where the scanner daemon is and what one scan of a message may cost.</summary>
    /// <remarks>
    /// Always present so that a deployment which states none of its keys still binds and validates, and read only where
    /// <see cref="UseScanner" /> is on. What it holds is a container's address and this adapter's bounds rather than a
    /// decision about mail, which is why it is a block instead of four more keys beside the switches.
    /// </remarks>
    public SpamScannerOptions Scanner { get; set; } = new();

    /// <summary>Gets or sets what happens to mail a verdict calls junk.</summary>
    /// <remarks>
    /// Always present so that a deployment which states none of its keys still binds and validates. Both of its switches
    /// are off, which is what keeps a classification derived data by default: a verdict is recorded and the mailbox is
    /// left exactly as it was.
    /// </remarks>
    public SpamActionOptions Actions { get; set; } = new();

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => this.FindErrors();

    /// <summary>Finds everything about this section that would otherwise be discovered on somebody's mail.</summary>
    internal IEnumerable<ValidationResult> FindErrors()
    {
        if (this.UseScanner && !this.Enabled)
        {
            yield return new ValidationResult(
                $"{SectionName} asks for a scanner while classification is disabled, and a scanner is only consulted where classification runs. Set Enabled to true, or remove UseScanner.",
                [nameof(this.UseScanner)]);
        }

        foreach (var result in FindScannedFolderErrors(this.ScannedFolders))
        {
            yield return result;
        }

        foreach (var result in this.FindThresholdErrors())
        {
            yield return result;
        }

        foreach (var result in this.Scanner.FindErrors(this.UseScanner))
        {
            yield return result;
        }

        foreach (var result in this.Actions.FindErrors(this.Enabled))
        {
            yield return result;
        }
    }

    /// <summary>Refuses an alias that is not a value this system could have issued, naming the alias rather than the position.</summary>
    private static IEnumerable<ValidationResult> FindScannedFolderErrors(string[]? scannedFolders) =>
        (scannedFolders ?? [])
            .Where(static alias => !IsUsableAlias(alias))
            .Select(static alias => new ValidationResult(
                $"{SectionName} names scanned folder '{alias}', which is not a usable folder alias.",
                [nameof(ScannedFolders)]));

    private static bool IsUsableAlias(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return false;
        }

        try
        {
            _ = MailFolderAlias.Create(alias);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private IEnumerable<ValidationResult> FindThresholdErrors()
    {
        if (this.ScannerThreshold is not { } threshold)
        {
            yield break;
        }

        if (!double.IsFinite(threshold) || threshold < SmallestThreshold || threshold > LargestThreshold)
        {
            yield return new ValidationResult(
                $"{SectionName} declares a ScannerThreshold of {threshold.ToString(CultureInfo.InvariantCulture)}, and a threshold is between {SmallestThreshold.ToString(CultureInfo.InvariantCulture)} and {LargestThreshold.ToString(CultureInfo.InvariantCulture)}. A value outside that range either files every message or can never be reached.",
                [nameof(this.ScannerThreshold)]);
        }
    }
}
