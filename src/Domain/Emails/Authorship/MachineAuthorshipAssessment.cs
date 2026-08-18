// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authorship;

/// <summary>How much one message's own text reads as machine written, and what that reading was reached from.</summary>
/// <remarks>
/// <para>
/// A heuristic estimate and never a measurement. Nothing here proves who or what wrote a message, no model is asked,
/// and no external service is consulted: the whole answer comes from characters in the text the deployment already
/// stored. Every reader of it — a tool description, a page, an agent reading a result — is told so, because a number
/// between zero and one invites being read as a probability and this one is not.
/// </para>
/// <para>
/// It is an informational value and not a safety indicator. A high likelihood says the text reads as machine written;
/// it does not say the message is unwanted, dishonest, or dangerous, and a great deal of ordinary correspondence is
/// drafted with a text generator by people who mean every word of it. Nothing in this system files, flags, hides, or
/// refuses a message because of it, and no rule reads it — publishing it is the whole of what is done with it, and
/// what to make of it is the reader's.
/// </para>
/// <para>
/// The likelihood, the band, and the signals are one record because none of them is re-derivable from the others. The
/// signals say what the text carried, the likelihood is what this profile made of that combination, and the band is
/// the reading a caller branches on; a record keeping only the number could not be read back after a weight moved,
/// which is what <see cref="ProfileRevision" /> exists to make visible.
/// </para>
/// </remarks>
public sealed record MachineAuthorshipAssessment
{
    private MachineAuthorshipAssessment(
        MachineAuthorshipBand band,
        double likelihood,
        MachineAuthorshipSignals signals,
        MachineAuthorshipProfileRevision profileRevision)
    {
        this.Band = band;
        this.Likelihood = likelihood;
        this.Signals = signals;
        this.ProfileRevision = profileRevision;
    }

    /// <summary>Gets the assessment a message nothing read carries.</summary>
    /// <remarks>
    /// It is the state of three ordinary messages and not an error in any of them: one whose deployment does not assess
    /// authorship, one whose body yielded no words, and one stored before this deployment assessed anything. It carries
    /// no profile revision, which is what separates it from a reading that ran and found nothing — that reading is
    /// <see cref="MachineAuthorshipBand.Unlikely" /> with no signals, and it is a different statement.
    /// </remarks>
    public static MachineAuthorshipAssessment NotAssessed { get; } = new(
        MachineAuthorshipBand.NotAssessed,
        likelihood: 0,
        MachineAuthorshipSignals.None,
        MachineAuthorshipProfileRevision.None);

    /// <summary>Gets the coarse reading of <see cref="Likelihood" />, which is the value a caller branches on.</summary>
    public MachineAuthorshipBand Band { get; }

    /// <summary>Gets how strongly the text read as machine written, from zero to one inclusive.</summary>
    /// <remarks>
    /// Zero on a text that carried no signal at all, and never one: no combination of heuristics reaches certainty, so
    /// the scale deliberately has no top. It is meaningless without <see cref="ProfileRevision" />, which names the
    /// weighting that produced it.
    /// </remarks>
    public double Likelihood { get; }

    /// <summary>Gets what the text carried, which is what the likelihood was computed from.</summary>
    public MachineAuthorshipSignals Signals { get; }

    /// <summary>Gets the weighting the reading was reached under, or none where nothing read the text.</summary>
    public MachineAuthorshipProfileRevision ProfileRevision { get; }

    /// <summary>Gets whether anything read this message's text at all.</summary>
    public bool WasAssessed => this.Band != MachineAuthorshipBand.NotAssessed;

    /// <summary>Records what one profile made of the signals it read out of a message's text.</summary>
    /// <param name="band">The coarse reading of the likelihood.</param>
    /// <param name="likelihood">How strongly the text read as machine written, from zero to one inclusive.</param>
    /// <param name="signals">What the text carried.</param>
    /// <param name="profileRevision">The weighting the reading was reached under.</param>
    /// <returns>The assessment.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the band says nothing was read, or when the likelihood is not a finite number within the scale.
    /// </exception>
    /// <remarks>
    /// The not-assessed band is refused here rather than accepted and normalized, because a reading that ran is a
    /// different statement from one that did not and only <see cref="NotAssessed" /> may make the second. A
    /// non-finite likelihood is refused for the reason a spam score is: it would compare false against every threshold
    /// whichever way the comparison is written, which reads as an ordinary message rather than as an assessment that
    /// failed.
    /// </remarks>
    public static MachineAuthorshipAssessment Assessed(
        MachineAuthorshipBand band,
        double likelihood,
        MachineAuthorshipSignals signals,
        MachineAuthorshipProfileRevision profileRevision)
    {
        if (band == MachineAuthorshipBand.NotAssessed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(band),
                band,
                "A reading that ran carries a band that says what it found; the absence of a reading is NotAssessed itself.");
        }

        if (!double.IsFinite(likelihood) || likelihood is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(likelihood),
                likelihood,
                "An authorship likelihood is a finite number from zero to one inclusive.");
        }

        return new MachineAuthorshipAssessment(band, likelihood, signals, profileRevision);
    }
}
