// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Domain.Emails.Authorship;

namespace MailFathom.Mcp.Tools.Authorship;

/// <summary>Publishes how much an email's own text read as machine written.</summary>
/// <remarks>
/// <para>
/// One shape, published by every tool that names an email: a listing row, a search match, the single-email read, and an
/// answer's citation all carry this record, so a client written against one reads all four. What the text carried, and
/// the weighting the number came from, are published only by the single-email read, as
/// <see cref="ReportedAuthorshipEvidence" />.
/// </para>
/// <para>
/// The descriptions are the advertised output schema and therefore the whole of what a model reading a result is told,
/// which is why they carry the two things that are easy to get wrong about this value. It is a heuristic estimate and
/// not a measured probability, and it is informational rather than a safety signal — a great deal of ordinary
/// correspondence is drafted with a text generator by people who mean every word of it, and a reader taking a neutral
/// observation for a finding against the sender is worse off than one who was told nothing.
/// </para>
/// <para>
/// Nothing here characterizes the email or the sender's intent, and nothing here is computed when the email is read.
/// Both values were established when the message was stored and are published as they stand.
/// </para>
/// </remarks>
[Description("How much this email's own text reads as machine written — generated or drafted with an AI text model rather than typed. A heuristic estimate from the email's own characters, not a measurement and not a probability. It is informational only: it is not a spam verdict, not a risk score, and not a statement that the email is unwanted or unsafe.")]
internal sealed record ReportedMachineAuthorship
{
    /// <summary>Gets the coarse reading, which is the value a caller is expected to branch on.</summary>
    [Description("The reading of likelihood: 'likely' when the text carries enough of what machine-written text carries that a person typing it is the less likely reading, 'possible' when it carries some of it in a combination a person also reaches, 'unlikely' when it was read and carries little or none of it, and 'notAssessed' when nothing read it — which is what an email with no readable body carries, what a deployment that turned the assessment off records, and what mail stored before this deployment assessed anything carries until it is re-read. 'likely' is not an accusation and warrants no action on its own.")]
    public required MachineAuthorshipState State { get; init; }

    /// <summary>Gets how strongly the text read as machine written, from zero to one inclusive.</summary>
    [Description("How strongly the text read as machine written, from 0 to 1. A heuristic score rather than a probability: 0 means the text was read and carried nothing, and the scale has no top because no combination of these signals reaches certainty. It is 0 as well when state is 'notAssessed', where it means nothing at all — read state first. Two scores are comparable only within one deployment and one release; get_email_content publishes the profile the number came from.")]
    public required double Likelihood { get; init; }

    /// <summary>Publishes the assessment a read returned.</summary>
    /// <param name="assessment">The stored assessment to publish.</param>
    /// <returns>The wire representation of <paramref name="assessment" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assessment" /> is <see langword="null" />.</exception>
    public static ReportedMachineAuthorship From(MachineAuthorshipAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        return new ReportedMachineAuthorship
        {
            State = PublishedState(assessment.Band),
            Likelihood = assessment.Likelihood,
        };
    }

    /// <summary>Reads the published value the stored band names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a stored band has no published value, which means one was added to the domain without deciding what
    /// a client should be told about it.
    /// </exception>
    private static MachineAuthorshipState PublishedState(MachineAuthorshipBand band) => band switch
    {
        MachineAuthorshipBand.NotAssessed => MachineAuthorshipState.NotAssessed,
        MachineAuthorshipBand.Unlikely => MachineAuthorshipState.Unlikely,
        MachineAuthorshipBand.Possible => MachineAuthorshipState.Possible,
        MachineAuthorshipBand.Likely => MachineAuthorshipState.Likely,
        _ => throw new ArgumentOutOfRangeException(
            nameof(band),
            band,
            "The stored machine-authorship band has no published protocol value."),
    };
}
