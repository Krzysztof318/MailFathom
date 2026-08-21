// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>Turns a schedule's occasion into the whole-mailbox run an account's synchronization runs then carry.</summary>
/// <remarks>
/// <para>
/// The job is deliberately short. What it does is record that a scheduled walk of the account is wanted, exactly as an
/// operator's request does; the walk itself happens where every other rule pass happens, which is the account's own
/// synchronization run with its per-account isolation, its backoff, and its slot count. A handler that walked the
/// mailbox here would be a second place rules run, and it would hold a worker for as long as somebody's mailbox takes.
/// </para>
/// <para>
/// Running it twice with one payload is the same as running it once, which is what the queue asks of every handler: a
/// second occasion finding a run outstanding is answered with that run and writes nothing.
/// </para>
/// </remarks>
public sealed class ScheduledMailRuleRunHandler : IJobHandler
{
    private readonly MailRuleEvaluationRunRequests runRequests;

    /// <summary>Initializes the handler from the intake that records a wanted run.</summary>
    /// <param name="runRequests">Records that a scheduled walk of the account is wanted, or answers with the run already outstanding.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="runRequests" /> is <see langword="null" />.</exception>
    public ScheduledMailRuleRunHandler(MailRuleEvaluationRunRequests runRequests)
    {
        ArgumentNullException.ThrowIfNull(runRequests);

        this.runRequests = runRequests;
    }

    /// <inheritdoc />
    public JobType JobType => JobType.RunScheduledMailRules;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the payload is not the contract this job type names.</exception>
    public Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        if (payload is not RunScheduledMailRulesJobPayload account)
        {
            throw new ArgumentException(
                $"A '{JobType.RunScheduledMailRules}' job carries a payload naming one account.",
                nameof(payload));
        }

        return this.runRequests.SubmitScheduledAsync(account.ToAccountId(), cancellationToken);
    }
}
