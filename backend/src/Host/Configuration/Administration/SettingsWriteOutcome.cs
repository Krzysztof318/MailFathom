// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Host.Configuration.Administration;

/// <summary>What one administrative configuration write did, in the terms the operator who asked for it acts on.</summary>
/// <remarks>
/// <para>
/// Three outcomes rather than two, because "nothing to do" is neither of the others. Removing a setting the layer does
/// not carry, and adopting a prefix the files decide nothing beneath, are both requests that were understood perfectly
/// and left the document alone — reporting either as a refusal would send an operator looking for a mistake, and
/// reporting it as a commit would claim a version the deployment never had.
/// </para>
/// <para>
/// <see cref="Version" /> is meaningful in all three, and it is always the version now in force: what a commit moved
/// to, what a refused write is still running on, and what a request that changed nothing left standing. That is the
/// number a caller composes its next write over.
/// </para>
/// </remarks>
internal sealed record SettingsWriteOutcome
{
    private SettingsWriteOutcome(
        bool committed,
        long version,
        MailFathomErrorCode refusal,
        IReadOnlyList<string> messages,
        IReadOnlyList<SettingChange> changes)
    {
        this.Committed = committed;
        this.Version = version;
        this.Refusal = refusal;
        this.Messages = messages;
        this.Changes = changes;
    }

    /// <summary>Gets whether the deployment's persisted configuration moved to a new version.</summary>
    public bool Committed { get; }

    /// <summary>Gets the version of the persisted configuration now in force.</summary>
    public long Version { get; }

    /// <summary>Gets the code naming why the write was refused, which is the unspecified default when nothing refused it.</summary>
    public MailFathomErrorCode Refusal { get; }

    /// <summary>Gets one sentence per reason the write was refused or changed nothing, and empty on a commit.</summary>
    public IReadOnlyList<string> Messages { get; }

    /// <summary>Gets what each named setting read as before the write and reads as after it, and empty unless the write committed.</summary>
    public IReadOnlyList<SettingChange> Changes { get; }

    /// <summary>Reports a write that committed.</summary>
    /// <param name="version">The version the commit produced.</param>
    /// <param name="changes">What each named setting read as before and reads as now.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="changes" /> is <see langword="null" />.</exception>
    internal static SettingsWriteOutcome CommittedAs(long version, IReadOnlyList<SettingChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return new SettingsWriteOutcome(committed: true, version, refusal: default, messages: [], changes);
    }

    /// <summary>Reports a write the deployment refused.</summary>
    /// <param name="refusal">The code naming why.</param>
    /// <param name="version">The version still in force.</param>
    /// <param name="messages">One sentence per reason, each naming what an operator has to change.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusal" /> names no failure, or <paramref name="messages" /> is empty.</exception>
    internal static SettingsWriteOutcome Refused(
        MailFathomErrorCode refusal,
        long version,
        IReadOnlyList<string> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (!refusal.IsSpecified)
        {
            throw new ArgumentException("A refused write names the failure it was refused with.", nameof(refusal));
        }

        if (messages.Count == 0)
        {
            throw new ArgumentException("A refused write says what an operator has to change.", nameof(messages));
        }

        return new SettingsWriteOutcome(committed: false, version, refusal, messages, changes: []);
    }

    /// <summary>Reports a request the deployment understood and that left the document as it was.</summary>
    /// <param name="version">The version still in force.</param>
    /// <param name="message">What there was nothing to do about.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="message" /> is <see langword="null" />, empty, or white space.</exception>
    internal static SettingsWriteOutcome NothingToChange(long version, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new SettingsWriteOutcome(committed: false, version, refusal: default, [message], changes: []);
    }
}
