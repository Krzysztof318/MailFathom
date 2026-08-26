// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>Keeps the one whole-mailbox run an account may have outstanding, and the progress an account run makes on it.</summary>
public interface IMailRuleEvaluationRunStore
{
    /// <summary>Reads the run this account is still waiting to have carried further.</summary>
    /// <param name="account">The account to read, named by its owner and its identifier together.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The outstanding run, or <see langword="null" /> when the account has none — including when the last one ended.</returns>
    Task<MailRuleEvaluationRun?> FindOutstandingAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken);

    /// <summary>Reads the run this account last had, whether it is still outstanding or has ended.</summary>
    /// <param name="account">The account to read, named by its owner and its identifier together.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The run, or <see langword="null" /> when the account has never been asked for one.</returns>
    /// <remarks>
    /// What an operator asking after the run they started reads. A run that has finished is exactly as much of an answer
    /// as one still going, so reporting only the outstanding one would leave "it completed an hour ago" and "you never
    /// asked" looking identical from the outside.
    /// </remarks>
    Task<MailRuleEvaluationRun?> FindLatestAsync(MailAccountIdentity account, CancellationToken cancellationToken);

    /// <summary>Stages a run — a request, a batch's progress, or an ending — in the session it commits through.</summary>
    /// <param name="session">The session the batch's evaluations are staged in, so progress and evaluations commit together.</param>
    /// <param name="run">The run as it stands.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns>A task that completes once the write is staged in the session.</returns>
    /// <remarks>
    /// One run per account, so this replaces whatever the account last had rather than appending. Committing the
    /// position with the evaluations it accounts for is what makes the run resumable rather than merely restartable: a
    /// crash between the two would otherwise either replay a batch or step over one.
    /// </remarks>
    Task SaveAsync(IPersistenceSession session, MailRuleEvaluationRun run, CancellationToken cancellationToken);

    /// <summary>Stages a run that is starting, unless the account's row has meanwhile become one it may not replace.</summary>
    /// <param name="session">The session the write is staged in.</param>
    /// <param name="run">The run this request wants to start.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns>The run the account already has, when this request must stand down; <see langword="null" /> once the write is staged.</returns>
    /// <remarks>
    /// Separate from <see cref="SaveAsync" /> because the two write for opposite reasons. A pass committing a batch owns
    /// the run it is carrying and overwrites it deliberately. A request is only entitled to the row while the row still
    /// says what it said when the request decided to write, so the check belongs beside the write rather than at the
    /// read that preceded it: between the two, another request or another schedule's occasion may have claimed the
    /// account, and an unconditional write would silently replace a wider run with a narrower one or reset a walk that
    /// was already under way. <see cref="MailRuleEvaluationRun.Supersedes" /> is the rule this applies.
    /// </remarks>
    Task<MailRuleEvaluationRun?> TryStartAsync(
        IPersistenceSession session,
        MailRuleEvaluationRun run,
        CancellationToken cancellationToken);
}
