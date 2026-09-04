// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
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
    /// <summary>Maps one stored row back onto the notification it holds.</summary>
    /// <param name="entity">The row read back.</param>
    /// <returns>The notification.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entity" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The three target columns are read back into the one shape they were flattened from, and a row whose columns
    /// name no shape this build declares reaches <see cref="NotificationTarget" />'s own refusal rather than being
    /// interpreted here.
    /// </remarks>
    public static Notification ToNotification(NotificationEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return Notification.Restore(
            NotificationId.Create(entity.Id),
            MailOwnerId.Create(entity.OwnerId),
            entity.Kind,
            entity.Title,
            entity.Body,
            entity.Source,
            TargetOf(entity),
            NotificationDeduplicationKey.Create(entity.DeduplicationKey),
            entity.OccurredAt,
            entity.IsRead);
    }

    /// <summary>Reads the three target columns back as the one shape they were flattened from.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the row names a shape without the column that shape needs.</exception>
    private static NotificationTarget TargetOf(NotificationEntity entity) => entity.TargetKind switch
    {
        NotificationTargetKind.Nothing => NotificationTarget.Nothing,
        NotificationTargetKind.Message when entity.TargetStoredEmailId is { } message =>
            NotificationTarget.ToMessage(StoredEmailId.Create(message)),
        NotificationTargetKind.Screen when entity.TargetScreen is { } screen =>
            NotificationTarget.ToScreen(screen),
        _ => throw new ArgumentOutOfRangeException(
            nameof(entity),
            entity.TargetKind,
            "A stored notification names a target shape without the column that shape is carried in."),
    };

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
