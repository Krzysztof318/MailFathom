// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Domain.Emails.Authorship;

namespace MailFathom.Mcp.Tools.Authorship;

/// <summary>Publishes what an email's text carried, which is what its authorship likelihood was computed from.</summary>
/// <remarks>
/// <para>
/// Only the single-email read publishes it, for the reason only that read publishes the sender evidence: a listing
/// exists to let a reader recognize a message, and this is how a reader judges a number on a message they have already
/// found. The number itself and its band travel with every result, as <see cref="ReportedMachineAuthorship" />.
/// </para>
/// <para>
/// The signals divide into two kinds that are worth very different things, and the descriptions say which is which
/// rather than leaving a caller to weigh eight names equally. The concealment signals are facts about the bytes — the
/// email carries characters no mail client renders — and are near-unambiguous. The prose signals are observations about
/// style, every one of which a careful writer also produces, and none of which means anything alone.
/// </para>
/// <para>
/// Nothing here is a finding against the email or its sender, and nothing here reproduces any part of the email's text:
/// what is published is which signals fired, never where or what they matched.
/// </para>
/// </remarks>
[Description("What this email's text carried that produced its machine-authorship likelihood, and the weighting that likelihood was computed under. Observations about the text, not findings against the email or its sender.")]
internal sealed record ReportedAuthorshipEvidence
{
    /// <summary>The order signals are published in, which is the order this deployment weighs them in.</summary>
    /// <remarks>
    /// A fixed table rather than a reflection over the flag set, so that a signal added to the domain without a
    /// published value of its own fails here rather than being silently dropped out of every result.
    /// </remarks>
    private static readonly (MachineAuthorshipSignals Stored, MachineAuthorshipSignal Published)[] PublishedSignals =
    [
        (MachineAuthorshipSignals.TagCharacters, MachineAuthorshipSignal.TagCharacters),
        (MachineAuthorshipSignals.VariationSelectorRun, MachineAuthorshipSignal.VariationSelectorRun),
        (MachineAuthorshipSignals.HiddenCharacters, MachineAuthorshipSignal.HiddenCharacters),
        (MachineAuthorshipSignals.BidirectionalOverrides, MachineAuthorshipSignal.BidirectionalOverrides),
        (MachineAuthorshipSignals.FormulaicFraming, MachineAuthorshipSignal.FormulaicFraming),
        (MachineAuthorshipSignals.UnspacedEmDashes, MachineAuthorshipSignal.UnspacedEmDashes),
        (MachineAuthorshipSignals.UnsolicitedListScaffolding, MachineAuthorshipSignal.ListScaffolding),
        (MachineAuthorshipSignals.UniformTypography, MachineAuthorshipSignal.UniformTypography),
    ];

    /// <summary>Every signal the table above publishes, which is what an assessment is checked against before it is read.</summary>
    private static readonly MachineAuthorshipSignals PublishableSignals = PublishedSignals.Aggregate(
        MachineAuthorshipSignals.None,
        static (published, signal) => published | signal.Stored);

    /// <summary>Gets what the text carried, strongest first, or empty when it carried nothing.</summary>
    [Description("What the text carried, strongest first, or empty when it carried nothing and empty as well when nothing read it. Concealment signals are facts about the email's characters and are close to unambiguous: 'tagCharacters' and 'variationSelectorRun' are invisible encodings that carry a hidden payload and have no legitimate use in mail, 'hiddenCharacters' are characters that render as nothing, and 'bidirectionalOverrides' reorder what a reader sees away from what the bytes say. Prose signals are observations about style that a careful writer also produces and that mean nothing individually: 'formulaicFraming', 'unspacedEmDashes', 'listScaffolding', and 'uniformTypography'. A concealment signal is worth knowing about on its own; a single prose signal is not.")]
    public required IReadOnlyList<MachineAuthorshipSignal> Signals { get; init; }

    /// <summary>Gets the weighting the likelihood was reached under, or <see langword="null" /> where nothing assessed the email.</summary>
    [Description("An opaque identifier for the weighting the likelihood was computed under, or null when nothing assessed this email. Two likelihoods carrying the same value are directly comparable; two carrying different values were reached under different weightings and should not be compared as numbers. It is not a version to act on and carries no meaning of its own.")]
    public string? ProfileRevision { get; init; }

    /// <summary>Publishes the evidence behind an assessment a read returned.</summary>
    /// <param name="assessment">The stored assessment to publish the evidence of.</param>
    /// <returns>The wire representation of what the text carried.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assessment" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the assessment carries a signal with no published value, which means one was added to the domain
    /// without deciding what a client should be told about it.
    /// </exception>
    public static ReportedAuthorshipEvidence From(MachineAuthorshipAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        if ((assessment.Signals & ~PublishableSignals) != MachineAuthorshipSignals.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assessment),
                assessment.Signals,
                "The assessment carries a machine-authorship signal with no published protocol value.");
        }

        return new ReportedAuthorshipEvidence
        {
            Signals =
            [
                .. PublishedSignals
                    .Where(signal => assessment.Signals.HasFlag(signal.Stored))
                    .Select(static signal => signal.Published),
            ],
            ProfileRevision = assessment.ProfileRevision.NamesAProfile ? assessment.ProfileRevision.Value : null,
        };
    }
}
