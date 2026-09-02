// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>Identifies one durable job independently of the trigger it was enqueued for.</summary>
/// <remarks>
/// The idempotency identity is what decides whether a trigger enqueues a new job, and it is the job type together with
/// the key the enqueuer composed. This surrogate is what everything afterwards refers to the job by, so renewing a
/// lease or completing the work names one value rather than restating that pair.
/// </remarks>
public readonly record struct JobId
{
    private JobId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a job identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated job identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static JobId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A job identifier cannot be empty.", nameof(value));
        }

        return new JobId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
