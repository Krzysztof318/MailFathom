// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Domain.Notifications;

namespace MailFathom.Application.Notifications;

/// <summary>Marks where one page of a person's notifications ended, so the next page continues from it.</summary>
/// <remarks>
/// <para>
/// The centre is ordered newest first by the instant a notification describes, with the identifier breaking a tie, and
/// this pairs those two values. That is what makes the walk keyset-based rather than offset-based, and the choice
/// matters more here than on a record nobody writes to while it is read: the client polls, so a notification raised
/// between two pages would shift an offset window and repeat or skip a row on every page after it.
/// </para>
/// <para>
/// The fingerprint every keyset cursor here carries is the owner rather than a set of filters, because the owner is
/// the only thing this reading narrows by and it comes off the credential rather than out of the request. So a cursor
/// is refused when it is presented by somebody it was not issued to, which is a signed-out and signed-in-again client
/// resuming a stale walk rather than an attack — the page it would continue is scoped to the caller's own owner either
/// way.
/// </para>
/// <para>
/// It carries no secret and needs no signature: every value in it is one the caller already received. The encoded form
/// is <see cref="KeysetCursorPayload" />'s, which every keyset cursor here shares.
/// </para>
/// </remarks>
public readonly record struct NotificationCursor
{
    private NotificationCursor(DateTimeOffset occurredAt, NotificationId notificationId, string ownerFingerprint)
    {
        this.OccurredAt = occurredAt;
        this.NotificationId = notificationId;
        this.OwnerFingerprint = ownerFingerprint;
    }

    /// <summary>Gets the instant the last notification the page returned describes.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>Gets the identity of that notification, which breaks a tie between two describing the same instant.</summary>
    public NotificationId NotificationId { get; }

    /// <summary>Gets the fingerprint of the owner this cursor was issued for.</summary>
    public string OwnerFingerprint { get; }

    /// <summary>Reduces an owner to the short stable text a cursor carries to prove whose walk it belongs to.</summary>
    /// <param name="owner">The owner the page was read for.</param>
    /// <returns>The fingerprint.</returns>
    public static string FingerprintOf(MailOwnerId owner) =>
        PageFilterFingerprint.Of(owner.Value.ToString("N", CultureInfo.InvariantCulture));

    /// <summary>Creates the cursor that continues a walk after one position in the centre.</summary>
    /// <param name="occurredAt">The instant the page ended on.</param>
    /// <param name="notificationId">The identity of the notification at that instant.</param>
    /// <param name="ownerFingerprint">The fingerprint of the owner the page was read for.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ownerFingerprint" /> is blank.</exception>
    public static NotificationCursor After(
        DateTimeOffset occurredAt,
        NotificationId notificationId,
        string ownerFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerFingerprint);

        return new NotificationCursor(occurredAt, notificationId, ownerFingerprint);
    }

    /// <summary>Reads a cursor a caller presented.</summary>
    /// <param name="text">The encoded cursor, as a previous page returned it.</param>
    /// <param name="cursor">The decoded cursor when the text is one this version issued; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the text decoded into a usable cursor; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Every notification describes a known instant, so a payload carrying none names no boundary here and is refused.
    /// Whether a decoded cursor belongs to the caller is a separate question its <see cref="OwnerFingerprint" />
    /// answers, and one this method deliberately does not ask.
    /// </remarks>
    public static bool TryDecode(string? text, out NotificationCursor? cursor)
    {
        cursor = null;

        if (!KeysetCursorPayload.TryDecode(text, out var payload) || payload.Position is not { } occurredAt)
        {
            return false;
        }

        cursor = new NotificationCursor(
            occurredAt,
            NotificationId.Create(payload.Identity),
            payload.FilterFingerprint);

        return true;
    }

    /// <summary>Writes the cursor as the opaque string a caller presents to continue the walk.</summary>
    /// <returns>The encoded cursor.</returns>
    public string Encode() =>
        KeysetCursorPayload.At(this.OccurredAt, this.NotificationId.Value, this.OwnerFingerprint).Encode();
}
