// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.Application.Observability;
using MailFathom.Application.Synchronization.Restore;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes how far a restoring account's mailbox has got back onto its source.</summary>
/// <remarks>
/// <para>
/// The counters follow the drain's, because the two halves of the switch are read together: an operator watching a
/// mailbox come back wants the same shape of answer they had watching it leave. Two of them have no counterpart
/// there — an append whose answer never arrived, and a pause — and both are worth a log line of their own, because
/// each is a restore that will stand still until somebody acts.
/// </para>
/// <para>
/// The dimensions are the account alias and MailFathom's own names, bounded by the configured accounts times the
/// seven kinds of failure. Nothing here is derived from a message.
/// </para>
/// </remarks>
public sealed partial class MailboxRestoreTelemetry : IMailboxRestoreTelemetry
{
    private const string AccountTagName = "mailfathom.mail.account";
    private const string FailureTagName = "mailfathom.mailbox.restore.failure";

    private readonly Counter<long> appended;
    private readonly Counter<long> stateWritten;
    private readonly Counter<long> unansweredAppends;
    private readonly Counter<long> failures;
    private readonly ILogger<MailboxRestoreTelemetry> logger;

    /// <summary>Initializes the instruments every restore pass reports through.</summary>
    /// <param name="logger">Records what a pass did, in counts and MailFathom's own names only.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger" /> is <see langword="null" />.</exception>
    public MailboxRestoreTelemetry(ILogger<MailboxRestoreTelemetry> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
        this.appended = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mailbox.restore.appended",
            unit: "{message}",
            description: "Messages MailFathom appended back onto the source it had drained them from.");
        this.stateWritten = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mailbox.restore.state_written",
            unit: "{message}",
            description: "Messages whose local folder, flags, and keywords were written down for the converger to carry onto the occurrence they kept.");
        this.unansweredAppends = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mailbox.restore.unanswered_appends",
            unit: "{message}",
            description: "Appends whose answer never came back, each of which holds its account in Restoring until an operator settles it.");
        this.failures = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mailbox.restore.failures",
            unit: "{message}",
            description: "Messages the restore did not put back, by the kind of failure; only a kind that recorded nothing for the message is attempted again.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// A pass that did nothing emits no line and adds nothing, for the reason the drain's does not: every configured
    /// account reports a pass per interval and an account that is not restoring has nothing to do. A pause is the one
    /// pass that did nothing and still has to be said, because it is a restore that stands still until an operator
    /// corrects a mapping and nothing else in the deployment names it.
    /// </remarks>
    public void Report(MailAccountId account, MailboxRestoreReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Pause is not MailboxRestorePause.None)
        {
            this.LogRestorePaused(account.Value, report.Pause);

            return;
        }

        if (DidNothing(report))
        {
            return;
        }

        var accountTag = new KeyValuePair<string, object?>(AccountTagName, account.Value);

        this.appended.Add(report.AppendedCount, accountTag);
        this.stateWritten.Add(report.StateWrittenCount, accountTag);
        this.unansweredAppends.Add(report.UnansweredAppendCount, accountTag);

        foreach (var (failure, count) in report.Failures)
        {
            this.failures.Add(
                count,
                accountTag,
                new KeyValuePair<string, object?>(FailureTagName, failure.ToString()));
        }

        this.LogRestorePassFinished(
            account.Value,
            report.AppendedCount,
            report.StateWrittenCount,
            report.FailedCount);

        if (report.UnansweredAppendCount > 0)
        {
            this.LogAppendsUnanswered(account.Value, report.UnansweredAppendCount);
        }

        if (report.EndedTheRestore)
        {
            this.LogRestoreFinished(account.Value);
        }
    }

    /// <summary>Answers whether the pass has anything to record at all.</summary>
    private static bool DidNothing(MailboxRestoreReport report) =>
        report.AppendedCount == 0
        && report.StateWrittenCount == 0
        && report.UnansweredAppendCount == 0
        && report.Failures.Count == 0
        && !report.EndedTheRestore;

    /// <summary>States what one pass put back, and what it could not.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Restored mail of account {AccountId} onto its source; {AppendedCount} messages were appended back, {StateWrittenCount} had their stored state written down, and {FailedCount} messages failed.")]
    private partial void LogRestorePassFinished(
        string accountId,
        int appendedCount,
        int stateWrittenCount,
        int failedCount);

    /// <summary>States that appends were issued whose answer never came back.</summary>
    /// <remarks>
    /// A warning rather than information, because each one is a message whose copy may or may not be in the user's
    /// folder and the account cannot finish restoring until somebody establishes which. Nothing retries it.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The source of account {AccountId} did not answer {UnansweredAppendCount} appends; each is recorded, none will be issued again, and the account stays in Restoring until an operator settles them.")]
    private partial void LogAppendsUnanswered(string accountId, int unansweredAppendCount);

    /// <summary>States what is holding the account's restore up.</summary>
    /// <remarks>
    /// Written on every pass rather than once, for the reason the drain writes its own refusal every pass: the restore
    /// stands still until the configuration is corrected, and a line per synchronization interval is what an operator
    /// reading the account's log finds.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The restore of account {AccountId} is standing still: {RestorePause}.")]
    private partial void LogRestorePaused(string accountId, MailboxRestorePause restorePause);

    /// <summary>States that the account's source is the truth about its mailbox again.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Account {AccountId} is mirroring its source again; everything MailFathom held is back on the source.")]
    private partial void LogRestoreFinished(string accountId);
}
