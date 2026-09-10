// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>Keeps the durable count of what each answering period has admitted and consumed, for the whole deployment.</summary>
/// <remarks>
/// <para>
/// Durable rather than held in memory, because a ceiling worded as the deployment's is overshot by the replica count
/// when each process counts its own: three replicas each admitting thirty runs an hour admit ninety, at a provider the
/// operator is billed by.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// records the trade this reverses: a write per provider call was refused and a write per admitted run was not, because
/// a run is already about to spend a provider's tokens and one row touched beside that is not measurable.
/// </para>
/// <para>
/// Nothing allocates a period. Its start is where the bounds place the current instant, so every replica derives the
/// same key from its own clock, the first run of a period inserts its row, and a period nobody asked a question in has
/// no row at all.
/// </para>
/// <para>
/// Both members answer with the period's own figure after the write, because that is the reading an operator's
/// instruments publish and it costs nothing beside a write that was going to happen. Neither is a query: what has been
/// spent is not a question any use case above this asks.
/// </para>
/// </remarks>
public interface IMailAnsweringSpendPeriodStore
{
    /// <summary>Takes an allowance for one run against the deployment's period, only where both ceilings still have room.</summary>
    /// <param name="periodStart">The period's start, as the bounds place it.</param>
    /// <param name="maximumRuns">The greatest number of runs this period may admit.</param>
    /// <param name="maximumTokens">The greatest number of tokens the runs of this period may consume.</param>
    /// <param name="cancellationToken">Cancels the admission.</param>
    /// <returns>The runs this period has admitted including this one, or zero where the period is spent.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either ceiling is below one.</exception>
    /// <remarks>
    /// One statement rather than a read and a decision, because every replica admits against the same row: a
    /// read-modify-write would let each of them find room the others had already taken. The run is counted by the act
    /// of admitting it rather than when it finishes, so a run still in flight already occupies its place.
    /// </remarks>
    Task<int> TryAdmitRunAsync(
        DateTimeOffset periodStart,
        int maximumRuns,
        long maximumTokens,
        CancellationToken cancellationToken);

    /// <summary>Adds what one run consumed to the period it ended in.</summary>
    /// <param name="periodStart">The start of the period the run ended in, as the bounds place it — which, for a run that spanned a roll-over, is a later period than the one it was admitted into.</param>
    /// <param name="tokenCount">The tokens the run sent and received together, across every call it made.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The tokens this period has consumed including these.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
    /// <remarks>
    /// Charged once as the run ends rather than once per provider call, which is the trade ADR 0031 took: a run
    /// already spends a provider's tokens, so one row touched beside that is not measurable, while a row touched per
    /// turn of a tool loop is. Expressed as an increment rather than as a read followed by a write, so two replicas
    /// answering at once add to each other instead of overwriting one another's total.
    /// </remarks>
    Task<long> RecordSpendAsync(DateTimeOffset periodStart, long tokenCount, CancellationToken cancellationToken);
}
