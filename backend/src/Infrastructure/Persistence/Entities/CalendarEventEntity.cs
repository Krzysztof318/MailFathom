// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Calendar;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One event of one person's calendar, whether they put it there or mail proposed it to them.</summary>
/// <remarks>
/// <para>
/// The calendar is the person's, so the row keys onto the user record and cascades from it: erasing a user takes their
/// calendar with them rather than leaving the days they had planned behind. It carries no mail account, because a
/// person assigned two mailboxes still has one day, and the message an event came from is what records which mail was
/// involved.
/// </para>
/// <para>
/// <see cref="SourceStoredEmailId" /> is a pointer and nothing more. Nothing of the message is copied here, and the key
/// is cleared rather than cascaded when the message goes — an event somebody accepted is theirs, and erasing the mail
/// it was found in is not a reason to take the meeting off their calendar.
/// </para>
/// <para>
/// Every column but the identity, the owner, and the origin is personal data: a title says who somebody is meeting,
/// and the times say when they are not somewhere else. It is held under the same terms as the mail beside it and
/// erasing it is a delete rather than a mark.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class CalendarEventEntity
{
    /// <summary>The longest title stored, which is the bound the domain value already refuses to exceed.</summary>
    internal const int MaximumTitleLength = CalendarEventTitle.MaximumLength;

    /// <summary>The longest imported identifier stored, which is the bound the domain value already refuses to exceed.</summary>
    internal const int MaximumImportedUidLength = ImportedCalendarEventUid.MaximumLength;

    public Guid Id { get; set; }

    /// <summary>Gets or sets the person whose calendar holds this event.</summary>
    /// <remarks>
    /// The one column every read narrows on, and it never changes: an event is not moved between calendars, and
    /// accepting a proposal leaves it on the calendar it was proposed to.
    /// </remarks>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets what the event is called, as whoever wrote it down wrote it.</summary>
    public required string Title { get; set; }

    /// <summary>Gets or sets when the event begins.</summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>Gets or sets when the event ends, or <see langword="null" /> when nothing said how long it lasts.</summary>
    /// <remarks>An end and a duration are one fact, so the end is stored and the duration is derived from it.</remarks>
    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>Gets or sets whether the event is on the calendar or offered to it.</summary>
    public CalendarEventOrigin Origin { get; set; }

    /// <summary>Gets or sets the message this event came out of, or <see langword="null" /> when no message named it.</summary>
    public Guid? SourceStoredEmailId { get; set; }

    /// <summary>Gets or sets the identifier the file this event was imported from named it by, or <see langword="null" /> when none did.</summary>
    /// <remarks>
    /// Unique within one calendar where it is present, which is the whole of what makes importing a file twice create
    /// nothing the second time. Two calendars holding one identifier is ordinary rather than a conflict: two people
    /// handed the same file each keep their own copy of what it described.
    /// </remarks>
    public string? ImportedUid { get; set; }

    /// <summary>Gets or sets when this event was first written here.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Gets or sets when it was last amended or accepted.</summary>
    public DateTimeOffset AmendedAt { get; set; }

    /// <summary>Gets or sets PostgreSQL's own row version, which is what a write over a row somebody else moved fails on.</summary>
    /// <remarks>
    /// An event is amended in place — retitled, moved, accepted — so it is exactly the mutable record
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
    /// requires a token on. What it settles above all is an event deleted while an amendment was in flight: the write
    /// then affects no row, which the token turns into a conflict, so the retry reads a calendar holding nothing and
    /// answers so rather than putting the event back.
    /// </remarks>
    public uint ConcurrencyVersion { get; set; }
}
