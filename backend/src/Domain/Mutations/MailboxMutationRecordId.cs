// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Mutations;

/// <summary>Identifies one durable mutation record independently of the request it was written for.</summary>
/// <remarks>
/// The record's idempotency identity is what decides whether a request is a new mutation, and it is a composite of the
/// occurrence, the requester, and the mutation. This surrogate is what everything afterwards refers to the record by, so
/// advancing a stage names one value rather than restating that composite.
/// </remarks>
public readonly record struct MailboxMutationRecordId
{
    private MailboxMutationRecordId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a mutation record identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated mutation record identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static MailboxMutationRecordId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A mailbox mutation record identifier cannot be empty.", nameof(value));
        }

        return new MailboxMutationRecordId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
