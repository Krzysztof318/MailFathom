// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Makes a change MailFathom asked for and has not seen finished visible while it is still unfinished.</summary>
/// <remarks>
/// <para>
/// The counter beside this one already reports every mutation that ended, which answers what happened. This answers the
/// question an operator actually opens a dashboard with: what is outstanding right now, for how long, and is any of it
/// stuck. Those are levels rather than events, so they are gauges read at scrape time from the last pass's answer, and
/// the age is computed when the gauge is read rather than when the pass ran — otherwise an account whose runs are an
/// interval apart would report an age that stepped rather than grew.
/// </para>
/// <para>
/// An account is published from its own pass and replaced whole by the next one, so a lifecycle that emptied stops
/// being reported instead of reporting its last non-zero value forever. Nothing is remembered for an account that has
/// never converged.
/// </para>
/// <para>
/// The dimensions are the account alias, the mutation name, and the lifecycle. All three are MailFathom's own words,
/// bounded by the configured accounts times four mutations times three lifecycles, and none of them is derived from a
/// message.
/// </para>
/// </remarks>
public sealed partial class MailboxConvergenceTelemetry
{
    private const string AccountTagName = "mailfathom.mail.account";
    private const string MutationTagName = "mailfathom.mailbox.mutation";
    private const string LifecycleTagName = "mailfathom.mailbox.mutation.lifecycle";

    private readonly ConcurrentDictionary<string, IReadOnlyList<MailboxMutationLifecycleCount>> outstandingByAccount =
        new(StringComparer.Ordinal);

    private readonly ILogger<MailboxConvergenceTelemetry> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the gauges an outstanding mutation is read through.</summary>
    /// <param name="logger">Records what a pass did, in counts and MailFathom's own names only.</param>
    /// <param name="timeProvider">Measures how long the oldest outstanding mutation has been outstanding.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailboxConvergenceTelemetry(ILogger<MailboxConvergenceTelemetry> logger, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.logger = logger;
        this.timeProvider = timeProvider;

        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.mailbox.mutations.outstanding",
            this.ObserveOutstandingCounts,
            unit: "{mutation}",
            description: "Changes MailFathom has asked a mail server for and not seen finished, by mutation and lifecycle.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.mailbox.mutations.oldest_outstanding_age",
            this.ObserveOldestAges,
            unit: "s",
            description: "How long the oldest unfinished change of each mutation and lifecycle has been outstanding.");
    }

    /// <summary>Publishes what one account's convergence pass found, and records what the pass did.</summary>
    /// <param name="accountId">The account the pass ran for.</param>
    /// <param name="report">What the pass did and what the account still owes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="report" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A pass that changed nothing emits no log line, because most passes have nothing to do and a line per account per
    /// interval would be noise an operator learns to ignore. The gauges are published either way, since an account
    /// whose outstanding work is unchanged is exactly the account somebody needs to be able to see.
    /// </remarks>
    public void Report(MailAccountId accountId, MailboxConvergenceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        this.outstandingByAccount[accountId.Value] = report.Outstanding;

        if (report.ChangedNothing)
        {
            return;
        }

        this.LogConvergencePassFinished(
            accountId.Value,
            report.CompletedCount,
            report.DeadLetteredCount,
            report.DeferredCount,
            report.FailedCount);
    }

    /// <summary>States what a pass moved, in the four outcomes a mutation can have reached in one.</summary>
    /// <remarks>
    /// The dead-lettered count is the one worth reacting to: it names changes nothing will attempt again, which stay on
    /// the gauge until somebody deals with them.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Converged the outstanding mailbox mutations of account {AccountId}; {CompletedCount} completed, {DeadLetteredCount} were given up on and stay visible, {DeferredCount} await a later pass, and {FailedCount} failed and will be attempted again.")]
    private partial void LogConvergencePassFinished(
        string accountId,
        int completedCount,
        int deadLetteredCount,
        int deferredCount,
        int failedCount);

    private IEnumerable<Measurement<long>> ObserveOutstandingCounts() =>
        [.. this.EnumerateOutstanding().Select(outstanding => new Measurement<long>(
            outstanding.Group.Count,
            TagsOf(outstanding.AccountId, outstanding.Group)))];

    private IEnumerable<Measurement<double>> ObserveOldestAges()
    {
        var observedAt = this.timeProvider.GetUtcNow();

        return
        [
            .. this.EnumerateOutstanding().Select(outstanding => new Measurement<double>(
                Math.Max(0, (observedAt - outstanding.Group.OldestRecordedAt).TotalSeconds),
                TagsOf(outstanding.AccountId, outstanding.Group))),
        ];
    }

    /// <summary>Flattens the published snapshots into one measurement source both gauges read.</summary>
    /// <remarks>
    /// The result is materialized by each caller before it is handed to the meter, because a gauge callback is invoked
    /// on the collector's schedule and a deferred query would be enumerated against whatever the dictionary held then
    /// rather than against what the snapshot said.
    /// </remarks>
    private IEnumerable<(string AccountId, MailboxMutationLifecycleCount Group)> EnumerateOutstanding() =>
        this.outstandingByAccount.SelectMany(
            account => account.Value,
            (account, group) => (account.Key, group));

    private static TagList TagsOf(string accountId, MailboxMutationLifecycleCount group) =>
        new()
        {
            { AccountTagName, accountId },
            { MutationTagName, group.Mutation.Name },
            { LifecycleTagName, group.Lifecycle.Name },
        };
}
