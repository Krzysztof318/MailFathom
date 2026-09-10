// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Coordination;

/// <summary>Names the hold a lease is taken under, so a later write can be refused when the lease has moved on.</summary>
/// <remarks>
/// <para>
/// It identifies one <em>hold</em> rather than one process, for the reason a job's lease owner identifies one attempt.
/// Renewal and release are conditional on this value still matching the row, so a replica whose lease was reclaimed and
/// which then releases late finds the scope held by whoever replaced it and writes nothing. A value that named only the
/// process would let a replica that lost its hold and took it back release a hold it no longer had — and would make a
/// restart under the same name indistinguishable from the hold it is replacing.
/// </para>
/// <para>
/// The text is MailFathom's own — a generated identity for the hold — and never anything from a message.
/// </para>
/// </remarks>
public sealed record WorkLeaseHolder
{
    /// <summary>The greatest length a holder may have, which bounds the column it is stored in.</summary>
    public const int MaximumLength = 128;

    private WorkLeaseHolder(string value) => this.Value = value;

    /// <summary>Gets the text a stored lease is compared against.</summary>
    public string Value { get; }

    /// <summary>Creates a holder for one hold.</summary>
    /// <param name="value">The generated identity of the hold.</param>
    /// <returns>A validated holder.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank, carries a control character, or is longer than <see cref="MaximumLength" />.</exception>
    public static WorkLeaseHolder Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmedValue = value.Trim();

        if (trimmedValue.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"A work lease holder may be at most {MaximumLength} characters long.",
                nameof(value));
        }

        if (trimmedValue.Any(char.IsControl))
        {
            throw new ArgumentException("A work lease holder cannot contain a control character.", nameof(value));
        }

        return new WorkLeaseHolder(trimmedValue);
    }

    /// <summary>Creates a holder for a new hold, unique across every process that shares the database.</summary>
    /// <returns>A holder nothing else will produce.</returns>
    /// <remarks>
    /// A random identity rather than a host name, because two replicas of one deployment are the case the
    /// compare-and-set exists for and neither can see what the other allocated. It is not a security token, so the
    /// ordinary UUID generator is what this needs.
    /// </remarks>
    public static WorkLeaseHolder NewHold() => new(Guid.CreateVersion7().ToString());

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
