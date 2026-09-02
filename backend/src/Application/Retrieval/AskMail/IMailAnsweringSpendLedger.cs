// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>Counts what answering has cost across the current period, and decides whether another question fits inside it.</summary>
/// <remarks>
/// <para>
/// One ledger for the whole process, because a ceiling over a period is one answer about the deployment: a ledger per
/// scope would let each concurrent question believe it was the first. Every member is safe to call from several runs at
/// once.
/// </para>
/// <para>
/// It is process-local and is not durable. A restart begins a new period with nothing spent, which is the deliberate
/// trade: making it durable would put a database write on the path of every provider call in every run, to defend
/// against a failure mode — a process restarting often enough to matter — that an operator already has to notice for
/// other reasons.
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
    /// <returns><see langword="true" /> when the run may proceed, and <see langword="false" /> when the period is spent.</returns>
    /// <remarks>
    /// The run is counted by the act of admitting it rather than when it finishes, so a run still in flight already
    /// occupies its place: the alternative would admit every concurrent question and count them afterwards, which is
    /// precisely the burst the ceiling exists to bound.
    /// </remarks>
    bool TryAdmitRun();

    /// <summary>Adds what one provider call consumed to the current period.</summary>
    /// <param name="usage">The tokens the call sent and received.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="usage" /> is <see langword="null" />.</exception>
    /// <remarks>Recorded per call rather than per run, so a run that is stopped part way through has still spent what it spent.</remarks>
    void RecordSpend(ChatTokenUsage usage);
}
