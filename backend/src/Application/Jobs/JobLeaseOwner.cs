// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>Names the attempt that holds a lease, so a later write can be refused when the lease has moved on.</summary>
/// <remarks>
/// <para>
/// It identifies one attempt rather than one process. Completion, renewal, and release are all conditional on this
/// value still matching the row, and a worker that lost its lease, finished late, and tried to write its result finds
/// the row owned by the attempt that replaced it and writes nothing. A value that named only the process would let a
/// reclaimed job be completed by the attempt whose lease had already expired.
/// </para>
/// <para>
/// The text is MailFathom's own — a generated identity for the attempt — and never anything from a message.
/// </para>
/// </remarks>
public sealed record JobLeaseOwner
{
    /// <summary>The greatest length an owner may have, which bounds the column it is stored in.</summary>
    public const int MaximumLength = 128;

    private JobLeaseOwner(string value) => this.Value = value;

    /// <summary>Gets the text a stored lease is compared against.</summary>
    public string Value { get; }

    /// <summary>Creates an owner for one attempt.</summary>
    /// <param name="value">The generated identity of the attempt.</param>
    /// <returns>A validated lease owner.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank, carries a control character, or is longer than <see cref="MaximumLength" />.</exception>
    public static JobLeaseOwner Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmedValue = value.Trim();

        if (trimmedValue.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"A job lease owner may be at most {MaximumLength} characters long.",
                nameof(value));
        }

        if (trimmedValue.Any(char.IsControl))
        {
            throw new ArgumentException("A job lease owner cannot contain a control character.", nameof(value));
        }

        return new JobLeaseOwner(trimmedValue);
    }

    /// <summary>Creates an owner for a new attempt, unique across every process that shares the database.</summary>
    /// <returns>A lease owner nothing else will produce.</returns>
    /// <remarks>
    /// A random identity rather than a host name and a counter, because two replicas of one deployment are the case the
    /// compare-and-set exists for and neither can see what the other allocated. It is not a security token, so the
    /// ordinary UUID generator is what this needs.
    /// </remarks>
    public static JobLeaseOwner NewAttempt() => new(Guid.CreateVersion7().ToString());

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
