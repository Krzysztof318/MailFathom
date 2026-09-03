// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Notifications;

/// <summary>Names the condition a notification was raised for, so raising it again while it is unread says nothing twice.</summary>
/// <remarks>
/// <para>
/// It is the condition rather than the occurrence, which is the whole of the deduplication rule: an account whose
/// credential is refused is refused again on every run until somebody acts, and a person who has not read the first
/// statement gains nothing from forty more. Once the statement has been read the key is free again, so the same
/// condition arising later is said again rather than suppressed forever.
/// </para>
/// <para>
/// It is MailFathom's own name for the condition and never anything read from mail: an account identifier and a fixed
/// word for what happened are what compose one, so the key carries no personal data beyond whose deployment it belongs
/// to.
/// </para>
/// </remarks>
public readonly record struct NotificationDeduplicationKey
{
    /// <summary>The longest key stored, which is generous for an account identifier and a fixed word.</summary>
    public const int MaximumLength = 256;

    private NotificationDeduplicationKey(string value) => this.Value = value;

    /// <summary>Gets the key text.</summary>
    public string Value { get; }

    /// <summary>Gets whether this key names a condition.</summary>
    /// <remarks>
    /// Being a struct, <see langword="default" /> is reachable and names none, carrying a <see langword="null" />
    /// <see cref="Value" /> that the deduplication index could not hold.
    /// </remarks>
    public bool IsSpecified => this.Value is not null;

    /// <summary>Creates a deduplication key from the condition's own name.</summary>
    /// <param name="value">The condition's name.</param>
    /// <returns>A validated deduplication key.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value" /> is longer than <see cref="MaximumLength" />.</exception>
    public static NotificationDeduplicationKey Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();

        ArgumentOutOfRangeException.ThrowIfGreaterThan(trimmed.Length, MaximumLength, nameof(value));

        return new NotificationDeduplicationKey(trimmed);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
