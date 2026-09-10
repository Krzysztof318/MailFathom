// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>Counts what answering has cost across the current period, and decides whether another question fits inside it.</summary>
/// <remarks>
/// <para>
/// One ledger for the whole deployment, because a ceiling over a period is one answer about it: a ledger per scope, per
/// process, or per replica would let each concurrent question believe it was the first. Every member is safe to call
/// from several runs at once.
/// </para>
/// <para>
/// It is the deployment's and it is durable, because the ceiling is worded as the deployment's: several replicas each
/// counting their own would admit the replica count times what their operator agreed to, at a provider that bills for
/// it, and a restart would begin every period again with nothing spent.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// records the trade that was reversed to get there: a write per provider call was refused on the grounds that a
/// question opens none of its own, and a write per admitted run was not, because a run is already about to spend a
/// provider's tokens.
/// </para>
/// <para>
/// The admission is a decision and never a wait. A question over the ceiling is refused with an answer the caller can
/// act on rather than held until the period turns over, because holding it would convert a spend ceiling into a queue
/// of requests occupying the endpoint that serves the rest of the surface.
/// </para>
/// <para>
/// Two members and no way to read what has been spent, because nothing above this boundary acts on that figure: a use
/// case decides whether to answer and never how close the period is to its ceiling. What an operator reads is published
/// by whatever implements this, as instruments rather than as a call.
/// </para>
/// </remarks>
public interface IMailAnsweringSpendLedger
{
    /// <summary>Takes an allowance for one run, if the current period has one left.</summary>
    /// <param name="cancellationToken">Cancels the admission.</param>
    /// <returns><see langword="true" /> when the run may proceed, and <see langword="false" /> when the period is spent.</returns>
    /// <remarks>
    /// The run is counted by the act of admitting it rather than when it finishes, so a run still in flight already
    /// occupies its place: the alternative would admit every concurrent question and count them afterwards, which is
    /// precisely the burst the ceiling exists to bound.
    /// </remarks>
    Task<bool> TryAdmitRunAsync(CancellationToken cancellationToken);

    /// <summary>Adds what one provider call consumed to the current period.</summary>
    /// <param name="usage">The tokens the call sent and received.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the period has been charged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="usage" /> is <see langword="null" />.</exception>
    /// <remarks>Recorded per call rather than per run, so a run that is stopped part way through has still spent what it spent.</remarks>
    Task RecordSpendAsync(ChatTokenUsage usage, CancellationToken cancellationToken);
}
