// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Notifications;

namespace MailFathom.Application.Notifications;

/// <summary>Carries one bounded page of a person's notifications and the boundary the next page continues from.</summary>
/// <param name="Notifications">The notifications, newest first.</param>
/// <param name="NextCursor">The cursor a caller presents for the following page, or <see langword="null" /> when this page reached the end of the centre.</param>
/// <remarks>
/// The absent cursor is the end of the walk rather than a page that happened to be short: a page is only ever short
/// because the centre held nothing more, so a caller stops when the cursor stops instead of comparing the count against
/// the size it asked for.
/// </remarks>
public sealed record NotificationPage(
    IReadOnlyList<Notification> Notifications,
    NotificationCursor? NextCursor);
