// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Restore;

/// <summary>What one restore pass put back onto a source, and what stopped the rest of it.</summary>
/// <param name="AppendedCount">Messages this pass wrote the occurrence of, whichever pass issued the append that put them back.</param>
/// <param name="StateWrittenCount">Messages whose local state this pass wrote down for the converger to carry.</param>
/// <param name="UnansweredAppendCount">Appends whose outcome this pass left unknown, whichever pass issued them, each of which now holds the account in its phase.</param>
/// <param name="Failures">How many messages each kind of failure cost, with kinds that cost none absent. A message is attempted again only where the pass recorded nothing for it.</param>
/// <param name="Pause">What is holding the restore up, or <see cref="MailboxRestorePause.None" /> where nothing is.</param>
/// <param name="EndedTheRestore">Whether this pass was the one that put the account back to mirroring its source.</param>
/// <remarks>
/// <para>
/// Counts only. What a pass did to somebody's mailbox is reported as figures an operator reads and a metric records,
/// never as a list of messages: a subject, an address, or a folder path in this answer would put mail content into a
/// log line about custody.
/// </para>
/// <para>
/// Two of the counts are the pass's own work rather than the pass's own appends. A pass begins by finishing the
/// placements an interrupted one recorded, so a run that issued no command at all can report a dozen messages back on
/// the source and one whose outcome it could not settle. What the counts add up to across passes is each message once,
/// which is what an operator reading a total needs; what they do not say is which pass the command went out in.
/// </para>
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

    /// <summary>Gets whether the pass failed at least one message, however the failure ended for that message.</summary>
    /// <remarks>
    /// Not every failure here is retried, which is why this says nothing about the next run. A failure that recorded
    /// nothing leaves the message a candidate and the next pass takes it again; one that opened the message's records
    /// and stamped it, settled it as unrestorable, or left an append standing has decided the message's outcome, and
    /// the next pass will not reach it.
    /// </remarks>
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
