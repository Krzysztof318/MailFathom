// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam;

/// <summary>What an operator decided about spam classification.</summary>
/// <remarks>
/// Every switch is off in <see cref="Disabled" />, which is what a deployment that configured nothing runs with: no
/// classification is attempted, no scanner is consulted, and no path is added for an operator who asked for none of it.
/// </remarks>
public sealed record SpamClassificationSettings
{
    private SpamClassificationSettings(
        bool isEnabled,
        bool usesScanner,
        IReadOnlyList<MailFolderAlias> scannedFolderAliases,
        double? scannerThreshold)
    {
        this.IsEnabled = isEnabled;
        this.UsesScanner = usesScanner;
        this.ScannedFolderAliases = scannedFolderAliases;
        this.ScannerThreshold = scannerThreshold;
        this.Profile = SpamClassificationProfile.Create(usesScanner, scannerThreshold);
    }

    /// <summary>Gets the settings a deployment that configured nothing runs with.</summary>
    public static SpamClassificationSettings Disabled { get; } = new(
        isEnabled: false,
        usesScanner: false,
        [],
        scannerThreshold: null);

    /// <summary>Gets whether classification runs at all.</summary>
    public bool IsEnabled { get; }

    /// <summary>Gets whether a configured scanner is consulted after the deterministic stage.</summary>
    /// <remarks>
    /// Off, only the deterministic stage runs, which is the whole working feature without a sidecar deployed. On without
    /// a scanner registered, the deterministic verdict is what a classification records — the switch is an operator's
    /// intent and the registration is whether an implementation exists, and the two are separate facts.
    /// </remarks>
    public bool UsesScanner { get; }

    /// <summary>Gets the folder aliases classification runs over, in a normalized order.</summary>
    /// <remarks>
    /// Empty means no folder is classified, which is only reachable while classification is off: a deployment that
    /// switches it on and names no folder is resolved to its accounts' inbox aliases before the settings are built, so
    /// the scope defaults to the inbox rather than to everything. Classifying every folder by default would spend the
    /// work on sent mail, on drafts, and on the archive, where a verdict answers no question anybody asked.
    /// </remarks>
    public IReadOnlyList<MailFolderAlias> ScannedFolderAliases { get; }

    /// <summary>Gets the score at or above which a scanner's verdict is spam, or <see langword="null" /> to keep the scanner's own.</summary>
    /// <remarks>
    /// It replaces the threshold in the scanner's answer rather than being compared beside it, so the record states one
    /// pair of numbers in one scale. It reaches no other stage: a provider header carries its own threshold in a scale
    /// this one knows nothing about.
    /// </remarks>
    public double? ScannerThreshold { get; }

    /// <summary>Gets the identity of the terms a verdict reached under these settings was decided by.</summary>
    /// <remarks>
    /// Derived once here rather than at each use, because it is a digest and every classification records it. What it
    /// covers and what it deliberately leaves out is <see cref="SpamClassificationProfile" />'s own contract.
    /// </remarks>
    public SpamClassificationProfile Profile { get; }

    /// <summary>Builds the settings an operator's answers describe.</summary>
    /// <param name="isEnabled">Whether classification runs.</param>
    /// <param name="usesScanner">Whether a configured scanner is consulted.</param>
    /// <param name="scannedFolderAliases">The folder aliases to classify, already resolved to the inbox where the operator named none.</param>
    /// <param name="scannerThreshold">The threshold to judge a scanner's score by, or <see langword="null" /> to keep the scanner's own.</param>
    /// <returns>The settings, with the alias list deduplicated and ordered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scannedFolderAliases" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="scannerThreshold" /> is not a finite number.</exception>
    /// <remarks>
    /// How long a message may wait on a verdict is not among these, because it is not one owner's to decide: it bounds
    /// how long the index may be held back by a scanner that has stopped answering, which is a cost the process bears.
    /// <see cref="SpamClassificationScope.MaximumClassificationWait" /> is where the deployment states it.
    /// </remarks>
    public static SpamClassificationSettings Create(
        bool isEnabled,
        bool usesScanner,
        IEnumerable<MailFolderAlias> scannedFolderAliases,
        double? scannerThreshold = null)
    {
        ArgumentNullException.ThrowIfNull(scannedFolderAliases);

        if (scannerThreshold is { } threshold && !double.IsFinite(threshold))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scannerThreshold),
                threshold,
                "A configured scanner threshold is a finite number.");
        }

        return new SpamClassificationSettings(
            isEnabled,
            usesScanner,
            [
                .. scannedFolderAliases
                    .DistinctBy(static alias => alias.Value, StringComparer.Ordinal)
                    .OrderBy(static alias => alias.Value, StringComparer.Ordinal),
            ],
            scannerThreshold);
    }

    /// <summary>Reports whether classification runs over one folder.</summary>
    /// <param name="folderAlias">MailFathom's own name for the folder.</param>
    /// <returns><see langword="true" /> when the configured scope names that alias.</returns>
    public bool Covers(MailFolderAlias folderAlias) => this.ScannedFolderAliases.Contains(folderAlias);
}
