// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;

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

    /// <summary>The width a subject too long to carry is reduced to, which is a lower-case SHA-256 in hexadecimal.</summary>
    private const int ReducedSubjectLength = 64;

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

    /// <summary>Composes a key from the name of a condition and the thing it is about.</summary>
    /// <param name="condition">MailFathom's own fixed word for what happened.</param>
    /// <param name="subject">What the condition is about, which is an account identifier today.</param>
    /// <returns>A validated deduplication key naming that condition about that subject.</returns>
    /// <exception cref="ArgumentException">Thrown when either part is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="condition" /> alone cannot fit within <see cref="MaximumLength" /> beside a reduced subject.</exception>
    /// <remarks>
    /// The subject is the unbounded half: an account identifier is the operator's own text and nothing validates its
    /// length, so a long enough one would push the composed key past <see cref="MaximumLength" /> and refuse every
    /// notification that account ever produced. Where the pair does not fit, the subject is replaced by a digest of
    /// itself rather than truncated, because two accounts sharing a long prefix would otherwise share one condition
    /// and silence each other's statements. The ordinary key stays the readable one; only an unreasonable identifier
    /// reaches the digest.
    /// </remarks>
    public static NotificationDeduplicationKey For(string condition, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(condition);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var trimmedCondition = condition.Trim();
        var trimmedSubject = subject.Trim();

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            trimmedCondition.Length + 1 + ReducedSubjectLength,
            MaximumLength,
            nameof(condition));

        var reachableSubject = trimmedCondition.Length + 1 + trimmedSubject.Length <= MaximumLength
            ? trimmedSubject
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(trimmedSubject)));

        return new NotificationDeduplicationKey($"{trimmedCondition}:{reachableSubject}");
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
