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
    /// <param name="accountId">The account to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The outstanding run, or <see langword="null" /> when the account has none — including when the last one ended.</returns>
    Task<MailRuleEvaluationRun?> FindOutstandingAsync(MailAccountId accountId, CancellationToken cancellationToken);

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
}
