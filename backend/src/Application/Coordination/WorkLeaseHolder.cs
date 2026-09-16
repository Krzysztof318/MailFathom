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
/// The text is MailFathom's own — a generated identity for the hold — and never anything from a message. A hold taken
/// by a build that stamps one carries that build ahead of the generated part, separated by <see cref="BuildSeparator" />,
/// which is the one thing anything reads out of the value rather than comparing it whole. It is written into the
/// holder rather than into a column of its own because a column would outlive the hold it described: an older build's
/// takeover rewrites the holder with one of its own and leaves every column it does not know exactly as it found it, so
/// the newer build's value would go on standing beside the older build's hold. Carrying it here makes the two
/// inseparable — a hold either names a build or is a hold some other build took.
/// </para>
/// </remarks>
public sealed record WorkLeaseHolder
{
    /// <summary>The greatest length a holder may have, which bounds the column it is stored in.</summary>
    public const int MaximumLength = 128;

    /// <summary>What separates the build a hold was taken by from the generated identity of the hold.</summary>
    /// <remarks>
    /// A character no semantic version and no UUID carries, so the split is unambiguous in both directions and a holder
    /// that names no build cannot be read as one that does.
    /// </remarks>
    public const char BuildSeparator = '/';

    /// <summary>The greatest length the build part may have, so a long informational version cannot crowd out the hold.</summary>
    private const int MaximumBuildLength = 64;

    private WorkLeaseHolder(string value) => this.Value = value;

    /// <summary>Gets the text a stored lease is compared against.</summary>
    public string Value { get; }

    /// <summary>Gets the build that took this hold, or <see langword="null" /> where the hold names none.</summary>
    /// <remarks>
    /// An absent build is a real answer rather than a missing one: it says the hold was taken by a build that does not
    /// stamp one, which is what a replica running a release older than the one that introduced the stamp is. Nothing
    /// compares this to decide an exclusion — the whole value is still what a conditional write is refused against.
    /// </remarks>
    public string? Build => this.Value.IndexOf(BuildSeparator, StringComparison.Ordinal) is var separator and > 0
        ? this.Value[..separator]
        : null;

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

    /// <summary>Creates a holder for a new hold, naming the build taking it.</summary>
    /// <param name="build">The version of the running build, which the composition root reads from its own assembly.</param>
    /// <returns>A holder nothing else will produce, whose <see cref="Build" /> is that version.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="build" /> is blank, carries a control character or the separator, or is longer than sixty-four characters.</exception>
    /// <remarks>
    /// This is how a production hold is taken, and what a build knowing about a mode an older build does not is
    /// recognized by. <see cref="NewHold()" /> stays for a hold with no build to name, and a lease taken under one
    /// reads as having been taken by an unknown build.
    /// </remarks>
    public static WorkLeaseHolder ForBuild(string build)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(build);

        var trimmedBuild = build.Trim();

        if (trimmedBuild.Length > MaximumBuildLength)
        {
            throw new ArgumentException(
                $"A work lease holder's build may be at most {MaximumBuildLength} characters long.",
                nameof(build));
        }

        if (trimmedBuild.Any(char.IsControl) || trimmedBuild.Contains(BuildSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A work lease holder's build cannot contain a control character or '{BuildSeparator}'.",
                nameof(build));
        }

        return new WorkLeaseHolder($"{trimmedBuild}{BuildSeparator}{Guid.CreateVersion7()}");
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
