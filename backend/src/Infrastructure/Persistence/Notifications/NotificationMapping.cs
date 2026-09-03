// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Notifications;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Notifications;

/// <summary>Turns a notification into the row it is stored as.</summary>
/// <remarks>
/// The target is flattened into its three columns here rather than being modelled as one, because the shape a row
/// carries is what a reader filters and joins on: a column per shape lets the message be a foreign key, which is what
/// erases a notification with the mail it leads to, and a serialized target could be neither.
/// </remarks>
internal static class NotificationMapping
{
    /// <summary>Maps one notification onto the row it is written as.</summary>
    /// <param name="notification">The notification to store.</param>
    /// <returns>The row.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notification" /> is <see langword="null" />.</exception>
    public static NotificationEntity ToEntity(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return new NotificationEntity
        {
            Id = notification.Id.Value,
            OwnerId = notification.Owner.Value,
            Kind = notification.Kind,
            Title = notification.Title,
            Body = notification.Body,
            Source = notification.Source,
            TargetKind = notification.Target.Kind,
            TargetStoredEmailId = notification.Target.Message?.Value,
            TargetScreen = notification.Target.Screen,
            DeduplicationKey = notification.DeduplicationKey.Value,
            OccurredAt = notification.OccurredAt,
            IsRead = notification.IsRead,
        };
    }
}
