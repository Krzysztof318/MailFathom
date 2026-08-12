// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>The local state a rule pass reads its candidates from and records what it has evaluated into.</summary>
/// <remarks>
/// <para>
/// Every member here reads or writes what an earlier synchronization run already committed. Nothing on this port reaches
/// a mail server, which is what makes evaluation safe to run as a step beside synchronization rather than inside it: a
/// pass cannot open a mailbox session, and therefore cannot touch a remote <c>\Seen</c> flag however long it runs.
/// </para>
/// <para>
/// Both walks are keyset reads over one account, ordered by the stored email's identity, because that is the only
/// ordering that is total, stable, and unaffected by anything a later write does to a row. The pass hands back the
/// position it reached rather than the implementation remembering one, so a batch that stopped short of its own end
/// resumes at the email nobody read.
/// </para>
/// </remarks>
public interface IMailRuleEvaluationStore
{
    /// <summary>Reads the account's emails that no pass has evaluated, oldest identity first.</summary>
    /// <param name="accountId">The account whose arrival queue is read.</param>
    /// <param name="resumeAfter">The identity the previous batch of this walk reached, or <see langword="null" /> to start at the beginning.</param>
    /// <param name="batchSize">How many emails to read at most.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The candidates, which is empty once the queue is drained.</returns>
    /// <remarks>
    /// The queue shrinks as the pass records evaluations, so a completed email never appears in a later batch. The
    /// resume position exists for the emails a pass skipped rather than evaluated: without it a batch whose head is
    /// waiting for extraction would be read again on the next batch and the walk would never move past it.
    /// </remarks>
    Task<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>> GetEmailsAwaitingFirstEvaluationAsync(
        MailAccountId accountId,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Reads the account's stored emails in identity order, whether or not a pass has evaluated them.</summary>
    /// <param name="accountId">The account whose mailbox is walked.</param>
    /// <param name="resumeAfter">The identity the requested run last committed, or <see langword="null" /> to start at the beginning.</param>
    /// <param name="batchSize">How many emails to read at most.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The candidates, which is empty once the walk has reached the end of the mailbox.</returns>
    /// <remarks>
    /// This walk does not shrink as it progresses, which is exactly why the requested run commits its position: the
    /// position is the whole of what a restart resumes from.
    /// </remarks>
    Task<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>> GetStoredEmailsAsync(
        MailAccountId accountId,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Reads the text extracted from one email's body.</summary>
    /// <param name="storedEmailId">The email a condition named the body text of.</param>
    /// <param name="cancellationToken">Cancels the read, which the evaluation timeout also reaches through.</param>
    /// <returns>The extracted text, or <see langword="null" /> when no extraction has produced any for this email.</returns>
    /// <remarks>
    /// Reached only when a condition names the body-text fact, and at most once per email per pass, which
    /// <see cref="Facts.MailRuleFacts" /> is what guarantees.
    /// </remarks>
    Task<string?> ReadExtractedBodyTextAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken);

    /// <summary>Records that a rule set has been evaluated for each of these emails.</summary>
    /// <param name="session">The session the batch's evaluations and the run's position commit together in.</param>
    /// <param name="storedEmailIds">The emails the batch evaluated, which excludes every email it skipped.</param>
    /// <param name="evaluatedAt">When the pass evaluated them.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns>A task that completes once the writes are staged in the session.</returns>
    /// <remarks>
    /// This is what takes an email out of the arrival queue, and therefore what makes a rule apply to mail arriving from
    /// now on rather than to a mailbox's whole history. An email the pass skipped is deliberately left out, so it
    /// returns to the queue's head and is evaluated once whatever it was waiting for has arrived.
    /// </remarks>
    Task RecordEvaluatedAsync(
        IPersistenceSession session,
        IReadOnlyList<StoredEmailId> storedEmailIds,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken);
}
