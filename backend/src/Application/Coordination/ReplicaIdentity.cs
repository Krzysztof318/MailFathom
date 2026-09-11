// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Coordination;

/// <summary>Names one replica of a deployment, in the words an operator finds that process's log by.</summary>
/// <remarks>
/// <para>
/// It is what a figure only one process can answer for is reported beside, so that an operator asking the same question
/// twice can tell whether two different answers describe two replicas or one deployment changing. A status surface that
/// carries a per-process figure without one reports a replica as though it were the whole, which is the reading
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// says the administrative surface exists to prevent.
/// </para>
/// <para>
/// It names a <em>process</em> and never a hold, which is what separates it from <see cref="WorkLeaseHolder" />. A lease
/// carries both: the holder is what a conditional write is refused against, and this is what an operator reads to find
/// the log or the trace the work is happening in. Nothing compares this value to decide anything, so a deployment whose
/// two replicas somehow chose one identity loses a reading rather than an exclusion.
/// </para>
/// <para>
/// The text is the deployment's own — a host name and a process — and never anything from a message. The composition
/// root composes it, because what names a process is the environment it is running in rather than something this layer
/// may read.
/// </para>
/// </remarks>
public sealed record ReplicaIdentity
{
    /// <summary>The greatest length a replica identity may have, which bounds the column it is stored in.</summary>
    public const int MaximumLength = 128;

    private ReplicaIdentity(string value) => this.Value = value;

    /// <summary>Gets the text this replica is reported and stored under.</summary>
    public string Value { get; }

    /// <summary>Creates an identity for one replica.</summary>
    /// <param name="value">The composed name of the process.</param>
    /// <returns>A validated identity.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank, carries a control character, or is longer than <see cref="MaximumLength" />.</exception>
    public static ReplicaIdentity Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmedValue = value.Trim();

        if (trimmedValue.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"A replica identity may be at most {MaximumLength} characters long.",
                nameof(value));
        }

        // A control character would make the identity unreadable in the answer an operator reads it out of, and it is
        // never part of a host name or of anything else a composition root names a process by.
        if (trimmedValue.Any(char.IsControl))
        {
            throw new ArgumentException("A replica identity cannot contain a control character.", nameof(value));
        }

        return new ReplicaIdentity(trimmedValue);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
