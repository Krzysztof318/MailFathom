// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Answering.Audit;

/// <summary>Identifies one entry of an answering audit trail, independently of the run it records.</summary>
/// <remarks>
/// It is its own identity rather than the run's because one run leaves an entry per account in its scope, so the run
/// identifier addresses a set rather than a row. It is also what a continuation cursor names to break a tie between two
/// entries that ended in the same instant.
/// </remarks>
public readonly record struct MailAnsweringAuditEntryId
{
    private MailAnsweringAuditEntryId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an audit entry identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated audit entry identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static MailAnsweringAuditEntryId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A mail answering audit entry identifier cannot be empty.", nameof(value));
        }

        return new MailAnsweringAuditEntryId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
