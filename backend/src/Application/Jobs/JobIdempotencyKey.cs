// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>Identifies one execution, in the words of whoever knows what the work is.</summary>
/// <remarks>
/// <para>
/// The key is opaque to the store and unique together with the job type across the whole table, terminal rows included.
/// It is derivable from the trigger alone, so an enqueuer composes it without reading the table first and a retried
/// enqueue is answered rather than refused.
/// </para>
/// <para>
/// It is composed of MailFathom's own names and identifiers — an account alias, a folder alias, an occurrence, a rule
/// identity and version — and never of a subject, an address, or anything else out of the message. An operator reads
/// this text when they ask what a stuck job is, so a digest would be shorter and would tell them nothing; mail content
/// in it would make the queue a second uncontrolled copy of personal data.
/// </para>
/// <para>
/// A row keeps its key in every terminal state, which is what stops the same trigger enqueuing the same work again. So
/// pruning terminal rows is what ends the deduplication, and whichever change adds pruning inherits a retention floor
/// of the longest window in which one trigger can legitimately fire again.
/// </para>
/// </remarks>
public sealed record JobIdempotencyKey
{
    /// <summary>The greatest length a key may have, which bounds the column it is stored in and the index over it.</summary>
    public const int MaximumLength = 256;

    private JobIdempotencyKey(string value) => this.Value = value;

    /// <summary>Gets the text two enqueues of the same work are compared by.</summary>
    public string Value { get; }

    /// <summary>Creates a key from the text the enqueuer composed.</summary>
    /// <param name="value">The composed identity of one execution.</param>
    /// <returns>A validated idempotency key.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank, carries a control character, or is longer than <see cref="MaximumLength" />.</exception>
    public static JobIdempotencyKey Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmedValue = value.Trim();

        if (trimmedValue.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"A job idempotency key may be at most {MaximumLength} characters long.",
                nameof(value));
        }

        // A control character would make the key unreadable in the query an operator asks what is stuck with, and it is
        // never part of a name an operator wrote or an identity MailFathom generated.
        if (trimmedValue.Any(char.IsControl))
        {
            throw new ArgumentException("A job idempotency key cannot contain a control character.", nameof(value));
        }

        return new JobIdempotencyKey(trimmedValue);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
