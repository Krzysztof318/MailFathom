// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Governance;

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>Counts what one period has already been asked to send, for an account and for the deployment.</summary>
/// <remarks>
/// <para>
/// Durable rather than held in memory, because the fault a ceiling exists to bound is precisely the one an in-process
/// counter cannot see: a process that crashes and restarts in a loop would begin every period again from nothing and
/// send the whole ceiling on each attempt.
/// </para>
/// <para>
/// It is counted from the outgoing records themselves rather than from a ledger beside them. A record is written for
/// every send this deployment is asked for, under the idempotency identity that makes the same request twice one
/// record, so counting them is exact where a second counter would have to be told which enqueues were new — and a
/// retried send that charged a ceiling twice would refuse mail nobody asked to send.
/// </para>
/// </remarks>
public interface IOutgoingMailUsageReader
{
    /// <summary>Reads what has been asked to leave since a period began.</summary>
    /// <param name="accountId">The account whose own counts are wanted beside the deployment's.</param>
    /// <param name="periodStart">The instant the period began, as the ceilings place it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The messages and recipients recorded in the period, for the account and for every account together.</returns>
    /// <remarks>
    /// The read is unconditional rather than cached. A period's counts are two indexed reads, a deployment with no
    /// ceiling never asks for them at all, and a cached figure would be wrong exactly when it matters — after a restart,
    /// or while a second process is sending against the same period.
    /// </remarks>
    Task<OutgoingMailUsage> ReadUsageSinceAsync(
        MailAccountId accountId,
        DateTimeOffset periodStart,
        CancellationToken cancellationToken);
}
