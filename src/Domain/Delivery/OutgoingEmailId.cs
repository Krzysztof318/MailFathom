// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery;

/// <summary>Identifies one durable outgoing record independently of the request it was written for.</summary>
/// <remarks>
/// The record's idempotency identity is what decides whether a request is a new send, and it is a composite of the
/// sending account and the authoring act. This surrogate is what everything afterwards refers to the record by, so
/// advancing a stage — or reaching the stored MIME — names one value rather than restating that composite.
/// </remarks>
public readonly record struct OutgoingEmailId
{
    private OutgoingEmailId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an outgoing message identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated outgoing message identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static OutgoingEmailId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An outgoing message identifier cannot be empty.", nameof(value));
        }

        return new OutgoingEmailId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
