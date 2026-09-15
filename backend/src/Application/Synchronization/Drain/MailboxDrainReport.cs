// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Drain;

/// <summary>What one drain pass did, and what the gate kept where it was.</summary>
/// <param name="DrainedCount">Messages the source no longer holds because this pass removed them.</param>
/// <param name="RemovedErasedCount">Messages erased locally whose source copy this pass removed.</param>
/// <param name="HeldBack">How many messages each reason kept on the source, with reasons that kept none absent.</param>
/// <param name="FailedBatches">How many batches each kind of failure cost, with kinds that cost none absent. Every one of them is attempted again by the next ordinary run.</param>
/// <param name="AbandonedBatchCount">Batches abandoned before a command went out because the folder reported another UIDVALIDITY.</param>
/// <param name="SourceCannotBeDrained">Whether the account was left mirroring its source because that source advertises no message-scoped expunge.</param>
/// <remarks>
/// Counts only. What a pass did to somebody's mailbox is reported as figures an operator reads and a metric records,
/// never as a list of messages: a subject, an address, or a folder path in this answer would put mail content into a
/// log line about storage.
/// </remarks>
public sealed record MailboxDrainReport(
    int DrainedCount,
    int RemovedErasedCount,
    IReadOnlyDictionary<MailboxDrainHoldBack, int> HeldBack,
    IReadOnlyDictionary<MailboxDrainFailure, int> FailedBatches,
    int AbandonedBatchCount,
    bool SourceCannotBeDrained = false)
{
    /// <summary>The report of a pass that had nothing to do, which is what a mirrored account's run produces.</summary>
    public static MailboxDrainReport Nothing { get; } = new(
        0,
        0,
        new Dictionary<MailboxDrainHoldBack, int>(),
        new Dictionary<MailboxDrainFailure, int>(),
        0);

    /// <summary>The report of a pass that left the account mirroring because its source has no message-scoped expunge.</summary>
    /// <remarks>
    /// Its own report rather than a failed batch, because nothing was attempted: the capability is established before
    /// the account moves, so the refusal is the whole of what the pass did and the account is still mirroring when the
    /// pass ends.
    /// </remarks>
    public static MailboxDrainReport SourceWithoutMessageScopedExpunge { get; } = new(
        0,
        0,
        new Dictionary<MailboxDrainHoldBack, int>(),
        new Dictionary<MailboxDrainFailure, int>(),
        0,
        SourceCannotBeDrained: true);

    /// <summary>Gets how many batches failed, however they failed.</summary>
    public int FailedBatchCount => this.FailedBatches.Values.Sum();

    /// <summary>Gets whether the pass failed at least one batch, which the next ordinary run attempts again.</summary>
    public bool Failed => this.FailedBatchCount > 0;
}
