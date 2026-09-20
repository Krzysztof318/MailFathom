// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Calendar.Extraction;
using MailFathom.Application.Emails.Enrichment;

namespace MailFathom.AI.CalendarEvents;

/// <summary>Reads nothing, on a deployment that has not turned the extraction on.</summary>
/// <remarks>
/// <para>
/// The default state of an instance, and what every deployment gets until an operator says otherwise. It exists as a
/// registration rather than as an absent one because the answer a caller needs is a reason it can act on, and a
/// missing service would have made every caller carry a null check and decide for itself what an absence meant.
/// </para>
/// <para>
/// Nothing is read and nothing is sent. Neither the passages nor the typed sentence is touched at all, so an instance
/// in this state cannot disclose either however often it is asked, which is the property the switch exists to give and
/// would not have if the refusal came after the turn had been composed.
/// </para>
/// </remarks>
internal sealed class InactiveCalendarEventExtractor : ICalendarEventExtractor
{
    /// <summary>The one instance, since it holds nothing and answers everything the same way.</summary>
    internal static readonly InactiveCalendarEventExtractor Instance = new();

    private InactiveCalendarEventExtractor()
    {
    }

    /// <inheritdoc />
    public bool IsActive => false;

    /// <inheritdoc />
    public Task<CalendarEventExtraction> ProposeFromEmailAsync(
        EnrichableEmail email,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            CalendarEventExtraction.Withholding(CalendarEventExtractionWithholding.NotActivated));
    }

    /// <inheritdoc />
    public Task<CalendarEventExtraction> DraftFromDescriptionAsync(
        CalendarEventDescription description,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(description);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            CalendarEventExtraction.Withholding(CalendarEventExtractionWithholding.NotActivated));
    }
}
