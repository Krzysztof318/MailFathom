// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>What a write to one owner's record did: the version it committed, or why the record is unchanged.</summary>
/// <remarks>
/// <para>
/// A refusal is a result rather than an exception, for the reason the deployment's own configuration write states: each
/// of them is something whoever asked for the write acts on directly — a record that will not bind, an account name
/// another owner already answers to, a record somebody else moved on, an owner a configuration source still supplies.
/// None is a failure of the machinery underneath, and each carries the code a surface reports and the sentences a
/// person reads.
/// </para>
/// <para>
/// <see cref="Version" /> is meaningful either way and deliberately so: it is the version now in force, so a refused
/// write says what the record stands at and a committed one says what it moved to. A caller refused for a superseded
/// version re-reads that version and decides again against it.
/// </para>
/// <para>
/// The messages quote what the caller wrote, which is why they travel back to that caller and stay out of the log: a
/// binder names the setting it refused and the value it was given, and the person holding the failed edit is the one
/// who already has both.
/// </para>
/// </remarks>
internal sealed record OwnerRecordWriteOutcome
{
    private OwnerRecordWriteOutcome(long version, MailFathomErrorCode refusal, IReadOnlyList<string> messages)
    {
        this.Version = version;
        this.Refusal = refusal;
        this.Messages = messages;
    }

    /// <summary>Gets the version of the owner's record now in force.</summary>
    public long Version { get; }

    /// <summary>Gets the code naming why the write was refused, which is the unspecified default when it committed or changed nothing.</summary>
    public MailFathomErrorCode Refusal { get; }

    /// <summary>Gets one sentence per reason the write was refused, or the single sentence saying nothing needed changing.</summary>
    /// <remarks>Several, because a record that fails to bind fails at every setting at once and a person fixing one at a time would learn the next only by writing again.</remarks>
    public IReadOnlyList<string> Messages { get; }

    /// <summary>Gets whether the record is what the caller asked for, whether or not a version was spent reaching it.</summary>
    public bool IsSettled => !this.Refusal.IsSpecified;

    /// <summary>Gets whether a version was spent, which is false for a write that found the record already as asked.</summary>
    public bool IsCommitted => this.IsSettled && this.Messages.Count == 0;

    /// <summary>Reports a write that committed.</summary>
    /// <param name="version">The version the commit produced.</param>
    /// <returns>The committed result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version" /> is not a version a commit can have produced.</exception>
    public static OwnerRecordWriteOutcome Committed(long version)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        return new OwnerRecordWriteOutcome(version, refusal: default, messages: []);
    }

    /// <summary>Reports a write the record already satisfied, so no version was spent.</summary>
    /// <param name="version">The version still in force.</param>
    /// <param name="message">The sentence saying what the record already stands at.</param>
    /// <returns>The settled result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="message" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version" /> is negative.</exception>
    /// <remarks>
    /// Reported apart from a commit because the two are different answers to the same request and a caller renders them
    /// differently: one says the record moved and the other says it did not have to. Neither is a refusal, which is why
    /// both carry no code.
    /// </remarks>
    public static OwnerRecordWriteOutcome NothingToChange(long version, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentOutOfRangeException.ThrowIfNegative(version);

        return new OwnerRecordWriteOutcome(version, refusal: default, [message]);
    }

    /// <summary>Reports a write that changed nothing because it was refused.</summary>
    /// <param name="refusal">The code naming why.</param>
    /// <param name="versionInForce">The version the owner's record still stands at.</param>
    /// <param name="messages">One sentence per reason, each naming the setting and never its value unless the caller supplied that value.</param>
    /// <returns>The refused result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusal" /> names no failure, or when <paramref name="messages" /> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="versionInForce" /> is negative.</exception>
    public static OwnerRecordWriteOutcome Refused(
        MailFathomErrorCode refusal,
        long versionInForce,
        IReadOnlyList<string> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentOutOfRangeException.ThrowIfNegative(versionInForce);

        if (!refusal.IsSpecified)
        {
            throw new ArgumentException("A refused write names the failure it was refused with.", nameof(refusal));
        }

        if (messages.Count == 0)
        {
            throw new ArgumentException("A refused write says what has to change.", nameof(messages));
        }

        return new OwnerRecordWriteOutcome(versionInForce, refusal, messages);
    }
}
