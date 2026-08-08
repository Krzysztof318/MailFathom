// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Mutations.Audit;

/// <summary>Identifies one audit entry, independently of the mutation record it was written from.</summary>
/// <remarks>
/// It is its own identity rather than the mutation record's because the two have different lifetimes: the record is
/// operational state that ends when the mutation does, and the entry outlives it. An entry keyed by the record would
/// stop being addressable the moment the record it borrowed its key from was pruned.
/// </remarks>
public readonly record struct MailboxMutationAuditEntryId
{
    private MailboxMutationAuditEntryId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an audit entry identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated audit entry identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static MailboxMutationAuditEntryId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A mailbox mutation audit entry identifier cannot be empty.", nameof(value));
        }

        return new MailboxMutationAuditEntryId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
