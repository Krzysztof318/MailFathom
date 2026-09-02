// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Configuration;

/// <summary>What a configuration write did: the version it committed, or why the deployment's settings are unchanged.</summary>
/// <remarks>
/// <para>
/// A refusal is a result rather than an exception because every one of them is something the administrator who asked
/// for the write acts on directly — a setting MailFathom persists nowhere, a value the configuration will not bind, a
/// document somebody else moved on, a secret written as material. Each is an expected outcome of an edit rather than a
/// failure of the machinery underneath it, and each carries the code a surface reports and the sentences an operator
/// reads.
/// </para>
/// <para>
/// <see cref="Version" /> is meaningful either way, and deliberately so: it is the version now in force, so a refused
/// write says what the deployment is running on and a committed one says what it moved to. A caller refused for a
/// superseded version re-reads that version and decides again against it. The one refusal that carries the version the
/// caller stated rather than one read from the row is a change refused before the document was read at all — a path
/// persisted nowhere, or secret material — because nothing about either depends on what the row holds and reading it
/// to answer would be a query MailFathom made to report a number the caller already supplied.
/// </para>
/// <para>
/// The messages quote what the caller themselves wrote, which is why they travel back to that caller and stay out of
/// the log. A validator names the setting it refused and the value it was given, and the operator holding the failed
/// edit is the one person who already has both.
/// </para>
/// </remarks>
public sealed record ConfigurationWriteResult
{
    private ConfigurationWriteResult(long version, MailFathomErrorCode refusal, IReadOnlyList<string> refusalMessages)
    {
        this.Version = version;
        this.Refusal = refusal;
        this.RefusalMessages = refusalMessages;
    }

    /// <summary>Gets the version of the persisted configuration now in force.</summary>
    public long Version { get; }

    /// <summary>Gets the code naming why the write was refused, which is the unspecified default when it committed.</summary>
    public MailFathomErrorCode Refusal { get; }

    /// <summary>Gets one sentence per reason the write was refused, empty when it committed.</summary>
    /// <remarks>Several, because a candidate that fails to validate fails at every setting at once and an operator fixing one at a time would learn the next only by writing again.</remarks>
    public IReadOnlyList<string> RefusalMessages { get; }

    /// <summary>Gets whether the write committed, in which case <see cref="Version" /> is the version it produced.</summary>
    public bool IsCommitted => !this.Refusal.IsSpecified;

    /// <summary>Reports a write that committed.</summary>
    /// <param name="version">The version the commit produced.</param>
    /// <returns>The committed result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version" /> is not a version a commit can have produced.</exception>
    public static ConfigurationWriteResult Committed(long version)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        return new ConfigurationWriteResult(version, refusal: default, refusalMessages: []);
    }

    /// <summary>Reports a write that changed nothing.</summary>
    /// <param name="refusal">The code naming why.</param>
    /// <param name="versionInForce">The version the deployment is still running on.</param>
    /// <param name="messages">One sentence per reason, each naming the setting and never its value unless the caller supplied that value.</param>
    /// <returns>The refused result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusal" /> names no failure, or when <paramref name="messages" /> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="versionInForce" /> is negative.</exception>
    public static ConfigurationWriteResult Refused(
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
            throw new ArgumentException("A refused write says what an operator has to change.", nameof(messages));
        }

        return new ConfigurationWriteResult(versionInForce, refusal, messages);
    }
}
