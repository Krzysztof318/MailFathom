// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>What one enrichment pass did, in counts and in the condition that stopped it.</summary>
/// <param name="DerivedEmailCount">How many messages the pass settled a derivation for, including ones with nothing to say.</param>
/// <param name="MarkedEmailCount">How many of those carried at least one mark.</param>
/// <param name="StoppedBy">The condition that ended the pass early, or <see langword="null" /> where it ran to its own bound.</param>
/// <param name="EmailsRemain">Whether the account still holds mail awaiting a derivation.</param>
/// <remarks>
/// Counts and a reason, and nothing derived from a message: a mark is what the derivation wrote about somebody's mail,
/// so it never reaches a log, a span, or an instrument. The two counts differ by exactly the messages a derivation
/// found nothing worth marking on, which is the number an operator reads to tell an enrichment that is working from one
/// that is answering emptily.
/// </remarks>
public readonly record struct MailEnrichmentPassReport(
    int DerivedEmailCount,
    int MarkedEmailCount,
    EmailEnrichmentWithholding? StoppedBy,
    bool EmailsRemain)
{
    /// <summary>Gets whether the pass found nothing to do and nothing to report.</summary>
    /// <remarks>
    /// A deployment that has not turned enrichment on is in that state rather than stopped by something, so it is empty
    /// here although it names a withholding. The alternative would repeat one line per account per run for the life of
    /// every default deployment, saying each time that a feature nobody asked for is still off.
    /// </remarks>
    public bool IsEmpty =>
        this.DerivedEmailCount == 0
        && this.StoppedBy is null or EmailEnrichmentWithholding.NotActivated;
}
