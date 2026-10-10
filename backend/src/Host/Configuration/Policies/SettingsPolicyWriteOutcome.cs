// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>What one write to a settings policy did.</summary>
/// <remarks>
/// A result rather than an exception, for the reason a write to a user's record reports one: every refusal here is
/// something an administrator acts on and continues from — a policy somebody else moved, a path that names nothing —
/// and each carries the version the next attempt is composed over.
/// </remarks>
internal sealed record SettingsPolicyWriteOutcome
{
    private SettingsPolicyWriteOutcome(
        long version,
        MailFathomErrorCode refusal,
        IReadOnlyList<string> messages,
        bool isCommitted = false)
    {
        this.Version = version;
        this.Refusal = refusal;
        this.Messages = messages;
        this.IsCommitted = isCommitted;
    }

    /// <summary>Gets the version in force once the write was answered, whichever way it went.</summary>
    public long Version { get; }

    /// <summary>Gets the code naming why the write was refused, unspecified where nothing refused it.</summary>
    public MailFathomErrorCode Refusal { get; }

    /// <summary>Gets one sentence per reason the write was refused or changed nothing, and none on a commit.</summary>
    public IReadOnlyList<string> Messages { get; }

    /// <summary>Gets a value indicating whether the policy moved to a new version.</summary>
    public bool IsCommitted { get; }

    /// <summary>Describes a write that committed.</summary>
    /// <param name="version">The version the commit produced.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version" /> is not positive.</exception>
    public static SettingsPolicyWriteOutcome Committed(long version)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        return new SettingsPolicyWriteOutcome(version, refusal: default, messages: [], isCommitted: true);
    }

    /// <summary>Describes a write whose candidate states what the policy in force already does.</summary>
    /// <param name="version">The version that stays in force.</param>
    /// <param name="message">The sentence saying so.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="message" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version" /> is negative.</exception>
    public static SettingsPolicyWriteOutcome NothingToChange(long version, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentOutOfRangeException.ThrowIfNegative(version);

        return new SettingsPolicyWriteOutcome(version, refusal: default, [message]);
    }

    /// <summary>Describes a write that was refused, leaving the policy as it was.</summary>
    /// <param name="refusal">The code naming why.</param>
    /// <param name="versionInForce">The version the next attempt is composed over.</param>
    /// <param name="messages">One sentence per thing to correct.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusal" /> names no failure, or <paramref name="messages" /> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="versionInForce" /> is negative.</exception>
    public static SettingsPolicyWriteOutcome Refused(
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

        return new SettingsPolicyWriteOutcome(versionInForce, refusal, messages);
    }
}
