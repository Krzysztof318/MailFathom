// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Calendar.Extraction;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>What one enrichment pass did, in counts and in the conditions that stopped it.</summary>
/// <param name="DerivedEmailCount">How many messages the pass settled a derivation for, including ones with nothing to say.</param>
/// <param name="MarkedEmailCount">How many of those carried at least one mark.</param>
/// <param name="ProposedEventCount">How many calendar proposals the pass wrote, counted across every calendar they reached.</param>
/// <param name="StoppedBy">The condition that ended the pass early while deriving marks, or <see langword="null" /> where none did.</param>
/// <param name="ProposalsStoppedBy">The condition that ended the pass early while reading dates, or <see langword="null" /> where none did.</param>
/// <param name="EmailsRemain">Whether the account still holds mail awaiting a derivation.</param>
/// <remarks>
/// <para>
/// Counts and reasons, and nothing derived from a message: a mark is what the derivation wrote about somebody's mail
/// and a proposal names what they are doing on a day, so neither ever reaches a log, a span, or an instrument. The two
/// message counts differ by exactly the messages a derivation found nothing worth marking on, which is the number an
/// operator reads to tell an enrichment that is working from one that is answering emptily.
/// </para>
/// <para>
/// The two stopping conditions stay apart because they name different work. Both halves of the pass are a provider
/// call and either can be refused first, and folding them into one field would report a spent allowance without saying
/// which half met it — which is what an operator weighing whether to raise a ceiling is asking.
/// </para>
/// </remarks>
public readonly record struct MailEnrichmentPassReport(
    int DerivedEmailCount,
    int MarkedEmailCount,
    int ProposedEventCount,
    EmailEnrichmentWithholding? StoppedBy,
    CalendarEventExtractionWithholding? ProposalsStoppedBy,
    bool EmailsRemain)
{
    /// <summary>Gets whether the pass found nothing to do and nothing to report.</summary>
    /// <remarks>
    /// A deployment that has not turned enrichment on is in that state rather than stopped by something, so it is empty
    /// here although it names a withholding. The alternative would repeat one line per account per run for the life of
    /// every default deployment, saying each time that a feature nobody asked for is still off. The same reading
    /// applies to a deployment that derives marks and proposes no events.
    /// </remarks>
    public bool IsEmpty =>
        this.DerivedEmailCount == 0
        && this.StoppedBy is null or EmailEnrichmentWithholding.NotActivated
        && this.ProposalsStoppedBy is null or CalendarEventExtractionWithholding.NotActivated;
}
