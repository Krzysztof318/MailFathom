// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authorship;

/// <summary>What each authorship signal is worth, and where the bands between the results fall.</summary>
/// <remarks>
/// <para>
/// The weighing is separated from the reading for the reason a trust policy is separated from an authentication
/// verdict: what a message's text carries is a fact about the message and stays true, while what that combination is
/// worth is a judgement this deployment makes and may make differently later. Keeping them apart is what lets the
/// second change without the first changing under it, and <see cref="Revision" /> is what says which judgement a stored
/// answer was reached under.
/// </para>
/// <para>
/// The weights are the project's rather than an operator's, and there is deliberately no configuration for them. A
/// number an operator can move is a number every stored answer has to be read against, and the value of the assessment
/// is that two messages in one mailbox are comparable; what an operator decides is whether the assessment runs at all,
/// which is a different question and the one <see cref="Disabled" /> answers.
/// </para>
/// </remarks>
public sealed class MachineAuthorshipProfile
{
    /// <summary>The likelihood at or above which a message reads as likely machine written.</summary>
    private const double LikelyFrom = 0.65;

    /// <summary>The likelihood at or above which a message reads as possibly machine written.</summary>
    private const double PossibleFrom = 0.30;

    private readonly IReadOnlyDictionary<MachineAuthorshipSignals, double> weights;

    private MachineAuthorshipProfile(IReadOnlyDictionary<MachineAuthorshipSignals, double> weights)
    {
        this.weights = weights;
        this.Revision = MachineAuthorshipProfileRevision.Of(weights, [PossibleFrom, LikelyFrom]);
    }

    /// <summary>Gets the weighting this release judges by.</summary>
    /// <remarks>
    /// <para>
    /// The two groups are an order of magnitude apart, and the gap is the point. A concealment signal says the message
    /// carries characters a mail client never renders, which a person typing does not produce and a program assembling
    /// text does; the strongest of them is close to conclusive on its own. A prose signal says the writing is shaped
    /// the way generated writing is shaped, which a careful writer also reaches, so every prose weight sits below the
    /// lowest band boundary: no single one of them moves a message out of the unlikely band at all, and it takes
    /// several of them together to reach the middle one.
    /// </para>
    /// <para>
    /// Within concealment, the ranking follows how much legitimate use the characters have left. Tag characters and a
    /// long variation-selector run encode a payload and have no other purpose in mail at all; invisible characters and
    /// direction controls have real if narrow ones, which the reading already narrows further before it reports them.
    /// </para>
    /// </remarks>
    public static MachineAuthorshipProfile Standard { get; } = new(new Dictionary<MachineAuthorshipSignals, double>
    {
        [MachineAuthorshipSignals.TagCharacters] = 0.90,
        [MachineAuthorshipSignals.VariationSelectorRun] = 0.80,
        [MachineAuthorshipSignals.HiddenCharacters] = 0.60,
        [MachineAuthorshipSignals.BidirectionalOverrides] = 0.50,
        [MachineAuthorshipSignals.FormulaicFraming] = 0.25,
        [MachineAuthorshipSignals.UnspacedEmDashes] = 0.22,
        [MachineAuthorshipSignals.UnsolicitedListScaffolding] = 0.22,
        [MachineAuthorshipSignals.UniformTypography] = 0.18,
    });

    /// <summary>Gets the profile of a deployment that has turned the assessment off.</summary>
    /// <remarks>
    /// It weighs nothing, so it reads no text and every message it meets carries
    /// <see cref="MachineAuthorshipAssessment.NotAssessed" /> — the same state as a message whose body yielded no
    /// words and as one stored before this deployment assessed anything. That is deliberate rather than a shortcut: all
    /// three are the absence of a reading, and a deployment that turned the assessment off must not be distinguishable
    /// from one that never ran it, or the column would say something about the operator rather than about the mail.
    /// </remarks>
    public static MachineAuthorshipProfile Disabled { get; } =
        new(new Dictionary<MachineAuthorshipSignals, double>());

    /// <summary>Gets the revision that names this weighting, which every answer it produces is stored with.</summary>
    public MachineAuthorshipProfileRevision Revision { get; }

    /// <summary>Gets whether this profile reads anything at all.</summary>
    public bool IsActive => this.weights.Count > 0;

    /// <summary>Reads a message's text and judges how much of it reads as machine written.</summary>
    /// <param name="deliveredText">The body as it was delivered, or <see langword="null" /> when the message yielded none.</param>
    /// <param name="writtenText">The body with quoted history and signatures removed, or <see langword="null" /> when the message yielded none.</param>
    /// <returns>
    /// The assessment, which is <see cref="MachineAuthorshipAssessment.NotAssessed" /> where this profile reads nothing
    /// or where the message carried no text to read.
    /// </returns>
    /// <remarks>
    /// A message with no text is not assessed rather than assessed as unlikely, because there is nothing to have read.
    /// The two are different statements and stay apart: the second says a reading ran and found nothing, which is a
    /// fact about a message that had words.
    /// </remarks>
    public MachineAuthorshipAssessment Assess(string? deliveredText, string? writtenText)
    {
        if (!this.IsActive || (string.IsNullOrEmpty(deliveredText) && string.IsNullOrEmpty(writtenText)))
        {
            return MachineAuthorshipAssessment.NotAssessed;
        }

        var signals = MachineAuthorshipSignalReader.Read(deliveredText, writtenText);
        var likelihood = this.Weigh(signals);

        return MachineAuthorshipAssessment.Assessed(BandOf(likelihood), likelihood, signals, this.Revision);
    }

    /// <summary>Combines what the signals are worth into one likelihood.</summary>
    /// <remarks>
    /// Each weight is read as the chance that its signal alone accounts for the text, and the combination is the chance
    /// that at least one of them does — so signals reinforce each other without any of them being able to push the
    /// result past the scale, and adding a signal to a message can only raise its likelihood. A sum with a cap would
    /// have neither property: two moderate signals would outweigh one conclusive one, and everything above the cap
    /// would read identically.
    /// </remarks>
    private double Weigh(MachineAuthorshipSignals signals)
    {
        var absent = this.weights
            .Where(weight => signals.HasFlag(weight.Key))
            .Aggregate(1.0, static (remaining, weight) => remaining * (1 - weight.Value));

        return Math.Clamp(1 - absent, 0, 1);
    }

    private static MachineAuthorshipBand BandOf(double likelihood) => likelihood switch
    {
        >= LikelyFrom => MachineAuthorshipBand.Likely,
        >= PossibleFrom => MachineAuthorshipBand.Possible,
        _ => MachineAuthorshipBand.Unlikely,
    };
}
