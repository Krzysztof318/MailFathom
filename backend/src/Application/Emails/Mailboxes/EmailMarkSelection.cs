// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Emails.Enrichment;

namespace MailFathom.Application.Emails.Mailboxes;

/// <summary>Selects mail by a reading a derivation already made about it, rather than by anything the message itself carries.</summary>
/// <remarks>
/// <para>
/// It reads what an enrichment pass stored and derives nothing: a caller asks for mail carrying a mark of one aspect,
/// or a mark whose commitment falls due inside a range, and the answer is whichever mail already has one. A deployment
/// that never enriched anything therefore answers with no mail rather than with a refusal, which is the honest reading
/// of "the mail somebody has said this about".
/// </para>
/// <para>
/// Every criterion stated here is met by <b>one</b> mark. A message carrying an undated commitment and a separate
/// commitment due tomorrow matches a range naming tomorrow because of the second mark; a message whose only commitment
/// is undated matches nothing. Spreading the criteria across two marks would answer "carries a commitment, and
/// separately has something due this week", which is a different question that no reader could tell from this one by
/// looking at the result.
/// </para>
/// <para>
/// The due bounds select dated commitments alone. A commitment that named no date compares to neither bound, exactly as
/// an undated email compares to neither bound of a received range, and for the same reason: it is not known to fall
/// inside the span the caller asked about.
/// </para>
/// </remarks>
public sealed record EmailMarkSelection
{
    /// <summary>Marks a criterion nobody named in <see cref="CanonicalText" />, where no value written beside it can be it.</summary>
    /// <remarks>An aspect is one of three words and an instant is digits, so neither produces this text and no caller can reach it.</remarks>
    private const string CanonicalAbsentValue = "-";

    private EmailMarkSelection(EmailEnrichmentAspect? aspect, DateTimeOffset? dueOnOrAfter, DateTimeOffset? dueBefore)
    {
        this.Aspect = aspect;
        this.DueOnOrAfter = dueOnOrAfter;
        this.DueBefore = dueBefore;
        this.CanonicalText = ComputeCanonicalText(aspect, dueOnOrAfter, dueBefore);
    }

    /// <summary>Gets the reading the mark must be, or <see langword="null" /> when any reading matches.</summary>
    public EmailEnrichmentAspect? Aspect { get; }

    /// <summary>Gets the inclusive start of the range the commitment falls due in, in UTC, or <see langword="null" /> for no start.</summary>
    public DateTimeOffset? DueOnOrAfter { get; }

    /// <summary>Gets the exclusive end of that range, in UTC, or <see langword="null" /> for no end.</summary>
    /// <remarks>The end is exclusive so consecutive weeks tile a calendar without both claiming the instant they meet.</remarks>
    public DateTimeOffset? DueBefore { get; }

    /// <summary>Gets the injective text of every criterion, which the selection wrapping this one writes into its own.</summary>
    public string CanonicalText { get; }

    /// <summary>Validates and normalizes what a request asked of the readings a message carries.</summary>
    /// <param name="aspect">The reading the mark must be, or <see langword="null" /> for any reading.</param>
    /// <param name="dueOnOrAfter">The inclusive start of the range the commitment falls due in, or <see langword="null" /> for no start.</param>
    /// <param name="dueBefore">The exclusive end of that range, or <see langword="null" /> for no end.</param>
    /// <returns>The validated selection, or <see langword="null" /> where the request narrowed by no reading at all.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="aspect" /> is not a defined member.</exception>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when the due range can select nothing.</exception>
    /// <remarks>
    /// A request naming none of the three is answered with <see langword="null" /> rather than with a selection that
    /// admits everything, so nothing narrows a list by a criterion that says nothing — and the canonical text of a list
    /// nobody narrowed this way stays what it was before this filter existed only where the wrapping selection says so.
    /// </remarks>
    public static EmailMarkSelection? Create(
        EmailEnrichmentAspect? aspect,
        DateTimeOffset? dueOnOrAfter,
        DateTimeOffset? dueBefore)
    {
        if (aspect is { } named && !Enum.IsDefined(named))
        {
            throw new ArgumentOutOfRangeException(
                nameof(aspect),
                aspect,
                "A mark is one of the readings a derivation makes, and no other value names one.");
        }

        if (aspect is null && dueOnOrAfter is null && dueBefore is null)
        {
            return null;
        }

        if (dueOnOrAfter is { } start && dueBefore is { } end && start >= end)
        {
            throw MailboxQueryFilterInvalidException.EmptyRange("commitment due range");
        }

        // Held as instants rather than at the offsets a caller wrote, for the reason the received range is: Npgsql binds
        // a timestamptz parameter at offset zero alone, and two callers naming one instant are one query.
        return new EmailMarkSelection(aspect, dueOnOrAfter?.ToUniversalTime(), dueBefore?.ToUniversalTime());
    }

    private static string ComputeCanonicalText(
        EmailEnrichmentAspect? aspect,
        DateTimeOffset? dueOnOrAfter,
        DateTimeOffset? dueBefore) => string.Concat(
        MailboxEmailSelection.LengthPrefixed(aspect is { } named ? named.ToString() : CanonicalAbsentValue),
        MailboxEmailSelection.LengthPrefixed(CanonicalInstant(dueOnOrAfter)),
        MailboxEmailSelection.LengthPrefixed(CanonicalInstant(dueBefore)));

    private static string CanonicalInstant(DateTimeOffset? instant) => instant is { } value
        ? value.UtcTicks.ToString(CultureInfo.InvariantCulture)
        : CanonicalAbsentValue;
}
