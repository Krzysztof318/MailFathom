// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.TestSupport;

/// <summary>Keeps what each answering period admitted and consumed in memory, so a tracker has a ledger to admit against.</summary>
/// <remarks>
/// Hand-written rather than substituted, because what a tracker test asserts is that the third question of a period is
/// refused: a substitute would answer from a script and the assertion would be about the script. It does what the
/// persisted store's two statements do — an admission conditional on both ceilings and a spend that adds — and it keeps
/// one entry per period, which is what lets a test roll a clock over and find the next window unspent.
/// </remarks>
internal sealed class InMemoryMailAnsweringSpendPeriodStore : IMailAnsweringSpendPeriodStore
{
    private readonly Dictionary<DateTimeOffset, SpentPeriod> periods = [];

    /// <summary>Gets how many periods have a row, which is what says a roll-over began a new one.</summary>
    public int PeriodCount => this.periods.Count;

    /// <summary>Reads what one period holds, so a test can assert the ledger rather than the tracker's own reading.</summary>
    /// <param name="periodStart">The period's start.</param>
    /// <returns>The runs it admitted and the tokens they consumed.</returns>
    public (int Runs, long Tokens) Spent(DateTimeOffset periodStart) =>
        this.periods.TryGetValue(periodStart, out var period) ? (period.Runs, period.Tokens) : (0, 0);

    /// <inheritdoc />
    public Task<int> TryAdmitRunAsync(
        DateTimeOffset periodStart,
        int maximumRuns,
        long maximumTokens,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRuns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTokens, 1);
        cancellationToken.ThrowIfCancellationRequested();

        var period = this.periods.GetValueOrDefault(periodStart, SpentPeriod.Unspent);

        if (period.Runs >= maximumRuns || period.Tokens >= maximumTokens)
        {
            return Task.FromResult(0);
        }

        var admitted = period with { Runs = period.Runs + 1 };
        this.periods[periodStart] = admitted;

        return Task.FromResult(admitted.Runs);
    }

    /// <inheritdoc />
    public Task<long> RecordSpendAsync(DateTimeOffset periodStart, long tokenCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokenCount);
        cancellationToken.ThrowIfCancellationRequested();

        var period = this.periods.GetValueOrDefault(periodStart, SpentPeriod.Unspent);
        var charged = period with { Tokens = period.Tokens + tokenCount };
        this.periods[periodStart] = charged;

        return Task.FromResult(charged.Tokens);
    }

    private sealed record SpentPeriod(int Runs, long Tokens)
    {
        public static SpentPeriod Unspent { get; } = new(Runs: 0, Tokens: 0);
    }
}
