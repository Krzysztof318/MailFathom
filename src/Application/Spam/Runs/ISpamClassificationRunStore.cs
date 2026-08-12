// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Spam.Runs;

/// <summary>Keeps the one whole-mailbox classification run an account may have outstanding, and the progress made on it.</summary>
public interface ISpamClassificationRunStore
{
    /// <summary>Reads the run this account is still waiting to have carried further.</summary>
    /// <param name="accountId">The account to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The outstanding run, or <see langword="null" /> when the account has none — including when the last one ended.</returns>
    Task<SpamClassificationRun?> FindOutstandingAsync(MailAccountId accountId, CancellationToken cancellationToken);

    /// <summary>Reads the run this account last had, whether it is still outstanding or has ended.</summary>
    /// <param name="accountId">The account to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The run, or <see langword="null" /> when the account has never been asked for one.</returns>
    /// <remarks>
    /// What an operator asking after the run they started reads. A run that has finished is exactly as much of an answer
    /// as one still going, so reporting only the outstanding one would leave "it completed an hour ago" and "you never
    /// asked" looking identical from the outside.
    /// </remarks>
    Task<SpamClassificationRun?> FindLatestAsync(MailAccountId accountId, CancellationToken cancellationToken);

    /// <summary>Stages a run — a request, a batch's progress, or an ending — in the session it commits through.</summary>
    /// <param name="session">The session the run's progress is staged in.</param>
    /// <param name="run">The run as it stands.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns>A task that completes once the write is staged in the session.</returns>
    /// <remarks>
    /// One run per account, so this replaces whatever the account last had rather than appending. Committing the position
    /// with the counts it accounts for is what makes the run resumable rather than merely restartable: a crash between
    /// the two would leave a walk that either repeated a batch or stepped over one.
    /// </remarks>
    Task SaveAsync(IPersistenceSession session, SpamClassificationRun run, CancellationToken cancellationToken);
}
