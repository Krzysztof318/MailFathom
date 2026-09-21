// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Restore;

/// <summary>What one restore pass put back onto a source, and what stopped the rest of it.</summary>
/// <param name="AppendedCount">Messages the source holds again because this pass appended them.</param>
/// <param name="StateWrittenCount">Messages whose local state this pass wrote down for the converger to carry.</param>
/// <param name="UnansweredAppendCount">Appends this pass issued whose answer never came back, each of which now holds the account in its phase.</param>
/// <param name="Failures">How many messages each kind of failure cost, with kinds that cost none absent. Every one of them is attempted again by the next ordinary run.</param>
/// <param name="Pause">What is holding the restore up, or <see cref="MailboxRestorePause.None" /> where nothing is.</param>
/// <param name="EndedTheRestore">Whether this pass was the one that put the account back to mirroring its source.</param>
/// <remarks>
/// Counts only. What a pass did to somebody's mailbox is reported as figures an operator reads and a metric records,
/// never as a list of messages: a subject, an address, or a folder path in this answer would put mail content into a
/// log line about custody.
/// </remarks>
public sealed record MailboxRestoreReport(
    int AppendedCount,
    int StateWrittenCount,
    int UnansweredAppendCount,
    IReadOnlyDictionary<MailboxRestoreFailure, int> Failures,
    MailboxRestorePause Pause = MailboxRestorePause.None,
    bool EndedTheRestore = false)
{
    /// <summary>The report of a pass that had nothing to do, which is what an account not restoring produces.</summary>
    public static MailboxRestoreReport Nothing { get; } = new(
        0,
        0,
        0,
        new Dictionary<MailboxRestoreFailure, int>());

    /// <summary>Gets how many messages failed, however they failed.</summary>
    public int FailedCount => this.Failures.Values.Sum();

    /// <summary>Gets whether the pass failed at least one message, which the next ordinary run attempts again.</summary>
    public bool Failed => this.FailedCount > 0;

    /// <summary>Builds the report of a pass that was held up before it attempted anything.</summary>
    /// <param name="pause">What is holding it up.</param>
    /// <returns>The report.</returns>
    public static MailboxRestoreReport HeldUpBy(MailboxRestorePause pause) => new(
        0,
        0,
        0,
        new Dictionary<MailboxRestoreFailure, int>(),
        pause);
}
