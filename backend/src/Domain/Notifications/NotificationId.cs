// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Notifications;

/// <summary>Identifies one notification, independently of whatever produced it.</summary>
/// <remarks>
/// It is an identity of its own rather than the identity of the thing it points at, because a notification points at
/// several kinds of thing and sometimes at none: two runs over one account leave two notifications, and a system
/// statement names no record at all.
/// </remarks>
public readonly record struct NotificationId
{
    private NotificationId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Gets whether this identifier names a notification.</summary>
    /// <remarks>
    /// Being a struct, <see langword="default" /> is reachable and addresses nothing; the private constructor is what
    /// keeps every other value validated, and this is what reports the one it cannot reach.
    /// </remarks>
    public bool IsSpecified => this.Value != Guid.Empty;

    /// <summary>Creates a notification identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated notification identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static NotificationId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A notification identifier cannot be empty.", nameof(value));
        }

        return new NotificationId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
