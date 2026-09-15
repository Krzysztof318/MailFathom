// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Drain;

/// <summary>What one drain pass did, and what the gate kept where it was.</summary>
/// <param name="DrainedCount">Messages the source no longer holds because this pass removed them.</param>
/// <param name="RemovedErasedCount">Messages erased locally whose source copy this pass removed.</param>
/// <param name="HeldBack">How many messages each reason kept on the source, with reasons that kept none absent.</param>
/// <param name="FailedBatchCount">Batches whose commands the source did not serve, which the account's backoff defers.</param>
/// <param name="AbandonedBatchCount">Batches abandoned before a command went out because the folder reported another UIDVALIDITY.</param>
/// <remarks>
/// Counts only. What a pass did to somebody's mailbox is reported as figures an operator reads and a metric records,
/// never as a list of messages: a subject, an address, or a folder path in this answer would put mail content into a
/// log line about storage.
/// </remarks>
public sealed record MailboxDrainReport(
    int DrainedCount,
    int RemovedErasedCount,
    IReadOnlyDictionary<MailboxDrainHoldBack, int> HeldBack,
    int FailedBatchCount,
    int AbandonedBatchCount)
{
    /// <summary>The report of a pass that had nothing to do, which is what a mirrored account's run produces.</summary>
    public static MailboxDrainReport Nothing { get; } = new(0, 0, new Dictionary<MailboxDrainHoldBack, int>(), 0, 0);

    /// <summary>Gets whether the pass failed at least one batch, which is what defers the account's next run.</summary>
    public bool Failed => this.FailedBatchCount > 0;
}
