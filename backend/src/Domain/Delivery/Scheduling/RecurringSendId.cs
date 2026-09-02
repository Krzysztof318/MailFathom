// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Scheduling;

/// <summary>Identifies one declaration that a message is sent again on every occasion a schedule names.</summary>
/// <remarks>
/// It outlives every occurrence it produces, which is what makes it the value both halves of cancelling name: stopping
/// one held occurrence names that occurrence's own record, and stopping the declaration names this. It is also half of
/// each occurrence's idempotency identity, so a value that changed would make the next occasion a first occasion.
/// </remarks>
public readonly record struct RecurringSendId
{
    private RecurringSendId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a recurring send identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated recurring send identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static RecurringSendId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A recurring send identifier cannot be empty.", nameof(value));
        }

        return new RecurringSendId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
