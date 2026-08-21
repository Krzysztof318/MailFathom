// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Contacts;

/// <summary>Identifies one person the contact book holds, independently of every address they use.</summary>
/// <remarks>
/// The identity is MailFathom's own and never an address, because an address is a thing a person has rather than a thing
/// they are: one person uses several, gives one up, and gains another, and a book keyed on an address could not say that
/// any of those was the same person. It is also the only identity a log line, a metric, or a failure may name, since it
/// is the one part of a contact record that is not personal data.
/// </remarks>
public readonly record struct ContactId
{
    private ContactId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a contact identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated contact identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static ContactId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A contact identifier cannot be empty.", nameof(value));
        }

        return new ContactId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
