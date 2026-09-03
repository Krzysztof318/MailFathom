// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Notifications;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One thing that happened to a person while nobody was looking at the screen.</summary>
/// <remarks>
/// The owner is a value, as it is on every other table, and the message is an association — which is the one place
/// this row deliberately differs from the audit trails beside it. A trail records an act and has to outlive what it
/// acted on; a notification describes something a person can still open, so a row pointing at mail that has been
/// deleted is a row that leads nowhere and is erased with it.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class NotificationEntity
{
    public Guid Id { get; set; }

    /// <summary>Gets or sets the owner it happened to, as a value rather than as an association.</summary>
    public Guid OwnerId { get; set; }

    public NotificationKind Kind { get; set; }

    /// <summary>Gets or sets the headline the row is drawn with, derived when the notification was produced.</summary>
    public required string Title { get; set; }

    /// <summary>Gets or sets the second line the row is drawn with, derived when the notification was produced.</summary>
    public required string Body { get; set; }

    /// <summary>Gets or sets what the source line names beyond the kind, and <see langword="null" /> where the kind is the whole of it.</summary>
    public string? Source { get; set; }

    public NotificationTargetKind TargetKind { get; set; }

    /// <summary>Gets or sets the message the notification leads to, which is the association the row is erased through.</summary>
    public Guid? TargetStoredEmailId { get; set; }

    /// <summary>Gets or sets the message the notification leads to.</summary>
    public StoredEmailEntity? TargetStoredEmail { get; set; }

    /// <summary>Gets or sets the screen the notification leads to, and <see langword="null" /> for every other shape.</summary>
    public NotificationScreen? TargetScreen { get; set; }

    /// <summary>Gets or sets the condition this notification was raised for.</summary>
    /// <remarks>
    /// Unique among one owner's unread rows, which is what makes a repeated raise idempotent: a condition already
    /// standing unread is refused by the index rather than checked for before the insert, and only the index closes the
    /// window between the check and the write.
    /// </remarks>
    public required string DeduplicationKey { get; set; }

    /// <summary>Gets or sets when the thing this row describes happened.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Gets or sets whether the person has read it, which is also what frees the condition to be said again.</summary>
    public bool IsRead { get; set; }
}
