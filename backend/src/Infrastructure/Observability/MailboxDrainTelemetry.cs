// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.Application.Observability;
using MailFathom.Application.Synchronization.Drain;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes how far a held account's source has been emptied, and what the gate is keeping on it.</summary>
/// <remarks>
/// <para>
/// A switch to holding a mailbox is the one operation here whose progress nobody can see from the mail: a drained
/// message looks exactly like a message that was never on the source, and a held-back one looks exactly like a drained
/// one. So the counters are the operator's whole view of it, and the held-back reason is the dimension that turns
/// <em>nothing is moving</em> into an answer.
/// </para>
/// <para>
/// The dimensions are the account alias and MailFathom's own name for a hold-back reason or a kind of failure, bounded
/// by the configured accounts times the five reasons a message is held back and the five kinds of failure, and none of
/// them is derived from a message. Nothing here carries a subject, an address, a folder path, or a UID.
/// </para>
/// </remarks>
public sealed partial class MailboxDrainTelemetry : IMailboxDrainTelemetry
{
    private const string AccountTagName = "mailfathom.mail.account";
    private const string HoldBackTagName = "mailfathom.mailbox.drain.held_back_reason";
    private const string FailureTagName = "mailfathom.mailbox.drain.failure";

    private readonly Counter<long> drained;
    private readonly Counter<long> removed;
    private readonly Counter<long> heldBack;
    private readonly Counter<long> failedBatches;
    private readonly Counter<long> abandonedBatches;
    private readonly ILogger<MailboxDrainTelemetry> logger;

    /// <summary>Initializes the instruments every drain pass reports through.</summary>
    /// <param name="logger">Records what a pass did, in counts and MailFathom's own names only.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger" /> is <see langword="null" />.</exception>
    public MailboxDrainTelemetry(ILogger<MailboxDrainTelemetry> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
        this.drained = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mailbox.drain.drained",
            unit: "{message}",
            description: "Messages whose source copy MailFathom removed once it verifiably held them.");
        this.removed = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mailbox.drain.removed_erased",
            unit: "{message}",
            description: "Messages erased locally whose source copy the drain removed afterwards.");
        this.heldBack = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mailbox.drain.held_back",
            unit: "{message}",
            description: "Messages the gate left on the source, by the reason it refused them.");
        this.failedBatches = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mailbox.drain.failed_batches",
            unit: "{batch}",
            description: "Batches whose commands the source did not serve, by the kind of failure, each attempted again by the next ordinary run.");
        this.abandonedBatches = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mailbox.drain.abandoned_batches",
            unit: "{batch}",
            description: "Batches abandoned before a command went out because the folder reported another UIDVALIDITY.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// A pass that did nothing emits no line and adds nothing, because most passes of a mirrored account have nothing
    /// to do and a line per account per interval is noise an operator learns to ignore.
    /// </remarks>
    public void Report(MailAccountId account, MailboxDrainReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (DidNothing(report))
        {
            return;
        }

        var accountTag = new KeyValuePair<string, object?>(AccountTagName, account.Value);

        this.drained.Add(report.DrainedCount, accountTag);
        this.removed.Add(report.RemovedErasedCount, accountTag);
        this.abandonedBatches.Add(report.AbandonedBatchCount, accountTag);

        foreach (var (reason, count) in report.HeldBack)
        {
            this.heldBack.Add(
                count,
                accountTag,
                new KeyValuePair<string, object?>(HoldBackTagName, reason.ToString()));
        }

        foreach (var (failure, count) in report.FailedBatches)
        {
            this.failedBatches.Add(
                count,
                accountTag,
                new KeyValuePair<string, object?>(FailureTagName, failure.ToString()));
        }

        if (report.DrainedCount == 0 && report.RemovedErasedCount == 0 && report.FailedBatchCount == 0)
        {
            return;
        }

        this.LogDrainPassFinished(
            account.Value,
            report.DrainedCount,
            report.RemovedErasedCount,
            report.FailedBatchCount);

        foreach (var (failure, count) in report.FailedBatches)
        {
            this.LogDrainBatchesFailed(account.Value, count, failure);
        }
    }

    /// <summary>Answers whether the pass has anything to record at all.</summary>
    /// <param name="report">What the pass did.</param>
    /// <returns><see langword="true" /> when no instrument would record anything but a zero.</returns>
    /// <remarks>
    /// Every configured account reports a pass per interval and a mirrored account's pass does nothing, so adding the
    /// zeroes would give a deployment that never enabled the mode a per-account series on every one of these
    /// instruments. A held-back message is not nothing, which is why the two dictionaries are part of the question.
    /// </remarks>
    private static bool DidNothing(MailboxDrainReport report) =>
        report.DrainedCount == 0
        && report.RemovedErasedCount == 0
        && report.AbandonedBatchCount == 0
        && report.HeldBack.Count == 0
        && report.FailedBatches.Count == 0;

    /// <summary>States what one pass took off the source, and what it could not.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Drained the source of account {AccountId}; {DrainedCount} messages left the source, {RemovedErasedCount} erased copies were removed, and {FailedBatchCount} batches failed and will be attempted again.")]
    private partial void LogDrainPassFinished(
        string accountId,
        int drainedCount,
        int removedErasedCount,
        int failedBatchCount);

    /// <summary>Names what refused a pass's failed batches, one line per kind.</summary>
    /// <remarks>
    /// A line per kind rather than one line naming all of them, because the kinds are what an operator acts on
    /// differently and a kind that repeats across runs is the source rather than the run —
    /// <see cref="MailboxDrainFailure.SourceCannotExpungeOneMessage" /> above all, which repeating can never get past.
    /// The name is MailFathom's own word for the refusal and carries nothing the source said.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The source of account {AccountId} refused {FailedBatchCount} drain batches: {DrainFailure}.")]
    private partial void LogDrainBatchesFailed(
        string accountId,
        int failedBatchCount,
        MailboxDrainFailure drainFailure);
}
