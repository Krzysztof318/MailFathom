// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>Identifies one locally stored email independently from its remote occurrence identity.</summary>
public readonly record struct StoredEmailId
{
    private StoredEmailId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a stored-email identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated stored-email identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static StoredEmailId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A stored email identifier cannot be empty.", nameof(value));
        }

        return new StoredEmailId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
