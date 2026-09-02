// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>Identifies one re-derivation of a scope's stored mail, independently of the scope it walks.</summary>
/// <remarks>
/// The scope is what makes a run unique while it is outstanding, and it is the key of the row the run is kept in. What
/// this adds is an identity a finished run does not share with the next one over the same scope, which is what lets the
/// jobs carrying a run be told apart from the jobs that carried the last one: a job's idempotency key is kept for as
/// long as its row is, terminal states included, so a second run keyed by the scope alone would be answered with the
/// job that finished the first one and would never start.
/// </remarks>
public readonly record struct StoredMailRederivationRunId
{
    private StoredMailRederivationRunId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a run identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated run identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static StoredMailRederivationRunId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A re-derivation run identifier cannot be empty.", nameof(value));
        }

        return new StoredMailRederivationRunId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
